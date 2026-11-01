using System;
using System.Collections.Generic;
using FinalAnswer.Application;
using FinalAnswer.Contracts;
using FinalAnswer.Domain;
using FinalAnswer.CursedBoxes;
using FinalAnswer.Infrastructure;
using FinalAnswer.PendingModules;
using FinalAnswer.Quiz;
using FinalAnswer.UI;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FinalAnswer.App
{
    public enum AppState { Boot, Menu, CreateRoom, GenerationCode, Lobby, InModule }

    /// <summary>
    /// Owns the local state machine (Java flow: MainMenu → RoomSettings → GenerationCode → CageLobby → modules)
    /// and the active module scope. It never parses socket JSON and holds no game rules: modules are chosen
    /// from the server snapshot. Developer tooling plugs in through <see cref="DeveloperTools"/>.
    /// </summary>
    public sealed class GameManager : IDisposable
    {
        readonly IRoomApi api;
        readonly RoomManager room;
        readonly LocalServerProcessHost localServer;
        readonly IMonotonicClock clock;
        readonly ArtCatalog art;
        readonly Localization texts;
        readonly ShellAudio audio;
        readonly ShellSettings settings;
        readonly RectTransform screens;
        readonly StatusBar statusBar;

        AppState state = AppState.Boot;
        MainMenuView menu;
        CreateRoomView createRoom;
        GenerationCodeView codeView;
        CageLobbyView lobby;
        SettingsView settingsView;
        IntroPlayer intro;
        IntroSchedule introSchedule;
        IGameModule module;
        bool firstMenu = true;

        // GenerationCode draft (created over HTTP, kept alive with heartbeats until OK).
        RoomSettings pendingSettings;
        RoomSnapshot draft;
        string draftUrl = "";
        int requestVersion;
        bool generating, heartbeatPending;
        float heartbeatTimer;
        string previewCode = "";

        string hostToken = "";
        Action<bool> serverWaiter;
        float serverWaitTimeout, serverPoll;

        public AppState State => state;
        public RoomManager Room => room;
        /// <summary>Configured server, or for this run only the developer server that replaced an outdated one.</summary>
        public string ServerUrl => sessionServer ?? settings.ServerUrl;
        /// <summary>Why the last server check failed (outdated protocol, port conflict …); empty when fine.</summary>
        public string ServerProblem { get; private set; } = "";
        /// <summary>The connected server accepts host-only Last Command DEBUG commands.</summary>
        public bool ServerDebug { get; private set; }
        string sessionServer;
        bool restartLastCommand;

        /// <summary>Developer server port (FA_DEV_PORT, default 18180). Never 8080, so a stale server there is left alone.</summary>
        public static int DevServerPort => int.TryParse(Environment.GetEnvironmentVariable("FA_DEV_PORT"), out var port) && port > 0 && port < 65536 ? port : 18180;
        public LocalServerProcessHost LocalServer => localServer;
        public IGameModule ActiveModule => module;
        public event Action<string> Log;

        public GameManager(IRoomApi api, RoomManager room, LocalServerProcessHost localServer, IMonotonicClock clock, ArtCatalog art,
            Localization texts, ShellAudio audio, ShellSettings settings, RectTransform screens, RectTransform overlay)
        {
            this.api = api;
            this.room = room;
            this.localServer = localServer;
            this.clock = clock;
            this.art = art;
            this.texts = texts;
            this.audio = audio;
            this.settings = settings;
            this.screens = screens;
            statusBar = new StatusBar(overlay, room.RetryNow);
            room.Snapshots.Changed += OnSnapshot;
            room.CommandRejected += OnRejected;
            room.RoomClosed += OnRoomClosed;
            room.ConnectionChanged += _ => RefreshStatus();
            room.EventReceived += e => Log?.Invoke("← " + e.Type + (e.IsBroadcast ? "" : " #" + Short(e.RequestId)));
        }

        public void Start() => ShowMenu();

        public void Tick(float delta)
        {
            room.Tick();
            module?.Tick(delta, room.ServerNow);
            lobby?.SetServerNow(room.ServerNow);
            TickIntro(delta);
            KeepDraftAlive(delta);
            PollServer(delta);
        }

        public void Dispose()
        {
            LeaveRoom();
            DiscardDraft();
            localServer.Stop();
        }

        // ---------------- Main menu ----------------

        public void ShowMenu()
        {
            Clear();
            state = AppState.Menu;
            menu = new MainMenuView(screens, art, firstMenu, settings.ReducedMotion, DeveloperTools.Enabled,
                Play, OpenCreateRoom, OpenCodePreview, OpenSettings, Quit);
            firstMenu = false;
            RefreshStatus();
        }

        void Play()
        {
            if (room.Snapshots.Current != null && room.State != ConnectionState.Idle && room.State != ConnectionState.Closed)
            {
                OnSnapshot(room.Snapshots.Current, force: true);
                return;
            }
            var dialog = new ModeInfoDialog(screens, art.Font("Fonts/NotoSans-SemiCondensedSemiBold"), art.Font("Fonts/NotoSans-Regular"), texts);
            dialog.Open(texts.Get("lobby.required_title"), texts.Get("lobby.required"));
        }

        void OpenSettings()
        {
            if (settingsView != null) return;
            settingsView = new SettingsView(screens, art.Font("Fonts/NotoSans-SemiCondensedSemiBold"), settings, () =>
            {
                settings.Save();
                settings.ApplyTo(audio, texts);
            }, () =>
            {
                UiKit.Dispose(settingsView.Root.gameObject);
                settingsView = null;
                if (state == AppState.Menu) ShowMenu();
            });
        }

        // ---------------- Room creation (RoomSettingsScreen) ----------------

        public void OpenCreateRoom()
        {
            Clear();
            state = AppState.CreateRoom;
            createRoom = new CreateRoomView(screens, art, texts, settings.ReducedMotion, chosen =>
            {
                pendingSettings = chosen;
                OpenGenerationCode();
            }, ShowMenu);
            createRoom.FocusName();
        }

        // ---------------- Generation code (GenerationCodeScreen) ----------------

        void OpenCodePreview()
        {
            pendingSettings = null;
            OpenGenerationCode();
        }

        /// <summary>Developer tour: open the code screen for given settings and press GENERATE.</summary>
        public void GenerateFor(RoomSettings chosen)
        {
            pendingSettings = chosen;
            OpenGenerationCode();
            Generate();
        }

        /// <summary>Developer tour: press OK on the code screen.</summary>
        public void ConfirmCode() => Confirm();

        public bool HasDraft => draft != null;

        /// <summary>Overrides the server address for this run only (not saved).</summary>
        public void UseServer(string url) { settings.ServerUrl = url.TrimEnd('/'); sessionServer = null; }

        void OpenGenerationCode()
        {
            Clear();
            state = AppState.GenerationCode;
            DiscardDraft();
            codeView = new GenerationCodeView(screens, art, settings.ReducedMotion, Generate, Reroll, Confirm, BackFromCode);
            codeView.SetStatus(texts.Get(pendingSettings == null ? "code.preview_hint" : "code.generate_hint"));
        }

        void Generate()
        {
            if (generating || draft != null || previewCode.Length > 0) return;
            if (pendingSettings == null)
            {
                previewCode = RoomCodes.Generate();
                codeView.ShowCode(previewCode);
                codeView.SetStatus(texts.Get("code.preview_ready"));
                return;
            }
            generating = true;
            codeView.SetBusy(true, false);
            codeView.SetStatus(texts.Get("code.creating"));
            var version = ++requestVersion;
            EnsureServer(ok =>
            {
                if (version != requestVersion || codeView == null) return;
                if (!ok)
                {
                    generating = false;
                    codeView.SetBusy(false, false);
                    codeView.SetStatus(ServerProblem.Length > 0 ? ServerProblem : texts.Get("error.server_unavailable") + (localServer.Status.Length > 0 ? "\n" + localServer.Status : ""));
                    return;
                }
                var url = ServerUrl;
                api.CreateRoom(url, pendingSettings.ToForm(), created =>
                {
                    if (version != requestVersion || codeView == null) { api.Leave(url, created.RoomCode, created.Token, null); return; }
                    generating = false;
                    if (!RoomCodes.IsValid(created.RoomCode))
                    {
                        api.Leave(url, created.RoomCode, created.Token, null);
                        codeView.SetBusy(false, false);
                        codeView.SetStatus(texts.Get("code.server_outdated"));
                        return;
                    }
                    draft = created;
                    draftUrl = url;
                    heartbeatTimer = 0;
                    codeView.SetBusy(false, false);
                    codeView.ShowCode(created.RoomCode);
                    codeView.SetStatus(texts.Get("code.ready"));
                    Log?.Invoke("room created " + created.RoomCode);
                }, error =>
                {
                    if (version != requestVersion || codeView == null) return;
                    generating = false;
                    codeView.SetBusy(false, false);
                    codeView.SetStatus(texts.Error(error));
                });
            });
        }

        void Reroll()
        {
            if (generating) return;
            if (draft == null)
            {
                if (previewCode.Length == 0) return;
                string next;
                do next = RoomCodes.Generate(); while (next == previewCode);
                previewCode = next;
                codeView.ShowCode(previewCode);
                codeView.SetStatus(texts.Get("code.preview_ready"));
                return;
            }
            generating = true;
            heartbeatPending = false;
            var version = ++requestVersion;
            codeView.SetBusy(false, true);
            codeView.SetStatus(texts.Get("code.rerolling"));
            api.Reroll(draftUrl, draft.RoomCode, draft.Token, updated =>
            {
                if (version != requestVersion || codeView == null) return;
                generating = false;
                heartbeatTimer = 0;
                codeView.SetBusy(false, false);
                if (!RoomCodes.IsValid(updated.RoomCode)) { codeView.SetStatus(texts.Get("code.server_outdated")); return; }
                draft = updated;
                codeView.ShowCode(updated.RoomCode);
                codeView.SetStatus(texts.Get("code.ready"));
            }, error =>
            {
                if (version != requestVersion || codeView == null) return;
                generating = false;
                codeView.SetBusy(false, false);
                codeView.SetStatus(texts.Error(error));
            });
        }

        void Confirm()
        {
            if (generating) return;
            if (draft == null)
            {
                if (previewCode.Length > 0) ShowMenu();
                return;
            }
            var created = draft;
            draft = null;
            requestVersion++;
            ActivateRoom(draftUrl, created);
        }

        void BackFromCode()
        {
            if (generating && draft != null) return;
            DiscardDraft();
            pendingSettings = null;
            ShowMenu();
        }

        void KeepDraftAlive(float delta)
        {
            if (draft == null || heartbeatPending || generating) return;
            heartbeatTimer += Mathf.Clamp(delta, 0, .1f);
            if (heartbeatTimer < 2) return;
            heartbeatTimer = 0;
            heartbeatPending = true;
            var version = requestVersion;
            api.Heartbeat(draftUrl, draft.RoomCode, draft.Token, updated =>
            {
                if (version != requestVersion) return;
                heartbeatPending = false;
                draft = updated;
                codeView?.SetConfirmEnabled(true);
            }, error =>
            {
                if (version != requestVersion) return;
                heartbeatPending = false;
                codeView?.SetConfirmEnabled(false);
                codeView?.SetStatus(texts.Error(error));
            });
        }

        void DiscardDraft()
        {
            requestVersion++;
            generating = heartbeatPending = false;
            previewCode = "";
            if (draft != null) api.Leave(draftUrl, draft.RoomCode, draft.Token, null);
            draft = null;
        }

        // ---------------- Active room ----------------

        /// <summary>Opens the realtime session for a created room and shows the cage lobby. Also used by the developer sandbox.</summary>
        public void ActivateRoom(string serverUrl, RoomSnapshot created)
        {
            hostToken = created.Token;
            room.Begin(serverUrl, created);
            ShowLobby();
        }

        void ShowLobby()
        {
            Clear();
            state = AppState.Lobby;
            lobby = new CageLobbyView(screens, art, texts, audio, settings.ReducedMotion, PhoneAddresses,
                StartQuiz, StartImpostor, StartCursed, StartLastCommand, ShowMenu, () => { LeaveRoom(); ShowMenu(); });
            if (room.Snapshots.Current != null) lobby.Apply(room.Snapshots.Current, room.ServerNow);
            RefreshStatus();
        }

        void OnSnapshot(RoomSnapshot snapshot) => OnSnapshot(snapshot, false);

        void OnSnapshot(RoomSnapshot snapshot, bool force)
        {
            if (!force && state != AppState.Lobby && state != AppState.InModule) return;
            var wanted = ModuleRouting.Select(snapshot);
            // STARTING belongs to the lobby: the cages swing and the blackout/title play before the first question.
            if (wanted == ModuleKind.Quiz && snapshot.Phase == QuizPhases.Starting) wanted = ModuleKind.Lobby;
            if (wanted == ModuleKind.Lobby)
            {
                if (state != AppState.Lobby || lobby == null) ShowLobby();
                else lobby.Apply(snapshot, room.ServerNow);
                if (restartLastCommand) { restartLastCommand = false; StartLastCommand(); } // developer "restart" from the machine
                SyncIntro(snapshot);
                return;
            }
            SyncIntroOff();
            if (module == null || module.Kind != wanted) EnterModule(wanted, snapshot);
            else module.ApplySnapshot(snapshot);
        }

        void SyncIntroOff()
        {
            if (intro == null) return;
            intro.Dispose();
            intro = null;
        }

        void EnterModule(ModuleKind kind, RoomSnapshot snapshot)
        {
            Clear();
            state = AppState.InModule;
            switch (kind)
            {
                case ModuleKind.Quiz: module = new QuizModule(screens); break;
                case ModuleKind.CursedBoxes: module = new CursedBoxesModule(screens); break;
                case ModuleKind.Impostor: module = new Impostor.ImpostorModule(screens); break;
                case ModuleKind.LastCommand: module = new LastCommandHost.LastCommandModule(screens, () => restartLastCommand = true); break;
                default: module = new PendingModule(screens, kind); break;
            }
            module.Enter(new ModuleContext(room, clock), snapshot);
            Log?.Invoke("module → " + kind);
        }

        /// <summary>Overrides the intro preference for this session (developer sandbox).</summary>
        public bool? IntroOverride { get; set; }

        public void StartQuiz()
        {
            var payload = room.RoomPayload();
            // Java sends the intro id with START_GAME; the server then gates the first question on INTRO_READY/SKIP_INTRO.
            if (IntroOverride ?? settings.PlayIntro) payload["introId"] = Schedule.IntroId;
            SendStart(MessageTypes.StartGame, payload);
        }

        /// <summary>Developer tour / tools: press SKIP INTRO.</summary>
        public void SkipIntro() => intro?.Skip();

        public bool IntroPlaying => intro != null && !intro.Finished;

        IntroSchedule Schedule => introSchedule ?? (introSchedule = IntroPlayer.LoadSchedule(art));

        void SyncIntro(RoomSnapshot snapshot)
        {
            var data = snapshot.Quiz?.Intro;
            var active = data != null && snapshot.Phase == QuizPhases.Starting && lobby != null;
            if (!active)
            {
                if (intro != null) { intro.Dispose(); intro = null; if (lobby != null) audio?.Room(true); }
                lobby?.SetCinematic(false);
                return;
            }
            var gameId = snapshot.Quiz.GameId;
            if (intro == null || intro.GameId != gameId)
            {
                intro?.Dispose();
                var introId = data.Value<string>("introId");
                intro = new IntroPlayer(screens, art, Schedule, gameId, data.Value<long>("elapsedMillis") / 1000f, type =>
                {
                    var payload = room.RoomPayload();
                    payload["gameId"] = gameId;
                    payload["introId"] = introId;
                    room.Send(type, payload);
                });
                Log?.Invoke("intro started (" + Schedule.Lines.Count + " lines, " + Schedule.Duration.ToString("0.0") + " s)");
            }
            lobby.SetCinematic(true);
        }

        void TickIntro(float delta)
        {
            if (intro == null) return;
            intro.Update(delta);
            audio?.Room(intro.AmbienceWanted);
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null && (keyboard.escapeKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame)) intro.Skip();
            if (keyboard != null && keyboard.pKey.wasPressedThisFrame) intro.Pause(!intro.Paused);
        }

        public void StartCursed() => SendStart(MessageTypes.StartCursedBoxes, LobbyPayload());

        public void StartImpostor()
        {
            var payload = LobbyPayload();
            var ids = new JArray();
            foreach (var p in room.Snapshots.Current.Players) if (p.Online) ids.Add(p.Id);
            payload["participantIds"] = ids;
            SendStart(MessageTypes.StartImpostor, payload);
        }

        public void StartLastCommand()
        {
            var payload = LobbyPayload();
            // Seeds travel as strings: 64-bit values never pass through float.
            var bytes = Guid.NewGuid().ToByteArray();
            payload["seed"] = (BitConverter.ToInt64(bytes, 0) & long.MaxValue).ToString(System.Globalization.CultureInfo.InvariantCulture);
            SendStart(MessageTypes.StartLastCommand, payload);
        }

        JObject LobbyPayload()
        {
            var payload = room.RoomPayload();
            payload["sessionId"] = room.Snapshots.Current?.LobbyId ?? "";
            return payload;
        }

        void SendStart(string type, JObject payload)
        {
            Log?.Invoke("→ " + type);
            if (room.Send(type, payload) == null) lobby?.ShowError(texts.Get("network.lost"));
        }

        void OnRejected(CommandRejection rejection)
        {
            Log?.Invoke("✗ " + rejection.CommandType + ": " + rejection.Code);
            if (state == AppState.Lobby) lobby?.ShowError(texts.Error(rejection.Code));
        }

        void OnRoomClosed(string reason)
        {
            hostToken = "";
            ShowMenu();
            statusBar.Set(texts.Get("error.room_missing") + " (" + reason + ")", Theme.Warning, false);
        }

        public void LeaveRoom()
        {
            if (room.State == ConnectionState.Idle && hostToken.Length == 0) return;
            var code = room.RoomCode;
            var url = room.BaseUrl;
            var token = hostToken;
            room.End();
            hostToken = "";
            // HTTP leave closes the room even if the socket was already gone.
            if (token.Length > 0 && url.Length > 0) api.Leave(url, code, token, null);
        }

        // ---------------- Server availability ----------------

        /// <summary>
        /// Health + protocol check. A compatible server is used as is. An outdated one is never connected to: release
        /// builds report it; developer builds on this machine switch (for this run) to the current server on
        /// <see cref="DevServerPort"/>, building and starting it from ../FinalAnswer when nothing answers there.
        /// </summary>
        public void EnsureServer(Action<bool> done)
        {
            var url = ServerUrl;
            api.Health(url, health =>
            {
                if (Accept(url, health)) { done(true); return; }
                var loopback = Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.IsLoopback || uri.Host == "localhost");
                var local = loopback && localServer.IsAvailable;
                if (health.Status == ServerStatus.Outdated)
                {
                    ServerProblem = "Сервер " + url + " устарел: в нём нет актуального THE LAST COMMAND (race/solo). "
                        + "Перезапустите его из текущего кода (tools/unity/run_dev_server.sh) или укажите другой адрес в SETTINGS.";
                    Debug.LogWarning("[Server] " + ServerProblem + " (" + health.Detail + ")");
                    if (!loopback || !DeveloperTools.Enabled) { done(false); return; }
                    var dev = "http://127.0.0.1:" + DevServerPort;
                    api.Health(dev, devHealth =>
                    {
                        if (Accept(dev, devHealth)) { sessionServer = dev; done(true); return; }
                        if (devHealth.Status == ServerStatus.Outdated) { ServerProblem += "\nНа порту " + DevServerPort + " тоже старый сервер."; done(false); return; }
                        if (!localServer.IsAvailable) { ServerProblem += "\nЗапустите актуальный сервер: tools/unity/run_dev_server.sh (порт " + DevServerPort + ")."; done(false); return; }
                        sessionServer = dev;
                        StartLocal(DevServerPort, done);
                    });
                    return;
                }
                if (!local) { ServerProblem = "Сервер недоступен: " + url; done(false); return; }
                StartLocal(uri.IsDefaultPort ? 8080 : uri.Port, done);
            });
        }

        bool Accept(string url, ServerHealth health)
        {
            if (!health.Compatible) return false;
            ServerProblem = "";
            ServerDebug = health.LastCommandDebug;
            Debug.Log("[Server] " + url + " compatible" + (health.LastCommandDebug ? ", Last Command debug enabled" : ""));
            return true;
        }

        void StartLocal(int port, Action<bool> done)
        {
            if (!localServer.IsRunning) localServer.Start(port, DeveloperTools.Enabled);
            if (!localServer.IsRunning) { ServerProblem = localServer.Status; done(false); return; }
            Log?.Invoke("local Java server starting on " + port + "…");
            serverWaiter = done;
            serverWaitTimeout = DeveloperTools.Enabled ? 180 : 40; // the developer launcher compiles the server first
            serverPoll = .5f;
        }

        void PollServer(float delta)
        {
            if (serverWaiter == null) return;
            serverWaitTimeout -= delta;
            serverPoll -= delta;
            if (serverPoll > 0) return;
            serverPoll = .5f;
            var waiter = serverWaiter;
            if (serverWaitTimeout <= 0 || !localServer.IsRunning) { serverWaiter = null; ServerProblem = localServer.Status; waiter(false); return; }
            var url = ServerUrl;
            api.Health(url, health =>
            {
                if (serverWaiter != waiter || !Accept(url, health)) return;
                serverWaiter = null;
                Log?.Invoke("local Java server ready");
                waiter(true);
            });
        }

        IReadOnlyList<ConnectionPanel.Address> PhoneAddresses()
        {
            var result = new List<ConnectionPanel.Address>();
            var snapshot = room.Snapshots.Current;
            if (snapshot != null && snapshot.PublicUrl.Length > 0)
            {
                result.Add(new ConnectionPanel.Address { Label = "PUBLIC", Url = snapshot.PublicUrl });
                return result;
            }
            if (!Uri.TryCreate(room.BaseUrl, UriKind.Absolute, out var uri)) return result;
            if (!(uri.IsLoopback || uri.Host == "localhost"))
            {
                result.Add(new ConnectionPanel.Address { Label = "SERVER", Url = uri.GetLeftPart(UriPartial.Authority) });
                return result;
            }
            foreach (var (label, ip) in LocalServerProcessHost.LanAddresses())
                result.Add(new ConnectionPanel.Address { Label = label, Url = uri.Scheme + "://" + ip + ":" + uri.Port });
            result.Add(new ConnectionPanel.Address { Label = "THIS COMPUTER ONLY", Url = uri.GetLeftPart(UriPartial.Authority) });
            return result;
        }

        // ---------------- Shared ----------------

        void Clear()
        {
            module?.Exit();
            module = null;
            SyncIntroOff();
            lobby?.Dispose();
            menu = null;
            createRoom = null;
            codeView = null;
            lobby = null;
            settingsView = null;
            UiKit.Clear(screens);
        }

        void RefreshStatus()
        {
            switch (room.State)
            {
                case ConnectionState.Reconnecting:
                    statusBar.Set(texts.Get("network.lost") + " (" + room.ReconnectAttempt + "/" + Domain.ReconnectPolicy.MaxAttempts + ")", Theme.Warning, true); break;
                case ConnectionState.Failed:
                    statusBar.Set(texts.Get("network.retry") + " · " + room.LastCloseReason, Theme.Bad, true); break;
                case ConnectionState.Connecting:
                case ConnectionState.Authenticating:
                    statusBar.Set(texts.Get("network.connecting"), Theme.Warning, false); break;
                default:
                    statusBar.Set("", Theme.Muted, false); break;
            }
        }

        static string Short(string id) => id.Length > 6 ? id.Substring(0, 6) : id;

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            UnityEngine.Application.Quit();
#endif
        }
    }

    /// <summary>RoomCodeGenerator.java: six characters without ambiguous glyphs (preview codes only; real codes come from the server).</summary>
    public static class RoomCodes
    {
        const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        static readonly System.Random Random = new System.Random();

        public static string Generate()
        {
            var chars = new char[6];
            for (var i = 0; i < chars.Length; i++) chars[i] = Alphabet[Random.Next(Alphabet.Length)];
            return new string(chars);
        }

        public static bool IsValid(string code)
        {
            if (code == null || code.Length != 6) return false;
            foreach (var c in code) if (Alphabet.IndexOf(c) < 0) return false;
            return true;
        }
    }
}

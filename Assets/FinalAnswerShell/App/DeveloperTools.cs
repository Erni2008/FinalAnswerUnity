using System;
using System.Collections.Generic;
using System.Text;
using FinalAnswer.Application;
using FinalAnswer.Contracts;
using FinalAnswer.Infrastructure;
using FinalAnswer.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace FinalAnswer.App
{
    public enum SandboxGame { Quiz, CursedBoxes, Impostor, LastCommand, LobbyOnly }

    /// <summary>
    /// Developer-only tooling, available in the Editor and Development builds (or with FA_DEV=1):
    ///  F1 — solo sandbox: starts the local Java server if needed, creates a room, fills it with in-process
    ///       phone bots and launches the chosen game, so one person can walk through a whole mini-game;
    ///  F3 — debug overlay: connection/snapshot state, event log, bot controls, reconnect and audio checks.
    /// Bots use the public phone API only; the server stays authoritative and unchanged.
    /// </summary>
    public sealed class DeveloperTools
    {
        public static bool Enabled =>
            UnityEngine.Application.isEditor || Debug.isDebugBuild || Environment.GetEnvironmentVariable("FA_DEV") == "1";

        static readonly string[] BotNames = { "Аня", "Борис", "Вика", "Глеб", "Даша", "Егор", "Жанна" };
        static readonly string[] GameTitles = { "ВИКТОРИНА (+ CURSED BOXES)", "CURSED BOXES", "THE IMPOSTOR", "THE LAST COMMAND", "ТОЛЬКО ЛОББИ" };

        readonly GameManager manager;
        readonly IRoomApi api;
        readonly IMonotonicClock clock;
        readonly ShellAudio audio;
        readonly RectTransform overlay;
        readonly List<DevPhoneBot> bots = new List<DevPhoneBot>();
        readonly Queue<string> log = new Queue<string>();

        RectTransform sandboxPanel, debugPanel;
        Text sandboxStatus, debugText;
        SandboxGame game = SandboxGame.Quiz;
        int botCount = 3, questionCount = 3, modeIndex;
        BotAnswerMode answerMode = BotAnswerMode.Random;
        SandboxGame? pendingStart;
        float startDelay;
        string sandboxRoom = "";
        bool playIntro;

        public DeveloperTools(GameManager manager, IRoomApi api, IMonotonicClock clock, ShellAudio audio, RectTransform overlay)
        {
            this.manager = manager;
            this.api = api;
            this.clock = clock;
            this.audio = audio;
            this.overlay = overlay;
            manager.Log += Write;
        }

        public void Tick(float delta)
        {
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.f1Key.wasPressedThisFrame) ToggleSandbox();
                if (keyboard.f3Key.wasPressedThisFrame) ToggleDebug();
            }
            foreach (var bot in bots) bot.Tick();
            PeriodicShots(delta);
            AutoStart(delta);
            if (debugPanel != null && debugPanel.gameObject.activeSelf) RenderDebug();
        }

        public void Dispose()
        {
            foreach (var bot in bots) bot.Leave();
            bots.Clear();
        }

        void Write(string line)
        {
            Debug.Log("[Dev] " + line);
            log.Enqueue(DateTime.Now.ToString("HH:mm:ss.f") + "  " + line);
            while (log.Count > 18) log.Dequeue();
        }

        // ---------------- F1: solo sandbox ----------------

        void ToggleSandbox()
        {
            if (sandboxPanel != null) { UiKit.Dispose(sandboxPanel.gameObject); sandboxPanel = null; return; }
            sandboxPanel = UiKit.Stretch(UiKit.Rect("DevSandbox", overlay));
            sandboxPanel.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, .7f);
            var panel = UiKit.Panel(sandboxPanel, "Panel", new Vector2(1100, 950), spacing: 12);
            var title = UiKit.Label(panel, "РЕЖИМ РАЗРАБОТЧИКА · СОЛО-ТЕСТ", 40, Theme.Accent, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Size(title.gameObject, height: 60);
            var hint = UiKit.Label(panel, "Сервер Java поднимется сам, комнату заполнят боты-телефоны, игра запустится. Вы управляете только хостом.",
                22, Theme.Muted, TextAnchor.MiddleCenter);
            UiKit.Size(hint.gameObject, height: 64);

            var gameStep = new Stepper(UiKit.Field(panel, "Игра"), 0, GameTitles.Length - 1, (int)game, i => GameTitles[i], wrap: true);
            var botsStep = new Stepper(UiKit.Field(panel, "Ботов-игроков"), 1, 7, botCount);
            gameStep.Changed += i =>
            {
                game = (SandboxGame)i;
                if (game == SandboxGame.Impostor && (botCount < 3 || botCount > 4)) botsStep.Set(3);
                if (game != SandboxGame.Impostor && game != SandboxGame.LobbyOnly && botCount < 2) botsStep.Set(2);
            };
            botsStep.Changed += v => botCount = v;
            var answers = new Stepper(UiKit.Field(panel, "Ответы ботов"), 0, 2, (int)answerMode,
                i => i == 0 ? "СЛУЧАЙНО" : i == 1 ? "ВСЕГДА A" : "МОЛЧАТ (таймаут)", wrap: true);
            answers.Changed += i => answerMode = (BotAnswerMode)i;
            var questions = new Stepper(UiKit.Field(panel, "Вопросов"), 1, 12, questionCount);
            questions.Changed += v => questionCount = v;
            var introStep = new Stepper(UiKit.Field(panel, "Интро перед викториной"), 0, 1, playIntro ? 1 : 0, i => i == 1 ? "ВКЛ (74 с)" : "ВЫКЛ", wrap: true);
            introStep.Changed += i => playIntro = i == 1;
            var mode = new Stepper(UiKit.Field(panel, "Режим комнаты"), 0, GameModes.All.Count - 1, modeIndex, i => GameModes.All[i].Title, wrap: true);
            mode.Changed += i => modeIndex = i;

            sandboxStatus = UiKit.Label(panel, "Сервер: " + manager.ServerUrl, 22, Theme.Muted, TextAnchor.MiddleCenter);
            UiKit.Size(sandboxStatus.gameObject, height: 70);
            var row = UiKit.Row(panel, "Buttons", 84);
            UiKit.Button(row, "ЗАКРЫТЬ (F1)", ToggleSandbox, height: 84).gameObject.AddComponent<UiSoundHooks>();
            UiKit.Button(row, "ЗАПУСТИТЬ", Launch, primary: true, height: 84, fontSize: 36).gameObject.AddComponent<UiSoundHooks>();
        }

        /// <summary>Adds phone bots to the active room (also used by the visual tour).</summary>
        public void AddBots(int count)
        {
            var snapshot = manager.Room.Snapshots.Current;
            if (snapshot == null) return;
            for (var i = 0; i < count && bots.Count < BotNames.Length; i++)
            {
                var index = bots.Count;
                var bot = new DevPhoneBot(api, () => new WebSocketTransport(), clock, manager.Room.BaseUrl, snapshot.RoomCode, BotNames[index], CageCharacterLibrary.Ids[index]) { AnswerMode = answerMode };
                bots.Add(bot);
                bot.Join(error => Write("bot join failed: " + error));
            }
        }

        string shotDir;
        float shotEvery, shotTimer;
        int shotIndex;

        /// <summary>`-fa-shots &lt;dir&gt; [-fa-shot-every seconds]`: periodic screenshots of this window (bug reports, QA).</summary>
        void PeriodicShots(float delta)
        {
            if (shotDir == null)
            {
                var args = Environment.GetCommandLineArgs();
                var i = Array.IndexOf(args, "-fa-shots");
                shotDir = i >= 0 && i + 1 < args.Length ? args[i + 1] : "";
                var e = Array.IndexOf(args, "-fa-shot-every");
                shotEvery = e >= 0 && e + 1 < args.Length && float.TryParse(args[e + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 5;
                if (shotDir.Length > 0) System.IO.Directory.CreateDirectory(shotDir);
            }
            if (shotDir.Length == 0) return;
            shotTimer += delta;
            if (shotTimer < shotEvery) return;
            shotTimer = 0;
            var phase = manager.Room.Snapshots.Current?.CursedBoxes?.Value<string>("phase") ?? manager.Room.Snapshots.Current?.Impostor?.Value<string>("phase") ?? manager.Room.Snapshots.Current?.Phase ?? "menu";
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(shotDir, (++shotIndex).ToString("000") + "_" + phase + ".png"));
        }

        /// <summary>`-fa-solo quiz|cursed|impostor|lastcommand|lobby [-fa-bots N]`: launch the sandbox at startup.</summary>
        public void LaunchFromCommandLine()
        {
            var args = Environment.GetCommandLineArgs();
            var i = Array.IndexOf(args, "-fa-solo");
            if (i < 0 || i + 1 >= args.Length) return;
            switch (args[i + 1].ToLowerInvariant())
            {
                case "cursed": game = SandboxGame.CursedBoxes; break;
                case "impostor": game = SandboxGame.Impostor; botCount = 3; break;
                case "lastcommand": game = SandboxGame.LastCommand; break;
                case "lobby": game = SandboxGame.LobbyOnly; break;
                default: game = SandboxGame.Quiz; break;
            }
            var b = Array.IndexOf(args, "-fa-bots");
            if (b >= 0 && b + 1 < args.Length && int.TryParse(args[b + 1], out var n)) botCount = Mathf.Clamp(n, 1, 7);
            var server = Array.IndexOf(args, "-fa-server");
            if (server >= 0 && server + 1 < args.Length) manager.UseServer(args[server + 1]);
            Launch();
        }

        void Launch()
        {
            if (game == SandboxGame.Impostor && (botCount < 3 || botCount > 4)) { SandboxStatus("THE IMPOSTOR: нужно 3 или 4 бота", Theme.Bad); return; }
            if (game != SandboxGame.LobbyOnly && game != SandboxGame.Impostor && botCount < 2) { SandboxStatus("Серверу нужно минимум 2 игрока", Theme.Bad); return; }
            manager.LeaveRoom();
            Dispose();
            SandboxStatus("Проверка сервера…", Theme.Warning);
            manager.EnsureServer(ok =>
            {
                if (!ok) { SandboxStatus("Сервер недоступен: " + manager.ServerUrl + "\n" + manager.LocalServer.Status, Theme.Bad); return; }
                var url = manager.ServerUrl;
                var settings = new RoomSettings
                {
                    Name = "DEV · " + GameTitles[(int)game],
                    MaxPlayers = Mathf.Clamp(botCount, 2, 8),
                    QuestionCount = questionCount,
                    GameMode = GameModes.All[modeIndex].Id,
                };
                SandboxStatus("Создание комнаты…", Theme.Warning);
                api.CreateRoom(url, settings.ToForm(), created =>
                {
                    sandboxRoom = created.RoomCode;
                    manager.IntroOverride = playIntro;
                    Write("sandbox room " + created.RoomCode + " · " + botCount + " bots · " + game);
                    manager.ActivateRoom(url, created);
                    for (var i = 0; i < botCount; i++)
                    {
                        var bot = new DevPhoneBot(api, () => new WebSocketTransport(), clock, url, created.RoomCode, BotNames[i], CageCharacterLibrary.Ids[i]) { AnswerMode = answerMode };
                        bots.Add(bot);
                        bot.Join(error => Write("bot join failed: " + error));
                    }
                    pendingStart = game == SandboxGame.LobbyOnly ? (SandboxGame?)null : game;
                    startDelay = 0;
                    if (sandboxPanel != null) ToggleSandbox();
                }, error => SandboxStatus("Комната не создана: " + error, Theme.Bad));
            });
        }

        /// <summary>Waits until every bot is ONLINE (and the cages have closed) before sending the host start command.</summary>
        void AutoStart(float delta)
        {
            if (pendingStart == null) return;
            var snapshot = manager.Room.Snapshots.Current;
            if (snapshot == null || snapshot.RoomCode != sandboxRoom || manager.Room.State != ConnectionState.Online) return;
            if (snapshot.Players.Count < bots.Count || snapshot.OnlinePlayerCount < bots.Count) { startDelay = 0; return; }
            startDelay += delta;
            if (startDelay < 3.2f) return; // let the cage close/lock animation and sounds finish
            var chosen = pendingStart.Value;
            pendingStart = null;
            switch (chosen)
            {
                case SandboxGame.Quiz: manager.StartQuiz(); break;
                case SandboxGame.CursedBoxes: manager.StartCursed(); break;
                case SandboxGame.Impostor: manager.StartImpostor(); break;
                case SandboxGame.LastCommand: manager.StartLastCommand(); break;
            }
        }

        void SandboxStatus(string text, Color color)
        {
            Write(text.Replace('\n', ' '));
            if (sandboxStatus == null) return;
            sandboxStatus.text = text;
            sandboxStatus.color = color;
        }

        // ---------------- F3: debug overlay ----------------

        void ToggleDebug()
        {
            if (debugPanel != null) { debugPanel.gameObject.SetActive(!debugPanel.gameObject.activeSelf); debugPanel.SetAsLastSibling(); return; }
            debugPanel = UiKit.Rect("DebugOverlay", overlay);
            debugPanel.anchorMin = debugPanel.anchorMax = new Vector2(1, 1);
            debugPanel.pivot = new Vector2(1, 1);
            debugPanel.sizeDelta = new Vector2(640, 1000);
            debugPanel.anchoredPosition = new Vector2(-12, -12);
            debugPanel.gameObject.AddComponent<Image>().color = new Color(0, 0, 0, .82f);
            var layout = debugPanel.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(14, 14, 12, 12);
            layout.spacing = 8;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandHeight = false;
            debugText = UiKit.Label(debugPanel, "", 17, new Color(.85f, .95f, .85f), TextAnchor.UpperLeft);
            debugText.font = Font.CreateDynamicFontFromOSFont("Menlo", 17) ?? debugText.font;
            UiKit.Size(debugText.gameObject, height: 700, flexibleHeight: 1);
            Row("SYNC", () => manager.Room.RequestSync(), "ОБРЫВ ХОСТА", () => manager.Room.SimulateConnectionLoss());
            Row("БОТЫ: ОБРЫВ", () => { foreach (var b in bots) b.DropConnection(); }, "БОТЫ: ВЕРНУТЬ", () => { foreach (var b in bots) b.Reconnect(); });
            Row("БОТ ВЫХОДИТ", () => { if (bots.Count > 0) { bots[bots.Count - 1].Leave(); bots.RemoveAt(bots.Count - 1); } }, "ОТВЕТЫ БОТОВ", CycleAnswers);
            Row("ЗВУК ▶ (ВСЕ)", PlayAllCues, "MUTE", () => { audio.Muted = !audio.Muted; Write("audio muted = " + audio.Muted); });
        }

        void Row(string a, Action actionA, string b, Action actionB)
        {
            var row = UiKit.Row(debugPanel, "Row", 46, 8);
            UiKit.Button(row, a, actionA, height: 46, fontSize: 18);
            UiKit.Button(row, b, actionB, height: 46, fontSize: 18);
        }

        void CycleAnswers()
        {
            answerMode = (BotAnswerMode)(((int)answerMode + 1) % 3);
            foreach (var bot in bots) bot.AnswerMode = answerMode;
            Write("bot answers = " + answerMode);
        }

        int cueIndex;
        List<string> soundList;
        AudioSource preview;

        /// <summary>Steps through every shipped clip (UI, cages, quiz, Cursed Boxes, Impostor, intro) and every host line.</summary>
        void PlayAllCues()
        {
            if (soundList == null)
            {
                soundList = new List<string>();
                foreach (var entry in ArtCatalog.Active.Entries)
                    if (entry.asset is AudioClip && !entry.key.StartsWith("Audio/Host/")) soundList.Add("clip:" + entry.key);
                if (HostVoice.Current != null) foreach (var line in HostVoice.Current.All) soundList.Add("voice:" + line);
            }
            if (soundList.Count == 0) return;
            var item = soundList[cueIndex++ % soundList.Count];
            if (item.StartsWith("voice:"))
            {
                var parts = item.Substring(6).Split(':');
                HostVoice.Current.Play(parts[0], parts[1]);
            }
            else
            {
                if (preview == null) preview = new GameObject("SoundPreview").AddComponent<AudioSource>();
                preview.Stop();
                preview.clip = ArtCatalog.Active.Clip(item.Substring(5));
                preview.volume = audio.Muted ? 0 : audio.Master;
                preview.Play();
            }
            Write("♪ " + cueIndex + "/" + soundList.Count + "  " + item);
        }

        void RenderDebug()
        {
            var room = manager.Room;
            var s = room.Snapshots.Current;
            var text = new StringBuilder();
            text.Append("FINAL ANSWER · DEBUG (F3)\n");
            text.Append("app: ").Append(manager.State).Append("   module: ").Append(manager.ActiveModule?.Kind.ToString() ?? "-").Append('\n');
            text.Append("socket: ").Append(room.State).Append(room.LastCloseReason.Length > 0 ? " (" + room.LastCloseReason + ")" : "").Append('\n');
            text.Append("server: ").Append(manager.ServerUrl).Append(manager.LocalServer.IsRunning ? "  [local process]" : "").Append('\n');
            if (s != null)
            {
                text.Append("room: ").Append(s.RoomCode).Append("  rev ").Append(s.Revision).Append("  phase ").Append(s.Phase).Append('\n');
                text.Append("players: ").Append(s.OnlinePlayerCount).Append('/').Append(s.Players.Count).Append("  canStart ").Append(s.CanStart).Append('\n');
                if (s.Quiz != null) text.Append("quiz: q").Append(s.Quiz.QuestionNumber).Append('/').Append(s.Quiz.QuestionCount).Append("  t-").Append(Math.Max(0, (s.Quiz.Deadline - room.ServerNow) / 1000)).Append("s\n");
                if (s.CursedBoxes != null) text.Append("cursed: ").Append(s.CursedBoxes.Value<string>("phase")).Append("  shots left ").Append(s.CursedBoxes.Value<int?>("remainingShotCount")).Append('\n');
                if (s.Impostor != null) text.Append("impostor: ").Append(s.Impostor.Value<string>("phase")).Append("  round ").Append(s.Impostor.Value<int?>("round")).Append('\n');
                if (s.LastCommand != null) text.Append("last command: session ").Append(s.LastCommand.Value<string>("sessionId")).Append('\n');
            }
            text.Append("\nbots (answers: ").Append(answerMode).Append(")\n");
            foreach (var bot in bots) text.Append("  ").Append(bot.Name).Append(" · ").Append(bot.CharacterId).Append(" · ").Append(bot.Status).Append(" · sent ").Append(bot.ActionsSent).Append('\n');
            text.Append("\nevents\n");
            foreach (var line in log) text.Append(line).Append('\n');
            debugText.text = text.ToString();
        }
    }
}

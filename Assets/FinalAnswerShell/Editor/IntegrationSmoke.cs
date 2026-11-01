using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using FinalAnswer.Application;
using FinalAnswer.Contracts;
using FinalAnswer.CursedBoxes;
using FinalAnswer.Infrastructure;
using FinalAnswer.Quiz;
using FinalAnswer.UI;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace FinalAnswer.EditorTools
{
    /// <summary>
    /// Headless end-to-end check against a real, unchanged Java RoomServer: HTTP create → WS AUTH → bots join
    /// over the phone API → START_GAME → the real Quiz / Cursed Boxes host modules drive every barrier to
    /// GAME_FINISHED. Run (server must be up, Node 22+ on PATH):
    ///   FA_SMOKE_SERVER=http://127.0.0.1:18080 Unity -batchmode -projectPath unity/FinalAnswer
    ///     -executeMethod FinalAnswer.EditorTools.IntegrationSmoke.Run
    /// </summary>
    public static class IntegrationSmoke
    {
        const double TimeoutSeconds = 240;

        static string server;
        static RoomManager room;
        static IGameModule module;
        static ModuleContext context;
        static Process bots;
        static GameObject canvas;
        static double startedAt, lastTick, finishedSeenAt;
        static bool started, botsLaunched;
        static readonly List<string> phases = new List<string>();
        static readonly StopwatchClock Clock = new StopwatchClock();

        public static void Run()
        {
            server = Environment.GetEnvironmentVariable("FA_SMOKE_SERVER") ?? "http://127.0.0.1:18080";
            ArtCatalog.Active = AssetDatabase.LoadAssetAtPath<ArtCatalog>("Assets/FinalAnswerShell/Settings/ArtCatalog.asset");
            Localization.Current = new Localization(ArtCatalog.Active.Text("Localization/messages_en.txt").text, ArtCatalog.Active.Text("Localization/messages_ru.txt").text);
            canvas = UiKit.CreateCanvas("SmokeCanvas").gameObject;
            room = new RoomManager(() => new WebSocketTransport(), Clock);
            context = new ModuleContext(room, Clock);
            room.CommandRejected += r => Log("rejected " + r.CommandType + ": " + r.Code);
            room.Snapshots.Changed += OnSnapshot;

            var settings = new RoomSettings { Name = "Unity smoke", MaxPlayers = 4, GameMode = "deadline", QuestionCount = 3 };
            new UnityRoomApi().CreateRoom(server, settings.ToForm(), created =>
            {
                Log("room created " + created.RoomCode + " mode=" + created.GameMode + " host=" + created.IsHost);
                room.Begin(server, created);
            }, error => Finish(false, "create failed: " + error));

            startedAt = lastTick = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;
        }

        static void Tick()
        {
            var now = EditorApplication.timeSinceStartup;
            var delta = (float)(now - lastTick);
            lastTick = now;
            if (now - startedAt > TimeoutSeconds) { Finish(false, "timeout; phases: " + string.Join(" → ", phases)); return; }
            room.Tick();
            module?.Tick(delta, room.ServerNow);
            // Press the host's "continue" button a moment after the crate results are shown.
            if (module is CursedBoxesModule cursed && cursed.IsFinished)
            {
                if (finishedSeenAt == 0) finishedSeenAt = now;
                else if (finishedSeenAt > 0 && now - finishedSeenAt > 2) { cursed.ReturnToLobby(); finishedSeenAt = -1; }
            }
            else if (finishedSeenAt != 0 && !(module is CursedBoxesModule)) finishedSeenAt = 0;

            var snapshot = room.Snapshots.Current;
            if (room.State == ConnectionState.Online && !botsLaunched)
            {
                botsLaunched = true;
                Log("host authenticated over WebSocket; launching 3 phone bots");
                LaunchBots(snapshot.RoomCode);
            }
            if (!started && snapshot != null && snapshot.CanStart && snapshot.Players.Count == 3)
            {
                started = true;
                Log("roster: " + snapshot.Players.Count + " online; sending START_GAME");
                room.Send(MessageTypes.StartGame, room.RoomPayload());
            }
            if (room.State == ConnectionState.Failed || room.State == ConnectionState.Closed)
                Finish(false, "connection " + room.State + ": " + room.LastCloseReason);
        }

        static void OnSnapshot(RoomSnapshot snapshot)
        {
            var wanted = ModuleRouting.Select(snapshot);
            if (wanted != ModuleKind.Lobby && (module == null || module.Kind != wanted))
            {
                module?.Exit();
                module = wanted == ModuleKind.CursedBoxes ? new CursedBoxesModule(canvas.transform) : (IGameModule)new QuizModule(canvas.transform);
                module.Enter(context, snapshot);
                Record("module:" + wanted);
            }
            else module?.ApplySnapshot(snapshot);

            var label = snapshot.CursedBoxes != null ? "cursed:" + snapshot.CursedBoxes.Value<string>("phase") : snapshot.Phase;
            if (snapshot.Quiz != null && snapshot.CursedBoxes == null) label += "#" + snapshot.Quiz.QuestionNumber;
            Record(label);
            if (snapshot.Phase == QuizPhases.GameFinished && snapshot.CursedBoxes == null)
            {
                var scores = new List<string>();
                foreach (var p in snapshot.Players) scores.Add(p.Name + "=" + p.Score + (p.IsGhost ? "(ghost)" : ""));
                Finish(true, "GAME_FINISHED; scores " + string.Join(", ", scores));
            }
        }

        static void Record(string label)
        {
            if (phases.Count > 0 && phases[phases.Count - 1] == label) return;
            phases.Add(label);
            Log("→ " + label);
        }

        static void LaunchBots(string code)
        {
            var script = Environment.GetEnvironmentVariable("FA_BOTS_SCRIPT") ?? Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "../../FinalAnswer/tools/unity/phone_bots.mjs"));
            var info = new ProcessStartInfo(Environment.GetEnvironmentVariable("FA_NODE") ?? "node", "\"" + script + "\" " + code + " --count 3 --server " + server)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            bots = Process.Start(info);
            bots.OutputDataReceived += (_, e) => { if (e.Data != null) Debug.Log("[bots] " + e.Data); };
            bots.ErrorDataReceived += (_, e) => { if (e.Data != null) Debug.Log("[bots!] " + e.Data); };
            bots.BeginOutputReadLine();
            bots.BeginErrorReadLine();
        }

        static void Finish(bool success, string message)
        {
            EditorApplication.update -= Tick;
            Log((success ? "PASS: " : "FAIL: ") + message);
            Log("phases: " + string.Join(" → ", phases));
            module?.Exit();
            room.End();
            try { if (bots != null && !bots.HasExited) bots.Kill(); } catch (Exception) { /* exited */ }
            UiKit.Dispose(canvas);
            if (UnityEngine.Application.isBatchMode) EditorApplication.Exit(success ? 0 : 1);
        }

        static void Log(string message) => Debug.Log("[FinalAnswer.Smoke] " + message);
    }
}

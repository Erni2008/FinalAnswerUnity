using System;
using System.Collections.Generic;
using System.IO;
using FinalAnswer.Contracts;
using UnityEngine;

namespace FinalAnswer.App
{
    /// <summary>
    /// Developer visual smoke test: `FinalAnswer -fa-tour &lt;dir&gt; [-fa-server url]` walks through the shell
    /// (menu → create room → code → cage lobby with bots → match start → quiz), saving a screenshot of this
    /// window at each step, then quits. Captures only the game's own frame buffer.
    /// </summary>
    public sealed class VisualTour
    {
        readonly GameManager manager;
        readonly DeveloperTools developer;
        readonly string directory;
        readonly Queue<(float wait, string shot, Func<bool> ready, Action action)> steps = new Queue<(float, string, Func<bool>, Action)>();
        float timer, timeout;
        Action afterCapture;
        float afterCaptureDelay;

        public static VisualTour FromCommandLine(GameManager manager, DeveloperTools developer)
        {
            var args = Environment.GetCommandLineArgs();
            var dir = Arg(args, "-fa-tour");
            if (dir == null || developer == null) return null;
            var server = Arg(args, "-fa-server");
            if (server != null) manager.UseServer(server);
            return new VisualTour(manager, developer, dir);
        }

        static string Arg(string[] args, string name)
        {
            var i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        VisualTour(GameManager manager, DeveloperTools developer, string directory)
        {
            this.manager = manager;
            this.developer = developer;
            this.directory = directory;
            Directory.CreateDirectory(directory);
            Step(3f, "01_main_menu", null, manager.OpenCreateRoom);
            Step(1.5f, "02_create_room", null, () => manager.GenerateFor(new RoomSettings { Name = "Тестовая комната", MaxPlayers = 4, GameMode = "standard" }));
            Step(1.5f, "03_generation_code", () => manager.HasDraft, manager.ConfirmCode);
            Step(1f, null, () => manager.State == AppState.Lobby, () => developer.AddBots(3));
            Step(5.5f, "04_cage_lobby", () => manager.Room.Snapshots.Current?.OnlinePlayerCount == 3, () => { manager.IntroOverride = true; manager.StartQuiz(); });
            Step(14f, "05_intro", () => manager.IntroPlaying, manager.SkipIntro);
            Step(1.2f, "06_quiz_typing", () => manager.State == AppState.InModule, null);
            Step(4f, "07_quiz_answering", () => manager.Room.Snapshots.Current?.Phase == QuizPhases.Answering, null);
            Step(13.5f, "08_quiz_clock_urgent", () => manager.Room.Snapshots.Current?.Phase == QuizPhases.Answering, null);
            Step(1.6f, "09_quiz_reveal", () => manager.Room.Snapshots.Current?.Phase == QuizPhases.Results, null);
            Step(.5f, null, null, () =>
            {
                Debug.Log("[Tour] done");
                UnityEngine.Application.Quit();
            });
        }

        void Step(float wait, string shot, Func<bool> ready, Action action) => steps.Enqueue((wait, shot, ready, action));

        public void Tick(float delta)
        {
            // CaptureScreenshot writes at the end of the frame: run the next action only after it has been taken.
            if (afterCapture != null)
            {
                afterCaptureDelay -= delta;
                if (afterCaptureDelay > 0) return;
                var pending = afterCapture;
                afterCapture = null;
                pending();
                return;
            }
            if (steps.Count == 0) return;
            var step = steps.Peek();
            timeout += delta;
            if (step.ready != null && !step.ready())
            {
                timer = 0;
                if (timeout > 60) { Debug.LogError("[Tour] timed out waiting before " + step.shot); UnityEngine.Application.Quit(1); steps.Clear(); }
                return;
            }
            timer += delta;
            if (timer < step.wait) return;
            if (step.shot != null)
            {
                var path = Path.Combine(directory, step.shot + ".png");
                ScreenCapture.CaptureScreenshot(path);
                Debug.Log("[Tour] " + path);
            }
            steps.Dequeue();
            timer = timeout = 0;
            afterCapture = step.action ?? (() => { });
            afterCaptureDelay = .25f;
        }
    }
}

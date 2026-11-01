using System;
using System.IO;
using FinalAnswer.Application;
using FinalAnswer.Infrastructure;
using FinalAnswer.UI;
using UnityEngine;
using UnityEngine.UI;

namespace FinalAnswer.App
{
    /// <summary>
    /// The single composition root (Bootstrap scene). Creates concrete services once and hands them to the
    /// GameManager; nothing else news up a socket or looks up global state.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AppRoot : MonoBehaviour
    {
        [SerializeField] ArtCatalog art;
        [SerializeField] Font uiFont;

        GameManager manager;
        DeveloperTools developer;
        VisualTour tour;
        ShellAudio audio;
        HostVoice hostVoice;
        LocalServerProcessHost localServer;

        void Awake()
        {
            if (FindObjectsByType<AppRoot>(FindObjectsInactive.Include).Length > 1)
            {
                Destroy(gameObject);
                return;
            }
            if (art == null) throw new InvalidOperationException("AppRoot has no ArtCatalog. Run Final Answer → Configure Project.");
            DontDestroyOnLoad(gameObject);
            UnityEngine.Application.targetFrameRate = 60;
            UnityEngine.Application.runInBackground = true; // the room keeps receiving while the window is unfocused

            ArtCatalog.Active = art;
            UiKit.Font = uiFont;
            UiKit.EnsureEventSystem();
            var camera = new GameObject("ShellCamera").AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.transform.SetParent(transform, false);

            // FitViewport semantics: Expand keeps the whole 1920×1080 design visible; backgrounds cover the rest.
            var canvas = UiKit.CreateCanvas("ShellCanvas");
            canvas.GetComponent<CanvasScaler>().screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            canvas.transform.SetParent(transform, false);
            UiKit.Fill(canvas.transform, "Background", Color.black);
            var screens = UiKit.Stretch(UiKit.Rect("Screens", canvas.transform));
            var overlay = UiKit.Stretch(UiKit.Rect("Overlay", canvas.transform));

            var settings = ShellSettings.Load();
            var texts = new Localization(art.Text("Localization/messages_en.txt").text, art.Text("Localization/messages_ru.txt").text);
            Localization.Current = texts;
            audio = new ShellAudio(gameObject, art);
            hostVoice = new HostVoice(gameObject, art);
            settings.ApplyTo(audio, texts);

            var clock = new StopwatchClock();
            var api = new UnityRoomApi();
            var room = new RoomManager(() => new WebSocketTransport(), clock);
            localServer = new LocalServerProcessHost(FindRepositoryRoot());
            manager = new GameManager(api, room, localServer, clock, art, texts, audio, settings, screens, overlay);
            if (DeveloperTools.Enabled) developer = new DeveloperTools(manager, api, clock, audio, overlay);
            manager.Start();
            tour = VisualTour.FromCommandLine(manager, developer);
            if (tour == null) developer?.LaunchFromCommandLine();
        }

        void Update()
        {
            var delta = Time.unscaledDeltaTime;
            manager?.Tick(delta);
            developer?.Tick(delta);
            tour?.Tick(delta);
            audio?.Update(delta);
            hostVoice?.Update();
        }

        void OnApplicationQuit() => Shutdown();

        void OnDestroy() => Shutdown();

        void Shutdown()
        {
            developer?.Dispose();
            developer = null;
            manager?.Dispose();
            manager = null;
            localServer?.Dispose();
            localServer = null;
        }

        /// <summary>
        /// Repository checkout that contains the Java server, for development/solo hosting only.
        /// FINAL_ANSWER_REPO overrides; otherwise walk up from the project/player data folder to settings.gradle.
        /// </summary>
        static string FindRepositoryRoot()
        {
            var configured = Environment.GetEnvironmentVariable("FINAL_ANSWER_REPO");
            if (!string.IsNullOrEmpty(configured) && File.Exists(Path.Combine(configured, "settings.gradle"))) return configured;
            var dir = new DirectoryInfo(UnityEngine.Application.dataPath);
            // FinalAnswerUnity sits next to the Java checkout (~/Documents/FinalAnswer).
            for (var probe = dir; probe != null; probe = probe.Parent)
            {
                var sibling = Path.Combine(probe.FullName, "FinalAnswer");
                if (File.Exists(Path.Combine(sibling, "settings.gradle")) && Directory.Exists(Path.Combine(sibling, "server"))) return sibling;
            }
            for (var i = 0; dir != null && i < 8; i++, dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "settings.gradle")) && Directory.Exists(Path.Combine(dir.FullName, "server")))
                    return dir.FullName;
            return null;
        }
    }
}

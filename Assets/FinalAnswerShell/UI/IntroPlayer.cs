using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace FinalAnswer.UI
{
    /// <summary>
    /// Measured intro schedule (IntroTimeline.java): scenes may start later than authored so speech is never
    /// shortened; sound cues are remapped onto the moved scenes.
    /// </summary>
    public sealed class IntroSchedule
    {
        public sealed class Speech { public string Id, Caption; public float Start, Duration, PauseAfter; public float End => Start + Duration; }
        public sealed class Cue { public string Id, File, Category; public float At, Until, Gain; }

        public readonly List<Speech> Lines = new List<Speech>();
        public readonly List<Cue> Cues = new List<Cue>();
        public readonly Dictionary<string, float> SceneStart = new Dictionary<string, float>();
        public string IntroId { get; }
        public float Duration { get; }
        public float FadeStart { get; }
        public float FadeDuration { get; }
        public float ExitFade { get; }
        public float AmbienceGain { get; }
        public float HostGain { get; }
        public bool Fits { get; }

        public IntroSchedule(JObject definition, Func<string, float> durations)
        {
            IntroId = definition.Value<string>("introId");
            FadeStart = definition.Value<float>("fadeStart");
            FadeDuration = definition.Value<float>("fadeDuration");
            ExitFade = definition.Value<float>("exitFade");
            AmbienceGain = definition["mix"]?.Value<float>("ambience") ?? .45f;
            HostGain = definition["mix"]?.Value<float>("host") ?? 1;
            var authored = new List<float>();
            var actual = new List<float>();
            float cursor = 0;
            var known = true;
            foreach (JObject scene in (JArray)definition["scenes"])
            {
                var start = Mathf.Max(cursor, scene.Value<float>("start"));
                cursor = start;
                authored.Add(scene.Value<float>("start"));
                actual.Add(start);
                SceneStart[scene.Value<string>("id")] = start;
                foreach (JObject line in (JArray)scene["lines"])
                {
                    var id = line.Value<string>("id");
                    var measured = durations(id);
                    if (!(measured > 0)) { known = false; measured = 0; }
                    var subtitle = line.Value<string>("subtitleText");
                    Lines.Add(new Speech
                    {
                        Id = id,
                        Caption = string.IsNullOrWhiteSpace(subtitle) ? line.Value<string>("text") : subtitle,
                        Start = cursor,
                        Duration = measured,
                        PauseAfter = line.Value<float>("pauseAfter"),
                    });
                    cursor += measured + line.Value<float>("pauseAfter");
                }
            }
            Duration = Mathf.Max(definition.Value<float>("targetDurationSeconds"), cursor + ExitFade);
            Fits = known && Duration <= definition.Value<float>("maximumDurationSeconds");
            foreach (JObject cue in (JArray)definition["cues"])
            {
                Cues.Add(new Cue
                {
                    Id = cue.Value<string>("id"),
                    File = cue.Value<string>("file"),
                    Category = cue.Value<string>("category"),
                    Gain = cue.Value<float>("gain"),
                    At = Remap(cue.Value<float>("at"), authored, actual),
                    Until = cue["until"] != null ? Remap(cue.Value<float>("until"), authored, actual) : 0,
                });
            }
        }

        static float Remap(float at, List<float> authored, List<float> actual)
        {
            for (var i = authored.Count - 1; i >= 0; i--) if (at >= authored[i]) return actual[i] + at - authored[i];
            return at;
        }
    }

    /// <summary>
    /// BasementIntro + IntroSequenceController: darkness opens onto the cage basement, the host speaks every line
    /// with subtitles, then fades out and reports INTRO_READY once (or SKIP_INTRO). The server's IntroGate
    /// still decides when the first question starts.
    /// </summary>
    public sealed class IntroPlayer
    {
        // Java intro SFX are referenced by path; map the ones shipped in this project to catalog keys.
        static readonly Dictionary<string, string> CueFiles = new Dictionary<string, string>
        {
            ["sounds/cages/runtime/chain_cage.wav"] = "Audio/Cages/chain_cage",
            ["sounds/questions/clock_ticks.wav"] = "Audio/Quiz/clock_ticks",
        };

        enum State { Playing, Exiting, Done }

        readonly RectTransform root;
        readonly Image darkness, plate, accent;
        readonly Text subtitle, notice;
        readonly Button skip;
        readonly IntroSchedule schedule;
        readonly SfxBank sfx;
        readonly HostVoice voice;
        readonly bool[] cueStarted, cueStopped;
        readonly Action<string> report;
        State state = State.Playing;
        float age, exitStarted, retry, subtitleStart = -100, subtitleEnd = -100, finalStart = 68;
        int nextLine;
        bool speaking, paused, skipped;
        float lastSpeechEnd = -100;
        int generation;

        public string GameId { get; }
        public bool Finished => state == State.Done;
        public bool Paused => paused;
        public bool AmbienceWanted => state != State.Done && age >= schedule.FadeStart;
        public float AmbienceGain => schedule.AmbienceGain;

        public static IntroSchedule LoadSchedule(ArtCatalog art)
        {
            var definition = JObject.Parse(art.Text("Data/intro_ru.json").text);
            return new IntroSchedule(definition, id => HostVoice.Current?.Duration("intro", id) ?? 0);
        }

        public IntroPlayer(Transform parent, ArtCatalog art, IntroSchedule schedule, string gameId, float elapsedSeconds, Action<string> report)
        {
            this.schedule = schedule;
            this.report = report;
            GameId = gameId;
            voice = HostVoice.Current;
            if (voice != null) voice.ScopeGain = schedule.HostGain;
            sfx = new SfxBank(parent, art, "IntroSfx");
            cueStarted = new bool[schedule.Cues.Count];
            cueStopped = new bool[schedule.Cues.Count];

            root = UiKit.Stretch(UiKit.Rect("Intro", parent));
            var design = UiKit.DesignFrame(root, 1920, 1080);
            accent = UiKit.Picture(design, "Accent", null, 0, 0, 1920, 1080);
            accent.color = new Color(1, .85f, .65f, 0);
            darkness = UiKit.Stretch(UiKit.Rect("Darkness", root)).gameObject.AddComponent<Image>();
            darkness.color = Color.black;
            darkness.raycastTarget = false;
            darkness.transform.SetSiblingIndex(0);
            plate = UiKit.Picture(design, "SubtitlePlate", null, 260, 112, 1400, 126);
            plate.color = new Color(.012f, .009f, .016f, 0);
            var body = art.Font("Fonts/NotoSans-SemiCondensedSemiBold");
            subtitle = UiKit.Label(design, "", 34, GenerationCodeView.Hex("EEE7DB"), TextAnchor.MiddleCenter);
            subtitle.font = body;
            subtitle.gameObject.AddComponent<Shadow>().effectColor = Color.black;
            UiKit.Place(subtitle.rectTransform, 310, 124, 1300, 110);
            notice = UiKit.Label(design, "", 18, GenerationCodeView.Hex("C8B7A8"), TextAnchor.MiddleLeft);
            notice.font = body;
            UiKit.Place(notice.rectTransform, 45, 34, 1300, 60);
            skip = UiKit.Button(design, "ПРОПУСТИТЬ ИНТРО »", Skip, height: 60, fontSize: 24);
            skip.gameObject.AddComponent<UiSoundHooks>();
            UiKit.Place((RectTransform)skip.transform, 1570, 32, 310, 60);
            foreach (var line in schedule.Lines) if (line.Id == "intro_05_01") finalStart = line.Start;

            age = Mathf.Max(0, elapsedSeconds);
            // Restore (reconnect / late snapshot): skip lines and cues that already ended.
            while (nextLine < schedule.Lines.Count && schedule.Lines[nextLine].End <= age) nextLine++;
            for (var i = 0; i < schedule.Cues.Count; i++)
            {
                var cue = schedule.Cues[i];
                if (cue.At < age && (cue.Until == 0 || cue.Until <= age)) cueStarted[i] = cueStopped[i] = true;
            }
            if (!schedule.Fits) notice.text = "ОЗВУЧКА ИНТРО НЕДОСТУПНА — НАЖМИТЕ «ПРОПУСТИТЬ»";
        }

        public void Skip()
        {
            if (state == State.Done) return;
            skipped = true;
            Complete();
        }

        public void Pause(bool value)
        {
            if (paused == value || state == State.Done) return;
            paused = value;
            voice?.Pause(value);
            report(value ? "INTRO_PAUSE" : "INTRO_RESUME");
        }

        public void Update(float delta)
        {
            if (state == State.Done)
            {
                // Resend the single completion signal until the server leaves STARTING (owner disposes us then).
                retry -= delta;
                if (retry <= 0) { retry = .75f; report(skipped ? "SKIP_INTRO" : "INTRO_READY"); }
                return;
            }
            if (paused) return;
            age += delta;
            voice?.Update();

            for (var i = 0; i < schedule.Cues.Count; i++)
            {
                var cue = schedule.Cues[i];
                if (!cueStarted[i] && age >= cue.At)
                {
                    cueStarted[i] = true;
                    if (CueFiles.TryGetValue(cue.File, out var key))
                        sfx.Play(key, cue.Gain * (cue.Category == "MUSIC" ? .45f : .35f), 0, cue.Until > cue.At);
                }
                if (cueStarted[i] && !cueStopped[i] && cue.Until > 0 && age >= cue.Until)
                {
                    cueStopped[i] = true;
                    if (CueFiles.TryGetValue(cue.File, out var key)) sfx.Stop(key);
                }
            }

            if (state == State.Playing)
            {
                if (!speaking && nextLine < schedule.Lines.Count)
                {
                    var line = schedule.Lines[nextLine];
                    var pause = nextLine == 0 ? 0 : schedule.Lines[nextLine - 1].PauseAfter;
                    if (age >= line.Start && age >= lastSpeechEnd + pause)
                    {
                        speaking = true;
                        nextLine++;
                        var token = generation;
                        var offset = lastSpeechEnd < 0 ? Mathf.Max(0, age - line.Start) : 0;
                        subtitle.text = line.Caption;
                        subtitleStart = age;
                        subtitleEnd = -100;
                        var started = voice != null && voice.Play("intro", line.Id, ok =>
                        {
                            if (token != generation) return;
                            speaking = false;
                            lastSpeechEnd = age;
                            subtitleEnd = age;
                        }, offset);
                        if (!started)
                        {
                            // No recording: keep the caption up for its measured slot, never stall.
                            speaking = false;
                            lastSpeechEnd = age + Mathf.Max(2.5f, line.Duration);
                            subtitleEnd = lastSpeechEnd;
                        }
                    }
                }
                if (!speaking && nextLine == schedule.Lines.Count && age >= schedule.Duration - schedule.ExitFade)
                {
                    state = State.Exiting;
                    exitStarted = age;
                }
            }
            else if (state == State.Exiting && age >= exitStarted + schedule.ExitFade) Complete();

            var fade = ArtButton.Smoother(Mathf.Clamp01((age - schedule.FadeStart) / schedule.FadeDuration));
            var exit = state == State.Exiting ? Mathf.Min(1, (age - exitStarted) / schedule.ExitFade) : 0;
            var opacity = Mathf.Max(1 - fade, exit);
            if (age >= 9 && exit == 0) opacity = Mathf.Max(opacity, .018f + .007f * Mathf.Sin(age * .65f));
            darkness.color = new Color(0, 0, 0, opacity);
            accent.color = new Color(1, .85f, .65f, age >= finalStart ? .025f * Mathf.Exp(-(age - finalStart) * 2) : 0);
            var alpha = subtitleStart < 0 ? 0 : Mathf.Min(1, (age - subtitleStart) / .18f);
            if (subtitleEnd >= 0) alpha *= Mathf.Clamp01(1 - (age - subtitleEnd) / .18f);
            subtitle.color = new Color(subtitle.color.r, subtitle.color.g, subtitle.color.b, alpha);
            plate.color = new Color(.012f, .009f, .016f, alpha * .65f);
        }

        void Complete()
        {
            generation++;
            state = State.Done;
            speaking = false;
            voice?.Stop();
            if (voice != null) voice.ScopeGain = 1;
            sfx.StopAll();
            darkness.color = Color.black;
            subtitle.color = Color.clear;
            plate.color = Color.clear;
            skip.interactable = false;
            retry = 0;
        }

        public void Dispose()
        {
            generation++;
            if (state != State.Done) { voice?.Stop(); if (voice != null) voice.ScopeGain = 1; }
            sfx.Dispose();
            UiKit.Dispose(root.gameObject);
        }
    }
}

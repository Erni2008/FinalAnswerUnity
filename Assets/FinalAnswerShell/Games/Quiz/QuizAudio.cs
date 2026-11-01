using System.Collections.Generic;
using FinalAnswer.UI;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FinalAnswer.Quiz
{
    /// <summary>
    /// Port of QuestionAudio + the question part of HostVoiceManager: two music beds alternating per question
    /// (0.65 s in / 0.55 s out), clock ticks positioned at the answer elapsed time, one door-lock when the server
    /// closes answers, and the pre-recorded host line for the question (audio/host/ru/manifest.json).
    /// </summary>
    public sealed class QuizAudio
    {
        const float MusicLevel = .35f, TickLevel = .55f, LockLevel = .85f, VoiceLevel = 1f;

        readonly GameObject host;
        readonly AudioSource[] music = new AudioSource[2];
        readonly AudioSource ticks, lockSource, voice;
        readonly Dictionary<string, (AudioClip clip, float duration)> voices = new Dictionary<string, (AudioClip, float)>();
        string question = "";
        int requested = -1, current = -1, lastSelected = -1;
        float fade, duck = 1, duckRemaining;
        bool wanted, ticking, answerSeen, locked;

        public QuizAudio(Transform parent, ArtCatalog art)
        {
            host = new GameObject("QuizAudio");
            host.transform.SetParent(parent, false);
            for (var i = 0; i < 2; i++)
            {
                music[i] = Source(art.Clip("Audio/Quiz/music" + (i + 1)), true);
            }
            ticks = Source(art.Clip("Audio/Quiz/clock_ticks"), true);
            lockSource = Source(art.Clip("Audio/Cages/door_lock_done"), false);
            voice = Source(null, false);
            LoadVoices(art);
        }

        AudioSource Source(AudioClip clip, bool loop)
        {
            var source = host.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = loop;
            source.playOnAwake = false;
            source.volume = 0;
            return source;
        }

        void LoadVoices(ArtCatalog art)
        {
            try
            {
                var manifest = JObject.Parse(art.Text("Audio/Host/manifest.json").text);
                if (!(manifest["questions"] is JObject questions)) return;
                foreach (var pair in questions)
                {
                    if (!(pair.Value is JObject entry) || entry.Value<string>("status") != "ready") continue;
                    var file = entry.Value<string>("file") ?? "";
                    if (!file.StartsWith("questions/") || !file.EndsWith(".ogg")) continue;
                    try { voices[pair.Key] = (art.Clip("Audio/Host/" + file.Substring(0, file.Length - 4)), entry.Value<float>("duration")); }
                    catch (KeyNotFoundException) { /* optional line missing from this build */ }
                }
            }
            catch (System.Exception error) { Debug.LogWarning("[Quiz] host voice manifest: " + error.Message); }
        }

        static float Master => ShellAudio.Current == null || ShellAudio.Current.Muted ? 0 : ShellAudio.Current.Master;
        static float Sfx => ShellAudio.Current?.Sfx ?? 1;

        /// <summary>Recorded host line duration for the question, or 0 when none exists (typing-only presentation).</summary>
        public float VoiceDuration(string questionId) => voices.TryGetValue(questionId, out var v) ? v.duration : 0;

        /// <summary>Starts a question: alternates the music bed and speaks the host line once per question key.</summary>
        public void Question(string key, string questionId, int number, bool speak)
        {
            if (question == key) return;
            question = key;
            requested = lastSelected < 0 ? ((number - 1) % 2 + 2) % 2 : 1 - lastSelected;
            lastSelected = requested;
            answerSeen = locked = false;
            wanted = true;
            voice.Stop();
            if (speak && voices.TryGetValue(questionId, out var line) && line.clip != null)
            {
                voice.clip = line.clip;
                voice.volume = VoiceLevel * Master * Sfx;
                voice.Play();
            }
        }

        /// <summary>lockedNow must come from the server closing answers, never from one player's submission.</summary>
        public void Phase(bool answering, bool lockedNow, double answerElapsed, double remaining)
        {
            if (answering && !locked) answerSeen = true;
            var wantTicks = answering && !locked && remaining > 0;
            if (lockedNow)
            {
                wantTicks = false;
                wanted = false;
                if (!locked && answerSeen) PlayLock();
                locked = true;
                voice.Stop();
            }
            if (wantTicks && !ticking && ticks.clip != null)
            {
                ticks.Play();
                ticks.time = (float)(answerElapsed % ticks.clip.length);
                ticking = true;
            }
            else if (!wantTicks && ticking)
            {
                ticks.Stop();
                ticking = false;
            }
        }

        void PlayLock()
        {
            if (lockSource.clip == null) return;
            duckRemaining = .75f; // important cue: duck the music bed
            lockSource.volume = LockLevel * Master * Sfx;
            lockSource.Play();
        }

        public void Update(float dt)
        {
            duckRemaining = Mathf.Max(0, duckRemaining - dt);
            var duckTarget = duckRemaining > 0 ? .48f : 1;
            duck += (duckTarget - duck) * (1 - Mathf.Exp(-dt / (duckTarget < duck ? .04f : .4f)));
            var target = wanted ? requested : -1;
            if (current >= 0 && current != target)
            {
                fade = Mathf.Max(0, fade - dt / .55f);
                if (fade == 0) { music[current].Stop(); current = -1; }
            }
            if (current < 0 && target >= 0) { current = target; fade = 0; }
            if (current >= 0)
            {
                if (current == target) fade = Mathf.Min(1, fade + dt / .65f);
                var gain = fade * MusicLevel * Master * duck;
                music[current].volume = gain;
                if (gain > 0 && !music[current].isPlaying) music[current].Play();
            }
            if (ticking) ticks.volume = TickLevel * Master * Sfx;
        }

        public void Leave()
        {
            wanted = false;
            ticking = false;
            ticks.Stop();
            voice.Stop();
        }

        public void Dispose()
        {
            if (host != null) UiKit.Dispose(host);
        }
    }
}

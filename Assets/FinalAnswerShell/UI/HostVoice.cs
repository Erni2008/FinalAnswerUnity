using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FinalAnswer.UI
{
    /// <summary>
    /// Port of HostVoiceManager: the pre-recorded host lines from audio/host/ru/manifest.json (sections
    /// questions / events / intro, status "ready" only), played on one voice channel. Lines never advance an
    /// authoritative phase; callers get a completion callback (true = played to the end).
    /// </summary>
    public sealed class HostVoice
    {
        public static HostVoice Current { get; private set; }

        readonly AudioSource source;
        readonly Dictionary<string, (AudioClip clip, float duration)> lines = new Dictionary<string, (AudioClip, float)>();
        Action<bool> completion;
        string playing = "";
        bool paused;
        public float ScopeGain = 1;

        public HostVoice(GameObject host, ArtCatalog art)
        {
            source = host.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.priority = 0;
            Load(art);
            Current = this;
        }

        void Load(ArtCatalog art)
        {
            JObject manifest;
            try { manifest = JObject.Parse(art.Text("Audio/Host/manifest.json").text); }
            catch (Exception error) { Debug.LogWarning("[HostVoice] manifest: " + error.Message); return; }
            foreach (var section in new[] { "questions", "events", "intro" })
            {
                if (!(manifest[section] is JObject entries)) continue;
                foreach (var pair in entries)
                {
                    if (!(pair.Value is JObject entry) || entry.Value<string>("status") != "ready") continue;
                    var file = entry.Value<string>("file") ?? "";
                    if (!System.Text.RegularExpressions.Regex.IsMatch(file, "^(questions|events|intro)/[A-Za-z0-9_-]+\\.ogg$")) continue;
                    try { lines[section + ":" + pair.Key] = (art.Clip("Audio/Host/" + file.Substring(0, file.Length - 4)), entry.Value<float>("duration")); }
                    catch (KeyNotFoundException) { /* line not shipped in this build */ }
                }
            }
        }

        public bool Busy => source.isPlaying || paused && playing.Length > 0;
        public string Playing => playing;
        public int Count => lines.Count;

        public bool Has(string section, string id) => lines.ContainsKey(section + ":" + id);

        /// <summary>Measured duration (seconds) of a ready line, or 0.</summary>
        public float Duration(string section, string id) => lines.TryGetValue(section + ":" + id, out var line) ? line.duration : 0;

        public IEnumerable<string> All => lines.Keys;

        /// <summary>Plays a line, replacing whatever is speaking (that one completes with false).</summary>
        public bool Play(string section, string id, Action<bool> done = null, float offset = 0)
        {
            if (!lines.TryGetValue(section + ":" + id, out var line) || line.clip == null) { done?.Invoke(false); return false; }
            Stop();
            playing = section + ":" + id;
            completion = done;
            source.clip = line.clip;
            source.time = Mathf.Clamp(offset, 0, Mathf.Max(0, line.clip.length - .05f));
            source.volume = Gain;
            source.Play();
            return true;
        }

        public void Stop()
        {
            if (playing.Length == 0) return;
            source.Stop();
            Finish(false);
        }

        public void Pause(bool value)
        {
            if (paused == value) return;
            paused = value;
            if (value) source.Pause(); else source.UnPause();
        }

        float Gain => ShellAudio.Current == null || ShellAudio.Current.Muted ? 0 : ShellAudio.Current.Master * ShellAudio.Current.Sfx * ScopeGain;

        public void Update()
        {
            if (playing.Length == 0) return;
            source.volume = Gain;
            if (!paused && !source.isPlaying) Finish(true);
        }

        void Finish(bool completed)
        {
            playing = "";
            var done = completion;
            completion = null;
            done?.Invoke(completed);
        }
    }

    /// <summary>One-shot SFX by catalog key, with per-key restart (no stacked tails) — for module sound managers.</summary>
    public sealed class SfxBank
    {
        readonly GameObject host;
        readonly ArtCatalog art;
        readonly Dictionary<string, AudioSource> voices = new Dictionary<string, AudioSource>();

        public SfxBank(Transform parent, ArtCatalog art, string name)
        {
            this.art = art;
            host = new GameObject(name);
            host.transform.SetParent(parent, false);
        }

        public void Play(string key, float gain, float pan = 0, bool loop = false)
        {
            if (!voices.TryGetValue(key, out var source))
            {
                AudioClip clip;
                try { clip = art.Clip(key); }
                catch (KeyNotFoundException) { Debug.LogWarning("[Sfx] missing " + key); return; }
                source = host.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.clip = clip;
                voices[key] = source;
            }
            var mix = ShellAudio.Current;
            source.volume = Mathf.Clamp01(gain) * (mix == null || mix.Muted ? 0 : mix.Master * mix.Sfx);
            source.panStereo = Mathf.Clamp(pan, -1, 1);
            source.loop = loop;
            source.Stop();
            source.Play();
        }

        public void Stop(string key)
        {
            if (voices.TryGetValue(key, out var source)) source.Stop();
        }

        public bool IsPlaying(string key) => voices.TryGetValue(key, out var source) && source.isPlaying;

        public void SetVolume(string key, float gain)
        {
            var mix = ShellAudio.Current;
            if (voices.TryGetValue(key, out var source)) source.volume = Mathf.Clamp01(gain) * (mix == null || mix.Muted ? 0 : mix.Master * mix.Sfx);
        }

        public void StopAll()
        {
            foreach (var source in voices.Values) if (source != null) source.Stop();
        }

        public void Dispose()
        {
            if (host != null) UiKit.Dispose(host);
        }
    }
}

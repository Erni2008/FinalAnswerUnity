using System.Collections.Generic;
using UnityEngine;

namespace FinalAnswer.UI
{
    /// <summary>
    /// Port of core/audio/GameAudio for the shell: UI hover/press, cage gate/lock/chains and the basement
    /// ambience, with the same levels, cooldowns and chain-motion detection. One instance per app.
    /// </summary>
    public sealed class ShellAudio
    {
        public enum Cue { Hover, Press, Gate, GateLock, Chain1, Chain2 }

        // GameAudio.Levels
        const float HoverLevel = .35f, PressLevel = .55f, GateLevel = .75f, LockLevel = .75f, ChainLevel = .55f, AmbienceLevel = .20f;
        const int VoiceCount = 12;

        public static ShellAudio Current { get; private set; }

        public float Master = 1, Sfx = 1, Ui = 1, Cages = 1, Environment = 1;
        public bool Muted;

        readonly Dictionary<Cue, AudioClip> clips = new Dictionary<Cue, AudioClip>();
        readonly AudioSource[] voices = new AudioSource[VoiceCount];
        readonly Cue[] voiceCue = new Cue[VoiceCount];
        readonly AudioSource ambience;
        readonly float[] gateCooldown = new float[8], cageCooldown = new float[8], angle = new float[8], velocity = new float[8];
        readonly bool[] observed = new bool[8], moving = new bool[8];
        float hoverCooldown, pressCooldown, ambienceGain;
        int chainVariant, nextVoice;
        bool ambienceWanted;

        public ShellAudio(GameObject host, ArtCatalog art)
        {
            clips[Cue.Hover] = art.Clip("Audio/Ui/hover_button");
            clips[Cue.Press] = art.Clip("Audio/Ui/button_press");
            clips[Cue.Gate] = art.Clip("Audio/Cages/gate_closes");
            clips[Cue.GateLock] = art.Clip("Audio/Cages/door_lock_done");
            clips[Cue.Chain1] = art.Clip("Audio/Cages/chain_cage");
            clips[Cue.Chain2] = art.Clip("Audio/Cages/chain_cage2");
            for (var i = 0; i < voices.Length; i++)
            {
                voices[i] = host.AddComponent<AudioSource>();
                voices[i].playOnAwake = false;
            }
            ambience = host.AddComponent<AudioSource>();
            ambience.clip = art.Clip("Audio/Environment/creepy_basement");
            ambience.loop = true;
            ambience.playOnAwake = false;
            ambience.volume = 0;
            Current = this;
        }

        public IEnumerable<Cue> AllCues => clips.Keys;

        public void Hover()
        {
            if (hoverCooldown > 0) return;
            hoverCooldown = .085f;
            Play(Cue.Hover, 1, 0);
        }

        public void Press()
        {
            if (pressCooldown > 0) return;
            pressCooldown = .025f;
            Play(Cue.Press, 1, 0);
        }

        public void Gate(int slot, float pan)
        {
            if (slot < 0 || slot >= 8 || gateCooldown[slot] > 0) return;
            gateCooldown[slot] = .65f;
            Play(Cue.Gate, 1, pan);
        }

        /// <summary>One full key recording covers overlapping cage locks without stacking identical tails.</summary>
        public void Lock(int slot, float pan)
        {
            if (slot < 0 || slot >= 8) return;
            for (var i = 0; i < voices.Length; i++) if (voices[i].isPlaying && voiceCue[i] == Cue.GateLock) return;
            Play(Cue.GateLock, 1, pan);
        }

        /// <summary>Observes the rendered cage angle (degrees), like GameAudio.cageMotion.</summary>
        public void CageMotion(int slot, bool occupied, float rotation, float dt, float pan)
        {
            if (slot < 0 || slot >= 8 || float.IsNaN(rotation) || dt <= 0) return;
            if (!occupied) { observed[slot] = moving[slot] = false; angle[slot] = velocity[slot] = 0; return; }
            if (!observed[slot]) { observed[slot] = true; angle[slot] = rotation; return; }
            float speed = (rotation - angle[slot]) / dt, energy = Mathf.Abs(speed), previous = Mathf.Abs(velocity[slot]);
            bool noticeable = energy > .5f, reverse = speed * velocity[slot] < 0 && Mathf.Max(energy, previous) > 1.5f;
            bool settling = moving[slot] && energy < .5f && previous > .8f;
            if (cageCooldown[slot] <= 0 && (noticeable && !moving[slot] || reverse || settling))
            {
                cageCooldown[slot] = .45f;
                chainVariant = 1 - chainVariant;
                Play(chainVariant == 0 ? Cue.Chain1 : Cue.Chain2, Mathf.Min(1, .18f + Mathf.Max(energy, previous) / 14), pan);
            }
            moving[slot] = noticeable;
            angle[slot] = rotation;
            velocity[slot] = speed;
        }

        /// <summary>Basement ambience while the cage lobby (or a room scene) is visible.</summary>
        public void Room(bool active) => ambienceWanted = active;

        public void Play(Cue cue, float gain, float pan)
        {
            if (Muted || !clips.TryGetValue(cue, out var clip) || clip == null) return;
            var source = voices[nextVoice];
            voiceCue[nextVoice] = cue;
            nextVoice = (nextVoice + 1) % voices.Length;
            source.clip = clip;
            source.panStereo = Mathf.Clamp(pan, -1, 1);
            source.volume = Mathf.Clamp01(gain) * Level(cue);
            source.Play();
        }

        public void Update(float dt)
        {
            hoverCooldown = Mathf.Max(0, hoverCooldown - dt);
            pressCooldown = Mathf.Max(0, pressCooldown - dt);
            for (var i = 0; i < 8; i++)
            {
                cageCooldown[i] = Mathf.Max(0, cageCooldown[i] - dt);
                gateCooldown[i] = Mathf.Max(0, gateCooldown[i] - dt);
            }
            var target = ambienceWanted && !Muted ? AmbienceLevel * Master * Environment : 0;
            ambienceGain = Mathf.MoveTowards(ambienceGain, target, dt * .25f);
            ambience.volume = ambienceGain;
            if (ambienceGain > 0 && !ambience.isPlaying) ambience.Play();
            else if (ambienceGain <= 0 && ambience.isPlaying) ambience.Stop();
        }

        float Level(Cue cue)
        {
            switch (cue)
            {
                case Cue.Hover: return HoverLevel * Master * Sfx * Ui;
                case Cue.Press: return PressLevel * Master * Sfx * Ui;
                case Cue.Gate: return GateLevel * Master * Sfx * Cages;
                case Cue.GateLock: return LockLevel * Master * Sfx * Cages;
                default: return ChainLevel * Master * Sfx * Cages;
            }
        }
    }
}

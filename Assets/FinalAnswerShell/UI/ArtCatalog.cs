using System;
using System.Collections.Generic;
using UnityEngine;

namespace FinalAnswer.UI
{
    /// <summary>
    /// Serialized references to every shell asset (Art/, Audio/, Fonts/), keyed by path relative to
    /// Assets/FinalAnswerShell without extension, e.g. "Art/Menu/Buttons/play_up". Filled by
    /// FinalAnswer → Configure Project; runtime never searches folders or uses Resources.
    /// </summary>
    [CreateAssetMenu(menuName = "Final Answer Shell/Art Catalog")]
    public sealed class ArtCatalog : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public string key;
            public UnityEngine.Object asset;
        }

        [SerializeField] List<Entry> entries = new List<Entry>();

        Dictionary<string, UnityEngine.Object> index;
        readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>();

        public static ArtCatalog Active { get; set; }

        public void SetEntries(List<Entry> next)
        {
            entries = next;
            index = null;
            sprites.Clear();
        }

        public IReadOnlyList<Entry> Entries => entries;

        public T Get<T>(string key) where T : UnityEngine.Object
        {
            if (index == null)
            {
                index = new Dictionary<string, UnityEngine.Object>(entries.Count);
                foreach (var entry in entries) if (entry.asset != null) index[entry.key] = entry.asset;
            }
            if (index.TryGetValue(key, out var found) && found is T typed) return typed;
            throw new KeyNotFoundException("Art catalog has no " + typeof(T).Name + " '" + key + "'. Run Final Answer → Configure Project.");
        }

        public Texture2D Texture(string key) => Get<Texture2D>(key);
        public AudioClip Clip(string key) => Get<AudioClip>(key);
        public Font Font(string key) => Get<Font>(key);
        public TextAsset Text(string key) => Get<TextAsset>(key);

        /// <summary>Whole texture as a sprite (cached).</summary>
        public Sprite Sprite(string key) => Crop(key, -1, -1, -1, -1);

        /// <summary>
        /// Sprite cropped with Java's RuntimeTextures.region coordinates (top-left origin, source pixels),
        /// so the measured libGDX crops are reused verbatim.
        /// </summary>
        public Sprite Crop(string key, int x, int y, int width, int height)
        {
            var cacheKey = key + "#" + x + "," + y + "," + width + "," + height;
            if (sprites.TryGetValue(cacheKey, out var cached) && cached != null) return cached;
            var texture = Texture(key);
            var rect = x < 0
                ? new Rect(0, 0, texture.width, texture.height)
                : new Rect(x, texture.height - y - height, width, height);
            var sprite = UnityEngine.Sprite.Create(texture, rect, new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect);
            sprite.name = cacheKey;
            sprites[cacheKey] = sprite;
            return sprite;
        }

        /// <summary>Frame sprites of a baked flipbook sheet (row-major, top-left first).</summary>
        public Sprite[] Frames(string key, int frameWidth, int frameHeight, int columns, int count)
        {
            var texture = Texture(key);
            var frames = new Sprite[count];
            for (var i = 0; i < count; i++)
            {
                var x = i % columns * frameWidth;
                var top = i / columns * frameHeight;
                frames[i] = UnityEngine.Sprite.Create(texture, new Rect(x, texture.height - top - frameHeight, frameWidth, frameHeight),
                    new Vector2(0.5f, 0.5f), 100, 0, SpriteMeshType.FullRect);
            }
            return frames;
        }
    }
}

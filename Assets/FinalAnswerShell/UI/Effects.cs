using UnityEngine;
using UnityEngine.UI;

namespace FinalAnswer.UI
{
    /// <summary>Shared procedural textures/materials for the ported libGDX effects.</summary>
    public static class EffectAssets
    {
        static Texture2D falloff, vignette;
        static Material additive;
        static Sprite falloffSprite, vignetteSprite;

        /// <summary>TableCandle's 256² radial falloff: alpha = (1 - r)³.</summary>
        public static Sprite Falloff
        {
            get
            {
                if (falloffSprite != null) return falloffSprite;
                falloff = new Texture2D(256, 256, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "Falloff" };
                var pixels = new Color32[256 * 256];
                for (var y = 0; y < 256; y++)
                for (var x = 0; x < 256; x++)
                {
                    float dx = (x - 127.5f) / 127.5f, dy = (y - 127.5f) / 127.5f;
                    var a = Mathf.Max(0, 1 - Mathf.Sqrt(dx * dx + dy * dy));
                    pixels[y * 256 + x] = new Color32(255, 255, 255, (byte)(a * a * a * 255));
                }
                falloff.SetPixels32(pixels);
                falloff.Apply(false, true);
                return falloffSprite = Sprite.Create(falloff, new Rect(0, 0, 256, 256), new Vector2(.5f, .5f));
            }
        }

        /// <summary>CyclingRoomBackground vignette: 9 % darkening towards the corners.</summary>
        public static Sprite Vignette
        {
            get
            {
                if (vignetteSprite != null) return vignetteSprite;
                vignette = new Texture2D(128, 128, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "Vignette" };
                var pixels = new Color32[128 * 128];
                for (var y = 0; y < 128; y++)
                for (var x = 0; x < 128; x++)
                {
                    float ex = (x / 127f - .5f) * 2, ey = (y / 127f - .5f) * 2;
                    var t = Mathf.Clamp01((Mathf.Sqrt(ex * ex + ey * ey) - .35f) / 1.15f);
                    pixels[y * 128 + x] = new Color32(0, 0, 0, (byte)(.09f * t * t * (3 - 2 * t) * 255));
                }
                vignette.SetPixels32(pixels);
                vignette.Apply(false, true);
                return vignetteSprite = Sprite.Create(vignette, new Rect(0, 0, 128, 128), new Vector2(.5f, .5f));
            }
        }

        static Sprite diskSprite;

        /// <summary>Antialiased solid disk (radial-filled for the clock's remaining-time sector).</summary>
        public static Sprite Disk
        {
            get
            {
                if (diskSprite != null) return diskSprite;
                const int size = 256;
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "Disk" };
                var pixels = new Color32[size * size];
                for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    float dx = x + .5f - size / 2f, dy = y + .5f - size / 2f;
                    var a = Mathf.Clamp01(size / 2f - Mathf.Sqrt(dx * dx + dy * dy));
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255));
                }
                texture.SetPixels32(pixels);
                texture.Apply(false, true);
                return diskSprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f));
            }
        }

        /// <summary>Solid white sprite for fills/gradients drawn with vertex colors.</summary>
        public static Sprite White => whiteSprite != null ? whiteSprite : (whiteSprite = MakeWhite());
        static Sprite whiteSprite;
        static Sprite MakeWhite()
        {
            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = "White" };
            var pixels = new Color32[16];
            for (var i = 0; i < 16; i++) pixels[i] = new Color32(255, 255, 255, 255);
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(.5f, .5f));
        }

        /// <summary>SRC_ALPHA, ONE blending for glows. Shader comes from the art catalog so it is included in builds.</summary>
        public static Material Additive
        {
            get
            {
                if (additive != null) return additive;
                additive = new Material(ArtCatalog.Active.Get<Shader>("UI/Shaders/UIAdditive")) { name = "UI Additive" };
                return additive;
            }
        }
    }

    /// <summary>Plays sprite frames in order; Loop or PingPong after an optional one-shot intro.</summary>
    public sealed class Flipbook : MonoBehaviour
    {
        Image image;
        Sprite[] frames;
        float fps = 24;
        int introFrames;
        float time;

        public float Time { get => time; set => time = value; }
        public bool PingPong { get; set; }
        public bool Paused { get; set; }

        public static Flipbook Create(Transform parent, string name, Sprite[] frames, float fps, int introFrames = 0)
        {
            var rect = UiKit.Rect(name, parent);
            var book = rect.gameObject.AddComponent<Flipbook>();
            book.image = rect.gameObject.AddComponent<Image>();
            book.image.raycastTarget = false;
            book.frames = frames;
            book.fps = fps;
            book.introFrames = introFrames;
            book.image.sprite = frames[0];
            return book;
        }

        public Image Image => image;

        void Update()
        {
            if (!Paused) time += UnityEngine.Time.unscaledDeltaTime;
            image.sprite = frames[FrameAt(time)];
        }

        public int FrameAt(float seconds)
        {
            var frame = Mathf.Max(0, Mathf.FloorToInt(seconds * fps));
            if (frame < introFrames) return frame;
            var loop = frames.Length - introFrames;
            if (loop <= 0) return frames.Length - 1;
            var k = frame - introFrames;
            if (!PingPong) return introFrames + k % loop;
            var period = Mathf.Max(1, loop * 2 - 2);
            k %= period;
            return introFrames + (k < loop ? k : period - k);
        }
    }

    /// <summary>
    /// CyclingRoomBackground / GenerationCodeBackground: hold, then blend to the next lighting variant without
    /// going through black. The project renders in Linear space, so the alpha blend is already in linear light.
    /// </summary>
    public sealed class CrossfadeBackground : MonoBehaviour
    {
        Image current, next;
        Sprite[] variants;
        float hold, transition, elapsed;
        public bool ReducedMotion { get; set; }

        public static CrossfadeBackground Create(Transform parent, Sprite[] variants, float hold, float transition, bool cover, bool vignette)
        {
            var root = UiKit.Stretch(UiKit.Rect("Background", parent));
            var bg = root.gameObject.AddComponent<CrossfadeBackground>();
            bg.variants = variants;
            bg.hold = hold;
            bg.transition = transition;
            bg.current = cover ? UiKit.Cover(root, "Current", variants[0]) : Stretched(root, "Current", variants[0]);
            bg.next = cover ? UiKit.Cover(root, "Next", variants[1 % variants.Length]) : Stretched(root, "Next", variants[1 % variants.Length]);
            if (vignette)
            {
                var v = UiKit.Stretch(UiKit.Rect("Vignette", root)).gameObject.AddComponent<Image>();
                v.sprite = EffectAssets.Vignette;
                v.raycastTarget = false;
            }
            return bg;
        }

        static Image Stretched(Transform parent, string name, Sprite sprite)
        {
            var image = UiKit.Stretch(UiKit.Rect(name, parent)).gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = false;
            return image;
        }

        void Update()
        {
            var step = hold + transition;
            if (!ReducedMotion) elapsed = (elapsed + Mathf.Clamp(UnityEngine.Time.unscaledDeltaTime, 0, .1f)) % (step * variants.Length);
            var index = (int)(elapsed / step);
            var phase = elapsed - index * step;
            var blend = ArtButton.Smoother(Mathf.Clamp01((phase - hold) / transition));
            current.sprite = variants[index];
            next.sprite = variants[(index + 1) % variants.Length];
            next.color = new Color(1, 1, 1, blend);
        }
    }

    /// <summary>TableCandle: 96-frame render at 24 fps, warm pool, contact shadows and flame glows anchored to the background.</summary>
    public sealed class CandleView : MonoBehaviour
    {
        static readonly float[] FootX = { .28f, .52f, .735f }, FootY = { .18f, .18f, .15f };
        static readonly float[] FlameX = { .285f, .525f, .74f }, FlameY = { .745f, .91f, .71f };

        RectTransform background;
        Image candle, pool, castShadow;
        readonly Image[] feet = new Image[3], outerGlow = new Image[3], innerGlow = new Image[3];
        Sprite[] frames;
        float time, animation;
        public bool ReducedMotion { get; set; }

        /// <summary>Background is the cover-fitted image rect; the candle follows its drawn size like the Java math.</summary>
        public static CandleView Create(RectTransform background, Sprite[] frames)
        {
            var root = UiKit.Stretch(UiKit.Rect("Candle", background));
            var view = root.gameObject.AddComponent<CandleView>();
            view.background = background;
            view.frames = frames;
            view.pool = view.Glow(root, true);
            view.castShadow = view.Glow(root, false);
            for (var i = 0; i < 3; i++) view.feet[i] = view.Glow(root, false);
            view.candle = UiKit.Rect("Frames", root).gameObject.AddComponent<Image>();
            view.candle.raycastTarget = false;
            for (var i = 0; i < 3; i++)
            {
                view.outerGlow[i] = view.Glow(root, true);
                view.innerGlow[i] = view.Glow(root, true);
            }
            return view;
        }

        Image Glow(Transform parent, bool additive)
        {
            var image = UiKit.Rect("Glow", parent).gameObject.AddComponent<Image>();
            image.sprite = EffectAssets.Falloff;
            image.raycastTarget = false;
            if (additive) image.material = EffectAssets.Additive;
            return image;
        }

        void LateUpdate()
        {
            var dt = ReducedMotion ? 0 : Mathf.Max(0, UnityEngine.Time.unscaledDeltaTime);
            time = (time + dt) % 1000f;
            animation = (animation + dt) % (96 / 24f);
            var size = background.rect.size;
            // Local coordinates with origin at the background's bottom-left corner.
            var unit = size.y / 945f;
            float baseX = size.x * .575f, baseY = size.y * .09f;
            var height = 288 * unit;
            float left = baseX - height / 2, bottom = baseY - height * .16f;
            var flicker = .94f + .035f * Mathf.Sin(time * 4.3f) + .025f * Mathf.Sin(time * 9.7f + .8f);

            Set(pool, baseX - 185 * unit, baseY - 48 * unit, 370 * unit, 105 * unit, new Color(1f, .32f, .075f, .30f * flicker));
            Set(castShadow, baseX - 12 * unit, baseY - 15 * unit, 140 * unit, 30 * unit, new Color(.025f, .008f, .02f, .42f));
            for (var i = 0; i < 3; i++)
                Set(feet[i], left + height * FootX[i] - 29 * unit, bottom + height * FootY[i] - 9 * unit, 58 * unit, 18 * unit, new Color(.015f, .008f, .018f, .7f));
            candle.sprite = frames[Mathf.Min(frames.Length - 1, (int)(animation * 24))];
            Set(candle, left, bottom, height, height, Color.white);
            for (var i = 0; i < 3; i++)
            {
                float x = left + height * FlameX[i], y = bottom + height * FlameY[i];
                var glow = flicker + .025f * Mathf.Sin(time * 7f + i * 2f);
                Set(outerGlow[i], x - 34 * unit, y - 44 * unit, 68 * unit, 88 * unit, new Color(1, .24f, .035f, .22f * glow));
                Set(innerGlow[i], x - 11 * unit, y - 20 * unit, 22 * unit, 40 * unit, new Color(1, .65f, .22f, .26f * glow));
            }
        }

        void Set(Image image, float x, float y, float w, float h, Color color)
        {
            var rect = image.rectTransform;
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(w, h);
            image.color = color;
        }
    }
}

using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FinalAnswer.UI
{
    public static class Theme
    {
        public static readonly Color Background = new Color32(0x0d, 0x0b, 0x0f, 0xff);
        public static readonly Color Panel = new Color32(0x1c, 0x17, 0x1d, 0xf2);
        public static readonly Color PanelBorder = new Color32(0x5a, 0x1e, 0x1e, 0xff);
        public static readonly Color Accent = new Color32(0xc8, 0x23, 0x23, 0xff);
        public static readonly Color AccentHover = new Color32(0xe0, 0x3a, 0x3a, 0xff);
        public static readonly Color Button = new Color32(0x2e, 0x26, 0x2f, 0xff);
        public static readonly Color ButtonHover = new Color32(0x45, 0x3a, 0x47, 0xff);
        public static readonly Color Disabled = new Color32(0x26, 0x22, 0x27, 0xff);
        public static readonly Color Text = new Color32(0xf2, 0xea, 0xdf, 0xff);
        public static readonly Color Muted = new Color32(0xa0, 0x95, 0x8c, 0xff);
        public static readonly Color Good = new Color32(0x5c, 0xc2, 0x6a, 0xff);
        public static readonly Color Warning = new Color32(0xf0, 0xb4, 0x3c, 0xff);
        public static readonly Color Bad = new Color32(0xe8, 0x50, 0x50, 0xff);
    }

    /// <summary>
    /// Builds the shell's uGUI hierarchy from code so screens stay diff-reviewable without scene YAML.
    /// One Canvas, one EventSystem (created by the app root).
    /// </summary>
    public static class UiKit
    {
        public static Font Font { get; set; }

        static Font ActiveFont => Font != null ? Font : (Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));

        public static Canvas CreateCanvas(string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static RectTransform Stretch(RectTransform rect, float padding = 0)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(padding, padding);
            rect.offsetMax = new Vector2(-padding, -padding);
            return rect;
        }

        public static RectTransform Centered(RectTransform rect, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = Vector2.zero;
            return rect;
        }

        public static Image Fill(Transform parent, string name, Color color)
        {
            var rect = Stretch(Rect(name, parent));
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            return image;
        }

        /// <summary>Panel with vertical layout; children size themselves via LayoutElement.</summary>
        public static RectTransform Panel(Transform parent, string name, Vector2 size, int padding = 36, float spacing = 18)
        {
            var rect = Centered(Rect(name, parent), size);
            rect.gameObject.AddComponent<Image>().color = Theme.Panel;
            rect.gameObject.AddComponent<Outline>().effectColor = Theme.PanelBorder;
            var layout = rect.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(padding, padding, padding, padding);
            layout.spacing = spacing;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            return rect;
        }

        public static RectTransform Row(Transform parent, string name, float height, float spacing = 14)
        {
            var rect = Rect(name, parent);
            var layout = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            Size(rect.gameObject, height: height);
            return rect;
        }

        public static RectTransform Column(Transform parent, string name, float spacing = 10)
        {
            var rect = Rect(name, parent);
            var layout = rect.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            return rect;
        }

        public static LayoutElement Size(GameObject go, float width = -1, float height = -1, float flexibleWidth = -1, float flexibleHeight = -1)
        {
            var element = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
            if (width >= 0) element.preferredWidth = width;
            if (height >= 0) { element.preferredHeight = height; element.minHeight = height; }
            if (flexibleWidth >= 0) element.flexibleWidth = flexibleWidth;
            if (flexibleHeight >= 0) element.flexibleHeight = flexibleHeight;
            return element;
        }

        public static Text Label(Transform parent, string text, int size = 30, Color? color = null, TextAnchor anchor = TextAnchor.MiddleLeft, FontStyle style = FontStyle.Normal)
        {
            var rect = Rect("Label", parent);
            var label = rect.gameObject.AddComponent<Text>();
            label.font = ActiveFont;
            label.text = text;
            label.fontSize = size;
            label.fontStyle = style;
            label.color = color ?? Theme.Text;
            label.alignment = anchor;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;
            return label;
        }

        public static Button Button(Transform parent, string text, Action onClick, bool primary = false, float height = 72, int fontSize = 30)
        {
            var rect = Rect("Button", parent);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = Color.white;
            var button = rect.gameObject.AddComponent<Button>();
            var colors = button.colors;
            colors.normalColor = primary ? Theme.Accent : Theme.Button;
            colors.highlightedColor = primary ? Theme.AccentHover : Theme.ButtonHover;
            colors.selectedColor = colors.normalColor;
            colors.pressedColor = Color.Lerp(colors.normalColor, Color.black, 0.3f);
            colors.disabledColor = Theme.Disabled;
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            button.targetGraphic = image;
            var label = Label(rect, text, fontSize, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            Stretch(label.rectTransform, 8);
            if (onClick != null) button.onClick.AddListener(() => onClick());
            Size(rect.gameObject, height: height);
            return button;
        }

        public static void SetButtonText(Button button, string text) => button.GetComponentInChildren<Text>().text = text;

        public static InputField Input(Transform parent, string value, int characterLimit, string placeholder = "", bool password = false)
        {
            var rect = Rect("Input", parent);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = new Color32(0x10, 0x0d, 0x11, 0xff);
            rect.gameObject.AddComponent<Outline>().effectColor = Theme.PanelBorder;
            var input = rect.gameObject.AddComponent<InputField>();
            var text = Label(rect, "", 28);
            text.supportRichText = false;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            Stretch(text.rectTransform, 14);
            var hint = Label(rect, placeholder, 28, Theme.Muted, TextAnchor.MiddleLeft, FontStyle.Italic);
            Stretch(hint.rectTransform, 14);
            input.textComponent = text;
            input.placeholder = hint;
            input.characterLimit = characterLimit;
            input.contentType = password ? InputField.ContentType.Password : InputField.ContentType.Standard;
            input.text = value;
            Size(rect.gameObject, height: 64);
            return input;
        }

        /// <summary>Labelled field: "caption" on the left, control area on the right.</summary>
        public static RectTransform Field(Transform parent, string caption, float height = 64)
        {
            var row = Row(parent, caption, height);
            var label = Label(row, caption, 28, Theme.Muted);
            Size(label.gameObject, width: 320, flexibleWidth: 0);
            return row;
        }

        /// <summary>
        /// libGDX FitViewport equivalent: a centered design-space frame (e.g. 1920×1080) that children place
        /// with Java's bottom-left coordinates. The shell canvas uses CanvasScaler Expand at 1920×1080.
        /// </summary>
        public static RectTransform DesignFrame(Transform parent, float width, float height)
        {
            var rect = Rect("Design", parent);
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = new Vector2(width, height);
            var scale = Mathf.Min(1920f / width, 1080f / height);
            rect.localScale = new Vector3(scale, scale, 1);
            return rect;
        }

        /// <summary>Places a child with Java Scene2D bounds (x, y from the bottom-left of its design frame).</summary>
        public static RectTransform Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(x + width / 2, y + height / 2);
            return rect;
        }

        public static Image Picture(Transform parent, string name, Sprite sprite, float x, float y, float width, float height)
        {
            var image = Place(Rect(name, parent), x, y, width, height).gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>Full-window background without bars or stretching (scale = max), like the Java cover draws.</summary>
        public static Image Cover(Transform parent, string name, Sprite sprite)
        {
            var holder = Stretch(Rect(name, parent));
            var rect = Rect("Image", holder);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = false;
            var fitter = rect.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fitter.aspectRatio = sprite.rect.width / sprite.rect.height;
            return image;
        }

        public static void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;
            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
            UnityEngine.Object.DontDestroyOnLoad(go);
        }

        public static void Clear(Transform parent)
        {
            for (var i = parent.childCount - 1; i >= 0; i--) Dispose(parent.GetChild(i).gameObject);
        }

        /// <summary>Destroy that also works for edit-mode tooling (headless smoke runs outside Play Mode).</summary>
        public static void Dispose(GameObject go)
        {
            if (go == null) return;
            if (UnityEngine.Application.isPlaying) UnityEngine.Object.Destroy(go);
            else UnityEngine.Object.DestroyImmediate(go);
        }
    }

    /// <summary>"◀ value ▶" control for bounded integers and option lists.</summary>
    public sealed class Stepper
    {
        readonly Text value;
        readonly Func<int, string> format;
        readonly int min, max;
        readonly bool wrap;
        public int Value { get; private set; }
        public event Action<int> Changed;

        public Stepper(Transform row, int min, int max, int initial, Func<int, string> format = null, bool wrap = false)
        {
            this.wrap = wrap;
            this.min = min;
            this.max = max;
            this.format = format ?? (v => v.ToString(System.Globalization.CultureInfo.InvariantCulture));
            UiKit.Size(UiKit.Button(row, "◀", () => Set(Value - 1), height: 64).gameObject, width: 80, flexibleWidth: 0);
            value = UiKit.Label(row, "", 30, Theme.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            UiKit.Size(value.gameObject, flexibleWidth: 1);
            UiKit.Size(UiKit.Button(row, "▶", () => Set(Value + 1), height: 64).gameObject, width: 80, flexibleWidth: 0);
            Set(initial);
        }

        public void Set(int next)
        {
            // Option lists wrap around; numeric ranges clamp.
            Value = wrap ? (next < min ? max : next > max ? min : next) : Math.Max(min, Math.Min(max, next));
            value.text = format(Value);
            Changed?.Invoke(Value);
        }
    }
}

using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FinalAnswer.UI
{
    /// <summary>
    /// Button drawn entirely from the supplied PNG states.
    /// Crossfade mode ports AnimatedImageButton (180 ms hover blend, immediate press, 1.2 % hover scale,
    /// delayed 450 ms entrance, input limited to the visible frame). Swap mode ports the static
    /// Scene2D buttons (CREATE/CANCEL/BACK/…): the state image changes immediately.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class ArtButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler,
        IPointerUpHandler, IPointerClickHandler, ICanvasRaycastFilter
    {
        Image baseImage, hoverImage, pressImage;
        Sprite up, hover, pressed;
        CanvasGroup group;
        Text label;
        Color labelUp, labelHover, labelPressed, labelChecked;
        bool crossfade, over, down, entering;
        float hoverAmount, pressAmount, entranceTime = 1, scaleHover = .012f, scalePress = .006f, brightness = 1;
        Rect hitInsets = new Rect(0, 0, 0, 0); // fractions: x=left, y=bottom, width=right, height=top
        bool tintIdle;

        public Action Clicked;
        public bool Interactable { get; set; } = true;
        public bool Checked { get; set; }
        public float AspectRatio { get; private set; } = 1;
        public RectTransform Rect => (RectTransform)transform;
        public Text Label => label;

        /// <summary>The blank grunge plate (player-count art) with a label — used for every in-game host action.</summary>
        public static ArtButton Plate(Transform parent, string text, Action onClick, int fontSize = 40)
        {
            var art = ArtCatalog.Active;
            return Create(parent, "Plate", art.Crop("Art/CreateRoom/Buttons/button_up", 293, 198, 1191, 483),
                    art.Crop("Art/CreateRoom/Buttons/button_hover", 161, 273, 1208, 452),
                    art.Crop("Art/CreateRoom/Buttons/button_pressed", 167, 263, 1198, 456), false, onClick).Scales(.02f, .015f)
                .WithLabel(text, art.Font("Fonts/RubikWetPaint-Regular"), fontSize, new Color(.95f, .9f, .84f), Color.white, new Color(1, .48f, .32f), new Color(.95f, .9f, .84f));
        }

        public static ArtButton Create(Transform parent, string name, Sprite up, Sprite hover, Sprite pressed, bool crossfade, Action onClick)
        {
            var rect = UiKit.Rect(name, parent);
            rect.pivot = new Vector2(.5f, .5f);
            var button = rect.gameObject.AddComponent<ArtButton>();
            button.Setup(up, hover, pressed, crossfade);
            button.Clicked = onClick;
            return button;
        }

        void Setup(Sprite upSprite, Sprite hoverSprite, Sprite pressedSprite, bool blend)
        {
            up = upSprite;
            hover = hoverSprite ?? upSprite;
            pressed = pressedSprite ?? hover;
            crossfade = blend;
            AspectRatio = up.rect.height / up.rect.width;
            group = gameObject.AddComponent<CanvasGroup>();
            baseImage = Layer("Up", up, true);
            if (crossfade)
            {
                hoverImage = Layer("Hover", hover, false);
                pressImage = Layer("Pressed", pressed, false);
            }
        }

        Image Layer(string layerName, Sprite sprite, bool raycast)
        {
            var image = UiKit.Stretch(UiKit.Rect(layerName, transform)).gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = raycast;
            image.preserveAspect = false;
            if (!raycast) image.color = new Color(1, 1, 1, 0);
            return image;
        }

        /// <summary>Restrict input to the painted frame (fractions of width/height), like AnimatedImageButton.hit.</summary>
        public ArtButton HitInsets(float left, float right, float bottom, float top)
        {
            hitInsets = new Rect(left, bottom, right, top);
            return this;
        }

        public ArtButton Scales(float hoverScale, float pressScale)
        {
            scaleHover = hoverScale;
            scalePress = pressScale;
            return this;
        }

        /// <summary>Text drawn over a blank artwork (player count numerals, OK).</summary>
        public ArtButton WithLabel(string text, Font font, int size, Color normal, Color hoverColor, Color pressedColor, Color checkedColor)
        {
            label = UiKit.Label(transform, text, size, normal, TextAnchor.MiddleCenter);
            label.font = font;
            UiKit.Stretch(label.rectTransform);
            label.gameObject.AddComponent<Shadow>().effectColor = new Color(.025f, 0, 0, .85f);
            labelUp = normal;
            labelHover = hoverColor;
            labelPressed = pressedColor;
            labelChecked = checkedColor;
            return this;
        }

        /// <summary>PlayerCountSelector: calm 0.76 idle, 0.86 checked, full brightness on interaction.</summary>
        public ArtButton IdleTint(bool enabled)
        {
            tintIdle = enabled;
            brightness = enabled ? .76f : 1;
            return this;
        }

        public void RestartEntrance()
        {
            entranceTime = 0;
            entering = true;
            hoverAmount = pressAmount = 0;
        }

        public void ShowImmediately()
        {
            entranceTime = 1;
            entering = false;
        }

        void Update()
        {
            var dt = Mathf.Clamp(Time.unscaledDeltaTime, 0, .1f);
            entranceTime += dt;
            var enabled = Interactable;
            var hoverTarget = enabled && over ? 1 : 0;
            hoverAmount = Approach(hoverAmount, hoverTarget, dt, hoverTarget > hoverAmount ? .06f : .075f);
            pressAmount = enabled && down ? 1 : 0;

            float entrance = 1;
            if (entering) entrance = Smooth(Mathf.Clamp01((entranceTime - .12f) / .45f));
            group.alpha = entrance * (enabled ? 1 : .45f);
            group.blocksRaycasts = enabled && entrance > .5f;

            if (crossfade)
            {
                hoverImage.color = new Color(1, 1, 1, hoverAmount);
                pressImage.color = new Color(1, 1, 1, pressAmount);
                var scale = 1 + hoverAmount * scaleHover - pressAmount * scalePress;
                transform.localScale = new Vector3(scale, scale, 1);
            }
            else
            {
                baseImage.sprite = enabled && down ? pressed : enabled && over ? hover : up;
                var target = enabled && down ? 1 - scalePress : enabled && over ? 1 + scaleHover : 1;
                var s = Mathf.Lerp(transform.localScale.x, target, 1 - Mathf.Exp(-dt / .1f));
                transform.localScale = new Vector3(s, s, 1);
            }

            if (tintIdle)
            {
                var wanted = enabled && (down || over) ? 1f : Checked ? .86f : .76f;
                brightness = Mathf.Lerp(brightness, wanted, 1 - Mathf.Exp(-dt / .16f));
                baseImage.color = new Color(brightness, brightness, brightness, 1);
            }

            if (label != null)
                label.color = !enabled ? Theme.Muted : down ? labelPressed : Checked ? (over ? Color.Lerp(labelChecked, Color.white, .1f) : labelChecked) : over ? labelHover : labelUp;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            over = true;
            if (Interactable) ShellAudio.Current?.Hover();
        }

        public void OnPointerExit(PointerEventData eventData) => over = down = false;

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left) down = true;
        }

        public void OnPointerUp(PointerEventData eventData) => down = false;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (!Interactable || eventData.button != PointerEventData.InputButton.Left) return;
            ShellAudio.Current?.Press();
            Clicked?.Invoke();
        }

        public bool IsRaycastLocationValid(Vector2 screenPoint, Camera eventCamera)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(Rect, screenPoint, eventCamera, out var local)) return false;
            var r = Rect.rect;
            var u = (local.x - r.xMin) / r.width;
            var v = (local.y - r.yMin) / r.height;
            return u >= hitInsets.x && u < 1 - hitInsets.width && v >= hitInsets.y && v < 1 - hitInsets.height;
        }

        static float Approach(float current, float target, float dt, float response)
        {
            var result = Mathf.Lerp(current, target, 1 - Mathf.Exp(-dt / response));
            return Mathf.Abs(result - target) < .001f ? target : result;
        }

        public static float Smooth(float p) => p * p * (3 - 2 * p);
        public static float Smoother(float p) => p * p * p * (p * (p * 6 - 15) + 10);
    }
}

using System;
using FinalAnswer.Contracts;
using UnityEngine;
using UnityEngine.UI;

namespace FinalAnswer.UI
{
    /// <summary>Port of screens/MainMenu: cover background, 96-frame candle, title scrim, floating logo, five PNG buttons.</summary>
    public sealed class MainMenuView
    {
        const float MenuLeft = 90, MenuTop = 650, MenuWidth = 660, ButtonWidth = 500, RowStep = 110;

        public RectTransform Root { get; }
        readonly RectTransform logo;
        readonly CanvasGroup logoGroup;
        readonly ArtButton[] buttons;
        readonly float logoHeight;
        float titleTime;
        bool reducedMotion;

        public MainMenuView(Transform parent, ArtCatalog art, bool firstShow, bool reduced, bool developer,
            Action play, Action createRoom, Action generationCode, Action settings, Action exit)
        {
            reducedMotion = reduced;
            Root = UiKit.Stretch(UiKit.Rect("MainMenu", parent));
            var background = UiKit.Cover(Root, "Background", art.Sprite("Art/Menu/main_screen_background"));
            var candleFrames = new Sprite[96];
            for (var i = 0; i < candleFrames.Length; i++) candleFrames[i] = art.Sprite("Art/Menu/Candle/" + (i + 1).ToString("0000"));
            CandleView.Create(background.rectTransform, candleFrames).ReducedMotion = reduced;

            var design = UiKit.DesignFrame(Root, 1920, 1080);
            UiKit.Picture(design, "TitleShade", TitleShade(), 0, 1080 - 520, 900, 520);

            var logoSprite = art.Sprite("Art/Menu/Final_Answer");
            logoHeight = 660 * logoSprite.rect.height / logoSprite.rect.width;
            var logoImage = UiKit.Picture(design, "Logo", logoSprite, 90, 1080 - 40 - logoHeight, 660, logoHeight);
            logo = logoImage.rectTransform;
            logoGroup = logo.gameObject.AddComponent<CanvasGroup>();

            string[] names = { "play", "create_room", "generation_code", "settings", "exit" };
            Action[] actions = { play, createRoom, generationCode, settings, exit };
            buttons = new ArtButton[names.Length];
            var top = MenuTop;
            var left = MenuLeft + (MenuWidth - ButtonWidth) / 2;
            for (var i = 0; i < names.Length; i++)
            {
                var path = "Art/Menu/Buttons/" + names[i] + "_";
                var button = ArtButton.Create(design, names[i], art.Sprite(path + "up"), art.Sprite(path + "hover"), art.Sprite(path + "pressed"), true, actions[i])
                    .HitInsets(.02f, .02f, .18f, .18f);
                var height = ButtonWidth * button.AspectRatio;
                UiKit.Place(button.Rect, left, top - height, ButtonWidth, height);
                if (firstShow && !reduced) button.RestartEntrance(); else button.ShowImmediately();
                buttons[i] = button;
                top -= RowStep;
            }

            var hint = UiKit.Label(design, developer ? "F1 / РЕЖИМ РАЗРАБОТЧИКА (СОЛО-ТЕСТ)    F3 / ОТЛАДКА" : "", 20, new Color(.70f, .58f, .40f));
            hint.font = art.Font("Fonts/NotoSans-Regular");
            UiKit.Place(hint.rectTransform, 90, 28, 1200, 30);
            titleTime = firstShow && !reduced ? 0 : 1;
            Root.gameObject.AddComponent<Ticker>().Tick = Animate;
        }

        void Animate(float delta)
        {
            titleTime += Mathf.Min(delta, .1f);
            var entrance = reducedMotion ? 1 : ArtButton.Smooth(Mathf.Clamp01(titleTime / .8f));
            var y = 1080 - 40 - logoHeight + (1 - entrance) * 14 + Mathf.Sin(titleTime * 1.15f) * (reducedMotion ? 0 : 2);
            logo.anchoredPosition = new Vector2(90 + 330, y + logoHeight / 2);
            logoGroup.alpha = entrance;
        }

        /// <summary>1×256 scrim: opacity .78·(1 − t)² from the top edge, color (0.025, 0.018, 0.04).</summary>
        static Sprite TitleShade()
        {
            var texture = new Texture2D(1, 256, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            // Java pixmap row y counts from the top; Unity row r from the bottom, so (1 − y/255) = r/255.
            for (var r = 0; r < 256; r++)
            {
                var t = r / 255f;
                texture.SetPixel(0, r, new Color(.025f, .018f, .04f, .78f * t * t));
            }
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0, 0, 1, 256), new Vector2(.5f, .5f));
        }
    }

    /// <summary>Port of OptionsMainWindow on the CyclingRoomBackground (RoomSettingsScreen).</summary>
    public sealed class CreateRoomView
    {
        // options_main.png measured coordinates (top-left origin), from OptionsMainWindow.layoutIn.
        const float Width = 1135, Height = 1386;

        public RectTransform Root { get; }
        readonly RectTransform window;
        readonly CanvasGroup windowGroup;
        readonly InputField roomName, password;
        readonly ArtButton[] counts = new ArtButton[7];
        readonly Text modeTitle, validation;
        readonly ModeInfoDialog infoDialog;
        readonly Localization texts;
        readonly float scale, baseY;
        int modeIndex, maxPlayers = 4;
        float animationTime, quietTime = 3, floatOffset;
        bool reducedMotion;

        public CreateRoomView(Transform parent, ArtCatalog art, Localization texts, bool reduced, Action<RoomSettings> create, Action cancel)
        {
            this.texts = texts;
            reducedMotion = reduced;
            Root = UiKit.Stretch(UiKit.Rect("CreateRoom", parent));
            CrossfadeBackground.Create(Root, new[] { art.Sprite("Art/CreateRoom/bright"), art.Sprite("Art/CreateRoom/medium"), art.Sprite("Art/CreateRoom/dim") },
                2f, 3.5f, true, true).ReducedMotion = reduced;
            var design = UiKit.DesignFrame(Root, 1920, 1080);

            scale = Mathf.Min((1920 - 160f) / Width, (1080 - 90f) / Height);
            float w = Width * scale, h = Height * scale;
            baseY = (1080 - h) / 2;
            window = UiKit.Place(UiKit.Rect("Window", design), (1920 - w) / 2, baseY, w, h);
            windowGroup = window.gameObject.AddComponent<CanvasGroup>();
            var artwork = UiKit.Stretch(UiKit.Rect("Artwork", window)).gameObject.AddComponent<Image>();
            artwork.sprite = art.Sprite("Art/CreateRoom/options_main");
            artwork.raycastTarget = false;

            var fieldFont = art.Font("Fonts/NotoSans-SemiCondensedSemiBold");
            var hintFont = art.Font("Fonts/NotoSans-Regular");
            roomName = Field(window, fieldFont, hintFont, 236, 436, 778, 68, texts.Get("room.name_placeholder"), 40, false);
            password = Field(window, fieldFont, hintFont, 236, 986, 778, 68, texts.Get("room.password_placeholder"), 64, true);
            roomName.onValueChanged.AddListener(_ => { validation.text = ""; quietTime = 0; });

            // PlayerCountSelector: seven exclusive buttons, crop per state to the visible metal frame.
            var countRow = Box(window, 230, 619, 794, 90);
            var countFont = art.Font("Fonts/Creepster-Regular");
            const float gap = 5;
            var buttonWidth = (794 * scale - gap * 6) / 7;
            var buttonHeight = buttonWidth * .64f;
            for (var i = 0; i < 7; i++)
            {
                var value = i + 2;
                var button = ArtButton.Create(countRow, "maxPlayers" + value,
                        art.Crop("Art/CreateRoom/Buttons/button_up", 293, 198, 1191, 483),
                        art.Crop("Art/CreateRoom/Buttons/button_hover", 161, 273, 1208, 452),
                        art.Crop("Art/CreateRoom/Buttons/button_pressed", 167, 263, 1198, 456), false, () => SelectCount(value))
                    .Scales(.02f, .015f).IdleTint(true)
                    .WithLabel(value.ToString(), countFont, 32, new Color(.93f, .86f, .79f), new Color(1, .96f, .87f), new Color(1, .48f, .32f), new Color(1f, .16f, .12f));
                UiKit.Place(button.Rect, i * (buttonWidth + gap), (90 * scale - buttonHeight) / 2, buttonWidth, buttonHeight);
                counts[i] = button;
            }
            SelectCount(4);

            // GameModeSelector: painted field, arrows, centered localized name, INFO below.
            var modeBox = Box(window, 227, 784, 800, 86);
            var field = UiKit.Stretch(UiKit.Rect("Field", modeBox)).gameObject.AddComponent<Image>();
            field.sprite = art.Crop("Art/CreateRoom/field", 36, 113, 2100, 446);
            field.color = new Color(.78f, .78f, .78f);
            field.raycastTarget = false;
            var boxW = 800 * scale;
            var boxH = 86 * scale;
            var arrowH = Mathf.Min(58f, boxH - 4);
            var arrowW = arrowH * 1.04f;
            var previous = Arrow(art, modeBox, "previous", () => SelectMode(modeIndex - 1));
            UiKit.Place(previous.Rect, 5, (boxH - arrowH) / 2 + .35f, arrowW, arrowH);
            var next = Arrow(art, modeBox, "next", () => SelectMode(modeIndex + 1));
            UiKit.Place(next.Rect, boxW - arrowW - 5, (boxH - arrowH) / 2 + .35f, arrowW, arrowH);
            var margin = arrowW + 19;
            modeTitle = UiKit.Label(modeBox, "", 32, new Color(.97f, .9f, .82f), TextAnchor.MiddleCenter);
            modeTitle.font = fieldFont;
            modeTitle.resizeTextForBestFit = true;
            modeTitle.resizeTextMinSize = 14;
            modeTitle.resizeTextMaxSize = 32;
            modeTitle.horizontalOverflow = HorizontalWrapMode.Wrap;
            UiKit.Place(modeTitle.rectTransform, margin, 0, boxW - margin * 2, boxH);
            var info = ArtButton.Create(modeBox, "gameModeInfo",
                art.Crop("Art/CreateRoom/Buttons/info_up", 30, 140, 2110, 430),
                art.Crop("Art/CreateRoom/Buttons/info_hover", 30, 140, 2110, 430),
                art.Crop("Art/CreateRoom/Buttons/info_pressed", 30, 140, 2110, 430), false, ShowInfo).Scales(0, 0);
            info.GetComponentInChildren<Image>().color = new Color(.85f, .85f, .85f);
            UiKit.Place(info.Rect, (boxW - 198) / 2 + 150, -52.8f - 6, 198, 52.8f);
            SelectMode(0);

            validation = UiKit.Label(window, "", 25, new Color(1f, .43f, .35f), TextAnchor.MiddleCenter);
            validation.font = hintFont;
            Place(validation.rectTransform, 236, 1110 - 36, 778, 36);

            // Footer: CREATE (left, 540) and CANCEL (right, 400).
            var createButton = ArtButton.Create(window, "confirmCreateRoom",
                art.Crop("Art/CreateRoom/Buttons/create_buttongen_up", 50, 80, 1820, 680),
                art.Crop("Art/CreateRoom/Buttons/create_buttongen_hover", 50, 80, 1820, 680),
                art.Crop("Art/CreateRoom/Buttons/create_buttongen_pressed", 50, 80, 1820, 680), false, () => RequestCreation(create)).Scales(0, 0);
            var cancelButton = ArtButton.Create(window, "cancelRoom",
                art.Crop("Art/CreateRoom/Buttons/cancel_button_up", 65, 175, 1845, 445),
                art.Crop("Art/CreateRoom/Buttons/cancel_button_hover", 65, 175, 1845, 445),
                art.Crop("Art/CreateRoom/Buttons/cancel_button_pressed", 65, 175, 1845, 445), false, () => { quietTime = 0; cancel(); }).Scales(0, 0);
            float cancelWidth = 400 * scale, createWidth = 540 * scale, footerGap = 24 * scale;
            var left = (w - cancelWidth - footerGap - createWidth) / 2;
            var bottom = 70 * scale;
            var createHeight = createWidth * createButton.AspectRatio;
            var cancelHeight = cancelWidth * cancelButton.AspectRatio;
            UiKit.Place(createButton.Rect, left, bottom + 14 * scale, createWidth, createHeight);
            UiKit.Place(cancelButton.Rect, left + createWidth + footerGap, bottom + (createHeight - cancelHeight) / 2, cancelWidth, cancelHeight);

            infoDialog = new ModeInfoDialog(Root, fieldFont, hintFont, texts);
            windowGroup.alpha = 0;
            Root.gameObject.AddComponent<Ticker>().Tick = Animate;
        }

        public void FocusName() => roomName.ActivateInputField();

        public void ShowError(string message) => validation.text = message ?? "";

        void RequestCreation(Action<RoomSettings> create)
        {
            quietTime = 0;
            var name = roomName.text.Trim();
            if (name.Length == 0)
            {
                validation.text = texts.Get("room.name_required");
                FocusName();
                return;
            }
            validation.text = "";
            create(new RoomSettings { Name = name, MaxPlayers = maxPlayers, GameMode = GameModes.All[modeIndex].Id, Password = password.text });
        }

        void SelectCount(int value)
        {
            maxPlayers = value;
            quietTime = 0;
            for (var i = 0; i < counts.Length; i++) if (counts[i] != null) counts[i].Checked = i + 2 == value;
        }

        void SelectMode(int index)
        {
            var count = GameModes.All.Count;
            modeIndex = (index % count + count) % count;
            modeTitle.text = texts.Get("mode." + GameModes.All[modeIndex].Id + ".name");
            quietTime = 0;
        }

        void ShowInfo()
        {
            var id = GameModes.All[modeIndex].Id;
            infoDialog.Open(texts.Get("mode." + id + ".name"), texts.Get("mode." + id + ".description"));
        }

        void Animate(float delta)
        {
            var dt = Mathf.Clamp(delta, 0, .1f);
            animationTime += dt;
            quietTime += dt;
            windowGroup.alpha = ArtButton.Smoother(Mathf.Min(animationTime / .65f, 1));
            if (reducedMotion) { SetY(baseY); return; }
            var entrance = ArtButton.Smoother(Mathf.Min(animationTime / .65f, 1));
            var idle = ArtButton.Smoother(Mathf.Clamp01((quietTime - 2.5f) / 1.5f));
            var target = Mathf.Sin(animationTime * Mathf.PI * 2 / 8f) * 1.8f * idle;
            floatOffset += (target - floatOffset) * (1 - Mathf.Exp(-dt / .4f));
            SetY(baseY - 14 * (1 - entrance) + floatOffset * entrance);
        }

        void SetY(float y) => window.anchoredPosition = new Vector2(window.anchoredPosition.x, y + window.sizeDelta.y / 2);

        RectTransform Box(Transform parent, float x, float top, float w, float h)
        {
            var rect = UiKit.Rect("Box", parent);
            return Place(rect, x, top, w, h);
        }

        /// <summary>Window-local placement from PNG coordinates measured at the top-left.</summary>
        RectTransform Place(RectTransform rect, float x, float top, float w, float h) =>
            UiKit.Place(rect, x * scale, (Height - top - h) * scale, w * scale, h * scale);

        InputField Field(Transform parent, Font font, Font hintFont, float x, float top, float w, float h, string hint, int limit, bool secret)
        {
            var rect = Place(UiKit.Rect("Field", parent), x, top, w, h);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(1, .45f, .25f, 0.0001f);
            var input = rect.gameObject.AddComponent<InputField>();
            var text = UiKit.Label(rect, "", 32, new Color(.96f, .91f, .86f));
            text.font = font;
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            UiKit.Stretch(text.rectTransform, 18);
            text.gameObject.AddComponent<Shadow>().effectColor = new Color(.02f, .01f, .02f, .75f);
            var placeholder = UiKit.Label(rect, hint, 25, new Color(.66f, .61f, .6f));
            placeholder.font = hintFont;
            UiKit.Stretch(placeholder.rectTransform, 18);
            input.textComponent = text;
            input.placeholder = placeholder;
            input.characterLimit = limit;
            input.caretColor = new Color(1f, .75f, .5f);
            input.caretWidth = 2;
            input.customCaretColor = true;
            input.selectionColor = new Color(.65f, .12f, .1f, .7f);
            if (secret) { input.contentType = InputField.ContentType.Password; input.asteriskChar = '•'; }
            input.onValueChanged.AddListener(_ => quietTime = 0);
            return input;
        }

        static ArtButton Arrow(ArtCatalog art, Transform parent, string direction, Action click)
        {
            var path = "Art/CreateRoom/Buttons/" + direction + "_";
            var button = ArtButton.Create(parent, direction + "GameMode",
                art.Crop(path + "up", 160, 70, 1040, 1000), art.Crop(path + "hover", 160, 70, 1040, 1000), art.Crop(path + "pressed", 160, 70, 1040, 1000), false, click);
            button.GetComponentInChildren<Image>().color = new Color(.85f, .85f, .85f);
            return button;
        }
    }

    /// <summary>ModeInfoDialog: modal description of the selected mode; closing never changes the selection.</summary>
    public sealed class ModeInfoDialog
    {
        readonly RectTransform root;
        readonly Text title, description;

        public ModeInfoDialog(Transform parent, Font titleFont, Font bodyFont, Localization texts)
        {
            root = UiKit.Stretch(UiKit.Rect("ModeInfoDialog", parent));
            var shade = root.gameObject.AddComponent<Image>();
            shade.color = new Color(.008f, .003f, .008f, .72f);
            var design = UiKit.DesignFrame(root, 1920, 1080);
            var panel = UiKit.Place(UiKit.Rect("Panel", design), (1920 - 820) / 2f, (1080 - 400) / 2f, 820, 400);
            panel.gameObject.AddComponent<Image>().color = new Color(.075f, .035f, .04f, .97f);
            panel.gameObject.AddComponent<Outline>().effectColor = new Color(.45f, .09f, .08f, 1);
            title = UiKit.Label(panel, "", 36, new Color(.98f, .9f, .82f), TextAnchor.MiddleCenter);
            title.font = titleFont;
            UiKit.Place(title.rectTransform, 28, 400 - 64, 764, 56);
            description = UiKit.Label(panel, "", 25, new Color(.88f, .84f, .81f), TextAnchor.UpperLeft);
            description.font = bodyFont;
            UiKit.Place(description.rectTransform, 40, 90, 740, 230);
            var close = UiKit.Button(panel, texts.Get("ui.close"), Close, height: 50, fontSize: 24);
            close.GetComponent<Image>().color = new Color(.23f, .045f, .045f);
            UiKit.Place((RectTransform)close.transform, (820 - 190) / 2f, 24, 190, 50);
            root.gameObject.SetActive(false);
        }

        public bool IsOpen => root.gameObject.activeSelf;

        public void Open(string name, string text)
        {
            title.text = name;
            description.text = text;
            root.SetAsLastSibling();
            root.gameObject.SetActive(true);
        }

        public void Close() => root.gameObject.SetActive(false);
    }

    /// <summary>
    /// Port of GenerationCodeScreen (1672×941 design space): lighting background, tiled six-cell code board,
    /// GENERATE → code + REROLL + OK, BACK. Room calls are made by the owner through the callbacks.
    /// </summary>
    public sealed class GenerationCodeView
    {
        const float W = 1672, H = 941;
        public RectTransform Root { get; }
        readonly Text[] cells = new Text[6];
        readonly ArtButton generate, reroll, confirm, back;
        readonly Text status;
        readonly CanvasGroup fade;
        float fadeTime;

        public GenerationCodeView(Transform parent, ArtCatalog art, bool reduced, Action onGenerate, Action onReroll, Action onConfirm, Action onBack)
        {
            Root = UiKit.Stretch(UiKit.Rect("GenerationCode", parent));
            Root.gameObject.AddComponent<Image>().color = Color.black;
            fade = Root.gameObject.AddComponent<CanvasGroup>();
            var design = UiKit.DesignFrame(Root, W, H);
            var bg = UiKit.Stretch(UiKit.Rect("Board", design));
            CrossfadeBackground.Create(bg, new[] { art.Sprite("Art/GenerationCode/stage2"), art.Sprite("Art/GenerationCode/stage3") }, 1.8f, 2.6f, false, false)
                .ReducedMotion = reduced;

            float backWidth = 240, backHeight = backWidth * 589f / 1999f;
            back = ArtButton.Create(design, "back",
                art.Crop("Art/GenerationCode/Buttons/back", 69, 67, 1999, 589),
                art.Crop("Art/GenerationCode/Buttons/back_hover", 58, 60, 2020, 601), null, false, onBack);
            UiKit.Place(back.Rect, 32, H - 32 - backHeight, backWidth, backHeight);

            // The original cell artwork repeated six times, keeping the board's outer corners (8 segments).
            var field = art.Texture("Art/GenerationCode/CodeField");
            const float fieldWidth = 680, fieldY = 390;
            var fieldHeight = fieldWidth * field.height / field.width;
            var fieldX = (W - fieldWidth) / 2;
            var board = UiKit.Place(UiKit.Rect("CodeField", design), fieldX, fieldY, fieldWidth, fieldHeight);
            for (var segment = 0; segment < 8; segment++)
            {
                var sourceX = segment == 0 ? 0 : segment == 7 ? .9f : .1f;
                var sourceWidth = segment == 0 || segment == 7 ? .1f : .2f;
                var sprite = art.Crop("Art/GenerationCode/CodeField", Mathf.RoundToInt(field.width * sourceX), 0, Mathf.RoundToInt(field.width * sourceWidth), field.height);
                var pieceWidth = fieldWidth * (segment == 0 || segment == 7 ? .1f : .8f / 6);
                var pieceX = segment == 0 ? 0 : segment == 7 ? fieldWidth * .9f : fieldWidth * (.1f + (segment - 1) * .8f / 6);
                UiKit.Picture(board, "Segment" + segment, sprite, pieceX, 0, pieceWidth, fieldHeight);
            }
            var codeFont = art.Font("Fonts/RoadRage-Regular");
            var cellWidth = fieldWidth * .8f / 6;
            for (var i = 0; i < 6; i++)
            {
                cells[i] = UiKit.Label(design, "", 88, Hex("F0E8DE"), TextAnchor.MiddleCenter);
                cells[i].font = codeFont;
                UiKit.Place(cells[i].rectTransform, fieldX + fieldWidth * .1f + i * cellWidth, fieldY, cellWidth, fieldHeight);
            }

            float generateWidth = 470, generateHeight = generateWidth * 586f / 2094f;
            generate = ArtButton.Create(design, "generate",
                art.Crop("Art/GenerationCode/Buttons/generate_up", 34, 68, 2094, 586),
                art.Crop("Art/GenerationCode/Buttons/generate_hover", 77, 73, 2021, 576),
                art.Crop("Art/GenerationCode/Buttons/generate_pressed", 65, 81, 2027, 564), false, onGenerate).Scales(0, 0);
            UiKit.Place(generate.Rect, fieldX + (fieldWidth - generateWidth) / 2, 300 - generateHeight / 2, generateWidth, generateHeight);

            reroll = ArtButton.Create(design, "reroll",
                art.Crop("Art/GenerationCode/Buttons/reroll_up", 143, 155, 994, 927),
                art.Crop("Art/GenerationCode/Buttons/reroll_hover", 84, 135, 1088, 974),
                art.Crop("Art/GenerationCode/Buttons/reroll_pressed", 87, 143, 1079, 970), false, onReroll).Scales(0, 0);
            UiKit.Place(reroll.Rect, fieldX + fieldWidth + 8, fieldY + (fieldHeight - 108) / 2, 116, 108);

            confirm = ArtButton.Create(design, "confirmCode",
                    art.Crop("Art/CreateRoom/Buttons/button_up", 293, 198, 1191, 483),
                    art.Crop("Art/CreateRoom/Buttons/button_hover", 161, 273, 1208, 452),
                    art.Crop("Art/CreateRoom/Buttons/button_pressed", 167, 263, 1198, 456), false, onConfirm).Scales(0, 0)
                .WithLabel("OK", codeFont, 64, Hex("F0E8DE"), Color.white, new Color(1, .48f, .32f), Hex("F0E8DE"));
            UiKit.Place(confirm.Rect, (W - 300) / 2, 250, 300, 100);

            status = UiKit.Label(design, "", 22, Hex("F0E8DE"), TextAnchor.MiddleCenter);
            status.font = art.Font("Fonts/NotoSans-Regular");
            UiKit.Place(status.rectTransform, (W - 860) / 2, 192, 860, 52);
            Reset();
            fade.alpha = 0;
            Root.gameObject.AddComponent<Ticker>().Tick = d => { fadeTime += d; fade.alpha = ArtButton.Smoother(Mathf.Clamp01(fadeTime / .18f)); };
        }

        public void Reset()
        {
            foreach (var cell in cells) cell.text = "";
            generate.gameObject.SetActive(true);
            generate.Interactable = true;
            confirm.gameObject.SetActive(false);
            reroll.gameObject.SetActive(false);
            back.Interactable = true;
        }

        public void ShowCode(string code)
        {
            for (var i = 0; i < cells.Length; i++) cells[i].text = i < code.Length ? code[i].ToString() : "";
            generate.gameObject.SetActive(false);
            confirm.gameObject.SetActive(true);
            reroll.gameObject.SetActive(true);
        }

        public void SetStatus(string text) => status.text = text;

        public void SetBusy(bool generating, bool rerolling)
        {
            generate.Interactable = !generating;
            reroll.Interactable = !rerolling;
            confirm.Interactable = !rerolling && !generating;
            back.Interactable = !rerolling;
        }

        public void SetConfirmEnabled(bool enabled) => confirm.Interactable = enabled;

        public static Color Hex(string rgb)
        {
            ColorUtility.TryParseHtmlString("#" + rgb, out var color);
            return color;
        }
    }

    /// <summary>Per-frame callback for views that are plain C# objects.</summary>
    public sealed class Ticker : MonoBehaviour
    {
        public Action<float> Tick;
        void Update() => Tick?.Invoke(Time.unscaledDeltaTime);
    }
}

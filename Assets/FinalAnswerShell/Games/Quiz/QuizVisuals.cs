using System.Collections.Generic;
using FinalAnswer.UI;
using UnityEngine;
using UnityEngine.UI;

namespace FinalAnswer.Quiz
{
    /// <summary>QuizBackground: lit room art for questions 1–2, slow zoom/pan, dark scrim and top/bottom shades.</summary>
    public sealed class QuizBackgroundView
    {
        readonly RectTransform root;
        readonly RawImage room;
        readonly Material material;
        readonly CanvasGroup group;
        readonly Vector2 frame;
        float visibility;
        double elapsed;
        bool active, reduced;

        public QuizBackgroundView(RectTransform design, ArtCatalog art, Vector2 size)
        {
            frame = size;
            root = UiKit.Stretch(UiKit.Rect("QuizBackground", design));
            group = root.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            var texture = art.Texture("Art/Quiz/room");
            room = UiKit.Rect("Room", root).gameObject.AddComponent<RawImage>();
            room.texture = texture;
            room.raycastTarget = false;
            material = new Material(art.Get<Shader>("UI/Shaders/QuizRoom")) { name = "Quiz Room" };
            room.material = material;
            var scrim = UiKit.Stretch(UiKit.Rect("Scrim", root)).gameObject.AddComponent<Image>();
            scrim.color = new Color(0, 0, 0, .31f);
            scrim.raycastTarget = false;
            Gradient(root, "TopShade", 0, size.y * .62f, size.x, size.y * .38f, 0, .57f);
            Gradient(root, "BottomShade", 0, 0, size.x, size.y * .22f, .68f, 0);
            group.alpha = 0;
        }

        public void ShowForQuestion(int number, bool reducedMotion)
        {
            active = number >= 1 && number <= 2; // the supplied room art covers questions 1–2
            reduced = reducedMotion;
        }

        public void Update(float delta)
        {
            if (active || visibility > 0) elapsed += delta;
            var target = active ? 1f : 0f;
            visibility = Mathf.MoveTowards(visibility, target, delta / .8f);
            group.alpha = ArtButton.Smoother(visibility);
            if (visibility <= 0) return;
            var zoom = reduced ? 1.02f : 1.02f + .004f * Mathf.Sin((float)elapsed * .075f);
            var texture = room.texture;
            var scale = Mathf.Max(frame.x / texture.width, frame.y / texture.height) * zoom;
            float w = texture.width * scale, h = texture.height * scale;
            var panX = reduced ? 0 : Mathf.Sin((float)elapsed * .055f) * frame.x * .0017f;
            var panY = reduced ? 0 : Mathf.Sin((float)elapsed * .063f) * frame.y * .0011f;
            UiKit.Place(room.rectTransform, (frame.x - w) / 2 + panX, (frame.y - h) / 2 + panY, w, h);
            material.SetFloat("_RoomTime", (float)elapsed);
            material.SetFloat("_Motion", reduced ? 0 : 1);
        }

        static void Gradient(Transform parent, string name, float x, float y, float w, float h, float bottom, float top)
        {
            var texture = new Texture2D(1, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (var i = 0; i < 64; i++) texture.SetPixel(0, i, new Color(0, 0, 0, Mathf.Lerp(bottom, top, i / 63f)));
            texture.Apply(false, true);
            var image = UiKit.Place(UiKit.Rect(name, parent), x, y, w, h).gameObject.AddComponent<RawImage>();
            image.texture = texture;
            image.raycastTarget = false;
        }
    }

    /// <summary>
    /// CinematicQuestion: each glyph fades in 35 ms after the previous one with a small seeded tilt/offset
    /// and a settling wobble, over a dark offset shadow. Kerning and wrapping come from the Text layout.
    /// </summary>
    [RequireComponent(typeof(Text))]
    public sealed class GlyphReveal : BaseMeshEffect
    {
        readonly List<UIVertex> stream = new List<UIVertex>();
        readonly List<UIVertex> output = new List<UIVertex>();
        float[] tilt = new float[0], offset = new float[0];
        double seconds;
        public bool Reduced { get; set; }

        public void Prepare(int seed, int glyphs)
        {
            var random = new System.Random(seed);
            tilt = new float[glyphs];
            offset = new float[glyphs];
            for (var i = 0; i < glyphs; i++)
            {
                tilt[i] = (float)(random.NextDouble() * 2 - 1) * 1.6f;
                offset[i] = (float)(random.NextDouble() * 2 - 1) * 1.5f;
            }
            seconds = 0;
            graphic.SetVerticesDirty();
        }

        public void SetTime(double value)
        {
            if (value == seconds) return;
            seconds = value;
            graphic.SetVerticesDirty();
        }

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive()) return;
            stream.Clear();
            output.Clear();
            vh.GetUIVertexStream(stream);
            var quads = stream.Count / 6;
            for (var pass = 0; pass < 2; pass++)
            for (var q = 0; q < quads; q++)
            {
                var age = seconds - q * QuizTimeline.CharacterDelay;
                if (age <= 0) continue;
                var p = (float)System.Math.Min(1, age / QuizTimeline.CharacterAppear);
                var alpha = p * p * (3 - 2 * p);
                var drift = Reduced ? 0 : -6 * (1 - p) + Mathf.Sin(p * Mathf.PI * 2) * 1.8f * (1 - p);
                var angle = Reduced ? 0 : (q < tilt.Length ? tilt[q] : 0) + Mathf.Sin(p * Mathf.PI * 2) * 2 * (1 - p);
                var rise = Reduced ? 0 : q < offset.Length ? offset[q] : 0;
                Vector3 center = Vector3.zero;
                for (var v = 0; v < 6; v++) center += stream[q * 6 + v].position;
                center /= 6;
                var rotation = Quaternion.Euler(0, 0, angle);
                for (var v = 0; v < 6; v++)
                {
                    var vertex = stream[q * 6 + v];
                    var local = rotation * (vertex.position - center);
                    vertex.position = center + local + new Vector3(pass == 0 ? 2 : 0, drift + rise - (pass == 0 ? 2 : 0), 0);
                    vertex.color = pass == 0
                        ? new Color32(22, 9, 9, (byte)(alpha * .85f * 255))
                        : new Color32(242, 239, 233, (byte)(alpha * 255));
                    output.Add(vertex);
                }
            }
            vh.Clear();
            vh.AddUIVertexTriangleStream(output);
        }
    }

    /// <summary>The question text block: fitted once per question, then moved/scaled up out of the way.</summary>
    public sealed class QuestionView
    {
        public const int FontSize = 86;
        readonly RectTransform rect;
        readonly Text text;
        readonly GlyphReveal reveal;
        readonly Vector2 frame;

        public QuestionView(RectTransform design, Font font, Vector2 size)
        {
            frame = size;
            rect = UiKit.Rect("Question", design);
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.pivot = new Vector2(.5f, .5f);
            text = rect.gameObject.AddComponent<Text>();
            text.font = font;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.supportRichText = false;
            text.raycastTarget = false;
            text.color = Color.white;
            reveal = rect.gameObject.AddComponent<GlyphReveal>();
        }

        public void Prepare(string id, string value, bool reduced)
        {
            text.text = value;
            var maxWidth = frame.x * .8f;
            rect.sizeDelta = new Vector2(maxWidth, frame.y * .27f);
            var size = Mathf.RoundToInt(FontSize * (value.Length < 65 ? 1.12f : 1));
            var settings = text.GetGenerationSettings(new Vector2(maxWidth, 0));
            while (size > 16)
            {
                settings.fontSize = size;
                text.fontSize = size;
                var height = text.cachedTextGeneratorForLayout.GetPreferredHeight(value, settings) / text.pixelsPerUnit;
                if (height <= frame.y * .27f) break;
                size = Mathf.RoundToInt(size * .94f);
            }
            var glyphs = 0;
            foreach (var c in value) if (!char.IsWhiteSpace(c)) glyphs++;
            reveal.Reduced = reduced;
            reveal.Prepare(id.GetHashCode(), glyphs);
        }

        public void Present(QuizTimeline timeline, bool withPlayers)
        {
            reveal.SetTime(timeline.Elapsed);
            var motion = ArtButton.Smoother(timeline.MoveProgress);
            var scale = 1 + (QuizTimeline.FinalQuestionScale - 1) * motion;
            rect.localScale = new Vector3(scale, scale, 1);
            rect.anchoredPosition = new Vector2(frame.x / 2, frame.y * (.55f + (withPlayers ? .15f : .19f) * motion));
        }
    }

    /// <summary>AnswerView: horror numeral + wrapped answer, tilted; on reveal the correct one grows ×1.4, others fade.</summary>
    public sealed class AnswerView
    {
        public const float CorrectScale = 1.40f;
        static readonly float[][] Placement = { new[] { .53f, .60f, 2f }, new[] { .35f, .455f, -2f }, new[] { .54f, .31f, 1f }, new[] { .40f, .165f, -1f } };
        readonly RectTransform rect;
        readonly CanvasGroup group;
        readonly Text number, answer;
        readonly Vector2 frame;
        readonly int index;
        float centerX, centerY, tilt;

        public AnswerView(RectTransform design, int index, Font numberFont, Font textFont, Vector2 size)
        {
            this.index = index;
            frame = size;
            rect = UiKit.Rect("Answer" + (index + 1), design);
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.pivot = new Vector2(.5f, .5f);
            group = rect.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            number = UiKit.Label(rect, (index + 1).ToString(), 112, GenerationCodeView.Hex("D4D900"), TextAnchor.MiddleLeft);
            number.font = numberFont;
            number.horizontalOverflow = HorizontalWrapMode.Overflow;
            answer = UiKit.Label(rect, "", 48, GenerationCodeView.Hex("F2EFE9"), TextAnchor.MiddleLeft);
            answer.font = textFont;
            answer.gameObject.AddComponent<Shadow>().effectColor = new Color(.06f, .02f, .02f, .8f);
        }

        public void Prepare(string value)
        {
            var p = Placement[index];
            centerX = frame.x * p[0];
            centerY = frame.y * p[1];
            tilt = p[2];
            var numberWidth = number.preferredWidth;
            var numberHeight = number.preferredHeight;
            var maxWidth = frame.x * .35f - numberWidth - 18;
            var maxHeight = frame.y * .09f;
            answer.text = value;
            answer.rectTransform.sizeDelta = new Vector2(maxWidth, maxHeight);
            var size = 48;
            var settings = answer.GetGenerationSettings(new Vector2(maxWidth, 0));
            float width = maxWidth, height = maxHeight;
            while (size > 10)
            {
                settings.fontSize = size;
                answer.fontSize = size;
                height = answer.cachedTextGeneratorForLayout.GetPreferredHeight(value, settings) / answer.pixelsPerUnit;
                width = Mathf.Min(maxWidth, answer.cachedTextGeneratorForLayout.GetPreferredWidth(value, answer.GetGenerationSettings(Vector2.zero)) / answer.pixelsPerUnit);
                if (height <= maxHeight) break;
                size = Mathf.RoundToInt(size * .94f);
            }
            var total = new Vector2(numberWidth + 18 + width, Mathf.Max(numberHeight, height));
            rect.sizeDelta = total;
            UiKit.Place(number.rectTransform, 0, (total.y - numberHeight) / 2, numberWidth, numberHeight);
            UiKit.Place(answer.rectTransform, numberWidth + 18, (total.y - height) / 2, width + 2, height);
            Present(0, false, -1, false);
        }

        /// <summary>resultSeconds &lt; 0 while the clock still runs/closes/falls (no independent timers).</summary>
        public void Present(float progress, bool correct, double resultSeconds, bool reduced)
        {
            var intro = ArtButton.Smoother(Mathf.Clamp01(progress));
            var grow = resultSeconds < 0 ? 0 : ArtButton.Smoother((float)System.Math.Min(1, resultSeconds / QuizTimeline.CorrectGrowSeconds));
            var fade = resultSeconds < 0 ? 0 : ArtButton.Smoother((float)System.Math.Min(1, resultSeconds / QuizTimeline.WrongFadeSeconds));
            group.alpha = intro * (correct ? 1 : 1 - fade);
            rect.gameObject.SetActive(group.alpha > 0);
            rect.localEulerAngles = new Vector3(0, 0, reduced ? 0 : tilt);
            var scale = (reduced ? 1 : .9f + .1f * intro) * (correct ? 1 + (CorrectScale - 1) * grow : 1);
            rect.localScale = new Vector3(scale, scale, 1);
            float x = centerX, y = centerY;
            if (correct && grow > 0)
            {
                // Keep the enlarged bounds inside the scene and below the question.
                var halfW = ExtentX(CorrectScale);
                var halfH = ExtentY(CorrectScale);
                x += (Mathf.Clamp(centerX, frame.x * .035f + halfW, frame.x * .965f - halfW) - centerX) * grow;
                y += (Mathf.Clamp(centerY, frame.y * .09f + halfH, frame.y * .655f - halfH) - centerY) * grow;
            }
            if (!reduced) y -= 10 * (1 - intro) + (correct ? 0 : 9 * fade);
            rect.anchoredPosition = new Vector2(x, y);
        }

        float ExtentX(float scale) => (Mathf.Abs(Mathf.Cos(tilt * Mathf.Deg2Rad)) * rect.sizeDelta.x + Mathf.Abs(Mathf.Sin(tilt * Mathf.Deg2Rad)) * rect.sizeDelta.y) * scale / 2;
        float ExtentY(float scale) => (Mathf.Abs(Mathf.Sin(tilt * Mathf.Deg2Rad)) * rect.sizeDelta.x + Mathf.Abs(Mathf.Cos(tilt * Mathf.Deg2Rad)) * rect.sizeDelta.y) * scale / 2;
    }

    /// <summary>
    /// ClockTimerActor: layered gothic clock with a 120° red remaining-time sector, a tension shake that grows
    /// in the last five seconds, the lid closing over seven frames and a fall off-screen.
    /// </summary>
    public sealed class ClockView
    {
        const float BodyCenterX = 656, BodyCenterY = 598, BodyWidth = 820, HandPivotX = 656, HandPivotY = 838;
        const float CanvasW = 1312, CanvasH = 1199, FaceRadius = 260, HandScale = .30f;
        static readonly int[] FrameFiles = { 1, 4, 5, 6, 8, 10, 11 };
        static readonly float[] FrameCenterY = { 598, 653, 647, 641, 661, 659, 665 };
        static readonly float[] FrameScale = { 1, 820f / 951, 820f / 926, 820f / 951, 820f / 1017, 820f / 1015, 820f / 962 };

        readonly RectTransform root;
        readonly CanvasGroup group;
        readonly Image baseFrame, overlayFrame, sector, hand;
        readonly Sprite[] frames = new Sprite[FrameFiles.Length];
        readonly float x, y, width;

        public ClockView(RectTransform design, ArtCatalog art, Vector2 size)
        {
            width = size.x * .20f;
            x = size.x * .74f;
            y = size.y * .12f;
            root = UiKit.Stretch(UiKit.Rect("Clock", design));
            group = root.gameObject.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            for (var i = 0; i < frames.Length; i++) frames[i] = art.Sprite("Art/Quiz/Clock/clock" + FrameFiles[i]);
            baseFrame = Layer("Frame", frames[0]);
            sector = Layer("Sector", EffectAssets.Disk);
            sector.type = Image.Type.Filled;
            sector.fillMethod = Image.FillMethod.Radial360;
            sector.fillOrigin = (int)Image.Origin360.Top;
            sector.fillClockwise = true;
            hand = Layer("Hand", art.Sprite("Art/Quiz/Clock/clock2"));
            overlayFrame = Layer("Closing", frames[1]);
        }

        Image Layer(string name, Sprite sprite)
        {
            var image = UiKit.Rect(name, root).gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = false;
            var r = image.rectTransform;
            r.anchorMin = r.anchorMax = Vector2.zero;
            return image;
        }

        public void Present(QuizTimeline t, bool reduced)
        {
            var state = t.Clock;
            var entrance = reduced ? 1 : t.ClockEntrance;
            if (entrance <= 0 || state == QuizTimeline.ClockState.Finished) { root.gameObject.SetActive(false); return; }
            root.gameObject.SetActive(true);
            group.alpha = entrance;
            var scale = width / BodyWidth;
            var cx = x + width / 2;
            var cy = y + (CanvasH - BodyCenterY) * scale;
            if (!reduced && state == QuizTimeline.ClockState.Running)
            {
                var remaining = t.ClockRemaining;
                var envelope = (float)System.Math.Min(1, System.Math.Min((QuizTimeline.AnswerSeconds - remaining) / .4, remaining / .25));
                var urgency = (float)System.Math.Max(0, 1 - remaining / 5);
                var amplitude = width * .002f * envelope * (1 + urgency * .35f);
                cx += amplitude * (Mathf.Sin((float)remaining * 17) + .3f * Mathf.Sin((float)remaining * 29));
                cy += amplitude * .65f * Mathf.Sin((float)remaining * 19 + .7f);
            }
            if (state == QuizTimeline.ClockState.Falling)
            {
                var p = t.FallingProgress;
                cy -= (y + CanvasH * scale + 32) * p * p * p;
            }

            var open = state == QuizTimeline.ClockState.Waiting || state == QuizTimeline.ClockState.Running;
            sector.gameObject.SetActive(open && t.ClockAngle > 0);
            overlayFrame.gameObject.SetActive(!open);
            if (open)
            {
                Frame(baseFrame, 0, cx, cy, scale, 1);
                var radius = FaceRadius * scale;
                Set(sector.rectTransform, cx - radius, cy - radius, radius * 2, radius * 2, 0, Vector2.one * .5f);
                sector.fillAmount = t.ClockAngle / 360f;
                var remaining = t.ClockRemaining;
                var urgency = remaining <= 5 ? (float)(1 - remaining / 5) : 0;
                var pulse = reduced ? 0 : .5f + .5f * Mathf.Sin((float)remaining * Mathf.PI * 2);
                var opacity = .46f + urgency * (.04f + .02f * pulse);
                sector.color = new Color((116 + urgency * 12) / 255f, 5 / 255f, 14 / 255f, GammaMatchedAlpha(opacity));
                Hand(cx, cy, scale, -t.ClockAngle);
            }
            else
            {
                var position = t.ClosingProgress * (frames.Length - 1);
                var index = Mathf.Min(frames.Length - 1, (int)position);
                var mix = position - index;
                mix = mix * mix * (3 - 2 * mix);
                Frame(baseFrame, index, cx, cy, scale, 1);
                hand.gameObject.SetActive(index == 0);
                if (index == 0) Hand(cx, cy, scale, 0);
                overlayFrame.gameObject.SetActive(index + 1 < frames.Length);
                if (index + 1 < frames.Length) Frame(overlayFrame, index + 1, cx, cy, scale, mix);
            }
        }

        /// <summary>
        /// libGDX blends in gamma space, this project in Linear. For a dark tint over the light dial, the alpha that
        /// reproduces the gamma-space result is noticeably higher (≈ ×1.4 at the Java opacity of 0.46).
        /// </summary>
        static float GammaMatchedAlpha(float gammaAlpha)
        {
            if (QualitySettings.activeColorSpace != ColorSpace.Linear) return gammaAlpha;
            const float face = .85f, tint = .46f; // representative dial and sector values (gamma)
            var target = Mathf.GammaToLinearSpace((1 - gammaAlpha) * face + gammaAlpha * tint);
            float faceLinear = Mathf.GammaToLinearSpace(face), tintLinear = Mathf.GammaToLinearSpace(tint);
            return Mathf.Clamp01((faceLinear - target) / (faceLinear - tintLinear));
        }

        void Frame(Image image, int index, float cx, float cy, float scale, float alpha)
        {
            image.sprite = frames[index];
            image.color = new Color(1, 1, 1, alpha);
            var s = scale * FrameScale[index];
            Set(image.rectTransform, cx - BodyCenterX * s, cy - (CanvasH - FrameCenterY[index]) * s, CanvasW * s, CanvasH * s, 0, Vector2.zero);
        }

        void Hand(float cx, float cy, float scale, float rotation)
        {
            hand.gameObject.SetActive(true);
            var s = scale * HandScale;
            var pivot = new Vector2(HandPivotX / CanvasW, (CanvasH - HandPivotY) / CanvasH);
            var r = hand.rectTransform;
            r.pivot = pivot;
            r.sizeDelta = new Vector2(CanvasW * s, CanvasH * s);
            r.anchoredPosition = new Vector2(cx, cy);
            r.localEulerAngles = new Vector3(0, 0, rotation);
        }

        static void Set(RectTransform r, float left, float bottom, float w, float h, float rotation, Vector2 pivot)
        {
            r.pivot = Vector2.zero;
            r.sizeDelta = new Vector2(w, h);
            r.anchoredPosition = new Vector2(left, bottom);
            r.localEulerAngles = new Vector3(0, 0, rotation);
        }
    }

    /// <summary>
    /// QuestionPlayers: the room's characters above the question. Thinking players are smaller and tremble while
    /// answers are visible; a submitted player grows back and animates; correct answers get a mint check.
    /// </summary>
    public sealed class PlayersRow
    {
        public const float ThinkingScale = 1f / 1.35f, GrowSeconds = .30f, AnimationSpeed = 1.5f;
        static readonly string[] ColorIds = { "ghost", "dracula", "plague_doctor", "puppeteer", "skeleton", "zombie", "pumpkin", "voodoo" };
        static readonly string[] LightColors = { "77DDD0", "EF8A9B", "E9C17B", "C4A0E8", "EEE0B6", "A9CC78", "F0A36C", "8EBFE8" };

        sealed class Entry
        {
            public string Id, CharacterId;
            public RectTransform Root, Body;
            public Flipbook Book;
            public Text Name, Status, Check;
            public float Time = 8, Size = ThinkingScale, From = ThinkingScale, Target = ThinkingScale, Age = GrowSeconds, Shake, Envelope, Phase;
            public bool Submitted, Online, Ghost, Correct;
        }

        readonly RectTransform root;
        readonly CageCharacterLibrary characters;
        readonly Font font;
        readonly List<Entry> entries = new List<Entry>();
        readonly float width;

        public PlayersRow(RectTransform design, CageCharacterLibrary characters, Font font, Vector2 size)
        {
            this.characters = characters;
            this.font = font;
            width = size.x;
            root = UiKit.Place(UiKit.Rect("Players", design), 0, size.y - 180, size.x, 180);
        }

        public int Count => entries.Count;

        public void Synchronize(FinalAnswer.Contracts.RoomSnapshot s, bool newQuestion)
        {
            var participants = new HashSet<string>();
            if (s.Raw["participantIds"] is Newtonsoft.Json.Linq.JArray ids) foreach (var id in ids) participants.Add(id.ToString());
            var results = new Dictionary<string, bool>();
            if ((s.Phase == "RESULTS" || s.Phase == "GAME_FINISHED") && s.Raw["results"] is Newtonsoft.Json.Linq.JArray rows)
                foreach (var row in rows)
                    results[row.Value<string>("playerId") ?? ""] = (row.Value<bool?>("isCorrect") ?? false) && (row.Value<bool?>("answeredInTime") ?? true);
            var present = new HashSet<string>();
            foreach (var p in s.Players)
            {
                if (!p.IsGhost && participants.Count > 0 && !participants.Contains(p.Id)) continue;
                present.Add(p.Id);
                var e = entries.Find(x => x.Id == p.Id) ?? Add(p.Id, p.CharacterId);
                if (newQuestion) { e.Submitted = false; e.Correct = false; }
                e.Online = p.Online;
                e.Ghost = p.IsGhost;
                e.Submitted |= p.HasSubmittedAnswer;
                e.Correct = results.TryGetValue(p.Id, out var ok) && ok;
                e.Name.text = p.Name;
                var target = e.Submitted ? 1 : ThinkingScale;
                if (target != e.Target) { e.From = e.Size; e.Target = target; e.Age = 0; }
            }
            for (var i = entries.Count - 1; i >= 0; i--)
            {
                if (present.Contains(entries[i].Id)) continue;
                UiKit.Dispose(entries[i].Root.gameObject);
                entries.RemoveAt(i);
            }
            // Seat order always follows the server roster, never the order in which entries were created.
            var order = new List<string>();
            foreach (var p in s.Players) order.Add(p.Id);
            entries.Sort((a, b) => order.IndexOf(a.Id).CompareTo(order.IndexOf(b.Id)));
            Layout();
        }

        Entry Add(string id, string characterId)
        {
            var e = new Entry { Id = id, CharacterId = characterId, Phase = (id.GetHashCode() & 0xffff) * .001f };
            e.Root = UiKit.Rect("Player", root);
            e.Root.anchorMin = e.Root.anchorMax = Vector2.zero;
            e.Body = UiKit.Rect("Body", e.Root);
            e.Body.anchorMin = e.Body.anchorMax = Vector2.zero;
            e.Body.pivot = new Vector2(.5f, 0);
            e.Book = characters.Create(e.Body, characterId);
            e.Name = UiKit.Label(e.Root, "", 26, Light(characterId), TextAnchor.MiddleCenter);
            e.Name.font = font;
            e.Name.resizeTextForBestFit = true;
            e.Name.resizeTextMinSize = 10;
            e.Name.resizeTextMaxSize = 26;
            e.Name.gameObject.AddComponent<Shadow>().effectColor = new Color(.015f, .008f, .008f, .9f);
            e.Status = UiKit.Label(e.Root, "", 12, new Color(.77f, .86f, .66f), TextAnchor.MiddleCenter);
            e.Status.font = font;
            e.Check = UiKit.Label(e.Root, "✔", 30, new Color(.65f, .96f, .70f), TextAnchor.MiddleCenter, FontStyle.Bold);
            e.Check.gameObject.AddComponent<Outline>().effectColor = new Color(.015f, .04f, .025f);
            entries.Add(e);
            return e;
        }

        void Layout()
        {
            if (entries.Count == 0) return;
            var cell = Mathf.Min(240, (width - 120) / entries.Count);
            var left = (width - cell * entries.Count) / 2;
            for (var i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                UiKit.Place(e.Root, left + i * cell, 0, cell, 180);
                UiKit.Place(e.Name.rectTransform, 8, 30, cell - 16, 26);
                UiKit.Place(e.Status.rectTransform, 8, 8, cell - 16, 16);
                UiKit.Place(e.Check.rectTransform, cell * .5f + cell * .30f - 20, 122, 40, 40);
            }
        }

        public void Advance(float delta, bool answersVisible, bool activeRound, bool reduced)
        {
            if (entries.Count == 0) return;
            var cell = Mathf.Min(240, (width - 120) / entries.Count);
            foreach (var e in entries)
            {
                e.Age += delta;
                var growth = reduced ? 1 : ArtButton.Smooth(Mathf.Min(1, e.Age / GrowSeconds));
                e.Size = e.From + (e.Target - e.From) * growth;
                var thinking = activeRound && e.Online && !e.Submitted;
                var nervous = answersVisible && thinking && !reduced;
                if (nervous) e.Shake += delta;
                e.Envelope = activeRound ? Mathf.Clamp01(e.Envelope + (nervous ? 1 : -1) * delta / .14f) : 0;
                var playing = activeRound && e.Online && e.Submitted || e.Ghost;
                if (playing) e.Time += delta * AnimationSpeed;
                e.Book.Time = e.Time;
                var shakeX = reduced ? 0 : Mathf.Sin(e.Shake * 31 + e.Phase) * .65f * e.Envelope;
                var shakeY = reduced ? 0 : Mathf.Sin(e.Shake * 27 + e.Phase * 1.7f) * .28f * e.Envelope;
                // Baked frames are 140×163 design units of the cage opening; fit ~135 px tall like the Java rig scale.
                // Baked frames are the 140×163 cage opening; the Java rig is fitted to ~135 px.
                var scale = Mathf.Min(130f / 163f, (cell - 24) / 150f) * e.Size;
                e.Body.sizeDelta = new Vector2(140, 163);
                e.Body.localScale = new Vector3(scale, scale, 1);
                e.Body.anchoredPosition = new Vector2(cell / 2 + shakeX, 50 + shakeY);
                e.Book.Image.color = e.Ghost ? new Color(.63f, .78f, .88f, .52f) : Color.white;
                e.Status.text = e.Ghost ? "GHOST" : !e.Online ? "OFFLINE" : "ALIVE";
                e.Status.color = e.Ghost ? new Color(.60f, .80f, .93f) : new Color(.77f, .86f, .66f);
                e.Check.gameObject.SetActive(e.Correct);
            }
        }

        static Color Light(string id)
        {
            var i = System.Array.IndexOf(ColorIds, id);
            if (i < 0) i = (id.GetHashCode() & 0x7fffffff) % 8;
            return GenerationCodeView.Hex(LightColors[i]);
        }
    }
}

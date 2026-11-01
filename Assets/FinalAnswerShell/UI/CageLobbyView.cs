using System;
using System.Collections.Generic;
using FinalAnswer.Contracts;
using FinalAnswer.Domain;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace FinalAnswer.UI
{
    /// <summary>Pure cage timeline (CageAnimator.java): spawn, look, gate close + impact, key, locked idle.</summary>
    public sealed class CageTimeline
    {
        public const float Spawn = .5f, Look = 1f, Close = .6f, Recoil = .4f, Key = .9f, Sad = .95f;
        public const float CloseAt = Spawn + Look, ImpactAt = CloseAt + Close, KeyAt = ImpactAt + .12f, SadAt = CloseAt, LockedAt = SadAt + Sad;

        public float Time { get; private set; }
        public bool Occupied { get; private set; }
        public bool ImpactNow { get; private set; }
        public bool LockNow { get; private set; }

        public void Start() { Occupied = true; Time = 0; ImpactNow = LockNow = false; }
        public void Reset() { Occupied = ImpactNow = LockNow = false; Time = 0; }

        public void Update(float delta)
        {
            ImpactNow = LockNow = false;
            if (!Occupied || float.IsNaN(delta) || delta <= 0) return;
            var before = Time;
            Time += delta;
            ImpactNow = before < ImpactAt && Time >= ImpactAt;
            LockNow = before < KeyAt + Key * .6f && Time >= KeyAt + Key * .6f;
        }

        /// <summary>Brief damped reaction at the gate impact only; degrees.</summary>
        public float ImpactRotation()
        {
            var age = Time - ImpactAt;
            return Occupied && age >= 0 && age < .4f ? Mathf.Sin(age * 45) * (1 - age / .4f) * .32f : 0;
        }

        public float Gate() { var p = Progress(Time, CloseAt, Close); return p * p * p; }

        public static float Progress(float time, float start, float duration) => Mathf.Clamp01((time - start) / duration);
        public static float Smooth(float p) => p * p * (3 - 2 * p);

        /// <summary>LobbyStartAnimation: blackout and the launch-only cage swing.</summary>
        public static float Darkness(float seconds) { var p = Mathf.Clamp01((seconds - .4f) / 2.1f); return p * p * (3 - 2 * p); }

        public static float Departure(float seconds, float phase)
        {
            if (seconds <= 0) return 0;
            var p = Mathf.Min(1, seconds / .35f);
            return p * p * (3 - 2 * p) * Mathf.Sin(seconds * 5 + phase) * 3.8f;
        }
    }

    /// <summary>Stable slot per player ID (CageAssignments.java): reorder/disconnect keeps the slot; removal frees it.</summary>
    public sealed class CageAssignments
    {
        readonly string[] slots = new string[8];
        public string PlayerAt(int slot) => slots[slot];

        public void Synchronize(IReadOnlyList<string> players)
        {
            var present = new HashSet<string>(players);
            for (var i = 0; i < slots.Length; i++) if (slots[i] != null && !present.Contains(slots[i])) slots[i] = null;
            foreach (var id in players)
            {
                if (Array.IndexOf(slots, id) >= 0) continue;
                for (var i = 0; i < slots.Length; i++) if (slots[i] == null) { slots[i] = id; break; }
            }
        }

        public void Clear() => Array.Clear(slots, 0, slots.Length);
    }

    /// <summary>One suspended cage (HangingCage.java) in 1920×1080 design space.</summary>
    public sealed class HangingCageView
    {
        const float Width = 300, Height = 333, RingX = 149, RingY = 324, KeyholeX = 226.4f, KeyholeY = 67.7f;
        const float Scale = .93f, Ceiling = 1035;

        readonly int slot;
        readonly float phase;
        readonly RectTransform cage, gateBars, key;
        readonly Image keyImage;
        readonly RectTransform characterMask;
        readonly Text nameplate;
        readonly CageCharacterLibrary characters;
        readonly CageTimeline timeline = new CageTimeline();
        Flipbook character;
        string characterId = "";
        float departure;

        public string PlayerId { get; private set; }
        public float Rotation { get; private set; }
        public float Pan => Mathf.Clamp((RingXFor(slot) / 1920f * 2 - 1) * .65f, -.65f, .65f);

        public static float RingXFor(int slot) => 642 + 161 * slot;
        static readonly float[] RingHeights = { 620, 955, 627, 960, 618, 950, 625, 958 };

        public HangingCageView(Transform design, ArtCatalog art, Font nameFont, CageCharacterLibrary characters, int slot)
        {
            this.slot = slot;
            this.characters = characters;
            phase = slot * 2.39996f;
            float ringX = RingXFor(slot), ringY = RingHeights[slot];

            // Hook and chain live in parent space and never rotate; the ring is the rotation origin, so it stays put.
            UiKit.Picture(design, "Hook" + slot, art.Crop("Art/Lobby/Cages/chain_hook", 54, 80, 1175, 1080), ringX - 12.5f, Ceiling - 23, 25, 23);
            const float strip = 66;
            var chainWidth = strip * 152f / 1536f;
            var top = Ceiling - 20;
            var ringScreenY = ringY;
            var length = top - ringScreenY;
            for (float offset = 0; offset < length; offset += strip)
            {
                var segment = Mathf.Min(strip, length - offset);
                var sprite = art.Crop("Art/Lobby/Cages/chain", 436, 0, 152, Mathf.Max(1, Mathf.RoundToInt(1536 * segment / strip)));
                UiKit.Picture(design, "Chain" + slot, sprite, ringX - chainWidth / 2, top - offset - segment, chainWidth, segment);
            }

            cage = UiKit.Rect("Cage" + slot, design);
            cage.anchorMin = cage.anchorMax = Vector2.zero;
            cage.sizeDelta = new Vector2(Width, Height);
            cage.pivot = new Vector2(RingX / Width, RingY / Height);
            cage.anchoredPosition = new Vector2(ringX, ringY);
            cage.localScale = new Vector3(Scale, Scale, 1);
            var body = UiKit.Stretch(UiKit.Rect("Body", cage)).gameObject.AddComponent<Image>();
            body.sprite = art.Sprite("Art/Lobby/Cages/cage_open");
            body.raycastTarget = false;

            // Character and bars are confined to the front opening (stencil masks follow the rotation).
            characterMask = Clip(cage, "CharacterClip", 108, 62, 140, 163);
            var gateClip = Clip(cage, "GateClip", 91, 58, 177, 179);
            gateBars = UiKit.Rect("Bars", gateClip);
            gateBars.anchorMin = gateBars.anchorMax = Vector2.zero;
            gateBars.pivot = Vector2.zero;
            gateBars.sizeDelta = new Vector2(146.88f, 215.68f);
            var bars = gateBars.gameObject.AddComponent<Image>();
            bars.sprite = art.Crop("Art/Lobby/Cages/cage_bars", 116, 4, 918, 1348);
            bars.raycastTarget = false;

            key = UiKit.Rect("Key", cage);
            key.anchorMin = key.anchorMax = Vector2.zero;
            key.sizeDelta = new Vector2(60, 120);
            key.pivot = new Vector2(30f / 60f, 15f / 120f); // shaft end is the pivot
            keyImage = key.gameObject.AddComponent<Image>();
            keyImage.sprite = art.Crop("Art/Lobby/Cages/cage_key", 330, 0, 625, 1220);
            keyImage.raycastTarget = false;

            nameplate = UiKit.Label(cage, "", 16, new Color(.105f, .055f, .025f), TextAnchor.MiddleCenter);
            nameplate.font = nameFont;
            nameplate.resizeTextForBestFit = true;
            nameplate.resizeTextMinSize = 6;
            nameplate.resizeTextMaxSize = 16;
            nameplate.horizontalOverflow = HorizontalWrapMode.Wrap;
            UiKit.Place(nameplate.rectTransform, 126, 27, 101, 18);
            // Nothing transient may show before the first Update (empty cages have no key or dropped gate).
            key.gameObject.SetActive(false);
            gateBars.gameObject.SetActive(false);
            Clear();
        }

        static RectTransform Clip(Transform parent, string name, float x, float y, float w, float h)
        {
            var rect = UiKit.Place(UiKit.Rect(name, parent), x, y, w, h);
            var image = rect.gameObject.AddComponent<Image>();
            image.raycastTarget = false;
            rect.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            return rect;
        }

        public void Assign(string playerId, string name, string selectedCharacter)
        {
            nameplate.text = (name ?? "").Replace('\n', ' ').Trim();
            if (playerId == PlayerId && selectedCharacter == characterId) return;
            var isNewPlayer = playerId != PlayerId;
            PlayerId = playerId;
            if (selectedCharacter != characterId || character == null)
            {
                if (character != null) UiKit.Dispose(character.gameObject);
                characterId = selectedCharacter;
                character = characters.Create(characterMask, characterId);
            }
            if (isNewPlayer) timeline.Start();
        }

        public void Clear()
        {
            PlayerId = null;
            characterId = "";
            nameplate.text = "";
            if (character != null) UiKit.Dispose(character.gameObject);
            character = null;
            timeline.Reset();
        }

        public void SetDeparture(float seconds, bool reducedMotion) => departure = reducedMotion ? 0 : CageTimeline.Departure(seconds, phase);

        public void Update(float delta, bool reducedMotion, ShellAudio audio)
        {
            timeline.Update(delta);
            if (timeline.ImpactNow) audio?.Gate(slot, Pan);
            if (timeline.LockNow) audio?.Lock(slot, Pan);
            Rotation = reducedMotion ? 0 : timeline.ImpactRotation() + departure;
            cage.localEulerAngles = new Vector3(0, 0, Rotation);
            if (character != null) character.Time = timeline.Time;

            var gate = timeline.Occupied ? timeline.Gate() : 0;
            gateBars.gameObject.SetActive(gate > 0);
            // Bars local to the gate clip (whose origin is at 91, 58 in cage space).
            gateBars.anchoredPosition = new Vector2(104.5f - 91, 42.1f - 58 + (1 - gate) * 215.68f);

            var p = (timeline.Time - CageTimeline.KeyAt) / CageTimeline.Key;
            var visible = timeline.Occupied && p > 0 && p < 1;
            key.gameObject.SetActive(visible);
            if (visible)
            {
                var insertion = CageTimeline.Smooth(CageTimeline.Progress(p, 0, .27f));
                var removal = CageTimeline.Smooth(CageTimeline.Progress(p, .79f, .21f));
                var turn = CageTimeline.Smooth(CageTimeline.Progress(p, .3f, .22f));
                var back = CageTimeline.Smooth(CageTimeline.Progress(p, .64f, .14f));
                var s = 1.1f - .18f * insertion + .18f * removal;
                key.anchoredPosition = new Vector2(KeyholeX + 45 * (1 - insertion + removal), KeyholeY);
                key.localScale = new Vector3(s, s, 1);
                key.localEulerAngles = new Vector3(0, 0, 90 * (turn - back));
                keyImage.color = new Color(1, 1, 1, Mathf.Min(1, Mathf.Min(p * 12, (1 - p) * 12)));
            }
            nameplate.gameObject.SetActive(PlayerId != null);
        }
    }

    /// <summary>Baked Java rig flipbooks (tools/unity/bake_cage_characters.sh); sprites are created once per character.</summary>
    public sealed class CageCharacterLibrary
    {
        public static readonly string[] Ids = { "skeleton", "ghost", "zombie", "dracula", "pumpkin", "plague_doctor", "voodoo", "puppeteer" };
        readonly ArtCatalog art;
        readonly Dictionary<string, (Sprite[] frames, int fps, int intro)> cache = new Dictionary<string, (Sprite[], int, int)>();

        public CageCharacterLibrary(ArtCatalog art) { this.art = art; }

        public Flipbook Create(Transform clip, string id)
        {
            if (Array.IndexOf(Ids, id) < 0) id = "skeleton"; // older identifiers keep the skeleton fallback
            if (!cache.TryGetValue(id, out var sheet))
            {
                var meta = JObject.Parse(art.Text("Art/Lobby/Characters/" + id + ".json").text);
                int w = meta.Value<int>("frameWidth"), h = meta.Value<int>("frameHeight");
                sheet = (art.Frames("Art/Lobby/Characters/" + id, w, h, meta.Value<int>("columns"), meta.Value<int>("totalFrames")),
                    meta.Value<int>("fps"), meta.Value<int>("introFrames"));
                cache[id] = sheet;
            }
            var book = Flipbook.Create(clip, id, sheet.frames, sheet.fps, sheet.intro);
            UiKit.Stretch(book.Image.rectTransform);
            book.PingPong = true;
            book.Paused = true; // driven by the cage timeline
            return book;
        }
    }

    /// <summary>Port of CageLobbyScreen (room view) including the STARTING blackout/round title transition.</summary>
    public sealed class CageLobbyView
    {
        public RectTransform Root { get; }
        readonly Localization texts;
        readonly ShellAudio audio;
        readonly HangingCageView[] cages = new HangingCageView[8];
        readonly CageAssignments assignments = new CageAssignments();
        readonly Text code, roomName, playerCount, connectedCount, status, roundTitle;
        readonly Button play, impostor, cursed, lastCommand;
        readonly Image blackout;
        readonly ConnectionPanel phone;
        readonly List<GameObject> chrome = new List<GameObject>();
        bool cinematic;
        string roomCode = "";
        bool reducedMotion, starting, pendingStart;
        float pendingSeconds;
        long transitionStartedAt;

        public CageLobbyView(Transform parent, ArtCatalog art, Localization texts, ShellAudio audio, bool reduced,
            Func<IReadOnlyList<ConnectionPanel.Address>> addresses,
            Action startGame, Action startImpostor, Action startCursed, Action startLastCommand, Action menu, Action leave)
        {
            this.texts = texts;
            this.audio = audio;
            reducedMotion = reduced;
            Root = UiKit.Stretch(UiKit.Rect("CageLobby", parent));
            Root.gameObject.AddComponent<Image>().color = new Color(.025f, .02f, .025f);
            var design = UiKit.DesignFrame(Root, 1920, 1080);
            UiKit.Picture(design, "Background", art.Sprite("Art/Lobby/background_cages"), 0, 0, 1920, 1080);

            var nameFont = art.Font("Fonts/NotoSans-SemiCondensedSemiBold");
            var library = new CageCharacterLibrary(art);
            for (var i = 0; i < cages.Length; i++) cages[i] = new HangingCageView(design, art, nameFont, library, i);

            var body = art.Font("Fonts/NotoSans-Regular");
            code = Label(design, art.Font("Fonts/RoadRage-Regular"), 66, "392014", 128, 503, 216, 94);
            roomName = Label(design, body, 26, "E9D8BE", 60, 310, 390, 50);
            playerCount = Label(design, body, 32, "E9D8BE", 60, 255, 390, 50);
            connectedCount = Label(design, body, 19, "CFB998", 60, 215, 390, 35);
            status = Label(design, body, 20, "CFB998", 650, 280, 1100, 32);
            status.alignment = TextAnchor.MiddleLeft;

            phone = new ConnectionPanel(design, body, addresses);
            UiKit.Place(phone.Root, 650, 145, 1100, 130);

            play = Button(design, body, texts.Get("lobby.start_game"), 650, 73, 250, 60, () => { startGame(); StartRequested(); });
            impostor = Button(design, body, "THE IMPOSTOR", 930, 73, 250, 60, startImpostor);
            cursed = Button(design, body, "CURSED BOXES", 60, 73, 390, 60, startCursed);
            lastCommand = Button(design, body, "THE LAST COMMAND", 60, 145, 520, 54, startLastCommand);
            Button(design, body, "МЕНЮ", 1210, 73, 250, 60, menu);
            Button(design, body, texts.Get("lobby.leave"), 1490, 73, 250, 60, leave);

            foreach (var item in new Component[] { code, roomName, playerCount, connectedCount, status, play, impostor, cursed, lastCommand })
                chrome.Add(item.gameObject);
            chrome.Add(phone.Root.gameObject);
            for (var i = design.childCount - 2; i < design.childCount; i++) chrome.Add(design.GetChild(i).gameObject); // MENU, LEAVE

            blackout = UiKit.Picture(design, "Blackout", null, 0, 0, 1920, 1080);
            blackout.color = new Color(0, 0, 0, 0);
            roundTitle = Label(design, art.Font("Fonts/RubikWetPaint-Regular"), 48, "DCC9AE", 220, 440, 1480, 160);
            roundTitle.gameObject.SetActive(false);
            audio?.Room(true);
            Root.gameObject.AddComponent<Ticker>().Tick = Animate;
        }

        /// <summary>Intro mode (BasementIntro.cinematicUi): only the basement and its cages remain visible.</summary>
        public void SetCinematic(bool value)
        {
            if (cinematic == value) return;
            cinematic = value;
            foreach (var item in chrome) item.SetActive(!value);
        }

        public void StartRequested() { pendingStart = true; pendingSeconds = 0; }
        public void StartRejected() { if (!starting) pendingStart = false; }

        public void Apply(RoomSnapshot snapshot, long serverNow)
        {
            if (snapshot.RoomCode != roomCode)
            {
                assignments.Clear();
                foreach (var cage in cages) cage.Clear();
                roomCode = snapshot.RoomCode;
                phone.Room(roomCode);
            }
            var ids = new List<string>();
            foreach (var p in snapshot.Players) ids.Add(p.Id);
            assignments.Synchronize(ids);
            for (var slot = 0; slot < cages.Length; slot++)
            {
                var id = assignments.PlayerAt(slot);
                if (id == null) { if (cages[slot].PlayerId != null) cages[slot].Clear(); continue; }
                foreach (var p in snapshot.Players) if (p.Id == id) cages[slot].Assign(id, p.Name, p.CharacterId);
            }

            code.text = snapshot.RoomCode;
            roomName.text = snapshot.Name;
            var online = snapshot.OnlinePlayerCount;
            playerCount.text = texts.Format("lobby.player_count", snapshot.Players.Count, snapshot.MaxPlayers);
            connectedCount.text = texts.Format("lobby.connected_count", online, snapshot.Players.Count);

            if (snapshot.Phase == QuizPhases.Starting && snapshot.Raw["transitionStartedAt"] != null)
            {
                var start = snapshot.Raw.Value<long>("transitionStartedAt");
                if (!starting || start != transitionStartedAt)
                {
                    starting = true;
                    pendingStart = false;
                    transitionStartedAt = start;
                    roundTitle.text = snapshot.Raw.Value<string>("roundTitle") ?? "РАУНД 1";
                }
            }
            this.serverNow = serverNow;

            var ready = snapshot.IsHost && snapshot.Players.Count >= 2 && online == snapshot.Players.Count && snapshot.HostConnected;
            var locked = starting || pendingStart;
            play.interactable = !locked && ready;
            cursed.interactable = !locked && ready;
            // THE LAST COMMAND also runs with a single phone: the machine then mirrors that player's terminal.
            lastCommand.interactable = !locked && snapshot.IsHost && snapshot.Players.Count >= 1 && online == snapshot.Players.Count && snapshot.HostConnected;
            impostor.interactable = !locked && snapshot.IsHost && (online == 3 || online == 4) && online == snapshot.Players.Count;
            var notice = snapshot.HostConnected ? texts.Get("network.connected") : texts.Get("network.connecting");
            if (snapshot.IsHost && !locked && !ready) notice += " · " + texts.Error(snapshot.Players.Count < 2 ? "NOT_ENOUGH_PLAYERS" : "PLAYER_OFFLINE");
            if (snapshot.IsHost && ready && !locked) notice += " · " + texts.Get("lobby.ready_to_start");
            status.text = notice;
        }

        long serverNow;
        public void SetServerNow(long now) => serverNow = now;

        public void ShowError(string message)
        {
            StartRejected();
            if (!string.IsNullOrEmpty(message)) status.text = message;
        }

        void Animate(float delta)
        {
            if (pendingStart && (pendingSeconds += delta) > 10) StartRejected();
            // The intro replaces the start transition (BasementIntro.resetTransition): no swing, blackout or title.
            var age = starting && !cinematic ? Mathf.Max(0, (serverNow - transitionStartedAt) / 1000f) : 0;
            for (var i = 0; i < cages.Length; i++)
            {
                cages[i].SetDeparture(age, reducedMotion);
                cages[i].Update(delta, reducedMotion, audio);
                audio?.CageMotion(i, cages[i].PlayerId != null, cages[i].Rotation, delta, cages[i].Pan);
            }
            var fade = starting && !cinematic ? CageTimeline.Darkness(age) : 0;
            blackout.color = new Color(0, 0, 0, fade);
            roundTitle.gameObject.SetActive(starting && !cinematic && age > 2.25f);
            roundTitle.color = new Color(roundTitle.color.r, roundTitle.color.g, roundTitle.color.b, Mathf.Clamp01((age - 2.25f) / .6f));
            phone.Root.gameObject.SetActive(!starting && !cinematic);
        }

        public void Dispose() => audio?.Room(false);

        static Text Label(Transform parent, Font font, int size, string color, float x, float y, float w, float h)
        {
            var label = UiKit.Label(parent, "", size, GenerationCodeView.Hex(color), TextAnchor.MiddleCenter);
            label.font = font;
            UiKit.Place(label.rectTransform, x, y, w, h);
            return label;
        }

        /// <summary>CageLobbyScreen's flat button style (dark wood tints, EBDCC6 text).</summary>
        static Button Button(Transform parent, Font font, string text, float x, float y, float w, float h, Action action)
        {
            var button = UiKit.Button(parent, text, action, height: h, fontSize: h < 58 ? 20 : 24);
            var label = button.GetComponentInChildren<Text>();
            label.font = font;
            label.color = GenerationCodeView.Hex("EBDCC6");
            var colors = button.colors;
            colors.normalColor = new Color(.08f, .055f, .05f, .88f);
            colors.highlightedColor = GenerationCodeView.Hex("604131");
            colors.pressedColor = GenerationCodeView.Hex("38241D");
            colors.selectedColor = colors.normalColor;
            colors.disabledColor = new Color(.08f, .055f, .05f, .6f);
            button.colors = colors;
            button.gameObject.AddComponent<UiSoundHooks>();
            UiKit.Place((RectTransform)button.transform, x, y, w, h);
            return button;
        }
    }

    /// <summary>Phone join panel: QR (generated locally, URL only — no token/password), address, network choice.</summary>
    public sealed class ConnectionPanel
    {
        public struct Address
        {
            public string Label;
            public string Url;
        }

        public RectTransform Root { get; }
        readonly RawImage qr;
        readonly Text address, details;
        readonly Func<IReadOnlyList<Address>> source;
        Texture2D texture;
        string room = "", url = "";
        int selected;
        float elapsed;

        public string Url => url;

        public ConnectionPanel(Transform parent, Font font, Func<IReadOnlyList<Address>> addresses)
        {
            source = addresses;
            Root = UiKit.Rect("ConnectionPanel", parent);
            var frame = UiKit.Place(UiKit.Rect("QrFrame", Root), 0, 2, 126, 126);
            frame.gameObject.AddComponent<Image>().color = Color.white;
            qr = UiKit.Stretch(UiKit.Rect("Qr", frame)).gameObject.AddComponent<RawImage>();
            address = UiKit.Label(Root, "", 17, GenerationCodeView.Hex("F0DCC2"));
            address.font = font;
            UiKit.Place(address.rectTransform, 140, 92, 960, 34);
            details = UiKit.Label(Root, "", 14, GenerationCodeView.Hex("BFA9A4"), TextAnchor.UpperLeft);
            details.font = font;
            UiKit.Place(details.rectTransform, 140, 36, 960, 54);
            var choose = UiKit.Button(Root, "NETWORK / REFRESH", () => { var list = source(); if (list.Count > 0) selected = (selected + 1) % list.Count; Refresh(); }, height: 34, fontSize: 15);
            choose.gameObject.AddComponent<UiSoundHooks>();
            UiKit.Place((RectTransform)choose.transform, 140, 0, 270, 34);
            Root.gameObject.AddComponent<Ticker>().Tick = d => { elapsed += d; if (elapsed >= 1) { elapsed = 0; Refresh(); } };
        }

        public void Room(string code)
        {
            if (room == code) return;
            room = code;
            Refresh();
        }

        void Refresh()
        {
            var choices = source();
            if (selected >= choices.Count) selected = 0;
            var baseUrl = choices.Count == 0 ? "" : choices[selected].Url;
            var next = baseUrl.Length == 0 || room.Length == 0 ? "" : baseUrl.TrimEnd('/') + "/?room=" + room;
            address.text = next.Length == 0 ? "PHONE CONNECTION NOT AVAILABLE" : next;
            details.text = choices.Count == 0 ? "" : choices[selected].Label + " [" + (selected + 1) + "/" + choices.Count + "]\nТелефоны: откройте адрес или отсканируйте QR. В нём нет пароля и токена.";
            if (next == url) return;
            url = next;
            if (texture != null) UnityEngine.Object.Destroy(texture);
            texture = null;
            qr.texture = null;
            if (next.Length == 0) return;
            try
            {
                var code = QrCode.Encode(next);
                const int quiet = 4;
                var size = code.Size + quiet * 2;
                texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                var pixels = new Color32[size * size];
                for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    int mx = x - quiet, my = code.Size - 1 - (y - quiet); // texture rows run bottom-up
                    var dark = mx >= 0 && mx < code.Size && my >= 0 && my < code.Size && code[mx, my];
                    pixels[y * size + x] = dark ? new Color32(0, 0, 0, 255) : new Color32(255, 255, 255, 255);
                }
                texture.SetPixels32(pixels);
                texture.Apply(false, false);
                qr.texture = texture;
            }
            catch (Exception)
            {
                details.text = "QR unavailable. Enter the displayed URL and room code manually.";
            }
        }
    }

    /// <summary>Hover/press sounds for plain uGUI buttons (UiAudioCapture equivalent).</summary>
    public sealed class UiSoundHooks : MonoBehaviour, UnityEngine.EventSystems.IPointerEnterHandler, UnityEngine.EventSystems.IPointerClickHandler
    {
        Selectable selectable;
        void Awake() => selectable = GetComponent<Selectable>();
        public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData eventData) { if (selectable == null || selectable.interactable) ShellAudio.Current?.Hover(); }
        public void OnPointerClick(UnityEngine.EventSystems.PointerEventData eventData) { if (selectable == null || selectable.interactable) ShellAudio.Current?.Press(); }
    }
}

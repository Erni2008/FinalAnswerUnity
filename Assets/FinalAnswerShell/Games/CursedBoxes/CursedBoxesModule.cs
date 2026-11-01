using System.Collections.Generic;
using System.Linq;
using FinalAnswer.Application;
using FinalAnswer.Contracts;
using FinalAnswer.UI;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace FinalAnswer.CursedBoxes
{
    /// <summary>
    /// Host presentation of Cursed Boxes, staged in 2D from the Java scene's own art (basement, 4×4 shelf, crates,
    /// muzzle flash, smoke, splinters, found characters and ghosts) with the CursedBoxesSoundManager cue set.
    /// Phones choose; the host confirms the existing milestones (CURSED_SCENE_READY, CURSED_SHOT_STARTED/IMPACT/
    /// COMPLETE per shotId) only after the matching moment was shown.
    /// </summary>
    public sealed class CursedBoxesModule : IGameModule
    {
        static readonly Vector2 Size = new Vector2(1672, 941);   // gallery.json image space
        const float ShelfFloorY = 675, ShelfHeight = 594;        // gallery.json
        const float ShotFlightSeconds = .6f, ShotRevealSeconds = 1.2f;
        const int BoxCount = 16;
        // Cubby openings in shelf.png (1536×1024), measured from the art: x ranges per column, floor y per row (top origin).
        static readonly float[][] Columns = { new[] { 218f, 462f }, new[] { 527f, 749f }, new[] { 810f, 1039f }, new[] { 1098f, 1350f } };
        static readonly float[] RowFloors = { 255, 470, 690, 900 };
        static readonly Dictionary<string, string> ShotArt = new Dictionary<string, string>
        {
            ["plague_doctor"] = "doktor_shot", ["dracula"] = "drakula-shot", ["pumpkin"] = "pumpkin_dead", ["puppeteer"] = "pupeetee_shot",
            ["skeleton"] = "skeleton_shot", ["voodoo"] = "voddoo_shoot", ["zombie"] = "zombie_shot",
        };

        readonly Transform parent;
        readonly HashSet<string> sentMilestones = new HashSet<string>();
        readonly HashSet<string> cues = new HashSet<string>();
        ModuleContext context;
        JObject state;
        RoomSnapshot snapshot;
        RectTransform root, design;
        ArtCatalog art;
        SfxBank sfx;
        readonly Image[] crates = new Image[BoxCount];
        readonly Rect[] slots = new Rect[BoxCount];
        Image flash, smoke, debris, found, survivorsShade;
        RectTransform survivorsRow;
        Text status, details;
        ArtButton finish;
        string sessionId = "", shotKey = "", foundShot = "";
        float shotTimer, effectAge = 99, foundAge = 99, heartbeat;
        int effectBox = -1;
        bool reduced;

        public CursedBoxesModule(Transform parent) { this.parent = parent; }

        public ModuleKind Kind => ModuleKind.CursedBoxes;

        public void Enter(ModuleContext moduleContext, RoomSnapshot first)
        {
            context = moduleContext;
            art = ArtCatalog.Active;
            reduced = PlayerPrefs.GetInt("fa.v1.reducedMotion", 0) == 1;
            context.Room.CommandRejected += OnRejected;
            Build();
            ApplySnapshot(first);
        }

        public void ApplySnapshot(RoomSnapshot next)
        {
            snapshot = next;
            state = next.CursedBoxes;
            if (state == null) return;
            var id = state.Value<string>("sessionId") ?? "";
            if (id != sessionId)
            {
                // New session: semantic caches never carry over.
                sessionId = id;
                sentMilestones.Clear();
                cues.Clear();
                shotKey = "";
                Once("intro", () => HostVoice.Current?.Play("events", "cursed_boxes_intro"));
            }
            Render();
        }

        public void Tick(float delta, long serverNow)
        {
            if (state == null) return;
            if (!(state.Value<bool?>("hostReady") ?? false)) SendOnce(MessageTypes.CursedSceneReady, "ready", null);

            if (state["pendingShot"] is JObject shot)
            {
                var id = shot.Value<string>("id") ?? "";
                var started = shot.Value<long?>("startedAt") ?? 0;
                var impacted = shot.Value<long?>("impactedAt") ?? 0;
                var stage = started == 0 ? MessageTypes.CursedShotStarted : impacted == 0 ? MessageTypes.CursedShotImpact : MessageTypes.CursedShotComplete;
                var key = id + ":" + stage;
                if (key != shotKey) { shotKey = key; shotTimer = 0; }
                shotTimer += delta;
                var wait = stage == MessageTypes.CursedShotStarted ? 0 : stage == MessageTypes.CursedShotImpact ? ShotFlightSeconds : ShotRevealSeconds;
                if (shotTimer >= wait)
                {
                    var box = shot.Value<int?>("boxId") ?? -1;
                    if (stage == MessageTypes.CursedShotStarted) Once("fire:" + id, () => Fire(box));
                    if (stage == MessageTypes.CursedShotImpact) Once("impact:" + id, () => Impact(box));
                    SendOnce(stage, key, id);
                }
                // The pump-action rack follows the shot (Java pumpAt .45 s); the spent shell lands after it.
                if (stage != MessageTypes.CursedShotStarted && shotTimer >= .45f) Once("pump:" + id, () => sfx.Play("Audio/CursedBoxes/reload_done", .75f));
                if (stage != MessageTypes.CursedShotStarted && shotTimer >= .9f) Once("shell:" + id, () => sfx.Play("Audio/CursedBoxes/shell_drop", .9f, .35f));
            }

            Animate(delta);
            Heartbeat(delta);
        }

        public void Exit()
        {
            if (context != null) context.Room.CommandRejected -= OnRejected;
            sfx?.Dispose();
            if (root != null) UiKit.Dispose(root.gameObject);
            root = null;
        }

        // ---------------- staging ----------------

        void Build()
        {
            root = UiKit.Stretch(UiKit.Rect("CursedBoxes", parent));
            root.gameObject.AddComponent<Image>().color = Color.black;
            design = UiKit.DesignFrame(root, Size.x, Size.y);
            sfx = new SfxBank(root, art, "CursedSfx");
            UiKit.Picture(design, "Background", art.Sprite("Art/CursedBoxes/background"), 0, 0, Size.x, Size.y);

            var scale = ShelfHeight / 1024f;
            var shelfWidth = 1536 * scale;
            var shelfX = (Size.x - shelfWidth) / 2;
            var shelfBottom = Size.y - ShelfFloorY;
            UiKit.Picture(design, "Shelf", art.Sprite("Art/CursedBoxes/shelf"), shelfX, shelfBottom, shelfWidth, ShelfHeight);
            var intact = art.Sprite("Art/CursedBoxes/crate-intact");
            var numberFont = art.Font("Fonts/Creepster-Regular");
            for (var i = 0; i < BoxCount; i++)
            {
                int row = i / 4, col = i % 4;
                var cellLeft = shelfX + Columns[col][0] * scale;
                var cellWidth = (Columns[col][1] - Columns[col][0]) * scale;
                var width = cellWidth * .94f;
                var height = width * 163f / 274f;
                var floor = shelfBottom + (1024 - RowFloors[row]) * scale;
                slots[i] = new Rect(cellLeft + (cellWidth - width) / 2, floor, width, height);
                crates[i] = UiKit.Picture(design, "Crate" + (i + 1), intact, slots[i].x, slots[i].y, width, height);
                var number = UiKit.Label(crates[i].transform, (i + 1).ToString(), 26, new Color(.95f, .86f, .7f, .85f), TextAnchor.UpperLeft);
                number.font = numberFont;
                UiKit.Stretch(number.rectTransform, 8);
            }

            smoke = Effect("Smoke", "Art/CursedBoxes/Effects/gun_smoke", false);
            debris = Effect("Debris", "Art/CursedBoxes/debris_a", false);
            flash = Effect("Flash", "Art/CursedBoxes/Effects/muzzle_flash", true);
            found = UiKit.Picture(design, "Found", null, 0, 0, 10, 10);
            found.preserveAspect = true;
            found.gameObject.SetActive(false);

            survivorsShade = UiKit.Picture(design, "SurvivorsShade", null, 0, 0, Size.x, Size.y);
            survivorsShade.color = new Color(0, 0, 0, 0);
            survivorsRow = UiKit.Place(UiKit.Rect("Survivors", design), 0, 170, Size.x, 520);

            var display = art.Font("Fonts/RubikWetPaint-Regular");
            var body = art.Font("Fonts/NotoSans-SemiCondensedSemiBold");
            Label("CURSED BOXES", display, 54, "DCC9AE", 0, Size.y - 86, Size.x, 70);
            status = Label("", body, 28, "F2EFE9", 0, Size.y - 130, Size.x, 40);
            details = Label("", body, 22, "CFB998", 0, 18, Size.x, 34);
            finish = ArtButton.Plate(design, "ПРОДОЛЖИТЬ", ReturnToLobby, 36);
            UiKit.Place(finish.Rect, (Size.x - 380) / 2, 50, 380, 380 * finish.AspectRatio);
            finish.gameObject.SetActive(false);
        }

        Image Effect(string name, string key, bool additive)
        {
            var image = UiKit.Picture(design, name, art.Sprite(key), 0, 0, 10, 10);
            image.preserveAspect = true;
            if (additive) image.material = EffectAssets.Additive;
            image.gameObject.SetActive(false);
            return image;
        }

        Text Label(string text, Font font, int size, string color, float x, float y, float w, float h)
        {
            var label = UiKit.Label(design, text, size, GenerationCodeView.Hex(color), TextAnchor.MiddleCenter);
            label.font = font;
            label.gameObject.AddComponent<Shadow>().effectColor = new Color(0, 0, 0, .85f);
            UiKit.Place(label.rectTransform, x, y, w, h);
            return label;
        }

        void Fire(int box)
        {
            sfx.Play("Audio/CursedBoxes/shotgun_shoot_done", 1);
            effectBox = box;
            effectAge = 0;
        }

        void Impact(int box)
        {
            // WOOD_BREAK with destruction headroom (.85 × .6) layered with WOOD_EXTRA (.75 × .6), as in Java.
            sfx.Play("Audio/CursedBoxes/box_destroy_done", .85f * .6f);
            sfx.Play("Audio/CursedBoxes/shooted_box2", .75f * .6f);
            if (box >= 0 && box < BoxCount) crates[box].sprite = art.Sprite("Art/CursedBoxes/crate-destroyed");
        }

        void Animate(float delta)
        {
            effectAge += delta;
            var shot = effectBox >= 0 && effectBox < BoxCount && effectAge < 1.4f;
            flash.gameObject.SetActive(shot && effectAge < .12f);
            smoke.gameObject.SetActive(shot);
            debris.gameObject.SetActive(shot && effectAge > ShotFlightSeconds && effectAge < 1.3f);
            if (shot)
            {
                var c = slots[effectBox].center;
                Center(flash.rectTransform, c, 300, 0);
                flash.color = new Color(1, .85f, .6f, 1 - effectAge / .12f);
                var smokeT = effectAge / 1.4f;
                Center(smoke.rectTransform, c + new Vector2(0, 40 * smokeT), 260 + 160 * smokeT, 0);
                smoke.color = new Color(1, 1, 1, .7f * (1 - smokeT));
                var d = Mathf.Clamp01((effectAge - ShotFlightSeconds) / .7f);
                Center(debris.rectTransform, c + new Vector2(0, -30 * d), 240 + 120 * d, reduced ? 0 : 12 * d);
                debris.color = new Color(1, 1, 1, 1 - d);
            }

            foundAge += delta;
            if (found.gameObject.activeSelf)
            {
                var p = Mathf.Clamp01(foundAge / .35f);
                var fade = foundAge > 2.6f ? Mathf.Clamp01((foundAge - 2.6f) / .5f) : 0;
                found.color = new Color(1, 1, 1, ArtButton.Smoother(p) * (1 - fade));
                found.rectTransform.localScale = Vector3.one * (reduced ? 1 : .85f + .15f * ArtButton.Smoother(p));
                if (fade >= 1) found.gameObject.SetActive(false);
            }
        }

        static void Center(RectTransform rect, Vector2 center, float width, float rotation)
        {
            rect.sizeDelta = new Vector2(width, width * 2 / 3f);
            rect.anchoredPosition = center;
            rect.localEulerAngles = new Vector3(0, 0, rotation);
        }

        /// <summary>Tension bed while targets are chosen and fired upon (HEARTBEAT, level .60).</summary>
        void Heartbeat(float delta)
        {
            var phase = state.Value<string>("phase") ?? "";
            var wanted = phase == "TARGET_SELECTION" || phase == "SELECTING" || phase == "FIRING";
            heartbeat = Mathf.MoveTowards(heartbeat, wanted ? .6f : 0, delta / .6f);
            const string key = "Audio/CursedBoxes/heart_beating_done";
            if (heartbeat > 0 && !sfx.IsPlaying(key)) sfx.Play(key, heartbeat, 0, true);
            else if (heartbeat <= 0) sfx.Stop(key);
            else sfx.SetVolume(key, heartbeat);
        }

        void Render()
        {
            var phase = state.Value<string>("phase") ?? "";
            var active = state.Value<string>("activePlayerName") ?? "";
            status.text = PhaseTitle(phase) + (active.Length > 0 ? "   ·   стреляет: " + active : "");

            if (state["boxes"] is JArray list)
            {
                foreach (var box in list.OfType<JObject>())
                {
                    var i = box.Value<int>("id");
                    if (i < 0 || i >= BoxCount) continue;
                    var boxState = box.Value<string>("state") ?? "AVAILABLE";
                    if (boxState == "DESTROYED") crates[i].sprite = art.Sprite("Art/CursedBoxes/crate-destroyed");
                    crates[i].color = boxState == "LOCKED" ? new Color(1, .82f, .62f) : Color.white;
                    // A shot crate that held a player: show that character's "shot" art popping out, once.
                    if (boxState == "DESTROYED" && box.Value<bool?>("empty") == false && !cues.Contains("box:" + i))
                    {
                        cues.Add("box:" + i);
                        if (shotKey.Length > 0) ShowFound(i, box.Value<string>("characterId") ?? "");
                    }
                }
            }

            var revealing = phase == "ELIMINATION" || phase == "REVEALING" || phase == "FINISHED";
            if (phase == "REVEALING") Once("reveal-start", () => sfx.Play("Audio/CursedBoxes/reveal_sound2", .8f));
            if (phase == "FINISHED") Once("reveal-done", () => sfx.Play("Audio/CursedBoxes/reveal_done", .8f));
            if (phase == "ELIMINATION" && state["ghosts"] is JArray ghosts && ghosts.Count > 0)
                Once("ghosts", () => sfx.Play("Audio/CursedBoxes/horror_appearance", .8f * .75f));
            ShowOutcomes(revealing);

            var text = "Осталось выстрелов: " + (state.Value<int?>("remainingShotCount") ?? state.Value<int?>("shotsRemaining") ?? 0);
            var notice = state.Value<string>("notice");
            if (!string.IsNullOrEmpty(notice)) text += "   ·   " + notice;
            details.text = text;
            var finished = phase == "FINISHED";
            finish.gameObject.SetActive(finished);
            if (finished) finish.Interactable = !sentMilestones.Contains(MessageTypes.CursedReturnToLobby + ":return");
        }

        void ShowFound(int box, string characterId)
        {
            Sprite sprite;
            if (characterId == "ghost") sprite = art.Sprite("Art/CursedBoxes/ghost_hit");
            else if (ShotArt.TryGetValue(characterId, out var file)) sprite = art.Sprite("Art/CursedBoxes/Characters/" + file);
            else return;
            found.sprite = sprite;
            var c = slots[box].center;
            const float size = 300f;
            var x = Mathf.Clamp(c.x, size / 2, Size.x - size / 2);
            var y = Mathf.Clamp(c.y + 60, size / 2, Size.y - size / 2);
            UiKit.Place(found.rectTransform, x - size / 2, y - size / 2, size, size);
            found.transform.SetAsLastSibling();
            found.gameObject.SetActive(true);
            foundAge = 0;
            sfx.Play("Audio/CursedBoxes/appearance2", .8f * .9f * .902f);
        }

        /// <summary>REVEALING/FINISHED: survivors in front, the fallen as ghosts.</summary>
        void ShowOutcomes(bool visible)
        {
            survivorsShade.color = new Color(0, 0, 0, visible ? .55f : 0);
            if (!visible) { UiKit.Clear(survivorsRow); return; }
            if (survivorsRow.childCount > 0 || !(state["outcomes"] is JArray outcomes)) return;
            var list = outcomes.OfType<JObject>().ToList();
            if (list.Count == 0) return;
            var cell = Mathf.Min(320, Size.x / list.Count);
            var left = (Size.x - cell * list.Count) / 2;
            var font = art.Font("Fonts/RubikWetPaint-Regular");
            for (var i = 0; i < list.Count; i++)
            {
                var id = list[i].Value<string>("playerId");
                var player = snapshot.Players.FirstOrDefault(p => p.Id == id);
                var survived = list[i].Value<bool>("survived");
                var image = UiKit.Picture(survivorsRow, "Outcome", PortraitFor(player?.CharacterId ?? "skeleton", survived), left + i * cell + 10, 70, cell - 20, 430);
                image.preserveAspect = true;
                if (!survived) image.color = new Color(.63f, .78f, .88f, .75f);
                var name = UiKit.Label(survivorsRow, (player?.Name ?? "?") + (survived ? "  ·  ВЫЖИЛ" : "  ·  ПРИЗРАК"), 26,
                    survived ? new Color(.77f, .86f, .66f) : new Color(.60f, .80f, .93f), TextAnchor.MiddleCenter);
                name.font = font;
                UiKit.Place(name.rectTransform, left + i * cell, 20, cell, 44);
            }
        }

        Sprite PortraitFor(string character, bool survived)
        {
            if (!survived) return art.Sprite("Art/CursedBoxes/ghost_idle");
            try { return art.Sprite("Art/CursedBoxes/Survivors/" + character); }
            catch (KeyNotFoundException)
            {
                return ShotArt.TryGetValue(character, out var file) ? art.Sprite("Art/CursedBoxes/Characters/" + file) : art.Sprite("Art/CursedBoxes/ghost_idle");
            }
        }

        void Once(string key, System.Action action)
        {
            if (cues.Add(key)) action();
        }

        void SendOnce(string type, string key, string shotId)
        {
            if (!sentMilestones.Add(type + ":" + key)) return;
            var payload = context.Room.RoomPayload();
            payload["sessionId"] = sessionId;
            if (shotId != null) payload["shotId"] = shotId;
            if (context.Room.Send(type, payload) == null) sentMilestones.Remove(type + ":" + key);
        }

        void OnRejected(CommandRejection rejection)
        {
            if (!rejection.CommandType.StartsWith("CURSED_", System.StringComparison.Ordinal)) return;
            sentMilestones.RemoveWhere(k => k.StartsWith(rejection.CommandType, System.StringComparison.Ordinal));
            status.text = "Сервер отклонил " + rejection.CommandType + ": " + rejection.Code;
        }

        public bool IsFinished => state != null && state.Value<string>("phase") == "FINISHED";

        /// <summary>The host's "continue" action (Java: exitNetworkCursed). Inside a quiz it resumes the next question.</summary>
        public void ReturnToLobby()
        {
            if (!IsFinished) return;
            SendOnce(MessageTypes.CursedReturnToLobby, "return", null);
            finish.Interactable = false;
        }

        static string PhaseTitle(string phase)
        {
            switch (phase)
            {
                case "PREPARING": return "ПОДГОТОВКА";
                case "HIDING_SELECTION": return "ИГРОКИ ПРЯЧУТСЯ В ЯЩИКАХ";
                case "TARGET_SELECTION": return "СТРЕЛКИ ВЫБИРАЮТ ЦЕЛИ";
                case "SELECTING": return "ВЫБОР ЯЩИКА";
                case "FIRING": return "ВЫСТРЕЛ";
                case "ELIMINATION": return "ВЫБЫВАНИЕ";
                case "REVEALING": return "РАСКРЫТИЕ";
                case "FINISHED": return "ИТОГ";
                default: return phase;
            }
        }
    }
}

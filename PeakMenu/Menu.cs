using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Peak.Afflictions;
using Photon.Pun;
using UnityEngine;

namespace PeakMenu
{
    public partial class Menu : MonoBehaviour
    {
        const KeyCode ToggleKey = KeyCode.F1;

        // ---------- state ----------
        bool open;
        float openT;                       // 0..1 fade
        Rect win = new Rect(40, 40, 440, 640);
        int tab;
        Vector2 scroll;
        string itemFilter = "";
        string status = "";
        float statusTime = -100f;

        // player toggles
        bool infStamina, godMode, noHunger, fly, noFallDamage, noWeight, infUses;
        float speedMult = 1f, jumpMult = 1f, flySpeed = 15f, gravScale = 1f, climbMult = 1f;
        // world
        bool freezeTime, markers, hud;
        float yeet = 25f;
        int qty = 1;

        float baseMove = -1f, baseJump = -1f, baseGravJump, baseGravMax, baseClimb = -1f, baseDayLen = -1f;
        Rigidbody[] bodies;
        Character bodiesOwner;

        MethodInfo warpMethod, fallMethod, setTargetMethod;
        FieldInfo invincibleField;

        string armedKey; float armedAt;

        static readonly string[] Tabs = { "PLAYER", "WORLD", "TRAVEL", "ITEMS", "FRIENDS", "GIFTS" };

        // friends / pranks
        bool aura; float auraRadius = 40f, auraApplied = -1f;
        readonly HashSet<int> ragdollLoop = new HashSet<int>();
        readonly HashSet<int> pogoLoop = new HashSet<int>();
        float nextLoop;
        // gifts
        int giftTarget;            // actor number, 0 = everyone
        int giftMode;              // 0 = use on them, 1 = hand to them
        string giftFilter = "";

        static readonly string[] BuffTags =
        {
            "Infinite Stamina", "Extra Stamina", "Cure All", "Heal All", "Invincibility", "Faster Boi", "No Hunger",
            "Low Gravity", "Mass Super Jump", "Climbing Chalk", "Glowing", "Bing Bong Shield", "Double Jump Amulet", "Sunscreen"
        };
        static readonly string[] TrollTags =
        {
            "Chaos", "Blind", "Numb", "Exhausted", "Poison Over Time", "Cold Over Time", "Drowsy Over Time",
            "Adds Hot", "Adds Hunger", "Skeleton", "Random Warp", "Warp To Player", "Launch", "Scoutmaster", "Random Mushroom"
        };

        Character Me => Character.localCharacter;

        // ---------- palette ----------
        static readonly Color Bg = new Color(0.070f, 0.075f, 0.095f, 0.97f);
        static readonly Color Card = new Color(0.125f, 0.135f, 0.165f, 1f);
        static readonly Color Btn = new Color(0.185f, 0.200f, 0.240f, 1f);
        static readonly Color BtnHover = new Color(0.260f, 0.280f, 0.335f, 1f);
        static readonly Color Accent = new Color(1.00f, 0.60f, 0.20f, 1f);
        static readonly Color AccentDim = new Color(0.62f, 0.37f, 0.14f, 1f);
        static readonly Color Cream = new Color(0.95f, 0.92f, 0.85f, 1f);
        static readonly Color Muted = new Color(0.60f, 0.62f, 0.68f, 1f);
        static readonly Color Track = new Color(0.05f, 0.055f, 0.07f, 1f);
        static readonly Color Good = new Color(0.45f, 0.85f, 0.50f, 1f);

        // ---------- styles ----------
        GUISkin skin;
        GUIStyle sWindow, sCard, sBtn, sBtnAccent, sTab, sTabActive, sSection, sMuted, sTitle, sSub, sRow, sStatus, sValue, sCenter, sSmallBtn, sSmallBtnOn;
        Texture2D tPill, tCircle;
        readonly Dictionary<int, float> anim = new Dictionary<int, float>();

        // ---------- item cache ----------
        class Entry { public Item item; public string name, display, search; public List<string> tags = new List<string>(); public bool fatal; }
        Dictionary<string, List<Entry>> byTag = new Dictionary<string, List<Entry>>();
        List<Entry> entries;
        List<Entry> filtered;
        string filteredFor;

        void Awake()
        {
            var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            warpMethod = typeof(Character).GetMethod("WarpPlayer", flags);
            fallMethod = typeof(Character).GetMethod("Fall", flags);
            setTargetMethod = typeof(Scoutmaster).GetMethod("SetCurrentTarget", flags);
            invincibleField = typeof(CharacterData).GetField("isInvincible", flags);
        }

        // ---------- gameplay ----------
        void Update()
        {
            if (Input.GetKeyDown(ToggleKey)) open = !open;
            openT = Mathf.MoveTowards(openT, open ? 1f : 0f, Time.unscaledDeltaTime * 6f);
            if (open)
            {
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
            }

            var dn = DayNightManager.instance;
            if (dn != null)
            {
                if (baseDayLen < 0f) baseDayLen = dn.dayLengthInMinutes;
                dn.dayLengthInMinutes = freezeTime ? 1e7f : baseDayLen;
            }

            var c = Me;
            if (c == null) return;

            try { ApplyPlayer(c); } catch (Exception e) { Say(e.Message); }
            try { ApplyAura(c); RunLoops(c); } catch (Exception e) { Say(e.Message); }
        }

        // Radiate-infinite-stamina: every player near the carrier gets infinite stamina,
        // handled by the game on each friend's own client.
        void ApplyAura(Character c)
        {
            if (Time.frameCount % 30 != 0) return;
            var a = c.refs.afflictions;
            bool has = a.HasAfflictionType(Affliction.AfflictionType.RadiateInfiniteStam, out var existing);
            if (aura)
            {
                bool stale = has && (existing.totalTime - existing.timeElapsed < 15f || Mathf.Abs(auraApplied - auraRadius) > 0.5f);
                if (!has || stale)
                {
                    if (has) a.RemoveAffliction(existing);
                    a.AddAffliction(new Affliction_RadiateInfiniteStam { radius = auraRadius, totalTime = 300f });
                    auraApplied = auraRadius;
                }
            }
            else if (has) a.RemoveAffliction(existing);
        }

        IEnumerable<Character> Friends(Character me)
        {
            foreach (var o in Character.AllCharacters.ToArray())
                if (o != null && o != me && !o.isBot && !o.isScoutmaster) yield return o;
        }

        void RunLoops(Character c)
        {
            if (Time.unscaledTime < nextLoop) return;
            nextLoop = Time.unscaledTime + 1.2f;
            if (ragdollLoop.Count == 0 && pogoLoop.Count == 0) return;
            foreach (var o in Friends(c))
            {
                int actor = o.photonView.OwnerActorNr;
                if (ragdollLoop.Contains(actor)) o.photonView.RPC("RPCA_Fall", RpcTarget.All, 2.5f, 0f);
                if (pogoLoop.Contains(actor)) o.photonView.RPC("RPCA_AddForceAtPosition", RpcTarget.All, Vector3.up * yeet, o.Center, 100f);
            }
        }

        void ApplyPlayer(Character c)
        {
            c.infiniteStam = infStamina;

            if (godMode && invincibleField != null) invincibleField.SetValue(c.data, true);
            if (Time.frameCount % 30 == 0)
            {
                var a = c.refs.afflictions;
                if (godMode) a.ClearAllStatus();
                else if (noHunger) a.SetStatus(CharacterAfflictions.STATUSTYPE.Hunger, 0f);
                if (noWeight) a.SetStatus(CharacterAfflictions.STATUSTYPE.Weight, 0f);
            }
            if (infUses && Time.frameCount % 15 == 0) RefillHeld(c);
            if (noFallDamage) c.data.lastGroundedHeight = c.Center.y;

            var m = c.refs.movement;
            if (baseMove < 0f)
            {
                baseMove = m.movementModifier; baseJump = m.jumpImpulse;
                baseGravJump = m.jumpGravity; baseGravMax = m.maxGravity;
            }
            m.movementModifier = baseMove * speedMult;
            m.jumpImpulse = baseJump * jumpMult;
            m.jumpGravity = baseGravJump * gravScale;
            m.maxGravity = baseGravMax * gravScale;

            var cl = c.refs.climbing;
            if (baseClimb < 0f) baseClimb = cl.climbSpeedMod;
            cl.climbSpeedMod = baseClimb * climbMult;
        }

        void RefillHeld(Character c)
        {
            var item = c.data.currentItem;
            if (item == null || item.totalUses <= 0) return;
            var d = item.GetData<OptionableIntItemData>(DataEntryKey.ItemUses);
            if (d != null && d.HasData && d.Value < item.totalUses)
            {
                d.Value = item.totalUses;
                item.SetUseRemainingPercentage(1f);
            }
        }

        void FixedUpdate()
        {
            var c = Me;
            if (c == null || !fly || open) return;

            if (bodiesOwner != c || bodies == null)
            {
                bodies = c.GetComponentsInChildren<Rigidbody>();
                bodiesOwner = c;
            }
            var cam = Camera.main;
            if (cam == null) return;

            Vector3 dir = Vector3.zero;
            if (Input.GetKey(KeyCode.W)) dir += cam.transform.forward;
            if (Input.GetKey(KeyCode.S)) dir -= cam.transform.forward;
            if (Input.GetKey(KeyCode.D)) dir += cam.transform.right;
            if (Input.GetKey(KeyCode.A)) dir -= cam.transform.right;
            if (Input.GetKey(KeyCode.Space)) dir += Vector3.up;
            if (Input.GetKey(KeyCode.LeftControl)) dir += Vector3.down;

            Vector3 vel = dir.normalized * flySpeed;
            foreach (var rb in bodies)
                if (rb != null) rb.linearVelocity = vel;
        }

        void Say(string s) { status = s; statusTime = Time.unscaledTime; }

        // ---------- texture / style helpers ----------
        static Texture2D Rounded(Color fill, int size, float r, Color? border = null, float bw = 1.5f)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false);
            t.hideFlags = HideFlags.HideAndDontSave;
            t.wrapMode = TextureWrapMode.Clamp;
            t.filterMode = FilterMode.Bilinear;
            float h = size / 2f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float px = Mathf.Abs(x + 0.5f - h) - (h - r);
                    float py = Mathf.Abs(y + 0.5f - h) - (h - r);
                    float d = new Vector2(Mathf.Max(px, 0), Mathf.Max(py, 0)).magnitude + Mathf.Min(Mathf.Max(px, py), 0) - r;
                    float a = Mathf.Clamp01(0.5f - d);
                    Color col = fill;
                    if (border.HasValue)
                    {
                        float k = Mathf.Clamp01(d + bw + 0.5f);
                        col = Color.Lerp(fill, border.Value, k);
                    }
                    col.a *= a;
                    t.SetPixel(x, y, col);
                }
            t.Apply();
            return t;
        }

        GUIStyle MakeBox(Color fill, int r, RectOffset padding, Color? border = null)
        {
            var s = new GUIStyle();
            var tex = Rounded(fill, r * 2 + 4, r, border);
            s.normal.background = tex; s.hover.background = tex; s.active.background = tex; s.focused.background = tex;
            s.onNormal.background = tex; s.onHover.background = tex; s.onActive.background = tex; s.onFocused.background = tex;
            s.border = new RectOffset(r + 1, r + 1, r + 1, r + 1);
            s.padding = padding;
            return s;
        }

        GUIStyle MakeButton(Color n, Color h, Color a, Color text, int fontSize = 14, bool bold = true)
        {
            var s = new GUIStyle();
            int r = 9;
            Texture2D tn = Rounded(n, 24, r), th = Rounded(h, 24, r), ta = Rounded(a, 24, r);
            s.normal.background = tn; s.hover.background = th; s.active.background = ta; s.focused.background = tn;
            s.onNormal.background = tn; s.onHover.background = th; s.onActive.background = ta; s.onFocused.background = tn;
            s.normal.textColor = text; s.hover.textColor = text; s.active.textColor = text; s.focused.textColor = text;
            s.onNormal.textColor = text; s.onHover.textColor = text; s.onActive.textColor = text; s.onFocused.textColor = text;
            s.border = new RectOffset(r + 1, r + 1, r + 1, r + 1);
            s.padding = new RectOffset(12, 12, 8, 8);
            s.margin = new RectOffset(3, 3, 3, 3);
            s.alignment = TextAnchor.MiddleCenter;
            s.fontSize = fontSize;
            s.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            s.clipping = TextClipping.Clip;
            return s;
        }

        void EnsureStyles()
        {
            if (skin != null) return;

            skin = Instantiate(GUI.skin);
            skin.hideFlags = HideFlags.HideAndDontSave;

            skin.label.fontSize = 14;
            skin.label.normal.textColor = Cream;
            skin.label.wordWrap = false;

            sWindow = MakeBox(Bg, 16, new RectOffset(16, 16, 12, 14), new Color(1f, 1f, 1f, 0.10f));
            sCard = MakeBox(Card, 12, new RectOffset(14, 14, 12, 12));
            sCard.margin = new RectOffset(0, 0, 4, 8);
            skin.window = sWindow;

            sBtn = MakeButton(Btn, BtnHover, AccentDim, Cream);
            sBtnAccent = MakeButton(AccentDim, Accent, Accent, Color.white);
            sRow = MakeButton(Btn, BtnHover, AccentDim, Cream, 13, false);
            sRow.alignment = TextAnchor.MiddleLeft;
            sRow.padding = new RectOffset(12, 8, 7, 7);
            skin.button = sBtn;

            sTab = MakeButton(new Color(0, 0, 0, 0), new Color(1, 1, 1, 0.06f), new Color(1, 1, 1, 0.10f), Muted, 12);
            sTabActive = MakeButton(new Color(1, 1, 1, 0.07f), new Color(1, 1, 1, 0.10f), new Color(1, 1, 1, 0.10f), Accent, 12);
            sTab.padding = new RectOffset(4, 4, 8, 8); sTabActive.padding = new RectOffset(4, 4, 8, 8);
            sTab.margin = new RectOffset(1, 1, 3, 3); sTabActive.margin = new RectOffset(1, 1, 3, 3);

            sSmallBtn = MakeButton(Btn, BtnHover, AccentDim, Cream, 12);
            sSmallBtn.padding = new RectOffset(8, 8, 5, 5);
            sSmallBtnOn = MakeButton(AccentDim, Accent, Accent, Color.white, 12);
            sSmallBtnOn.padding = new RectOffset(8, 8, 5, 5);

            var tf = MakeBox(Track, 9, new RectOffset(12, 12, 8, 8), new Color(1f, 1f, 1f, 0.12f));
            tf.normal.textColor = Cream; tf.focused.textColor = Cream; tf.hover.textColor = Cream;
            tf.fontSize = 14;
            tf.alignment = TextAnchor.MiddleLeft;
            tf.clipping = TextClipping.Clip;
            skin.textField = tf;
            skin.settings.cursorColor = Accent;
            skin.settings.selectionColor = new Color(Accent.r, Accent.g, Accent.b, 0.35f);

            sSection = new GUIStyle(skin.label) { fontSize = 11, fontStyle = FontStyle.Bold };
            sSection.normal.textColor = Accent;
            sMuted = new GUIStyle(skin.label) { fontSize = 12 };
            sMuted.normal.textColor = Muted;
            sTitle = new GUIStyle(skin.label) { fontSize = 26, fontStyle = FontStyle.Bold };
            sTitle.normal.textColor = Accent;
            sSub = new GUIStyle(skin.label) { fontSize = 12, fontStyle = FontStyle.Bold };
            sSub.normal.textColor = Muted;
            sStatus = new GUIStyle(skin.label) { fontSize = 12 };
            sValue = new GUIStyle(skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight };
            sValue.normal.textColor = Accent;
            sCenter = new GUIStyle(skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };

            var vb = new GUIStyle();
            vb.normal.background = Rounded(new Color(1, 1, 1, 0.05f), 8, 4);
            vb.border = new RectOffset(4, 4, 4, 4);
            vb.fixedWidth = 8;
            vb.margin = new RectOffset(4, 0, 0, 0);
            var vt = new GUIStyle();
            vt.normal.background = Rounded(AccentDim, 8, 4);
            vt.hover.background = Rounded(Accent, 8, 4);
            vt.active.background = Rounded(Accent, 8, 4);
            vt.border = new RectOffset(4, 4, 4, 4);
            vt.fixedWidth = 8;
            var none = new GUIStyle { fixedWidth = 0, fixedHeight = 0 };
            skin.verticalScrollbar = vb;
            skin.verticalScrollbarThumb = vt;
            skin.verticalScrollbarUpButton = none;
            skin.verticalScrollbarDownButton = none;
            skin.horizontalScrollbar = new GUIStyle { fixedHeight = 0 };
            skin.horizontalScrollbarThumb = new GUIStyle { fixedHeight = 0 };
            skin.horizontalScrollbarLeftButton = none;
            skin.horizontalScrollbarRightButton = none;

            tPill = Rounded(Color.white, 20, 10);
            tCircle = Rounded(Color.white, 16, 8);
        }

        static void Fill(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = new Color(c.r, c.g, c.b, c.a * old.a);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        // Rounded bar: two round end caps plus a flat middle, so long bars keep circular ends
        // instead of stretching one texture into a lens shape.
        void Pill(Rect r, Color c)
        {
            if (r.width <= 0.5f) return;
            var old = GUI.color;
            GUI.color = new Color(c.r, c.g, c.b, c.a * old.a);
            float h = r.height;
            if (r.width <= h)
            {
                GUI.DrawTexture(r, tPill);
            }
            else
            {
                float half = h / 2f;
                GUI.DrawTextureWithTexCoords(new Rect(r.x, r.y, half, h), tPill, new Rect(0f, 0f, 0.5f, 1f));
                GUI.DrawTextureWithTexCoords(new Rect(r.xMax - half, r.y, half, h), tPill, new Rect(0.5f, 0f, 0.5f, 1f));
                GUI.DrawTexture(new Rect(r.x + half, r.y, r.width - h, h), Texture2D.whiteTexture);
            }
            GUI.color = old;
        }

        float Anim(int id, float target)
        {
            anim.TryGetValue(id, out float v);
            v = Mathf.MoveTowards(v, target, Time.unscaledDeltaTime * 8f);
            anim[id] = v;
            return v;
        }

        // ---------- custom controls ----------
        bool Switch(string label, bool value)
        {
            Rect r = GUILayoutUtility.GetRect(10, 30, GUILayout.ExpandWidth(true));
            int id = GUIUtility.GetControlID(FocusType.Passive, r);
            var e = Event.current;
            bool hover = r.Contains(e.mousePosition);

            if (e.type == EventType.MouseDown && e.button == 0 && hover)
            {
                value = !value;
                e.Use();
            }
            if (e.type == EventType.Repaint)
            {
                float t = Anim(id, value ? 1f : 0f);
                if (hover) Fill(new Rect(r.x - 6, r.y, r.width + 12, r.height), new Color(1, 1, 1, 0.04f));
                GUI.Label(new Rect(r.x, r.y, r.width - 56, r.height), label, skin.label);

                var track = new Rect(r.xMax - 44, r.y + (r.height - 22) / 2f, 40, 22);
                Pill(track, Color.Lerp(new Color(0.28f, 0.30f, 0.36f), Accent, t));
                var knob = new Rect(track.x + 3 + t * 18, track.y + 3, 16, 16);
                var old = GUI.color;
                GUI.color = new Color(1, 1, 1, old.a);
                GUI.DrawTexture(knob, tCircle);
                GUI.color = old;
            }
            return value;
        }

        float Slider(string label, float value, float min, float max, string fmt)
        {
            Rect head = GUILayoutUtility.GetRect(10, 20, GUILayout.ExpandWidth(true));
            Rect r = GUILayoutUtility.GetRect(10, 22, GUILayout.ExpandWidth(true));
            int id = GUIUtility.GetControlID(FocusType.Passive, r);
            var e = Event.current;

            Rect hit = new Rect(r.x - 6, r.y - 4, r.width + 12, r.height + 8);
            if (e.type == EventType.MouseDown && e.button == 0 && hit.Contains(e.mousePosition))
            {
                GUIUtility.hotControl = id;
                e.Use();
            }
            if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && GUIUtility.hotControl == id)
            {
                float n = Mathf.InverseLerp(r.x + 8, r.xMax - 8, e.mousePosition.x);
                value = Mathf.Lerp(min, max, Mathf.Clamp01(n));
                e.Use();
            }
            if (e.type == EventType.MouseUp && GUIUtility.hotControl == id)
            {
                GUIUtility.hotControl = 0;
                e.Use();
            }

            if (e.type == EventType.Repaint)
            {
                GUI.Label(head, label, skin.label);
                GUI.Label(head, value.ToString(fmt), sValue);

                float n = Mathf.InverseLerp(min, max, value);
                var track = new Rect(r.x, r.y + r.height / 2f - 3, r.width, 6);
                Pill(track, Track);
                float kx = Mathf.Lerp(r.x + 8, r.xMax - 8, n);
                Pill(new Rect(track.x, track.y, kx - track.x, 6), Accent);
                bool active = GUIUtility.hotControl == id || hit.Contains(e.mousePosition);
                float ks = active ? 18 : 16;
                var old = GUI.color;
                GUI.color = new Color(Cream.r, Cream.g, Cream.b, old.a);
                GUI.DrawTexture(new Rect(kx - ks / 2f, r.y + r.height / 2f - ks / 2f, ks, ks), tCircle);
                GUI.color = old;
            }
            GUILayout.Space(4);
            return value;
        }

        // button that must be clicked twice within 3 seconds
        bool ConfirmButton(string key, string label, GUIStyle style)
        {
            bool armed = armedKey == key && Time.unscaledTime - armedAt < 3f;
            if (GUILayout.Button(armed ? "Click again to confirm" : label, armed ? sBtnAccent : style))
            {
                if (armed) { armedKey = null; return true; }
                armedKey = key; armedAt = Time.unscaledTime;
            }
            return false;
        }

        void Section(string title)
        {
            GUILayout.Label(title, sSection);
            GUILayout.Space(2);
        }

        // ---------- window ----------
        void OnGUI()
        {
            EnsureStyles();

            var prevSkin = GUI.skin;
            var prevColor = GUI.color;
            GUI.skin = skin;

            try { DrawOverlays(); } catch { }

            if (openT > 0.01f)
            {
                GUI.color = new Color(1, 1, 1, Mathf.SmoothStep(0, 1, openT));
                win = GUILayout.Window(94021, win, DrawWindow, GUIContent.none, sWindow,
                    GUILayout.Width(480), GUILayout.Height(640));
                win.x = Mathf.Clamp(win.x, -win.width + 80, Screen.width - 80);
                win.y = Mathf.Clamp(win.y, 0, Screen.height - 40);
            }

            GUI.color = prevColor;
            GUI.skin = prevSkin;
        }

        // in-world markers + HUD (drawn even when the menu is closed)
        void DrawOverlays()
        {
            var c = Me;
            if (c == null) return;
            var cam = Camera.main;
            if (cam == null) return;

            if (markers)
            {
                foreach (var o in Character.AllCharacters.ToArray())
                {
                    if (o == null || o == c || o.isBot) continue;
                    Vector3 sp = cam.WorldToScreenPoint(o.Head + Vector3.up * 0.7f);
                    if (sp.z <= 0f) continue;
                    float d = Vector3.Distance(cam.transform.position, o.Center);
                    string txt = (o.characterName ?? "Player") + "  " + d.ToString("0") + " m";
                    var r = new Rect(sp.x - 85, Screen.height - sp.y - 13, 170, 26);
                    Pill(r, new Color(0.07f, 0.075f, 0.095f, 0.85f));
                    GUI.Label(r, txt, sCenter);
                }
            }

            if (hud)
            {
                string txt = "Height " + c.Center.y.ToString("0") + " m   |   Speed " +
                             c.data.avarageVelocity.magnitude.ToString("0.0") + " m/s   |   Stamina " +
                             (c.GetTotalStamina() * 100f).ToString("0") + "%";
                var r = new Rect(Screen.width / 2f - 190, 10, 380, 28);
                Pill(r, new Color(0.07f, 0.075f, 0.095f, 0.85f));
                GUI.Label(r, txt, sCenter);
            }
        }

        void DrawWindow(int id)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("PEAK", sTitle, GUILayout.Height(34));
            GUILayout.BeginVertical();
            GUILayout.Space(8);
            GUILayout.Label("MOD MENU", sSub);
            GUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            GUILayout.BeginVertical();
            GUILayout.Space(10);
            GUILayout.Label("F1 to hide", sMuted);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            Rect line = GUILayoutUtility.GetRect(10, 2, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
                for (int i = 0; i < 24; i++)
                {
                    float t = i / 23f;
                    Fill(new Rect(line.x + line.width * t, line.y, line.width / 24f + 1, 2),
                        Color.Lerp(Accent, new Color(Accent.r, Accent.g, Accent.b, 0f), t));
                }
            GUILayout.Space(8);

            GUILayout.BeginHorizontal();
            for (int i = 0; i < Tabs.Length; i++)
            {
                if (GUILayout.Button(Tabs[i], i == tab ? sTabActive : sTab)) { tab = i; scroll = Vector2.zero; }
                if (i == tab && Event.current.type == EventType.Repaint)
                {
                    Rect tr = GUILayoutUtility.GetLastRect();
                    Fill(new Rect(tr.x + 10, tr.yMax - 3, tr.width - 20, 3), Accent);
                }
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(8);

            scroll = GUILayout.BeginScrollView(scroll, false, false);
            var c = Me;
            if (c == null)
            {
                GUILayout.BeginVertical(sCard);
                GUILayout.Label("No character yet", skin.label);
                GUILayout.Label("Load into a level to use the menu.", sMuted);
                GUILayout.EndVertical();
            }
            else
            {
                switch (tab)
                {
                    case 0: DrawPlayer(c); break;
                    case 1: DrawWorld(c); break;
                    case 2: DrawTeleport(c); break;
                    case 3: DrawItems(c); break;
                    case 4: DrawPlayers(c); break;
                    case 5: DrawGifts(c); break;
                }
            }
            GUILayout.EndScrollView();

            GUILayout.FlexibleSpace();
            float age = Time.unscaledTime - statusTime;
            float a = Mathf.Clamp01(1f - (age - 3f));
            sStatus.normal.textColor = new Color(Good.r, Good.g, Good.b, a);
            GUILayout.Label(age < 4f ? status : " ", sStatus);

            GUI.DragWindow(new Rect(0, 0, 10000, 54));
        }

        // ---------- helpers for actions ----------
        void Warp(Character c, Vector3 pos)
        {
            if (warpMethod != null) warpMethod.Invoke(c, new object[] { pos, true });
            else c.transform.position = pos;
        }

        void Ragdoll(Character c, float seconds)
        {
            if (fallMethod != null) fallMethod.Invoke(c, new object[] { seconds, 0f });
        }

        void Launch(Character c, Vector3 dir, float speed)
        {
            foreach (var rb in c.GetComponentsInChildren<Rigidbody>())
                if (rb != null) rb.linearVelocity += dir.normalized * speed;
        }

        // ---------- tabs ----------
        void DrawPlayer(Character c)
        {
            GUILayout.BeginVertical(sCard);
            Section("SURVIVAL");
            infStamina = Switch("Infinite stamina", infStamina);
            godMode = Switch("God mode", godMode);
            noHunger = Switch("No hunger", noHunger);
            noFallDamage = Switch("No fall damage", noFallDamage);
            noWeight = Switch("Weightless pack", noWeight);
            infUses = Switch("Infinite item uses", infUses);
            GUILayout.EndVertical();

            GUILayout.BeginVertical(sCard);
            Section("MOVEMENT");
            speedMult = Slider("Speed", speedMult, 1f, 5f, "0.0x");
            jumpMult = Slider("Jump", jumpMult, 1f, 4f, "0.0x");
            gravScale = Slider("Gravity", gravScale, 0.1f, 1f, "0.00x");
            climbMult = Slider("Climb speed", climbMult, 1f, 4f, "0.0x");
            fly = Switch("Fly", fly);
            if (fly)
            {
                GUILayout.Label("WASD to move  ·  Space up  ·  Ctrl down  ·  menu closed", sMuted);
                GUILayout.Space(4);
                flySpeed = Slider("Fly speed", flySpeed, 3f, 60f, "0");
            }
            GUILayout.EndVertical();

            GUILayout.BeginVertical(sCard);
            Section("FUN");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Ragdoll", sBtn)) { Ragdoll(c, 4f); Say("Splat"); }
            if (GUILayout.Button("Launch up", sBtn)) { Launch(c, Vector3.up, 35f); Say("Wheee"); }
            if (GUILayout.Button("Launch forward", sBtn))
            {
                var cam = Camera.main;
                Launch(c, cam != null ? cam.transform.forward : c.transform.forward, 40f);
                Say("Yeeted");
            }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Super jump", sBtn))
            {
                try { c.refs.movement.DoSuperJump(45f, 1f, 1, 4f); Say("Boing"); }
                catch (Exception e) { Say(e.Message); }
            }
            if (GUILayout.Button("Toggle skeleton", sBtn))
            {
                try { c.data.SetSkeleton(!c.data.isSkeleton); Say("Spooky"); }
                catch (Exception e) { Say(e.Message); }
            }
            if (GUILayout.Button("Random look", sBtn))
            {
                try { c.refs.customization.RandomizeCosmetics(); Say("New look"); }
                catch (Exception e) { Say(e.Message); }
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            GUILayout.BeginVertical(sCard);
            Section("ACTIONS");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Full stamina", sBtnAccent)) { c.AddStamina(1f); Say("Stamina refilled"); }
            if (GUILayout.Button("Clear effects", sBtn)) { c.refs.afflictions.ClearAllStatus(); Say("Status effects cleared"); }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Revive", sBtn)) { Character.Revive(); Say("Revive requested"); }
            if (GUILayout.Button("Reset sliders", sBtn))
            { speedMult = 1f; jumpMult = 1f; gravScale = 1f; climbMult = 1f; Say("Reset"); }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        void DrawWorld(Character c)
        {
            bool host = PhotonNetwork.IsMasterClient;
            var dn = DayNightManager.instance;

            GUILayout.BeginVertical(sCard);
            Section("OVERLAYS");
            markers = Switch("Player markers (name + distance)", markers);
            hud = Switch("HUD (height, speed, stamina)", hud);
            GUILayout.EndVertical();

            GUILayout.BeginVertical(sCard);
            Section("TIME");
            if (dn != null)
            {
                float cur = dn.timeOfDay % 24f;
                float v = Slider("Time of day", cur, 0f, 24f, "0.0");
                if (Mathf.Abs(v - cur) > 0.001f) dn.setTimeOfDay(v);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Dawn", sSmallBtn)) dn.setTimeOfDay(6f);
                if (GUILayout.Button("Noon", sSmallBtn)) dn.setTimeOfDay(12f);
                if (GUILayout.Button("Dusk", sSmallBtn)) dn.setTimeOfDay(19f);
                if (GUILayout.Button("Midnight", sSmallBtn)) dn.setTimeOfDay(0f);
                GUILayout.EndHorizontal();
                freezeTime = Switch("Freeze time of day", freezeTime);
                if (!host) GUILayout.Label("Only the host's clock is shared; yours may snap back.", sMuted);
            }
            else GUILayout.Label("No day/night cycle in this scene", sMuted);
            float ts = Time.timeScale;
            float nts = Slider("Game speed", ts, 0.1f, 3f, "0.0x");
            if (Mathf.Abs(nts - ts) > 0.001f) Time.timeScale = nts;
            if (GUILayout.Button("Reset game speed", sSmallBtn)) Time.timeScale = 1f;
            GUILayout.EndVertical();

            GUILayout.BeginVertical(sCard);
            Section(host ? "BIOME JUMP (everyone moves)" : "BIOME JUMP (host only)");
            string[] names = { "Beach", "Tropics", "Alpine", "Caldera", "The Kiln", "Peak" };
            for (int row = 0; row < 2; row++)
            {
                GUILayout.BeginHorizontal();
                for (int i = row * 3; i < row * 3 + 3; i++)
                    if (GUILayout.Button(names[i], sBtn))
                    {
                        if (!host) Say("Only the host can jump biomes");
                        else { MapHandler.JumpToSegment((Segment)i); Say("Jumping to " + names[i]); }
                    }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();

            GUILayout.BeginVertical(sCard);
            Section("SCOUTMASTER");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Sic on me (30s)", sBtn)) SicScoutmaster(c, 30f);
            if (GUILayout.Button("Call off", sBtn)) SicScoutmaster(null, 0f);
            GUILayout.EndHorizontal();
            GUILayout.Label("Needs a Scoutmaster in the level (ascent 0+).", sMuted);
            GUILayout.EndVertical();

            GUILayout.BeginVertical(sCard);
            Section("RUN & PROFILE");
            if (ConfirmButton("win", "Force win (ends the run for everyone)", sBtn)) { Character.TestWin(); Say("Win triggered"); }
            GUILayout.BeginHorizontal();
            if (ConfirmButton("asc", "Unlock all ascents", sBtn)) { Ascents.UnlockAll(); Say("Ascents unlocked"); }
            if (ConfirmButton("cos", "Unlock all cosmetics", sBtn)) { PassportManager.TestAllCosmetics(); Say("Cosmetics unlock requested"); }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }

        void SicScoutmaster(Character target, float seconds)
        {
            try
            {
                if (!Scoutmaster.GetPrimaryScoutmaster(out var sm)) { Say("No Scoutmaster in this level"); return; }
                setTargetMethod.Invoke(sm, new object[] { target, seconds });
                Say(target == null ? "Scoutmaster called off" : "Scoutmaster is coming");
            }
            catch (Exception e) { Say(e.Message); }
        }

        void DrawTeleport(Character c)
        {
            GUILayout.BeginVertical(sCard);
            Section("QUICK");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("To crosshair", sBtnAccent))
            {
                var cam = Camera.main;
                if (cam != null && Physics.Raycast(cam.transform.position, cam.transform.forward, out var hit, 2000f))
                { Warp(c, hit.point + Vector3.up * 1.5f); Say("Teleported"); }
                else Say("Nothing in sight");
            }
            if (GUILayout.Button("Up 50 m", sBtn)) { Warp(c, c.Center + Vector3.up * 50f); Say("Teleported up"); }
            if (GUILayout.Button("To spawn", sBtn))
            {
                try { Warp(c, SpawnPoint.allSpawnPoints[0].transform.position + Vector3.up); Say("Back at spawn"); }
                catch { Say("No spawn point found"); }
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();

            var flags = FindObjectsOfType<CheckpointFlag>().OrderBy(f => f.transform.position.y).ToArray();
            var fires = FindObjectsOfType<Campfire>().OrderBy(f => f.transform.position.y).ToArray();

            GUILayout.BeginVertical(sCard);
            Section("CHECKPOINTS");
            if (flags.Length == 0) GUILayout.Label("None found in this scene", sMuted);
            for (int i = 0; i < flags.Length; i++)
                if (GUILayout.Button("Checkpoint " + (i + 1) + "   ·   " + flags[i].transform.position.y.ToString("0") + " m", sRow))
                { Warp(c, flags[i].transform.position + Vector3.up); Say("Teleported"); }
            GUILayout.EndVertical();

            GUILayout.BeginVertical(sCard);
            Section("CAMPFIRES");
            if (fires.Length == 0) GUILayout.Label("None found in this scene", sMuted);
            for (int i = 0; i < fires.Length; i++)
                if (GUILayout.Button("Campfire " + (i + 1) + "   ·   " + fires[i].transform.position.y.ToString("0") + " m", sRow))
                { Warp(c, fires[i].transform.position + Vector3.up * 2f); Say("Teleported"); }
            GUILayout.EndVertical();
        }

        // ---------- items ----------
        void BuildItems()
        {
            var db = ItemDatabase.Instance;
            if (db == null) return;
            if (entries != null && entries.Count == db.itemLookup.Count) return;

            entries = new List<Entry>();
            foreach (var item in db.itemLookup.Values)
            {
                if (item == null) continue;
                string name = item.gameObject.name;
                string disp = null;
                try { disp = item.GetName(); } catch { }
                if (string.IsNullOrWhiteSpace(disp)) disp = name;
                var entry = new Entry { item = item, name = name, display = disp, search = (disp + " " + name).ToLowerInvariant() };
                try { Tag(entry); } catch { }
                entries.Add(entry);
            }
            entries = entries.OrderBy(e => e.display).ToList();
            byTag = new Dictionary<string, List<Entry>>();
            foreach (var e in entries)
                foreach (var t in e.tags)
                {
                    if (!byTag.TryGetValue(t, out var list)) byTag[t] = list = new List<Entry>();
                    list.Add(e);
                }
            filteredFor = null;
        }

        void DrawItems(Character c)
        {
            BuildItems();
            if (entries == null) { GUILayout.Label("Item database not loaded", sMuted); return; }

            itemFilter = GUILayout.TextField(itemFilter);
            GUILayout.Space(4);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Amount", sMuted, GUILayout.Width(60));
            foreach (int q in new[] { 1, 3, 5, 10 })
                if (GUILayout.Button("x" + q, qty == q ? sSmallBtnOn : sSmallBtn, GUILayout.Width(46))) qty = q;
            GUILayout.EndHorizontal();

            if (filteredFor != itemFilter || filtered == null)
            {
                string f = itemFilter.Trim().ToLowerInvariant();
                filtered = f.Length == 0 ? entries : entries.Where(e => e.search.Contains(f)).ToList();
                filteredFor = itemFilter;
            }

            GUILayout.Label(filtered.Count + " items  ·  click to spawn", sMuted);
            GUILayout.Space(2);

            for (int i = 0; i < filtered.Count; i += 2)
            {
                GUILayout.BeginHorizontal();
                for (int j = i; j < i + 2; j++)
                {
                    if (j >= filtered.Count) { GUILayout.FlexibleSpace(); break; }
                    if (GUILayout.Button(filtered[j].display, sRow, GUILayout.Width(208)))
                        SpawnItem(c, filtered[j]);
                }
                GUILayout.EndHorizontal();
            }
        }

        void SpawnItem(Character c, Entry e)
        {
            try
            {
                for (int k = 0; k < qty; k++)
                    ItemDatabase.Add(e.item, c.Center + c.transform.forward * 1.5f + Vector3.up * (1f + 0.4f * k));
                Say("Spawned " + (qty > 1 ? qty + "x " : "") + e.display);
            }
            catch (Exception ex) { Say(ex.Message); }
        }

    }
}

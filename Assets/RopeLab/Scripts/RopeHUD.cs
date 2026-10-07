using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace RopeLab
{
    /// <summary>
    /// Minimal TLOU-style HUD built at runtime: centre dot, context prompts with key caps,
    /// objective card (top-left), rope tension bar, throw charge ring, controls help (H).
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public class RopeHUD : MonoBehaviour
    {
        public static RopeHUD Instance { get; private set; }

        Font _font;
        Text _title, _objective, _status, _help, _toast;
        RectTransform _promptRoot;
        readonly List<GameObject> _promptPool = new List<GameObject>();
        Image _tensionFill, _chargeFill, _dot;
        GameObject _tensionRoot, _chargeRoot, _helpRoot, _objectiveRoot;
        float _toastTime;
        int _promptsUsed;
        // world-anchored prompt ("E  Carry" floating next to the nearest rope node)
        RectTransform _worldRoot, _worldDot;
        Text _worldKey, _worldLabel;
        bool _worldUsed, _worldVisible;
        Vector2 _worldPos;

        static readonly Color Panel = new Color(0.05f, 0.06f, 0.06f, 0.72f);
        static readonly Color Accent = new Color(0.95f, 0.78f, 0.35f, 1f);
        static readonly Color Text1 = new Color(0.93f, 0.92f, 0.88f, 1f);
        static readonly Color Text2 = new Color(0.7f, 0.7f, 0.66f, 1f);

        void Awake()
        {
            Instance = this;
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;

            // Centre dot
            _dot = MakeImage(transform, "Dot", new Color(1, 1, 1, 0.8f));
            SetRect(_dot.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(6, 6));

            // Objective card
            var card = MakeImage(transform, "Objective", Panel);
            _objectiveRoot = card.gameObject;
            SetRect(card.rectTransform, new Vector2(0, 1), new Vector2(40, -40), new Vector2(520, 160), new Vector2(0, 1));
            var bar = MakeImage(card.transform, "Accent", Accent);
            SetRect(bar.rectTransform, new Vector2(0, 1), Vector2.zero, new Vector2(5, 160), new Vector2(0, 1));
            _title = MakeText(card.transform, "Title", 26, Accent, FontStyle.Bold);
            SetRect(_title.rectTransform, new Vector2(0, 1), new Vector2(22, -14), new Vector2(480, 34), new Vector2(0, 1));
            _objective = MakeText(card.transform, "Body", 19, Text1);
            SetRect(_objective.rectTransform, new Vector2(0, 1), new Vector2(22, -50), new Vector2(480, 74), new Vector2(0, 1));
            _status = MakeText(card.transform, "Status", 17, new Color(0.45f, 0.95f, 0.5f), FontStyle.Bold);
            SetRect(_status.rectTransform, new Vector2(0, 0), new Vector2(22, 8), new Vector2(480, 24), new Vector2(0, 0));

            // Prompts (bottom centre)
            var pr = new GameObject("Prompts", typeof(RectTransform), typeof(VerticalLayoutGroup));
            pr.transform.SetParent(transform, false);
            _promptRoot = (RectTransform)pr.transform;
            SetRect(_promptRoot, new Vector2(0.5f, 0), new Vector2(0, 185), new Vector2(520, 200), new Vector2(0.5f, 0));
            var vl = pr.GetComponent<VerticalLayoutGroup>();
            vl.childAlignment = TextAnchor.LowerCenter; vl.spacing = 8; vl.childControlHeight = false; vl.childControlWidth = false; vl.childForceExpandHeight = false;

            // Tension bar (bottom centre, above prompts)
            var tb = MakeImage(transform, "Tension", Panel);
            _tensionRoot = tb.gameObject;
            SetRect(tb.rectTransform, new Vector2(0.5f, 0), new Vector2(0, 90), new Vector2(300, 10), new Vector2(0.5f, 0));
            _tensionFill = MakeImage(tb.transform, "Fill", Accent);
            _tensionFill.type = Image.Type.Filled; _tensionFill.fillMethod = Image.FillMethod.Horizontal;
            _tensionFill.sprite = WhiteSprite();
            SetRect(_tensionFill.rectTransform, new Vector2(0, 0.5f), Vector2.zero, new Vector2(300, 10), new Vector2(0, 0.5f));
            var tl = MakeText(tb.transform, "Label", 15, Text2);
            tl.text = "ROPE SLACK"; tl.alignment = TextAnchor.LowerCenter;
            SetRect(tl.rectTransform, new Vector2(0.5f, 1), new Vector2(0, 20), new Vector2(300, 20), new Vector2(0.5f, 0));

            // Throw charge ring around dot
            var cr = MakeImage(transform, "Charge", new Color(1, 1, 1, 0.15f));
            _chargeRoot = cr.gameObject;
            cr.sprite = RingSprite(); cr.type = Image.Type.Simple;
            SetRect(cr.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(56, 56));
            _chargeFill = MakeImage(cr.transform, "Fill", Accent);
            _chargeFill.sprite = RingSprite(); _chargeFill.type = Image.Type.Filled; _chargeFill.fillMethod = Image.FillMethod.Radial360;
            _chargeFill.fillOrigin = 2; _chargeFill.fillClockwise = true;
            SetRect(_chargeFill.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(56, 56));

            // World prompt: a small ring on the rope node + a key cap and label beside it.
            var wr = new GameObject("WorldPrompt", typeof(RectTransform));
            wr.transform.SetParent(transform, false);
            _worldRoot = (RectTransform)wr.transform;
            SetRect(_worldRoot, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(10, 10));
            var ring = MakeImage(_worldRoot, "Ring", Accent);
            ring.sprite = RingSprite();
            _worldDot = ring.rectTransform;
            SetRect(_worldDot, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(22, 22));
            var tag = MakeImage(_worldRoot, "Tag", Panel);
            SetRect(tag.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(18, 26), new Vector2(128, 34), new Vector2(0, 0));
            var wcap = MakeImage(tag.transform, "Key", Text1);
            SetRect(wcap.rectTransform, new Vector2(0, 0.5f), new Vector2(6, 0), new Vector2(24, 24), new Vector2(0, 0.5f));
            _worldKey = MakeText(wcap.transform, "K", 17, new Color(0.08f, 0.08f, 0.08f), FontStyle.Bold);
            _worldKey.alignment = TextAnchor.MiddleCenter;
            SetRect(_worldKey.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(24, 24));
            _worldLabel = MakeText(tag.transform, "Label", 19, Text1, FontStyle.Bold);
            _worldLabel.alignment = TextAnchor.MiddleLeft;
            _worldLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
            SetRect(_worldLabel.rectTransform, new Vector2(0, 0.5f), new Vector2(38, 0), new Vector2(90, 30), new Vector2(0, 0.5f));
            wr.SetActive(false);

            // Toast (top centre)
            _toast = MakeText(transform, "Toast", 24, Text1, FontStyle.Bold);
            _toast.alignment = TextAnchor.MiddleCenter;
            var sh = _toast.gameObject.AddComponent<Shadow>(); sh.effectDistance = new Vector2(2, -2);
            SetRect(_toast.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -60), new Vector2(900, 40), new Vector2(0.5f, 1));

            // Help (top right)
            var help = MakeImage(transform, "Help", Panel);
            _helpRoot = help.gameObject;
            SetRect(help.rectTransform, new Vector2(1, 1), new Vector2(-40, -40), new Vector2(360, 252), new Vector2(1, 1));
            _help = MakeText(help.transform, "Text", 17, Text1);
            _help.supportRichText = true;
            SetRect(_help.rectTransform, new Vector2(0, 1), new Vector2(18, -14), new Vector2(330, 280), new Vector2(0, 1));
            _help.text =
                "<b><color=#F2C75A>ROPE CONTROLS</color></b>\n" +
                "<b>E</b>   Grab rope at any point / Drop\n" +
                "<b>Hold E</b>   Gather & coil a loose rope\n" +
                "<b>Alt</b>   Grip tight (rope stops sliding)\n" +
                "<b>Hold LMB</b>   Aim throw, release to throw\n" +
                "<b>F</b>   Tie / Untie · Plug / Unplug\n" +
                "<b>Q</b>   Climb hanging rope\n" +
                "   <b>W/S</b> up/down · <b>Space</b> jump off\n" +
                "<b>R</b>   Reset current test\n" +
                "<b>1-6</b>   Teleport to test\n" +
                "<b>G</b>   Show rope nodes (debug)\n" +
                "<b>H</b>   Hide info panels";

            SetObjective("ROPE LAB", "Walk into a test bay. Press 1-5 to jump to a test.");
            ShowTension(false, 0f);
            ShowCharge(false, 0f);
        }

        void Update()
        {
            if (UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.hKey.wasPressedThisFrame)
            {
                // H hides only the explanation panels (objective + controls); prompts, Carry label,
                // slack bar and toasts stay.
                bool showInfo = !_helpRoot.activeSelf;
                _helpRoot.SetActive(showInfo);
                _objectiveRoot.SetActive(showInfo);
            }
            if (_toastTime > 0f)
            {
                _toastTime -= Time.deltaTime;
                var c = _toast.color; c.a = Mathf.Clamp01(_toastTime); _toast.color = c;
            }
        }

        void LateUpdate()
        {
            for (int i = _promptsUsed; i < _promptPool.Count; i++) _promptPool[i].SetActive(false);
            _promptsUsed = 0;
            if (!_worldUsed && _worldVisible) { _worldRoot.gameObject.SetActive(false); _worldVisible = false; }
            _worldUsed = false;
        }

        /// <summary>
        /// Call every frame to show a key prompt anchored to a point in the world (e.g. the rope node the player
        /// would grab). It follows the point smoothly and hides itself when not called.
        /// </summary>
        public void WorldPrompt(Vector3 world, string key, string label)
        {
            var cam = Camera.main;
            if (!cam) return;
            Vector3 sp = cam.WorldToScreenPoint(world);
            if (sp.z <= 0f) return;                                  // behind the camera
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)transform, sp, null, out var local);
            _worldUsed = true;
            if (!_worldVisible) { _worldRoot.gameObject.SetActive(true); _worldVisible = true; _worldPos = local; }
            _worldPos = Vector2.Lerp(_worldPos, local, 1f - Mathf.Exp(-18f * Time.unscaledDeltaTime));
            _worldRoot.anchoredPosition = _worldPos;
            _worldKey.text = key;
            _worldLabel.text = label;
            float pulse = 1f + 0.12f * Mathf.Sin(Time.unscaledTime * 6f);
            _worldDot.localScale = new Vector3(pulse, pulse, 1f);
        }

        // ---------------------------------------------------------------- API
        public void SetObjective(string title, string body)
        {
            _title.text = title.ToUpperInvariant();
            _objective.text = body;
            _status.text = "";
        }

        public void SetStatus(string s) => _status.text = s;

        public void Toast(string msg, float seconds = 2.5f)
        {
            _toast.text = msg; _toastTime = seconds;
        }

        /// <summary>Call every frame for each prompt to show ("E", "Grab rope").</summary>
        public void Prompt(string key, string label)
        {
            GameObject row;
            if (_promptsUsed < _promptPool.Count) row = _promptPool[_promptsUsed];
            else row = MakePromptRow();
            _promptsUsed++;
            row.SetActive(true);
            row.transform.SetSiblingIndex(_promptsUsed - 1);
            row.transform.GetChild(0).GetChild(0).GetComponent<Text>().text = key;
            var lbl = row.transform.GetChild(1).GetComponent<Text>();
            lbl.text = label;
            // Label starts after the key cap, whatever the key text length ("E", "Hold E", "W / S").
            float keyW = 8 + 18 + key.Length * 11f;
            lbl.rectTransform.offsetMin = new Vector2(keyW + 14, 0);
            float w = keyW + 14 + lbl.preferredWidth + 20;
            ((RectTransform)row.transform).sizeDelta = new Vector2(Mathf.Max(200, w), 40);
        }

        public void ShowTension(bool show, float slack01)
        {
            _tensionRoot.SetActive(show);
            if (!show) return;
            _tensionFill.fillAmount = Mathf.Clamp01(slack01);
            _tensionFill.color = Color.Lerp(new Color(0.95f, 0.3f, 0.2f), Accent, Mathf.Clamp01(slack01 * 3f));
        }

        public void ShowCharge(bool show, float t)
        {
            _chargeRoot.SetActive(show);
            _dot.enabled = !show;
            if (show) _chargeFill.fillAmount = Mathf.Clamp01(t);
        }

        // ---------------------------------------------------------------- UI helpers
        GameObject MakePromptRow()
        {
            var row = MakeImage(_promptRoot, "Prompt", Panel).gameObject;
            ((RectTransform)row.transform).sizeDelta = new Vector2(260, 40);
            var cap = MakeImage(row.transform, "Key", Text1);
            SetRect(cap.rectTransform, new Vector2(0, 0.5f), new Vector2(8, 0), new Vector2(0, 28), new Vector2(0, 0.5f));
            var fit = cap.gameObject.AddComponent<ContentSizeFitter>();
            fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            var hl = cap.gameObject.AddComponent<HorizontalLayoutGroup>();
            hl.padding = new RectOffset(9, 9, 2, 2); hl.childAlignment = TextAnchor.MiddleCenter;
            var kt = MakeText(cap.transform, "K", 18, new Color(0.08f, 0.08f, 0.08f), FontStyle.Bold);
            kt.alignment = TextAnchor.MiddleCenter;
            var lbl = MakeText(row.transform, "Label", 20, Text1);
            lbl.alignment = TextAnchor.MiddleLeft;
            lbl.horizontalOverflow = HorizontalWrapMode.Overflow;
            var lr = lbl.rectTransform;
            lr.anchorMin = new Vector2(0, 0); lr.anchorMax = new Vector2(1, 1);
            lr.offsetMin = new Vector2(72, 0); lr.offsetMax = new Vector2(-10, 0);
            _promptPool.Add(row);
            return row;
        }

        Image MakeImage(Transform parent, string n, Color c)
        {
            var go = new GameObject(n, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = c; img.raycastTarget = false;
            return img;
        }

        Text MakeText(Transform parent, string n, int size, Color c, FontStyle style = FontStyle.Normal)
        {
            var go = new GameObject(n, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = _font; t.fontSize = size; t.color = c; t.fontStyle = style; t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        static void SetRect(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size, Vector2? pivot = null)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot ?? new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        static Sprite _white, _ring;
        static Sprite WhiteSprite()
        {
            if (_white) return _white;
            var tex = Texture2D.whiteTexture;
            return _white = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
        }

        static Sprite RingSprite()
        {
            if (_ring) return _ring;
            const int s = 128;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[s * s];
            for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(s / 2f, s / 2f));
                float a = Mathf.Clamp01(1f - Mathf.Abs(d - 54f) / 5f);
                px[y * s + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
            tex.SetPixels32(px); tex.Apply();
            return _ring = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f));
        }
    }
}

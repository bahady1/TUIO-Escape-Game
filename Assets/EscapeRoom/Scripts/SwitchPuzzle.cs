using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace EscapeRoom
{
    /// <summary>
    /// Puzzle 1 - the switchbox in the server room.
    ///
    /// 1. Face the switchbox wall in the server room. Put the ring (tangible 4 or 7) on the switchbox
    ///    for 'placeSeconds' (2 s), then turn it ~90 degrees -> the switchbox zooms in.
    /// 2. Tap a knob to move it: middle -> down -> up -> middle ...  (states: 0 up, 1 middle, 2 down)
    /// 3. Matching 'solution' restores the power (lights on).
    /// 4. Turn the ring back ~90 degrees to step away. The ring may be lifted off while zoomed in.
    /// </summary>
    public class SwitchPuzzle : MonoBehaviour
    {
        public const int Up = 0, Middle = 1, Down = 2;

        [Header("Solution (0 up, 1 middle, 2 down) - top row left->right, then bottom row left->right")]
        public int[] solution = { 1, 0, 1, 0, 2, 0, 1, 2 };

        [Header("Switchbox in the scene")]
        public WallViewController room;
        [Tooltip("Wall of 'room' the switchbox hangs on (0 North, 1 East, 2 South, 3 West).")]
        [Range(0, 3)] public int panelWall = 2;
        [Tooltip("Front face of the 3D switchbox; its screen area is where the ring has to be placed.")]
        public Renderer panelFace;
        [Tooltip("The 8 knobs on the 3D switchbox (mirror the zoomed-in panel).")]
        public Transform[] worldKnobs = new Transform[8];
        public float worldKnobTravel = 0.07f;

        [Header("Ring tangible")]
        public int[] ringIds = { 4, 7 };
        public float placeSeconds = 2f;
        [Tooltip("How far the ring has to be turned (a little under 90 so it is not fiddly).")]
        public float turnDegrees = 80f;
        [Tooltip("Extra room around the switchbox on screen where the ring still counts (pixels).")]
        public float hotspotMarginPx = 90f;

        [Header("Panel art")]
        public Sprite panelSprite;
        [Tooltip("Knob sprite per slot (top row left->right, then bottom row).")]
        public Sprite[] knobSprites = new Sprite[8];

        [Header("Text")]
        public string solvedHint = "Power restored! The lights are back on - now look for a way through the door.";
        public Color accentColor = new(0.25f, 0.85f, 1f, 1f);

        public bool Solved { get; private set; }
        public bool Zoomed { get; private set; }
        public int GetState(int slot) => _state[slot];
        public event Action OnSolved;

        // Layout of SwitchPanel.png (1437 x 1972), measured in pixels from the top-left corner.
        static readonly Vector2 PanelPx = new(1437f, 1972f);
        static readonly float[] SlotX = { 260f, 568f, 874f, 1182f };
        static readonly float[] RowY = { 858f, 1480f };
        const float TravelPx = 112f;
        static readonly Vector2 KnobPx = new(172f, 176f);
        static readonly Vector2 SlotHitPx = new(230f, 560f);
        static readonly Rect ScreenPx = new(710f, 190f, 370f, 245f);
        static readonly Rect IndicatorPx = new(1190f, 215f, 75f, 195f);
        const float PanelHeightUi = 1220f;

        readonly int[] _state = { 1, 1, 1, 1, 1, 1, 1, 1 };
        Vector3[] _worldKnobMiddle;

        // UI
        Font _font;
        Canvas _canvas;
        RectTransform _zoomRoot, _panel, _hotspot;
        CanvasGroup _zoomGroup;
        readonly RectTransform[] _knobs = new RectTransform[8];
        readonly RectTransform[] _slotHit = new RectTransform[8];
        Image _hotspotRing, _hotspotFill, _indicator;
        Text _hotspotLabel, _screenText, _footer;
        float _scale;

        // Ring state
        float _dwell, _grace, _enterAccum, _exitAccum;
        bool _armed, _animating;
        int _entryDir = 1;
        TangibleInput _input;

        void Awake()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _worldKnobMiddle = new Vector3[worldKnobs.Length];
            for (int i = 0; i < worldKnobs.Length; i++)
                if (worldKnobs[i] != null) _worldKnobMiddle[i] = worldKnobs[i].localPosition;
            BuildUI();
        }

        void Start()
        {
            _input = TangibleInput.Instance != null ? TangibleInput.Instance : FindAnyObjectByType<TangibleInput>();
            if (_input != null)
            {
                _input.Rotated += OnRotated;
                _input.Tapped += OnTap;
                _input.ObjectRemoved += OnRemoved;
            }
            UpdateScreen();
        }

        void OnDestroy()
        {
            if (_input == null) return;
            _input.Rotated -= OnRotated;
            _input.Tapped -= OnTap;
            _input.ObjectRemoved -= OnRemoved;
        }

        bool IsRing(int id) => Array.IndexOf(ringIds, id) >= 0;

        // ------------------------------------------------------------------ ring handling

        void Update()
        {
            UpdateKnobVisuals();

            bool started = GameHUD.Instance == null || GameHUD.Instance.GameStarted;
            if (!started || room == null || Zoomed || _animating || !TryGetHotspot(out var spot))
            {
                if (!Zoomed) Disarm();
                _hotspot.gameObject.SetActive(false);
                return;
            }

            _hotspot.gameObject.SetActive(true);
            float sf = _canvas.scaleFactor;
            _hotspot.position = spot.center;
            float size = Mathf.Max(spot.width, spot.height) + 40f;
            _hotspot.sizeDelta = new Vector2(size, size) / sf;

            bool ringHere = RingInside(spot);
            if (ringHere)
            {
                _grace = 0.6f;
                if (!_armed)
                {
                    _dwell += Time.deltaTime;
                    if (_dwell >= placeSeconds)
                    {
                        _armed = true;
                        _enterAccum = 0f;
                    }
                }
            }
            else if ((_grace -= Time.deltaTime) <= 0f)
            {
                Disarm();
            }

            // Visual feedback.
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 3f);
            _hotspotFill.fillAmount = _armed ? 1f : Mathf.Clamp01(_dwell / placeSeconds);
            _hotspotFill.color = _armed ? accentColor : new Color(accentColor.r, accentColor.g, accentColor.b, 0.85f);
            _hotspotRing.color = new Color(1f, 1f, 1f, ringHere || _armed ? 0.8f : 0.18f + 0.22f * pulse);
            _hotspotLabel.text = _armed ? "Turn the ring 90°" : _dwell > 0f ? "Hold the ring still..." : "";
        }

        void Disarm()
        {
            _armed = false;
            _dwell = 0f;
            _enterAccum = 0f;
        }

        bool RingInside(Rect spot)
        {
            if (_input == null) return false;
            var area = new Rect(spot.xMin - hotspotMarginPx, spot.yMin - hotspotMarginPx,
                spot.width + 2f * hotspotMarginPx, spot.height + 2f * hotspotMarginPx);
            foreach (int id in ringIds)
                if (_input.TryGetScreenPosition(id, out var p) && area.Contains(p)) return true;
            return false;
        }

        /// <summary>Screen rectangle (pixels) of the switchbox while the room faces its wall.</summary>
        public bool TryGetHotspot(out Rect rect)
        {
            rect = default;
            if (panelFace == null || room == null || room.viewCamera == null || room.CurrentWall != panelWall) return false;
            var cam = room.viewCamera;
            var b = panelFace.bounds;
            Vector2 min = new(float.MaxValue, float.MaxValue), max = new(float.MinValue, float.MinValue);
            for (int i = 0; i < 8; i++)
            {
                var c = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                var sp = cam.WorldToScreenPoint(c);
                if (sp.z <= 0f) return false;
                min = Vector2.Min(min, sp);
                max = Vector2.Max(max, sp);
            }
            var pr = cam.pixelRect;
            min = Vector2.Max(min, pr.min);
            max = Vector2.Min(max, pr.max);
            if (max.x - min.x < 8f || max.y - min.y < 8f) return false;
            rect = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            return true;
        }

        void OnRotated(int id, float delta)
        {
            if (!IsRing(id) || _animating) return;
            if (Zoomed)
            {
                // Only turning back (opposite to the way in) counts; over-turning further does not need undoing.
                _exitAccum = Mathf.Max(0f, _exitAccum - delta * _entryDir);
                if (_exitAccum >= turnDegrees) ZoomOut();
                return;
            }
            if (!_armed) return;
            _enterAccum += delta;
            if (Mathf.Abs(_enterAccum) >= turnDegrees)
            {
                _entryDir = _enterAccum > 0f ? 1 : -1;
                ZoomIn();
            }
        }

        void OnRemoved(int id)
        {
            if (IsRing(id) && !Zoomed) Disarm();
        }

        // ------------------------------------------------------------------ zoom

        public void ZoomIn()
        {
            if (Zoomed) return;
            Zoomed = true;
            _exitAccum = 0f;
            Disarm();
            if (room != null) room.inputLocked = true;
            StartCoroutine(AnimateZoom(true));
        }

        public void ZoomOut()
        {
            if (!Zoomed) return;
            Zoomed = false;
            if (room != null) room.inputLocked = false;
            StartCoroutine(AnimateZoom(false));
        }

        IEnumerator AnimateZoom(bool open)
        {
            _animating = true;
            _zoomRoot.gameObject.SetActive(true);
            for (float t = 0f; t < 1f; t += Time.deltaTime / 0.35f)
            {
                float k = Mathf.SmoothStep(0f, 1f, open ? t : 1f - t);
                _zoomGroup.alpha = k;
                _panel.localScale = Vector3.one * Mathf.Lerp(0.35f, 1f, k);
                yield return null;
            }
            _zoomGroup.alpha = open ? 1f : 0f;
            _panel.localScale = Vector3.one;
            _zoomRoot.gameObject.SetActive(open);
            _animating = false;
        }

        // ------------------------------------------------------------------ knobs

        void OnTap(Vector2 screen)
        {
            if (!Zoomed || _animating || Solved) return;
            for (int i = 0; i < 8; i++)
            {
                if (!RectTransformUtility.RectangleContainsScreenPoint(_slotHit[i], screen, null)) continue;
                Cycle(i);
                return;
            }
        }

        /// <summary>Screen position (pixels) of a slot while zoomed in (for tests / tutorials).</summary>
        public Vector2 SlotScreenPosition(int slot) => RectTransformUtility.WorldToScreenPoint(null, _slotHit[slot].position);

        /// <summary>Moves a knob one step: middle -> down -> up -> middle.</summary>
        public void Cycle(int slot)
        {
            if (Solved) return;
            _state[slot] = _state[slot] switch { Middle => Down, Down => Up, _ => Middle };
            if (IsSolved()) Solve();
        }

        bool IsSolved()
        {
            for (int i = 0; i < 8; i++)
                if (_state[i] != solution[i]) return false;
            return true;
        }

        void Solve()
        {
            Solved = true;
            UpdateScreen();
            if (PowerController.Instance != null) PowerController.Instance.RestorePower();
            if (GameHUD.Instance != null) GameHUD.Instance.ShowHint(solvedHint, false);
            Debug.Log("[EscapeRoom] Puzzle 1 solved - power restored.");
            OnSolved?.Invoke();
        }

        void UpdateScreen()
        {
            if (_screenText == null) return;
            _screenText.text = Solved ? "POWER\nON" : "POWER\nOFF";
            _screenText.color = Solved ? new Color(0.3f, 1f, 0.4f) : new Color(1f, 0.3f, 0.25f);
            _indicator.color = Solved ? new Color(0.2f, 1f, 0.3f, 0.9f) : new Color(0f, 0f, 0f, 0f);
            _footer.text = Solved
                ? "Power restored.  Turn the ring back 90° to step away."
                : "Tap a knob to move it.  Turn the ring back 90° to step away.";
        }

        float OffsetPx(int state) => state == Up ? -TravelPx : state == Down ? TravelPx : 0f;

        void UpdateKnobVisuals()
        {
            float k = 1f - Mathf.Exp(-16f * Time.deltaTime);
            for (int i = 0; i < 8; i++)
            {
                if (_knobs[i] != null)
                {
                    var target = new Vector2(SlotX[i % 4] * _scale, -(RowY[i / 4] + OffsetPx(_state[i])) * _scale);
                    _knobs[i].anchoredPosition = Vector2.Lerp(_knobs[i].anchoredPosition, target, k);
                }
                if (i < worldKnobs.Length && worldKnobs[i] != null)
                {
                    float y = _state[i] == Up ? worldKnobTravel : _state[i] == Down ? -worldKnobTravel : 0f;
                    var target = _worldKnobMiddle[i] + Vector3.up * y;
                    worldKnobs[i].localPosition = Vector3.Lerp(worldKnobs[i].localPosition, target, k);
                }
            }
        }

        // ------------------------------------------------------------------ UI

        void BuildUI()
        {
            var canvasGo = new GameObject("Switchbox Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 150;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(GameHUD.ReferenceWidth, GameHUD.ReferenceHeight);
            scaler.matchWidthOrHeight = 1f;
            var root = (RectTransform)canvasGo.transform;
            var ringSprite = MakeRingSprite();

            // Hotspot marker over the switchbox.
            _hotspot = NewRect("Ring Hotspot", root);
            _hotspot.anchorMin = _hotspot.anchorMax = Vector2.zero;
            _hotspotRing = AddImage(_hotspot, Color.white, ringSprite);
            var fill = NewRect("Progress", _hotspot);
            Stretch(fill);
            _hotspotFill = AddImage(fill, accentColor, ringSprite);
            _hotspotFill.type = Image.Type.Filled;
            _hotspotFill.fillMethod = Image.FillMethod.Radial360;
            _hotspotFill.fillOrigin = (int)Image.Origin360.Top;
            _hotspotFill.fillAmount = 0f;
            var label = NewRect("Label", _hotspot);
            label.anchorMin = new Vector2(0.5f, 0f);
            label.anchorMax = new Vector2(0.5f, 0f);
            label.pivot = new Vector2(0.5f, 1f);
            label.anchoredPosition = new Vector2(0f, -10f);
            label.sizeDelta = new Vector2(600f, 50f);
            _hotspotLabel = AddText(label, 34, TextAnchor.UpperCenter, Color.white);
            _hotspot.gameObject.SetActive(false);

            // Zoomed-in switchbox over the server room half (above the bottom bar).
            float bar = GameHUD.Instance != null ? GameHUD.Instance.barHeight : 84f;
            _zoomRoot = NewRect("Switchbox Zoom", root);
            _zoomRoot.anchorMin = new Vector2(0.5f, 0f);
            _zoomRoot.anchorMax = new Vector2(1f, 1f);
            _zoomRoot.offsetMin = new Vector2(3f, bar);
            _zoomRoot.offsetMax = Vector2.zero;
            _zoomGroup = _zoomRoot.gameObject.AddComponent<CanvasGroup>();
            _zoomGroup.blocksRaycasts = false;
            AddImage(_zoomRoot, new Color(0f, 0f, 0f, 0.9f), null);

            _scale = PanelHeightUi / PanelPx.y;
            _panel = NewRect("Panel", _zoomRoot);
            _panel.anchorMin = _panel.anchorMax = new Vector2(0.5f, 0.5f);
            _panel.sizeDelta = PanelPx * _scale;
            _panel.anchoredPosition = new Vector2(0f, 25f);
            AddImage(_panel, Color.white, panelSprite);

            // Display text and indicator lamp.
            var screen = PanelChild("Display", ScreenPx);
            _screenText = AddText(screen, 64, TextAnchor.MiddleCenter, Color.red);
            _screenText.fontStyle = FontStyle.Bold;
            _screenText.lineSpacing = 0.9f;
            var ind = PanelChild("Indicator", IndicatorPx);
            _indicator = AddImage(ind, Color.clear, null);

            for (int i = 0; i < 8; i++)
            {
                var hit = PanelChild("Slot " + (i + 1), new Rect(SlotX[i % 4] - SlotHitPx.x / 2f, RowY[i / 4] - SlotHitPx.y / 2f, SlotHitPx.x, SlotHitPx.y));
                _slotHit[i] = hit;

                var knob = NewRect("Knob " + (i + 1), _panel);
                knob.anchorMin = knob.anchorMax = new Vector2(0f, 1f);
                knob.pivot = new Vector2(0.5f, 0.5f);
                knob.sizeDelta = KnobPx * _scale;
                knob.anchoredPosition = new Vector2(SlotX[i % 4] * _scale, -RowY[i / 4] * _scale);
                AddImage(knob, Color.white, i < knobSprites.Length ? knobSprites[i] : null);
                _knobs[i] = knob;
            }

            var foot = NewRect("Footer", _zoomRoot);
            foot.anchorMin = new Vector2(0f, 0f);
            foot.anchorMax = new Vector2(1f, 0f);
            foot.pivot = new Vector2(0.5f, 0f);
            foot.sizeDelta = new Vector2(0f, 60f);
            foot.anchoredPosition = new Vector2(0f, 8f);
            _footer = AddText(foot, 30, TextAnchor.MiddleCenter, Color.white);

            _zoomRoot.gameObject.SetActive(false);
        }

        RectTransform PanelChild(string n, Rect px)
        {
            var rt = NewRect(n, _panel);
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(px.x * _scale, -px.y * _scale);
            rt.sizeDelta = new Vector2(px.width * _scale, px.height * _scale);
            return rt;
        }

        static Sprite MakeRingSprite()
        {
            const int n = 256;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            float c = (n - 1) / 2f, outer = n / 2f - 2f, inner = outer - 14f;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(c, c));
                float a = Mathf.Clamp01(outer - d) * Mathf.Clamp01(d - inner);
                px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
        }

        static RectTransform NewRect(string n, Transform parent)
        {
            var go = new GameObject(n, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        static Image AddImage(RectTransform rt, Color c, Sprite sprite)
        {
            var i = rt.gameObject.AddComponent<Image>();
            i.sprite = sprite;
            i.color = c;
            i.raycastTarget = false;
            if (sprite != null) i.preserveAspect = true;
            return i;
        }

        Text AddText(RectTransform rt, int size, TextAnchor anchor, Color color)
        {
            var t = rt.gameObject.AddComponent<Text>();
            t.font = _font;
            t.fontSize = size;
            t.alignment = anchor;
            t.color = color;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }
    }
}

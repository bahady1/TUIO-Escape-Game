using System.Collections;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace EscapeRoom
{
    /// <summary>
    /// Story text before the game. Each line types itself out; a tap / click anywhere first finishes the
    /// line, the next tap goes to the next line. After the last line the game (and the timer) starts.
    /// </summary>
    public class IntroDialogue : MonoBehaviour
    {
        [System.Serializable]
        public struct DialogueLine
        {
            [Tooltip("Label above the text (place / time, or who is speaking). Empty = 'Default Speaker'.")]
            public string speaker;
            [TextArea(2, 4)] public string text;
            [Tooltip("Shown as a red system alert (faster typing, the box shakes).")]
            public bool alert;
            [Tooltip("The power fails when this line appears.")]
            public bool cutsPower;

            public DialogueLine(string speaker, string text, bool alert = false, bool cutsPower = false)
            {
                this.speaker = speaker;
                this.text = text;
                this.alert = alert;
                this.cutsPower = cutsPower;
            }
        }

        public DialogueLine[] story =
        {
            new("LAB 24  ·  07:42",
                "Exam day. You and your teammate slipped into Lab 24 early to squeeze in one last review. The building is empty. Only the servers are humming."),
            new("LAB 24  ·  07:43",
                "One of you settles in at the workbenches of the main lab. The other ducks into the server room next door to grab the backup drive with your whole project on it."),
            new("LAB 24  ·  07:44",
                "Then the lights flicker. Once. Twice. A sharp crack runs through the walls... and everything goes dark.",
                cutsPower: true),
            new("SECURITY SYSTEM",
                "POWER FAILURE DETECTED.  LOCKDOWN PROTOCOL ACTIVE.  ALL INTERNAL DOORS SEALED.",
                alert: true),
            new("LAB 24  ·  07:45",
                "A red emergency glow is all that's left. You can hear each other through the locked door, but you can't get through it. The exam starts in 15 minutes. Bring the power back. Find a way out. Together."),
        };
        [Tooltip("Label used when a line has no speaker.")]
        public string defaultSpeaker = "LAB 24";
        public float charactersPerSecond = 45f;
        public float startDelay = 0.6f;

        public Color accentColor = new(0.25f, 0.85f, 1f, 1f);
        public Color alertColor = new(1f, 0.28f, 0.22f, 1f);

        public bool IsShowing { get; private set; }

        Font _font;
        GameObject _root;
        CanvasGroup _group;
        Text _text, _continue, _counter, _speaker;
        Image _accent;
        RectTransform _box;
        Vector2 _boxHome;
        int _line = -1;
        bool _typing;
        Coroutine _typer;
        float _shownAt;

        void Awake()
        {
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildUI();
        }

        IEnumerator Start()
        {
            if (TangibleInput.Instance != null) TangibleInput.Instance.Tapped += OnTap;
            IsShowing = true;
            _group.alpha = 0f;
            yield return new WaitForSeconds(startDelay);
            for (float t = 0f; t < 1f; t += Time.deltaTime * 3f)
            {
                _group.alpha = t;
                yield return null;
            }
            _group.alpha = 1f;
            NextLine();
        }

        void OnDestroy()
        {
            if (TangibleInput.Instance != null) TangibleInput.Instance.Tapped -= OnTap;
        }

        void Update()
        {
            if (!IsShowing) return;
            if (_continue != null)
                _continue.color = new Color(1f, 1f, 1f, _typing ? 0f : 0.45f + 0.55f * Mathf.PingPong(Time.time * 1.5f, 1f));
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame)) Advance();
#else
            if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return)) Advance();
#endif
        }

        void OnTap(Vector2 _) => Advance();

        /// <summary>Same as a tap: finish typing, or go to the next line.</summary>
        public void Advance()
        {
            if (!IsShowing || _line < 0) return;
            if (Time.unscaledTime - _shownAt < 0.15f) return; // ignore the tap that opened the line
            if (_typing)
            {
                if (_typer != null) StopCoroutine(_typer);
                _text.text = story[_line].text;
                _typing = false;
                return;
            }
            NextLine();
        }

        void NextLine()
        {
            _line++;
            if (_line >= story.Length)
            {
                StartCoroutine(Finish());
                return;
            }
            var line = story[_line];
            _shownAt = Time.unscaledTime;
            _counter.text = $"{_line + 1} / {story.Length}";
            _speaker.text = string.IsNullOrEmpty(line.speaker) ? defaultSpeaker : line.speaker;

            var color = line.alert ? alertColor : accentColor;
            _speaker.color = color;
            _accent.color = color;
            _text.color = line.alert ? Color.Lerp(alertColor, Color.white, 0.35f) : Color.white;
            _text.fontStyle = line.alert ? FontStyle.Bold : FontStyle.Normal;

            if (line.cutsPower && PowerController.Instance != null) PowerController.Instance.CutPower();
            if (line.alert) StartCoroutine(Shake(0.45f, 14f));
            _typer = StartCoroutine(Type(line.text, line.alert ? 1.6f : 1f));
        }

        IEnumerator Shake(float seconds, float strength)
        {
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                float k = 1f - t / seconds;
                _box.anchoredPosition = _boxHome + Random.insideUnitCircle * strength * k;
                yield return null;
            }
            _box.anchoredPosition = _boxHome;
        }

        IEnumerator Type(string line, float speed)
        {
            _typing = true;
            _text.text = "";
            float shown = 0f;
            while (shown < line.Length)
            {
                shown += Time.deltaTime * charactersPerSecond * speed;
                _text.text = line.Substring(0, Mathf.Min(line.Length, Mathf.FloorToInt(shown)));
                yield return null;
            }
            _text.text = line;
            _typing = false;
        }

        IEnumerator Finish()
        {
            IsShowing = false;
            for (float t = 1f; t > 0f; t -= Time.deltaTime * 3f)
            {
                _group.alpha = t;
                yield return null;
            }
            _root.SetActive(false);
            // Safety net: if a line cut the power but it was skipped very fast, make sure it is off.
            bool anyCut = false;
            foreach (var l in story) anyCut |= l.cutsPower;
            if (anyCut && PowerController.Instance != null && PowerController.Instance.PowerOn)
                PowerController.Instance.CutPower(false);
            if (GameHUD.Instance != null) GameHUD.Instance.BeginGame();
        }

        // ------------------------------------------------------------------ UI

        void BuildUI()
        {
            _root = new GameObject("Intro Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
            _root.transform.SetParent(transform, false);
            var canvas = _root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 300;
            var scaler = _root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(GameHUD.ReferenceWidth, GameHUD.ReferenceHeight);
            scaler.matchWidthOrHeight = 1f;
            _group = _root.GetComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            var root = (RectTransform)_root.transform;

            var dim = Rect("Dim", root);
            Stretch(dim);
            Img(dim, new Color(0f, 0f, 0f, 0.55f));

            var box = Rect("Dialogue Box", root);
            box.anchorMin = box.anchorMax = new Vector2(0.5f, 0.5f);
            box.sizeDelta = new Vector2(1800f, 380f);
            box.anchoredPosition = new Vector2(0f, -120f);
            Img(box, new Color(0.04f, 0.05f, 0.07f, 0.94f));
            _box = box;
            _boxHome = box.anchoredPosition;

            var border = Rect("Accent", box);
            border.anchorMin = new Vector2(0f, 1f);
            border.anchorMax = new Vector2(1f, 1f);
            border.pivot = new Vector2(0.5f, 1f);
            border.sizeDelta = new Vector2(0f, 5f);
            _accent = Img(border, accentColor);

            var name = Rect("Speaker", box);
            name.anchorMin = new Vector2(0f, 1f);
            name.anchorMax = new Vector2(1f, 1f);
            name.pivot = new Vector2(0f, 1f);
            name.offsetMin = new Vector2(50f, -80f);
            name.offsetMax = new Vector2(-50f, -20f);
            _speaker = Txt(name, 32, TextAnchor.MiddleLeft, accentColor);
            _speaker.fontStyle = FontStyle.Bold;
            _speaker.text = defaultSpeaker;

            var counterRt = Rect("Counter", box);
            counterRt.anchorMin = new Vector2(0f, 1f);
            counterRt.anchorMax = new Vector2(1f, 1f);
            counterRt.pivot = new Vector2(1f, 1f);
            counterRt.offsetMin = new Vector2(50f, -80f);
            counterRt.offsetMax = new Vector2(-50f, -20f);
            _counter = Txt(counterRt, 26, TextAnchor.MiddleRight, new Color(1f, 1f, 1f, 0.5f));

            var body = Rect("Text", box);
            Stretch(body);
            body.offsetMin = new Vector2(50f, 70f);
            body.offsetMax = new Vector2(-50f, -90f);
            _text = Txt(body, 40, TextAnchor.UpperLeft, Color.white);
            _text.horizontalOverflow = HorizontalWrapMode.Wrap;
            _text.lineSpacing = 1.15f;

            var cont = Rect("Continue", box);
            cont.anchorMin = new Vector2(1f, 0f);
            cont.anchorMax = new Vector2(1f, 0f);
            cont.pivot = new Vector2(1f, 0f);
            cont.anchoredPosition = new Vector2(-40f, 20f);
            cont.sizeDelta = new Vector2(700f, 50f);
            _continue = Txt(cont, 28, TextAnchor.MiddleRight, Color.white);
            _continue.text = "Tap anywhere to continue  ▶";
        }

        static RectTransform Rect(string n, Transform parent)
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

        static Image Img(RectTransform rt, Color c)
        {
            var i = rt.gameObject.AddComponent<Image>();
            i.color = c;
            i.raycastTarget = false;
            return i;
        }

        Text Txt(RectTransform rt, int size, TextAnchor anchor, Color color)
        {
            var t = rt.gameObject.AddComponent<Text>();
            t.font = _font;
            t.fontSize = size;
            t.alignment = anchor;
            t.color = color;
            t.raycastTarget = false;
            t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }
    }
}

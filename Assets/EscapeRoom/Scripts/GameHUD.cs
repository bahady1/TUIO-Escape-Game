using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace EscapeRoom
{
    /// <summary>
    /// Bottom edge of the table: room / wall indicators, the 15-minute countdown and a one-line hint.
    /// Also splits the screen: left half = left room camera, right half = right room camera.
    /// The UI is created at runtime, so nothing has to be wired by hand.
    ///
    /// From other scripts:  GameHUD.Instance.ShowHint("...");  GameHUD.Instance.Win();
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public class GameHUD : MonoBehaviour
    {
        public const float ReferenceWidth = 2560f;   // QHD
        public const float ReferenceHeight = 1440f;

        public static GameHUD Instance { get; private set; }

        [Header("Rooms (left / right half of the table)")]
        public WallViewController leftRoom;
        public WallViewController rightRoom;

        [Header("Timer")]
        public float timeLimitMinutes = 15f;
        [Tooltip("Off when an IntroDialogue starts the game after the story.")]
        public bool startTimerOnPlay = true;
        [Tooltip("Below this many seconds the timer turns red and pulses.")]
        public float warningSeconds = 60f;

        [Header("Hints (one line each)")]
        [TextArea(1, 2)] public string startMessage = "Turn object 1 to look around the lab, object 2 for the server room.";
        [TextArea(1, 2)] public string[] hints =
        {
            "The 3D printer was the last thing anyone used in the lab.",
            "Posters are not only decoration - look closely.",
            "The server room door needs a code. The lab knows it.",
            "Count what you see, not what you expect.",
        };
        [Tooltip("Press H to show the next hint (testing / game master).")]
        public bool hintHotkey = true;

        [Header("Bottom bar (pixels at 2560x1440)")]
        public float barHeight = 84f;
        public Color barColor = new(0.05f, 0.06f, 0.08f, 1f);
        public Color accentColor = new(0.25f, 0.85f, 1f, 1f);
        public Color warningColor = new(1f, 0.3f, 0.25f, 1f);

        public event Action TimeUp;
        public event Action<string> HintShown;

        public float RemainingSeconds { get; private set; }
        public bool IsRunning { get; private set; }
        /// <summary>False while the intro dialogue is shown; tangibles are ignored until then.</summary>
        public bool GameStarted { get; private set; }
        public bool IsFinished { get; private set; }
        public int HintsUsed { get; private set; }

        Font _font;
        Text _timerText, _hintText, _leftLabel, _rightLabel, _bannerText;
        GameObject _banner;
        int _nextHint;
        Coroutine _hintFade;

        /// <summary>Viewport of the left (true) or right (false) room for a given bar height.</summary>
        public static Rect ViewportFor(bool left, float barHeightPx)
        {
            float b = Mathf.Clamp01(barHeightPx / ReferenceHeight);
            return new Rect(left ? 0f : 0.5f, b, 0.5f, 1f - b);
        }

        void Awake()
        {
            Instance = this;
            RemainingSeconds = timeLimitMinutes * 60f;
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildUI();
            ApplyViewports();
        }

        void Start()
        {
            if (startTimerOnPlay) BeginGame();
            UpdateTimerText();
            UpdateRoomLabels();
        }

        /// <summary>Starts the game: tangibles become active, the timer starts and the first hint line appears.</summary>
        public void BeginGame()
        {
            if (GameStarted) return;
            GameStarted = true;
            ShowHint(startMessage, false);
            StartTimer();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void ApplyViewports()
        {
            if (leftRoom != null && leftRoom.viewCamera != null) leftRoom.viewCamera.rect = ViewportFor(true, barHeight);
            if (rightRoom != null && rightRoom.viewCamera != null) rightRoom.viewCamera.rect = ViewportFor(false, barHeight);
        }

        // ------------------------------------------------------------------ timer

        public void StartTimer()
        {
            if (IsFinished) return;
            GameStarted = true;
            IsRunning = true;
        }

        public void PauseTimer() => IsRunning = false;

        public void ResetTimer()
        {
            RemainingSeconds = timeLimitMinutes * 60f;
            IsFinished = false;
            if (_banner != null) _banner.SetActive(false);
            UpdateTimerText();
        }

        public void AddTime(float seconds)
        {
            RemainingSeconds = Mathf.Max(0f, RemainingSeconds + seconds);
            UpdateTimerText();
        }

        /// <summary>Call when the players solved the room.</summary>
        public void Win(string message = "YOU ESCAPED!")
        {
            if (IsFinished) return;
            IsRunning = false;
            IsFinished = true;
            float used = timeLimitMinutes * 60f - RemainingSeconds;
            ShowBanner($"{message}\n<size=48>Time: {Format(used)}   Hints used: {HintsUsed}</size>", accentColor);
        }

        void Update()
        {
            if (IsRunning)
            {
                RemainingSeconds -= Time.deltaTime;
                if (RemainingSeconds <= 0f)
                {
                    RemainingSeconds = 0f;
                    IsRunning = false;
                    IsFinished = true;
                    ShowBanner("TIME'S UP!", warningColor);
                    ShowHint("The lab is locked down. Ask the game master to try again.", false);
                    TimeUp?.Invoke();
                }
            }
            UpdateTimerText();
            UpdateRoomLabels();

#if ENABLE_INPUT_SYSTEM
            if (hintHotkey && Keyboard.current != null && Keyboard.current.hKey.wasPressedThisFrame) ShowNextHint();
#else
            if (hintHotkey && Input.GetKeyDown(KeyCode.H)) ShowNextHint();
#endif
        }

        void UpdateTimerText()
        {
            if (_timerText == null) return;
            _timerText.text = Format(RemainingSeconds);
            bool warn = RemainingSeconds <= warningSeconds;
            _timerText.color = warn
                ? Color.Lerp(warningColor, Color.white, IsRunning ? Mathf.PingPong(Time.time * 2f, 1f) * 0.6f : 0f)
                : Color.white;
        }

        static string Format(float seconds)
        {
            int s = Mathf.Max(0, Mathf.CeilToInt(seconds));
            return $"{s / 60:00}:{s % 60:00}";
        }

        // ------------------------------------------------------------------ hints

        /// <summary>Show a one-line hint at the bottom of the table.</summary>
        public void ShowHint(string text, bool countAsHint = true)
        {
            if (_hintText == null) return;
            if (countAsHint) HintsUsed++;
            _hintText.text = string.IsNullOrEmpty(text) ? "" : $"<color=#{ColorUtility.ToHtmlStringRGB(accentColor)}>HINT</color>   {text}";
            if (_hintFade != null) StopCoroutine(_hintFade);
            _hintFade = StartCoroutine(FadeIn(_hintText));
            HintShown?.Invoke(text);
        }

        /// <summary>Shows the next hint from the 'hints' list.</summary>
        public void ShowNextHint()
        {
            if (hints == null || hints.Length == 0) return;
            ShowHint(hints[_nextHint % hints.Length]);
            _nextHint++;
        }

        public void ClearHint() => ShowHint("", false);

        IEnumerator FadeIn(Text t)
        {
            var c = t.color;
            for (float a = 0f; a < 1f; a += Time.unscaledDeltaTime * 3f)
            {
                c.a = a;
                t.color = c;
                yield return null;
            }
            c.a = 1f;
            t.color = c;
        }

        // ------------------------------------------------------------------ labels

        void UpdateRoomLabels()
        {
            if (_leftLabel != null) _leftLabel.text = RoomLabel(leftRoom);
            if (_rightLabel != null) _rightLabel.text = RoomLabel(rightRoom);
        }

        string RoomLabel(WallViewController room)
        {
            if (room == null) return "";
            var dots = new char[4];
            for (int i = 0; i < 4; i++) dots[i] = i == room.CurrentWall ? '●' : '○';
            return $"<b>{room.roomName.ToUpperInvariant()}</b>  <color=#{ColorUtility.ToHtmlStringRGB(accentColor)}>{new string(dots)}</color>  <size=22>{room.CurrentWallName.ToUpperInvariant()}</size>";
        }

        // ------------------------------------------------------------------ UI construction

        void ShowBanner(string text, Color color)
        {
            if (_banner == null) return;
            _bannerText.text = text;
            _bannerText.color = color;
            _banner.SetActive(true);
        }

        void BuildUI()
        {
            var canvasGo = new GameObject("HUD Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f; // keep bar height proportional to screen height -> matches camera viewports
            var root = (RectTransform)canvasGo.transform;

            // Divider between the two rooms (above the bar).
            var divider = NewRect("Room Divider", root);
            divider.anchorMin = new Vector2(0.5f, 0f);
            divider.anchorMax = new Vector2(0.5f, 1f);
            divider.offsetMin = new Vector2(-3f, barHeight);
            divider.offsetMax = new Vector2(3f, 0f);
            AddImage(divider, Color.black);

            // Bottom bar.
            var bar = NewRect("Bottom Bar", root);
            bar.anchorMin = new Vector2(0f, 0f);
            bar.anchorMax = new Vector2(1f, 0f);
            bar.pivot = new Vector2(0.5f, 0f);
            bar.sizeDelta = new Vector2(0f, barHeight);
            AddImage(bar, barColor);

            var line = NewRect("Top Line", bar);
            line.anchorMin = new Vector2(0f, 1f);
            line.anchorMax = new Vector2(1f, 1f);
            line.pivot = new Vector2(0.5f, 1f);
            line.sizeDelta = new Vector2(0f, 3f);
            AddImage(line, accentColor * new Color(1f, 1f, 1f, 0.8f));

            // Left room label.
            var left = NewRect("Left Room", bar);
            left.anchorMin = new Vector2(0f, 0f);
            left.anchorMax = new Vector2(0f, 1f);
            left.pivot = new Vector2(0f, 0.5f);
            left.anchoredPosition = new Vector2(30f, 0f);
            left.sizeDelta = new Vector2(440f, 0f);
            _leftLabel = AddText(left, 30, TextAnchor.MiddleLeft, Color.white);

            // Timer box.
            var timerBox = NewRect("Timer", bar);
            timerBox.anchorMin = new Vector2(0f, 0.5f);
            timerBox.anchorMax = new Vector2(0f, 0.5f);
            timerBox.pivot = new Vector2(0f, 0.5f);
            timerBox.anchoredPosition = new Vector2(490f, -1f);
            timerBox.sizeDelta = new Vector2(240f, barHeight - 22f);
            AddImage(timerBox, new Color(1f, 1f, 1f, 0.07f));
            var timerText = NewRect("Time", timerBox);
            Stretch(timerText);
            _timerText = AddText(timerText, 50, TextAnchor.MiddleCenter, Color.white);
            _timerText.fontStyle = FontStyle.Bold;

            // Hint line (one line, shrinks to fit).
            var hint = NewRect("Hint", bar);
            hint.anchorMin = new Vector2(0f, 0.5f);
            hint.anchorMax = new Vector2(1f, 0.5f);
            hint.offsetMin = new Vector2(770f, -23f);
            hint.offsetMax = new Vector2(-500f, 23f);
            _hintText = AddText(hint, 34, TextAnchor.MiddleLeft, Color.white);
            _hintText.horizontalOverflow = HorizontalWrapMode.Wrap;
            _hintText.verticalOverflow = VerticalWrapMode.Truncate;
            _hintText.resizeTextForBestFit = true;
            _hintText.resizeTextMinSize = 18;
            _hintText.resizeTextMaxSize = 34;

            // Right room label.
            var right = NewRect("Right Room", bar);
            right.anchorMin = new Vector2(1f, 0f);
            right.anchorMax = new Vector2(1f, 1f);
            right.pivot = new Vector2(1f, 0.5f);
            right.anchoredPosition = new Vector2(-30f, 0f);
            right.sizeDelta = new Vector2(440f, 0f);
            _rightLabel = AddText(right, 30, TextAnchor.MiddleRight, Color.white);

            // End-of-game banner (hidden).
            var banner = NewRect("Banner", root);
            banner.anchorMin = banner.anchorMax = new Vector2(0.5f, 0.5f);
            banner.sizeDelta = new Vector2(1500f, 320f);
            banner.anchoredPosition = new Vector2(0f, barHeight * 0.5f);
            AddImage(banner, new Color(0f, 0f, 0f, 0.85f));
            var bannerText = NewRect("Text", banner);
            Stretch(bannerText);
            _bannerText = AddText(bannerText, 110, TextAnchor.MiddleCenter, Color.white);
            _bannerText.fontStyle = FontStyle.Bold;
            _banner = banner.gameObject;
            _banner.SetActive(false);
        }

        static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        static void AddImage(RectTransform rt, Color color)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
        }

        Text AddText(RectTransform rt, int size, TextAnchor anchor, Color color)
        {
            var t = rt.gameObject.AddComponent<Text>();
            t.font = _font;
            t.fontSize = size;
            t.alignment = anchor;
            t.color = color;
            t.supportRichText = true;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }
    }
}

using System;
using System.Collections.Generic;
using TuioNet.Tuio11;
using TuioNet.Tuio20;
using TuioUnity.Common;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace EscapeRoom
{
    /// <summary>
    /// Single entry point for everything the table sends:
    ///   - tangibles (by marker / symbol id): rotation deltas + screen position
    ///   - finger touches (TUIO cursors) and mouse clicks: "Tapped" with a screen position
    ///
    /// Simulation without the table:
    ///   A / D            turn object 1          Left / Right   turn object 2
    ///   mouse wheel      turn object 1 (left half) or 2 (right half)
    ///   R                place / remove the ring (object 4) at the mouse position
    ///   Q / E            turn the ring -90 / +90 degrees
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class TangibleInput : MonoBehaviour
    {
        public static TangibleInput Instance { get; private set; }

        [Tooltip("TUIO session in the scene. Found automatically when empty.")]
        public TuioSessionBehaviour session;

        [Tooltip("Angle changes smaller than this (degrees) are treated as tracker jitter.")]
        public float jitterThreshold = 0.5f;

        [Tooltip("Log every tangible that is placed on / removed from the table (handy to find the marker ids).")]
        public bool logObjects = true;

        [Header("Taps")]
        [Tooltip("Mouse clicks also count as taps (the table's touch screen usually sends mouse clicks too).")]
        public bool mouseClicksAsTaps = true;
        [Tooltip("A TUIO touch and the matching OS click arrive together; taps this close in time and space count once.")]
        public float tapDedupeSeconds = 0.25f;
        public float tapDedupePixels = 90f;

        [Header("Simulation (keyboard / mouse, no table needed)")]
        public bool enableSimulation = true;
        public int leftSimulatedId = 1;
        public int rightSimulatedId = 2;
        public int ringSimulatedId = 4;
        [Tooltip("Degrees sent per key press. Match it to 'Degrees Per Wall' to move one wall per press.")]
        public float simulatedKeyStep = 60f;
        public float simulatedWheelStep = 20f;
        public float simulatedRingStep = 90f;

        /// <summary>(symbolId, deltaDegrees). Positive = clockwise.</summary>
        public event Action<int, float> Rotated;
        public event Action<int> ObjectAdded;
        public event Action<int> ObjectRemoved;
        /// <summary>Finger touch or click, in screen pixels.</summary>
        public event Action<Vector2> Tapped;

        sealed class Tracked
        {
            public int Id;
            public Func<float> AngleRadians;
            public Func<Vector2> Position; // TUIO normalised, y down
            public float LastDegrees;
        }

        readonly Dictionary<uint, Tracked> _tracked = new();
        readonly Dictionary<int, Vector2> _simulated = new();
        Tuio11Dispatcher _d11;
        Tuio20Dispatcher _d20;
        float _lastTapTime = -10f;
        Vector2 _lastTapPos;

        void Awake()
        {
            Instance = this;
            if (session == null) session = FindAnyObjectByType<TuioSessionBehaviour>();
            if (session == null)
                Debug.LogWarning("[EscapeRoom] No TuioSessionBehaviour in the scene - only keyboard/mouse simulation is active.");
        }

        void OnEnable()
        {
            if (session == null) return;
            var dispatcher = session.TuioDispatcher;
            if (dispatcher is Tuio11Dispatcher d11)
            {
                _d11 = d11;
                _d11.OnObjectAdd += Add11;
                _d11.OnObjectRemove += Remove11;
                _d11.OnCursorAdd += Cursor11;
            }
            else if (dispatcher is Tuio20Dispatcher d20)
            {
                _d20 = d20;
                _d20.OnObjectAdd += Add20;
                _d20.OnObjectRemove += Remove20;
            }
        }

        void OnDisable()
        {
            if (_d11 != null)
            {
                _d11.OnObjectAdd -= Add11;
                _d11.OnObjectRemove -= Remove11;
                _d11.OnCursorAdd -= Cursor11;
                _d11 = null;
            }
            if (_d20 != null)
            {
                _d20.OnObjectAdd -= Add20;
                _d20.OnObjectRemove -= Remove20;
                _d20 = null;
            }
            _tracked.Clear();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ---------- TUIO 1.1 ----------
        void Add11(object sender, Tuio11Object obj) =>
            Track(obj.SessionId, (int)obj.SymbolId, () => obj.Angle, () => new Vector2(obj.Position.X, obj.Position.Y));

        void Remove11(object sender, Tuio11Object obj) => Untrack(obj.SessionId);

        void Cursor11(object sender, Tuio11Cursor cursor) => Tap(ToScreen(new Vector2(cursor.Position.X, cursor.Position.Y)));

        // ---------- TUIO 2.0 ----------
        void Add20(object sender, Tuio20Object obj)
        {
            if (obj.ContainsNewTuioToken())
            {
                var token = obj.Token;
                Track(obj.SessionId, (int)token.ComponentId, () => token.Angle, () => new Vector2(token.Position.X, token.Position.Y));
            }
            else if (obj.ContainsNewTuioPointer())
            {
                var p = obj.Pointer;
                Tap(ToScreen(new Vector2(p.Position.X, p.Position.Y)));
            }
        }

        void Remove20(object sender, Tuio20Object obj) => Untrack(obj.SessionId);

        // ---------- tracking ----------
        void Track(uint sessionId, int symbolId, Func<float> angle, Func<Vector2> position)
        {
            _tracked[sessionId] = new Tracked
            {
                Id = symbolId,
                AngleRadians = angle,
                Position = position,
                LastDegrees = angle() * Mathf.Rad2Deg
            };
            if (logObjects) Debug.Log($"[EscapeRoom] Tangible {symbolId} placed on the table.");
            ObjectAdded?.Invoke(symbolId);
        }

        void Untrack(uint sessionId)
        {
            if (!_tracked.Remove(sessionId, out var t)) return;
            if (logObjects) Debug.Log($"[EscapeRoom] Tangible {t.Id} removed from the table.");
            ObjectRemoved?.Invoke(t.Id);
        }

        static Vector2 ToScreen(Vector2 tuio) => new(tuio.x * Screen.width, (1f - tuio.y) * Screen.height);

        /// <summary>Is the tangible with this marker id on the table right now?</summary>
        public bool IsPresent(int symbolId) => TryGetScreenPosition(symbolId, out _);

        /// <summary>Screen position (pixels) of a tangible that is on the table.</summary>
        public bool TryGetScreenPosition(int symbolId, out Vector2 screen)
        {
            foreach (var t in _tracked.Values)
            {
                if (t.Id != symbolId) continue;
                screen = ToScreen(t.Position());
                return true;
            }
            return _simulated.TryGetValue(symbolId, out screen);
        }

        void Tap(Vector2 screen)
        {
            if (Time.unscaledTime - _lastTapTime < tapDedupeSeconds && Vector2.Distance(screen, _lastTapPos) < tapDedupePixels) return;
            _lastTapTime = Time.unscaledTime;
            _lastTapPos = screen;
            Tapped?.Invoke(screen);
        }

        void Update()
        {
            foreach (var t in _tracked.Values)
            {
                float degrees = t.AngleRadians() * Mathf.Rad2Deg;
                float delta = Mathf.DeltaAngle(t.LastDegrees, degrees);
                if (Mathf.Abs(delta) < jitterThreshold) continue;
                t.LastDegrees = degrees;
                Rotated?.Invoke(t.Id, delta);
            }

            UpdateMouseAndKeys();
        }

        /// <summary>Feed a rotation as if it came from the table (also usable from other scripts / UI buttons).</summary>
        public void Simulate(int symbolId, float deltaDegrees) => Rotated?.Invoke(symbolId, deltaDegrees);

        /// <summary>Place (or move) a simulated tangible at a screen position.</summary>
        public void SimulatePlace(int symbolId, Vector2 screen)
        {
            bool isNew = !_simulated.ContainsKey(symbolId);
            _simulated[symbolId] = screen;
            if (isNew) ObjectAdded?.Invoke(symbolId);
        }

        public void SimulateRemove(int symbolId)
        {
            if (_simulated.Remove(symbolId)) ObjectRemoved?.Invoke(symbolId);
        }

        /// <summary>Feed a tap as if it came from the table.</summary>
        public void SimulateTap(Vector2 screen) => Tap(screen);

        void UpdateMouseAndKeys()
        {
            bool click = false, rKey = false, qKey = false, eKey = false, aKey = false, dKey = false, left = false, right = false;
            Vector2 mousePos = Vector2.zero;
            float scroll = 0f;
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null)
            {
                rKey = kb.rKey.wasPressedThisFrame; qKey = kb.qKey.wasPressedThisFrame; eKey = kb.eKey.wasPressedThisFrame;
                aKey = kb.aKey.wasPressedThisFrame; dKey = kb.dKey.wasPressedThisFrame;
                left = kb.leftArrowKey.wasPressedThisFrame; right = kb.rightArrowKey.wasPressedThisFrame;
            }
            var mouse = Mouse.current;
            if (mouse != null)
            {
                mousePos = mouse.position.ReadValue();
                click = mouse.leftButton.wasPressedThisFrame;
                scroll = mouse.scroll.ReadValue().y;
            }
#else
            rKey = Input.GetKeyDown(KeyCode.R); qKey = Input.GetKeyDown(KeyCode.Q); eKey = Input.GetKeyDown(KeyCode.E);
            aKey = Input.GetKeyDown(KeyCode.A); dKey = Input.GetKeyDown(KeyCode.D);
            left = Input.GetKeyDown(KeyCode.LeftArrow); right = Input.GetKeyDown(KeyCode.RightArrow);
            mousePos = Input.mousePosition;
            click = Input.GetMouseButtonDown(0);
            scroll = Input.mouseScrollDelta.y;
#endif
            if (mouseClicksAsTaps && click) Tap(mousePos);
            if (!enableSimulation) return;

            if (dKey) Simulate(leftSimulatedId, simulatedKeyStep);
            if (aKey) Simulate(leftSimulatedId, -simulatedKeyStep);
            if (right) Simulate(rightSimulatedId, simulatedKeyStep);
            if (left) Simulate(rightSimulatedId, -simulatedKeyStep);
            if (Mathf.Abs(scroll) > 0.01f)
            {
                int id = mousePos.x < Screen.width * 0.5f ? leftSimulatedId : rightSimulatedId;
                Simulate(id, -Mathf.Sign(scroll) * simulatedWheelStep);
            }

            // Simulated ring: R places it at the mouse (and it follows the mouse), R again removes it.
            if (rKey)
            {
                if (_simulated.ContainsKey(ringSimulatedId)) SimulateRemove(ringSimulatedId);
                else SimulatePlace(ringSimulatedId, mousePos);
            }
            else if (_simulated.ContainsKey(ringSimulatedId))
            {
                _simulated[ringSimulatedId] = mousePos;
            }
            if (eKey) Simulate(ringSimulatedId, simulatedRingStep);
            if (qKey) Simulate(ringSimulatedId, -simulatedRingStep);
        }
    }
}

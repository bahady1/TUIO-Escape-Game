using System;
using System.Collections.Generic;
using TuioNet.Common;
using TuioNet.Tuio11;
using TuioNet.Tuio20;
using TuioUnity.Common;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace EscapeRoom
{
    public enum PuzzleInputMode { LocalMouse, Table }

    /// <summary>
    /// Additive adapter for Puzzles 2/3. Shares the scene's existing TUIO dispatcher;
    /// never opens a connection or changes TangibleInput/Puzzle 1 configuration.
    /// Local confirmations deliberately simulate two people for a one-mouse demo.
    /// Table confirmations only accept two distinct physical marker identities.
    /// </summary>
    [DefaultExecutionOrder(50)]
    public class CooperativePuzzleInput : MonoBehaviour
    {
        [Header("Existing session (never creates a second client)")]
        public TuioSessionBehaviour session;
        public PuzzleInputMode mode = PuzzleInputMode.LocalMouse;

        [Header("Verified laboratory bindings; -1 means unbound")]
        public int roleATokenId = -1;
        public int roleBTokenId = -1;
        public int packetTokenId = -1;
        public int decoderTokenId = -1;
        public int relaySelectorTokenId = -1;

        [Header("Tracking calibration")]
        [Tooltip("TUIO normally uses top-left origin; Unity screen coordinates use bottom-left.")]
        public bool flipY = true;
        public bool flipX;
        [Min(0.01f)] public float rotationJitterDegrees = 1f;
        [Tooltip("Reject stale physical positions after this many seconds without a TUIO frame. Verify the lab's heartbeat cadence.")]
        [Min(0.1f)] public float streamTimeoutSeconds = 1f;
        [Tooltip("Logs observed marker IDs for binding, never puzzle answers.")]
        public bool logObservedMarkers = true;

        /// <summary>Screen-pixel press: mouse only in LocalMouse; TUIO cursor/pointer only in Table.</summary>
        public event Action<Vector2> Tapped;
        /// <summary>Physical marker ID and signed rotation delta in degrees. Table mode only.</summary>
        public event Action<int, float> TokenRotated;
        public event Action Changed;

        private sealed class Token
        {
            public int Id;
            public Func<Vector2> Position;
            public Func<float> Angle;
            public float LastDegrees;
        }

        private readonly Dictionary<uint, Token> tokens = new Dictionary<uint, Token>();
        private readonly HashSet<uint> pointers = new HashSet<uint>();
        private readonly bool[] localConfirmation = new bool[2];
        private readonly List<Token> rotationSnapshot = new List<Token>();
        private Tuio11Dispatcher dispatcher11;
        private Tuio20Dispatcher dispatcher20;
        private TuioSessionBehaviour boundSession;
        private PuzzleInputMode previousMode;
        private float lastFrameTime = float.NegativeInfinity;

        public bool IsTableMode => mode == PuzzleInputMode.Table;
        public bool HasFreshTracking => boundSession != null && boundSession.isActiveAndEnabled &&
            Time.unscaledTime - lastFrameTime <= Mathf.Max(0.1f, streamTimeoutSeconds);
        public bool RoleBindingsValid => AreRoleBindingsValid(roleATokenId, roleBTokenId);
        public bool LocalPlayerAHeld => !IsTableMode && localConfirmation[0];
        public bool LocalPlayerBHeld => !IsTableMode && localConfirmation[1];
        public string Status
        {
            get
            {
                if (!IsTableMode) return "LOCAL DEMO - confirmation pads simulate two players";
                if (session == null) return "TABLE - no existing TUIO session assigned";
                if (!RoleBindingsValid) return "TABLE - bind two different A/B marker IDs in the Inspector";
                if (!HasFreshTracking) return "TABLE - waiting for live TUIO frames";
                return "TABLE - live tracking; hold A/B markers on their own pads";
            }
        }

        private void Awake()
        {
            if (session == null)
            {
                // Prefer the exact session used by the existing input entry point.
                var existing = TangibleInput.Instance != null ? TangibleInput.Instance : FindAnyObjectByType<TangibleInput>();
                if (existing != null) session = existing.session;
                if (session == null) session = FindAnyObjectByType<TuioSessionBehaviour>();
            }
            previousMode = mode;
        }

        private void OnEnable()
        {
            ClearLocalConfirmations();
            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
            ClearLocalConfirmations();
        }

        public void SetMode(PuzzleInputMode value)
        {
            if (mode == value) return;
            mode = value;
            previousMode = value;
            ClearLocalConfirmations();
            ResetRotationBaselines();
            Changed?.Invoke();
        }

        public void SetLocalConfirmation(int player, bool held)
        {
            if (IsTableMode || player < 1 || player > 2) return;
            if (localConfirmation[player - 1] == held) return;
            localConfirmation[player - 1] = held;
            Changed?.Invoke();
        }

        public bool GetLocalConfirmation(int player)
            => !IsTableMode && player >= 1 && player <= 2 && localConfirmation[player - 1];

        public void ClearLocalConfirmations()
        {
            localConfirmation[0] = localConfirmation[1] = false;
            Changed?.Invoke();
        }

        public static bool AreRoleBindingsValid(int a, int b) => a >= 0 && b >= 0 && a != b;

        public bool IsRoleHeld(int player, RectTransform pad)
        {
            if (!isActiveAndEnabled || player < 1 || player > 2) return false;
            if (!IsTableMode) return localConfirmation[player - 1];
            if (!RoleBindingsValid || pad == null || !pad.gameObject.activeInHierarchy) return false;
            int id = player == 1 ? roleATokenId : roleBTokenId;
            return TryGetTokenScreenPosition(id, out Vector2 screen) && ContainsScreenPoint(pad, screen);
        }

        public static bool ContainsScreenPoint(RectTransform area, Vector2 screen)
        {
            if (area == null || !area.gameObject.activeInHierarchy) return false;
            var canvas = area.GetComponentInParent<Canvas>();
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            return RectTransformUtility.RectangleContainsScreenPoint(area, screen, camera);
        }

        /// <summary>Only live physical markers, never TangibleInput's keyboard-simulated markers.</summary>
        public bool TryGetTokenScreenPosition(int symbolId, out Vector2 screen)
        {
            screen = default;
            if (!isActiveAndEnabled || !IsTableMode || symbolId < 0 || !HasFreshTracking) return false;
            Token match = null;
            foreach (Token token in tokens.Values)
            {
                if (token.Id != symbolId) continue;
                // Ambiguous duplicates must not silently count as a role token.
                if (match != null) return false;
                match = token;
            }
            if (match == null) return false;
            Vector2 normalised = match.Position();
            if (!IsValidPosition(normalised)) return false;
            screen = ToScreen(normalised);
            return true;
        }

        public int[] GetObservedTokenIds()
        {
            var ids = new HashSet<int>();
            foreach (Token token in tokens.Values) ids.Add(token.Id);
            var result = new int[ids.Count];
            ids.CopyTo(result);
            Array.Sort(result);
            return result;
        }

        public static Vector2 NormalizedToScreen(Vector2 normalised, Vector2 size, bool flipX = false, bool flipY = true)
            => new Vector2((flipX ? 1f - normalised.x : normalised.x) * size.x,
                           (flipY ? 1f - normalised.y : normalised.y) * size.y);

        private Vector2 ToScreen(Vector2 normalised)
            => NormalizedToScreen(normalised, new Vector2(Screen.width, Screen.height), flipX, flipY);

        private static bool IsValidPosition(Vector2 value)
            => !float.IsNaN(value.x) && !float.IsNaN(value.y) &&
               value.x >= 0f && value.x <= 1f && value.y >= 0f && value.y <= 1f;

        private void Subscribe()
        {
            if (boundSession == session && session != null && session.isActiveAndEnabled &&
                (dispatcher11 != null || dispatcher20 != null)) return;
            Unsubscribe();
            if (session == null || !session.isActiveAndEnabled) return;
            boundSession = session;
            var dispatcher = session.TuioDispatcher;
            if (dispatcher is Tuio11Dispatcher d11)
            {
                dispatcher11 = d11;
                d11.OnObjectAdd += Add11;
                d11.OnObjectUpdate += Update11;
                d11.OnObjectRemove += Remove11;
                d11.OnCursorAdd += Cursor11;
                d11.OnRefresh += Refresh;
                foreach (var token in d11.GetTuioObjects()) Track11(token, false);
            }
            else if (dispatcher is Tuio20Dispatcher d20)
            {
                dispatcher20 = d20;
                d20.OnObjectAdd += Add20;
                d20.OnObjectUpdate += Update20;
                d20.OnObjectRemove += Remove20;
                d20.OnRefresh += Refresh;
                foreach (var token in d20.GetTuioTokens()) Track20(token, false);
                foreach (var pointer in d20.GetTuioPointer()) pointers.Add(pointer.SessionId);
            }
        }

        private void Unsubscribe()
        {
            if (dispatcher11 != null)
            {
                dispatcher11.OnObjectAdd -= Add11;
                dispatcher11.OnObjectUpdate -= Update11;
                dispatcher11.OnObjectRemove -= Remove11;
                dispatcher11.OnCursorAdd -= Cursor11;
                dispatcher11.OnRefresh -= Refresh;
            }
            if (dispatcher20 != null)
            {
                dispatcher20.OnObjectAdd -= Add20;
                dispatcher20.OnObjectUpdate -= Update20;
                dispatcher20.OnObjectRemove -= Remove20;
                dispatcher20.OnRefresh -= Refresh;
            }
            dispatcher11 = null;
            dispatcher20 = null;
            boundSession = null;
            tokens.Clear();
            pointers.Clear();
            lastFrameTime = float.NegativeInfinity;
        }

        private void Refresh(object sender, TuioTime time) => lastFrameTime = Time.unscaledTime;

        private void Add11(object sender, Tuio11Object token) => Track11(token, true);
        private void Update11(object sender, Tuio11Object token) => Track11(token, false);
        private void Remove11(object sender, Tuio11Object token) { tokens.Remove(token.SessionId); Changed?.Invoke(); }
        private void Cursor11(object sender, Tuio11Cursor cursor)
            => PhysicalTap(new Vector2(cursor.Position.X, cursor.Position.Y));

        private void Track11(Tuio11Object token, bool announce)
            => Track(token.SessionId, (int)token.SymbolId,
                () => new Vector2(token.Position.X, token.Position.Y), () => token.Angle, announce);

        private void Track20(Tuio20Token token, bool announce)
            => Track(token.SessionId, (int)token.ComponentId,
                () => new Vector2(token.Position.X, token.Position.Y), () => token.Angle, announce);

        private void Add20(object sender, Tuio20Object obj) => Observe20(obj, true);
        private void Update20(object sender, Tuio20Object obj) => Observe20(obj, false);
        private void Observe20(Tuio20Object obj, bool announce)
        {
            if (obj.ContainsTuioToken()) Track20(obj.Token, announce);
            else tokens.Remove(obj.SessionId);
            if (obj.ContainsTuioPointer())
            {
                if (pointers.Add(obj.SessionId))
                    PhysicalTap(new Vector2(obj.Pointer.Position.X, obj.Pointer.Position.Y));
            }
            else pointers.Remove(obj.SessionId);
        }

        private void Remove20(object sender, Tuio20Object obj)
        {
            tokens.Remove(obj.SessionId);
            pointers.Remove(obj.SessionId);
            Changed?.Invoke();
        }

        private void Track(uint sessionId, int id, Func<Vector2> position, Func<float> angle, bool announce)
        {
            if (tokens.TryGetValue(sessionId, out Token existing) && existing.Id == id)
            {
                // Refresh delegates too: SDK updates may replace a component instance.
                existing.Position = position;
                existing.Angle = angle;
                return;
            }
            tokens[sessionId] = new Token { Id = id, Position = position, Angle = angle, LastDegrees = angle() * Mathf.Rad2Deg };
            if (announce && logObservedMarkers) Debug.Log($"[Cooperative puzzles] Physical marker observed: {id}");
            Changed?.Invoke();
        }

        private void PhysicalTap(Vector2 normalised)
        {
            if (IsTableMode && IsValidPosition(normalised)) Tapped?.Invoke(ToScreen(normalised));
        }

        private void ResetRotationBaselines()
        {
            foreach (Token token in tokens.Values) token.LastDegrees = token.Angle() * Mathf.Rad2Deg;
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused) ClearLocalConfirmations();
        }

        private void Update()
        {
            if (mode != previousMode)
            {
                previousMode = mode;
                ClearLocalConfirmations();
                ResetRotationBaselines();
                Changed?.Invoke();
            }
            if (session != boundSession || (boundSession != null && !boundSession.isActiveAndEnabled)) Subscribe();

            // Snapshot before raising external events: listeners can disable this adapter.
            rotationSnapshot.Clear();
            rotationSnapshot.AddRange(tokens.Values);
            foreach (Token token in rotationSnapshot)
            {
                float degrees = token.Angle() * Mathf.Rad2Deg;
                float delta = Mathf.DeltaAngle(token.LastDegrees, degrees);
                if (Mathf.Abs(delta) < Mathf.Max(0.01f, rotationJitterDegrees)) continue;
                token.LastDegrees = degrees;
                if (IsTableMode && HasFreshTracking && TryGetTokenScreenPosition(token.Id, out _))
                    TokenRotated?.Invoke(token.Id, delta);
            }
            if (IsTableMode) return;
#if ENABLE_INPUT_SYSTEM
            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame) Tapped?.Invoke(mouse.position.ReadValue());
#else
            if (Input.GetMouseButtonDown(0)) Tapped?.Invoke(Input.mousePosition);
#endif
        }
    }
}

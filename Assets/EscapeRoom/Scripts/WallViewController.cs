using System;
using UnityEngine;

namespace EscapeRoom
{
    /// <summary>
    /// One per room. Sits at the centre of the room on the floor; its camera looks around the room.
    /// The view follows the assigned tangible continuously: turning it by 'degreesPerWall' turns the view
    /// 90 degrees (one wall), a small turn moves the view a little. Clockwise = to the right.
    /// </summary>
    public class WallViewController : MonoBehaviour
    {
        public static readonly string[] WallNames = { "North", "East", "South", "West" };

        [Header("Room")]
        public string roomName = "Main Lab";
        [Tooltip("Marker / symbol id of the tangible that turns this room's view.")]
        public int tangibleId = 1;
        public Camera viewCamera;
        [Range(0, 3)] public int startWall;

        [Header("Turning")]
        [Tooltip("Turning the tangible this far turns the view by one wall (90 degrees). " +
                 "The camera follows the tangible continuously, so a small turn moves the view a little.")]
        public float degreesPerWall = 60f;
        public bool invertDirection;
        [Tooltip("Smoothing while the camera follows the tangible (seconds). Lower = snappier.")]
        public float followSmoothing = 0.12f;
        [Tooltip("Smoothing for whole-wall moves (keyboard, Turn(), snapping).")]
        public float turnTime = 0.35f;
        [Tooltip("When the tangible stops turning, glide to the nearest wall. Off = stay exactly where the token points.")]
        public bool snapToNearestWall = false;
        public float snapDelay = 0.8f;

        [Header("Camera placement")]
        public float eyeHeight = 1.6f;
        [Tooltip("Camera is pulled back from the room centre so the whole facing wall fits in view.")]
        public float distanceBehindCenter = 2.2f;
        [Tooltip("Positive looks slightly down.")]
        public float pitch = 6f;

        public int CurrentWall { get; private set; }
        public string CurrentWallName => WallNames[CurrentWall];

        /// <summary>Raised whenever the view moves to another wall.</summary>
        public event Action<WallViewController> WallChanged;

        float _yaw, _targetYaw, _yawVelocity, _smoothing, _lastTurnTime;
        TangibleInput _input;

        void Start()
        {
            CurrentWall = startWall;
            _yaw = _targetYaw = startWall * 90f;
            _smoothing = turnTime;
            ApplyCamera(_yaw);

            _input = TangibleInput.Instance != null ? TangibleInput.Instance : FindAnyObjectByType<TangibleInput>();
            if (_input != null)
            {
                _input.Rotated += OnRotated;
                _input.ObjectRemoved += OnRemoved;
            }
        }

        void OnDestroy()
        {
            if (_input == null) return;
            _input.Rotated -= OnRotated;
            _input.ObjectRemoved -= OnRemoved;
        }

        /// <summary>When true the tangible does not turn this room (e.g. while zoomed into a puzzle).</summary>
        [NonSerialized] public bool inputLocked;

        void OnRotated(int id, float delta)
        {
            if (id != tangibleId || inputLocked) return;
            if (GameHUD.Instance != null && !GameHUD.Instance.GameStarted) return; // intro still running

            // Continuous: the view turns proportionally with the tangible.
            float viewDelta = (invertDirection ? -delta : delta) * 90f / Mathf.Max(1f, degreesPerWall);
            _targetYaw += viewDelta;
            _smoothing = followSmoothing;
            _lastTurnTime = Time.time;
            UpdateCurrentWall();
        }

        void OnRemoved(int id)
        {
        }

        /// <summary>Wall the view mostly faces = nearest multiple of 90 degrees.</summary>
        void UpdateCurrentWall()
        {
            int wall = ((Mathf.RoundToInt(_targetYaw / 90f) % 4) + 4) % 4;
            if (wall == CurrentWall) return;
            CurrentWall = wall;
            WallChanged?.Invoke(this);
        }

        /// <summary>Turn the view by a number of walls (+1 = clockwise / right, -1 = back), landing exactly on a wall.</summary>
        public void Turn(int walls)
        {
            if (walls == 0) return;
            _targetYaw = Mathf.Round(_targetYaw / 90f) * 90f + walls * 90f;
            _smoothing = turnTime;
            UpdateCurrentWall();
        }

        /// <summary>Jump straight to a wall (0 North, 1 East, 2 South, 3 West).</summary>
        public void ShowWall(int wall)
        {
            int diff = ((wall - CurrentWall) % 4 + 4) % 4;
            if (diff == 3) diff = -1;
            Turn(diff);
        }

        void LateUpdate()
        {
            if (snapToNearestWall && Time.time - _lastTurnTime > snapDelay)
            {
                float snapped = Mathf.Round(_targetYaw / 90f) * 90f;
                if (!Mathf.Approximately(snapped, _targetYaw))
                {
                    _targetYaw = snapped;
                    _smoothing = turnTime;
                }
            }
            _yaw = Mathf.SmoothDamp(_yaw, _targetYaw, ref _yawVelocity, Mathf.Max(0.01f, _smoothing));
            ApplyCamera(_yaw);
        }

        /// <summary>Places the camera for the given yaw. Also used by the editor builder.</summary>
        public void ApplyCamera(float yaw)
        {
            if (viewCamera == null) return;
            var flat = Quaternion.Euler(0f, yaw, 0f);
            Vector3 pos = transform.position + Vector3.up * eyeHeight - flat * Vector3.forward * distanceBehindCenter;
            viewCamera.transform.SetPositionAndRotation(pos, Quaternion.Euler(pitch, yaw, 0f));
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            for (int i = 0; i < 4; i++)
            {
                var dir = Quaternion.Euler(0f, i * 90f, 0f) * Vector3.forward;
                Gizmos.DrawLine(transform.position + Vector3.up * eyeHeight, transform.position + Vector3.up * eyeHeight + dir * 1.5f);
            }
        }
    }
}

using System;
using UnityEngine;

namespace EscapeRoom
{
    /// <summary>
    /// One per room. Sits at the centre of the room on the floor and points its camera at one of the four walls.
    /// Turning the assigned tangible by 'degreesPerWall' turns the view to the next wall
    /// (clockwise = next wall to the right, counter-clockwise = back).
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
        [Tooltip("How far the tangible must be turned to move one wall.")]
        public float degreesPerWall = 60f;
        public bool invertDirection;
        public float turnTime = 0.35f;

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

        float _accumulated;
        float _yaw, _targetYaw, _yawVelocity;
        TangibleInput _input;

        void Start()
        {
            CurrentWall = startWall;
            _yaw = _targetYaw = startWall * 90f;
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
            _accumulated += invertDirection ? -delta : delta;

            while (_accumulated >= degreesPerWall)
            {
                _accumulated -= degreesPerWall;
                Turn(1);
            }
            while (_accumulated <= -degreesPerWall)
            {
                _accumulated += degreesPerWall;
                Turn(-1);
            }
        }

        void OnRemoved(int id)
        {
            if (id == tangibleId) _accumulated = 0f;
        }

        /// <summary>Turn the view by a number of walls (+1 = clockwise / right, -1 = back).</summary>
        public void Turn(int walls)
        {
            if (walls == 0) return;
            CurrentWall = ((CurrentWall + walls) % 4 + 4) % 4;
            _targetYaw += walls * 90f;
            WallChanged?.Invoke(this);
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
            _yaw = Mathf.SmoothDamp(_yaw, _targetYaw, ref _yawVelocity, turnTime);
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

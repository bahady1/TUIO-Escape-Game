using UnityEngine;

namespace EscapeRoom
{
    /// <summary>
    /// The door in the middle of the wall between the lab and the server room.
    /// Call Open() from your puzzle logic (e.g. when the keypad code is solved).
    /// </summary>
    public class Door : MonoBehaviour
    {
        [Tooltip("Pivot the door panel rotates around.")]
        public Transform hinge;
        public float openAngle = 100f;
        public float openSpeed = 1.2f;
        public bool startOpen;

        public bool IsOpen { get; private set; }
        float _t;

        void Awake()
        {
            IsOpen = startOpen;
            _t = startOpen ? 1f : 0f;
        }

        public void Open() => IsOpen = true;
        public void Close() => IsOpen = false;
        public void Toggle() => IsOpen = !IsOpen;

        void Update()
        {
            if (hinge == null) return;
            _t = Mathf.MoveTowards(_t, IsOpen ? 1f : 0f, Time.deltaTime * openSpeed);
            float eased = Mathf.SmoothStep(0f, 1f, _t);
            hinge.localRotation = Quaternion.Euler(0f, openAngle * eased, 0f);
        }
    }
}

using UnityEngine;
using UnityEngine.UI;

namespace EscapeRoom
{
    // All actions use CooperativePuzzleInput. Button.onClick is deliberately unwired,
    // so a TUIO cursor and an OS-emulated click cannot execute an action twice.
    public class PuzzleControl : MonoBehaviour
    {
        public string action;
        public string value;
        public int role;
        public RectTransform Rect => (RectTransform)transform;
        public bool Contains(Vector2 position) => gameObject.activeInHierarchy &&
            RectTransformUtility.RectangleContainsScreenPoint(Rect, position, null);
    }
}

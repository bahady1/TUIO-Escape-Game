using UnityEngine;
using UnityEngine.UI;

namespace EscapeRoom
{
    public class SwitchButtonVisual : MonoBehaviour
    {
        public BlackoutProtocol puzzle;
        [Range(1, 8)] public int switchNumber = 1;
        public Text label;
        public Image face;
        public string colourName;
        void OnEnable() { if (puzzle != null) puzzle.Changed += Refresh; Refresh(); }
        void OnDisable() { if (puzzle != null) puzzle.Changed -= Refresh; }
        public void Refresh()
        {
            if (puzzle == null) return;
            bool on = puzzle.GetSwitchState(switchNumber);
            if (label != null) label.text = $"#{switchNumber}  {colourName}\n{puzzle.GetDiagnostic(switchNumber)}\n{(on ? "ON  /  1" : "OFF  /  0")}";
            if (face != null) face.color = on ? new Color(.08f, .44f, .35f) : new Color(.13f, .18f, .25f);
        }
    }
}

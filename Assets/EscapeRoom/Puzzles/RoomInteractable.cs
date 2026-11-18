using UnityEngine;

namespace EscapeRoom
{
    /// <summary>
    /// A 3D object in one of the rooms that players can tap: a puzzle console or a clue poster.
    /// Needs a collider on itself or a child. Created by
    /// Escape Room > Puzzles 2 + 3 > Place puzzles + clues in the rooms.
    /// </summary>
    public class RoomInteractable : MonoBehaviour
    {
        public enum Kind { Console, Clue }

        public Kind kind = Kind.Clue;
        [Tooltip("1 = Player A (Main Lab, left half), 2 = Player B (Server Room, right half).")]
        [Range(1, 2)] public int role = 1;
        [Tooltip("Puzzle the clue belongs to (2 = Blackout Protocol, 3 = Ghost Administrator). Ignored for consoles.")]
        [Range(2, 3)] public int puzzle = 2;
        public string title;
        [TextArea(4, 16)] public string body;
        [Tooltip("Optional world-space text on the object (console screen / poster heading).")]
        public UnityEngine.UI.Text worldLabel;
    }
}

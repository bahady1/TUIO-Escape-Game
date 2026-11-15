using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;
using UnityEngine.Events;

namespace EscapeRoom
{
    /// <summary>
    /// Directed network and cipher rules from the LAB24 player and facilitator PDFs.
    /// Input ownership and physical marker identity are enforced by the table input adapter.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public class GhostAdministrator : MonoBehaviour
    {
        [Header("Puzzle Settings")]
        public bool activateOnStart;
        public float confirmationHoldSeconds = 2f;

        [Header("Puzzle Events")]
        public UnityEvent onPuzzleSolved = new UnityEvent();

        public const string Ciphertext = "STIR";
        public const int MaximumLatency = 10;
        private static readonly Dictionary<string, int> Edges = new Dictionary<string, int>
        {
            { "S>A", 4 }, { "S>B", 3 }, { "A>C", 3 }, { "A>D", 1 },
            { "B>D", 2 }, { "B>E", 2 }, { "C>V", 5 }, { "D>V", 1 }, { "E>V", 4 }
        };

        private readonly List<string> route = new List<string>();
        private ReadOnlyCollection<string> readOnlyRoute;
        private bool playerAConfirm;
        private bool playerBConfirm;
        private bool playerAMustRelease;
        private bool playerBMustRelease;
        private float confirmationTimer;

        public IReadOnlyList<string> Route => readOnlyRoute ?? (readOnlyRoute = route.AsReadOnly());
        public int TotalLatency { get; private set; }
        public int Shift { get; private set; }
        public bool IsActive { get; private set; }
        public bool Solved { get; private set; }
        public bool RouteValidated { get; private set; }
        public bool PasswordAccepted { get; private set; }
        public string Feedback { get; private set; } = "Waiting for activation.";
        public float ConfirmationProgress => Solved ? 1f : Mathf.Clamp01(confirmationTimer / RequiredHoldSeconds);
        private float RequiredHoldSeconds => Mathf.Max(2f, confirmationHoldSeconds);

        public event Action OnSolved;
        public event Action Changed;

        private void Awake()
        {
            if (activateOnStart) ActivatePuzzle();
        }

        public void ActivatePuzzle()
        {
            IsActive = true;
            ResetPuzzle();
        }

        public void DeactivatePuzzle()
        {
            IsActive = false;
            ClearConfirmation();
            Changed?.Invoke();
        }

        public static bool TryGetLinkLatency(string from, string to, out int latency)
        {
            return Edges.TryGetValue(from + ">" + to, out latency);
        }

        public bool TryAppendNode(string node)
        {
            if (!IsActive || Solved) return false;
            if (route.Count == 0)
            {
                if (node != "S") return Reject("A route must begin at S.");
            }
            else
            {
                string last = route[route.Count - 1];
                // A marker dwelling at a node is not a second traversal.
                if (node == last) return false;
                if (!TryGetLinkLatency(last, node, out int latency))
                    return Reject("No directed link to that node. Keep the current route or clear it.");
                TotalLatency += latency;
            }

            route.Add(node);
            RouteValidated = false;
            InvalidatePassword();
            Feedback = node == "V"
                ? "Route reaches the vault. Player B must validate it against the security bulletin."
                : "Packet received. Follow a directed link to the next node.";
            Changed?.Invoke();
            return true;
        }

        public void ClearRoute()
        {
            if (!IsActive || Solved) return;
            route.Clear();
            TotalLatency = 0;
            RouteValidated = false;
            InvalidatePassword();
            Feedback = "Route cleared. Begin a new packet at S.";
            Changed?.Invoke();
        }

        public bool ValidateRoute()
        {
            if (!IsActive || Solved) return false;
            RouteValidated = false;
            InvalidatePassword();
            if (route.Count < 2 || route[0] != "S" || route[route.Count - 1] != "V")
                return Reject("Route incomplete. Begin at S and reach V.");
            if (route.Contains("D")) return Reject("Route rejected: router D is compromised.");
            int total = 0;
            for (int i = 1; i < route.Count; i++)
            {
                if (!TryGetLinkLatency(route[i - 1], route[i], out int latency))
                    return Reject("Route rejected: every move must follow a directed link.");
                total += latency;
            }
            if (total > MaximumLatency) return Reject("Route rejected: latency exceeds the 10 ms limit.");
            TotalLatency = total;
            RouteValidated = true;
            Feedback = "Route authorised. Use its latency and the decryption protocol to set the decoder.";
            Changed?.Invoke();
            return true;
        }

        public void SetShift(int shift)
        {
            if (!IsActive || Solved || shift < 0 || shift > 4 || Shift == shift) return;
            Shift = shift;
            InvalidatePassword();
            Feedback = "Decoder changed. Enter the decrypted four-letter passphrase.";
            Changed?.Invoke();
        }

        public static string Decode(string encoded, int shift)
        {
            if (encoded == null) throw new ArgumentNullException(nameof(encoded));
            int rotation = ((shift % 26) + 26) % 26;
            char[] result = encoded.ToCharArray();
            for (int i = 0; i < result.Length; i++)
            {
                if (result[i] >= 'A' && result[i] <= 'Z')
                    result[i] = (char)('A' + (result[i] - 'A' - rotation + 26) % 26);
            }
            return new string(result);
        }

        public bool SubmitPassword(string enteredPassword)
        {
            if (!IsActive || Solved) return false;
            PasswordAccepted = RouteValidated && Shift == TotalLatency % 5
                && enteredPassword != null && enteredPassword.Length == 4
                && string.Equals(enteredPassword, Decode(Ciphertext, Shift), StringComparison.Ordinal);
            confirmationTimer = 0f;
            playerAMustRelease = playerAConfirm;
            playerBMustRelease = playerBConfirm;
            Feedback = PasswordAccepted
                ? "Passphrase accepted. Release any held pads; both operators must authorise for two seconds."
                : "Access denied. Check route authorisation, decoder shift and the four letters.";
            Changed?.Invoke();
            return PasswordAccepted;
        }

        public void SetPlayerConfirmation(int player, bool held)
        {
            if (player == 1)
            {
                playerAConfirm = held;
                if (!held) playerAMustRelease = false;
            }
            else if (player == 2)
            {
                playerBConfirm = held;
                if (!held) playerBMustRelease = false;
            }
            else return;
            if (!held) confirmationTimer = 0f;
        }

        private void Update() => TickConfirmation(Time.deltaTime);

        public void TickConfirmation(float deltaSeconds)
        {
            if (!IsActive || Solved) return;
            if (!PasswordAccepted || !playerAConfirm || !playerBConfirm || playerAMustRelease || playerBMustRelease)
            {
                confirmationTimer = 0f;
                return;
            }
            if (float.IsNaN(deltaSeconds) || float.IsInfinity(deltaSeconds) || deltaSeconds <= 0f) return;
            confirmationTimer += deltaSeconds;
            if (confirmationTimer < RequiredHoldSeconds) return;
            Solved = true;
            IsActive = false;
            Feedback = "Vault authorised. Ghost Administrator cleared.";
            Changed?.Invoke();
            OnSolved?.Invoke();
            onPuzzleSolved?.Invoke();
        }

        public void ResetPuzzle()
        {
            route.Clear();
            TotalLatency = 0;
            Shift = 0;
            RouteValidated = false;
            PasswordAccepted = false;
            Solved = false;
            ClearConfirmation();
            Feedback = "Player A routes the packet. Player B applies the security and decryption rules.";
            Changed?.Invoke();
        }

        private bool Reject(string message)
        {
            Feedback = message;
            Changed?.Invoke();
            return false;
        }

        private void InvalidatePassword()
        {
            PasswordAccepted = false;
            confirmationTimer = 0f;
        }

        private void OnDisable() => ClearConfirmation();

        private void ClearConfirmation()
        {
            playerAConfirm = false;
            playerBConfirm = false;
            playerAMustRelease = false;
            playerBMustRelease = false;
            confirmationTimer = 0f;
        }
    }
}

using System;
using UnityEngine;
using UnityEngine.Events;

namespace EscapeRoom
{
    [DefaultExecutionOrder(200)]
    public class BlackoutProtocol : MonoBehaviour
    {
        [Header("Puzzle Settings")]
        public bool activateOnStart = false;
        public float confirmationHoldSeconds = 2f;

        [Header("Puzzle Events")]
        public UnityEvent onPuzzleSolved = new UnityEvent();

        // Eight diagnostic binary signatures
        private readonly string[] diagnostics =
        {
            "1011", "0110", "1101", "0100",
            "1110", "1001", "0011", "1010"
        };

        // Eight switches: false = OFF, true = ON
        private readonly bool[] switches = new bool[8];

        private bool codeAccepted;
        private bool playerAConfirm;
        private bool playerBConfirm;
        private float confirmationTimer;
        private bool playerAMustRelease;
        private bool playerBMustRelease;

        public bool IsActive { get; private set; }
        public bool Solved { get; private set; }
        public bool CodeAccepted => codeAccepted;
        public float ConfirmationProgress => Solved ? 1f : Mathf.Clamp01(confirmationTimer / RequiredHoldSeconds);
        public string Feedback { get; private set; } = "Waiting for activation.";
        private float RequiredHoldSeconds => Mathf.Max(2f, confirmationHoldSeconds);

        public event Action OnSolved;
        public event Action Changed;

        private void Awake()
        {
            if (activateOnStart)
                ActivatePuzzle();
        }

        // Called when Puzzle 1 has been completed
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

        public string GetDiagnostic(int number) => number >= 1 && number <= 8 ? diagnostics[number - 1] : string.Empty;

        // Check whether a binary signature has exactly two 1s
        private bool IsStable(string binary)
        {
            int count = 0;

            foreach (char bit in binary)
            {
                if (bit == '1')
                    count++;
            }

            return count == 2;
        }

        // Change one switch, numbered from 1 to 8
        public void SetSwitch(int number, bool isOn)
        {
            if (!IsActive || Solved)
                return;

            if (number < 1 || number > 8)
                return;

            if (switches[number - 1] == isOn)
                return;

            switches[number - 1] = isOn;

            // Any switch change invalidates the previous code
            codeAccepted = false;
            confirmationTimer = 0f;
            Feedback = "Relay changed. Check the maintenance rule, then submit the checksum.";
            Changed?.Invoke();
        }

        public void ToggleSwitch(int number)
        {
            if (number < 1 || number > 8)
                return;

            SetSwitch(number, !switches[number - 1]);
        }

        public bool GetSwitchState(int number)
        {
            if (number < 1 || number > 8)
                return false;

            return switches[number - 1];
        }


        // Verify all eight switches
        public bool CheckSwitches()
        {
            for (int i = 0; i < 8; i++)
            {
                bool expected = IsStable(diagnostics[i]);

                if (switches[i] != expected)
                    return false;
            }

            return true;
        }

        // Convert four switch states into a decimal number
        private int ReadRow(int start)
        {
            int value = 0;

            for (int i = start; i < start + 4; i++)
            {
                value = value * 2 + (switches[i] ? 1 : 0);
            }

            return value;
        }

        // Generate the password using binary and XOR
        public string GenerateCode()
        {
            int top = ReadRow(0);
            int bottom = ReadRow(4);
            int xor = top ^ bottom;

            return $"{top}{bottom}{xor}";
        }

        // Check the player's submitted password
        public bool SubmitCode(string enteredCode)
        {
            if (!IsActive || Solved)
                return false;

            // Derive the accepted checksum from the diagnostic rule, not a second hard-coded answer.
            codeAccepted = CheckSwitches() && enteredCode != null && enteredCode.Length == 3 && enteredCode == GenerateCode();
            confirmationTimer = 0f;
            playerAMustRelease = playerAConfirm;
            playerBMustRelease = playerBConfirm;
            Feedback = codeAccepted
                ? "Checksum accepted. Release any held pads; both operators must confirm for two seconds."
                : "Reset denied. Review the relay pattern and checksum with your partner.";
            Changed?.Invoke();

            return codeAccepted;
        }

        // Future TUIO controller will update these states
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

        /// <summary>Advance an uninterrupted dual-role hold. Exposed for deterministic tests.</summary>
        public void TickConfirmation(float deltaSeconds)
        {
            if (!IsActive || Solved)
                return;

            if (!codeAccepted ||
                !playerAConfirm ||
                !playerBConfirm || playerAMustRelease || playerBMustRelease)
            {
                confirmationTimer = 0f;
                return;
            }

            if (float.IsNaN(deltaSeconds) || float.IsInfinity(deltaSeconds) || deltaSeconds <= 0f) return;
            confirmationTimer += deltaSeconds;

            if (confirmationTimer >= RequiredHoldSeconds)
            {
                CompletePuzzle();
            }
        }

        private void CompletePuzzle()
        {
            if (Solved)
                return;

            Solved = true;
            IsActive = false;

            Feedback = "Auxiliary power restored. Ghost Administrator is available.";
            Changed?.Invoke();
            OnSolved?.Invoke();
            onPuzzleSolved?.Invoke();
        }

        public void ResetPuzzle()
        {
            for (int i = 0; i < 8; i++)
                switches[i] = false;

            codeAccepted = false;
            ClearConfirmation();
            Solved = false;
            Feedback = "Set the stable relays and ask your partner to enter the checksum.";
            Changed?.Invoke();
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

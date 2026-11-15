using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace EscapeRoom
{
    [DefaultExecutionOrder(100)]
    public class CooperativePuzzleFlow : MonoBehaviour
    {
        public enum StartPuzzle { AfterPuzzle1, Blackout, Ghost }
        public StartPuzzle startPuzzle;
        public BlackoutProtocol blackout;
        public GhostAdministrator ghost;
        public CooperativePuzzleInput input;
        public SwitchPuzzle puzzle1;
        public Door exitDoor;
        public GameObject legacyBlackoutCanvas;
        public GameObject screen, blackoutPanel, ghostPanel, victoryPanel;
        public GameObject clueA2, clueB2, clueA3, clueB3;
        public Text status, entry, route, shift, modeLabel, holdLabel, hintLabel;
        public RectTransform padA, padB;
        public Image padAImage, padBImage, progress;
        public UnityEvent onAllPuzzlesSolved = new UnityEvent();
        public bool manageDemoSession;
        public int ActivePuzzle { get; private set; }
        public string Entry => typed;
        readonly List<Canvas> hiddenCanvases = new List<Canvas>();
        readonly List<WallViewController> lockedViews = new List<WallViewController>();
        PuzzleControl[] controls;
        string typed = "", lastPacket = "", lastRelay = "";
        float packetDwell, relayDwell, decoderAngle;
        int hintIndex;
        int stageStartedFrame = -1;
        bool legacyWasActive, suspended, previousPuzzle1Enabled, previousIntroEnabled, timerWasRunning;
        IntroDialogue intro;
        TangibleInput legacyInput;
        bool legacyInputWasActive;
        bool started;
        bool openedExitDoor, completedHud;

        void Start()
        {
            controls = GetComponentsInChildren<PuzzleControl>(true);
            input.Tapped += OnInputTap;
            input.TokenRotated += OnTokenRotated;
            blackout.OnSolved += OnBlackoutSolved;
            ghost.OnSolved += OnGhostSolved;
            if (puzzle1 != null) puzzle1.OnSolved += StartBlackout;
            if (legacyBlackoutCanvas != null) { legacyWasActive = legacyBlackoutCanvas.activeSelf; legacyBlackoutCanvas.SetActive(false); }
            started = true;
            if (startPuzzle == StartPuzzle.Blackout) StartBlackout();
            else if (startPuzzle == StartPuzzle.Ghost) StartGhost();
            else { screen.SetActive(false); if (puzzle1 != null && puzzle1.Solved) StartBlackout(); }
            ApplySessionMode();
        }

        void OnDestroy()
        {
            if (!started) return;
            if (input != null) { input.Tapped -= OnInputTap; input.TokenRotated -= OnTokenRotated; }
            if (blackout != null) blackout.OnSolved -= OnBlackoutSolved;
            if (ghost != null) ghost.OnSolved -= OnGhostSolved;
            if (puzzle1 != null) puzzle1.OnSolved -= StartBlackout;
            RestoreRoom();
            if (legacyBlackoutCanvas != null) legacyBlackoutCanvas.SetActive(legacyWasActive);
        }

        void SuspendRoom()
        {
            if (suspended) return;
            suspended = true;
            legacyInput = TangibleInput.Instance;
            if (legacyInput != null && !legacyInput.transform.IsChildOf(transform))
            {
                legacyInputWasActive = legacyInput.gameObject.activeSelf;
                legacyInput.gameObject.SetActive(false); // OnDisable detaches all legacy dispatcher listeners.
            }
            if (puzzle1 != null) { previousPuzzle1Enabled = puzzle1.enabled; puzzle1.enabled = false; }
            intro = FindAnyObjectByType<IntroDialogue>();
            if (intro != null) { previousIntroEnabled = intro.enabled; intro.enabled = false; intro.StopAllCoroutines(); }
            // Existing scripts' delegates can remain subscribed when disabled. Their visible
            // canvases and room navigation are suppressed while the cooperative stage owns input.
            foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (!canvas.transform.IsChildOf(transform) && canvas.enabled) { hiddenCanvases.Add(canvas); canvas.enabled = false; }
            foreach (var view in FindObjectsByType<WallViewController>(FindObjectsSortMode.None))
                if (!view.inputLocked) { view.inputLocked = true; lockedViews.Add(view); }
            if (GameHUD.Instance != null) { timerWasRunning = GameHUD.Instance.IsRunning; GameHUD.Instance.PauseTimer(); }
        }

        void RestoreRoom()
        {
            if (!suspended) return;
            foreach (var canvas in hiddenCanvases) if (canvas != null) canvas.enabled = true;
            foreach (var view in lockedViews) if (view != null) view.inputLocked = false;
            if (puzzle1 != null) puzzle1.enabled = previousPuzzle1Enabled;
            if (intro != null) intro.enabled = previousIntroEnabled;
            if (timerWasRunning && GameHUD.Instance != null) GameHUD.Instance.StartTimer();
            if (legacyInput != null && legacyInputWasActive) legacyInput.gameObject.SetActive(true);
            hiddenCanvases.Clear(); lockedViews.Clear(); suspended = false;
        }

        public void StartBlackout()
        {
            SuspendRoom(); ghost.DeactivatePuzzle(); blackout.ActivatePuzzle();
            ActivePuzzle = 2; BeginStage();
        }
        public void StartGhost()
        {
            SuspendRoom(); blackout.DeactivatePuzzle(); ghost.ActivatePuzzle();
            ActivePuzzle = 3; BeginStage();
        }
        void BeginStage()
        {
            stageStartedFrame = Time.frameCount;
            if (openedExitDoor && exitDoor != null) exitDoor.Close();
            openedExitDoor = false;
            if (completedHud && GameHUD.Instance != null) { GameHUD.Instance.ResetTimer(); GameHUD.Instance.PauseTimer(); }
            completedHud = false;
            typed = ""; hintIndex = 0; decoderAngle = 0; lastPacket = lastRelay = "";
            input.ClearLocalConfirmations();
            screen.SetActive(true); blackoutPanel.SetActive(ActivePuzzle == 2); ghostPanel.SetActive(ActivePuzzle == 3); victoryPanel.SetActive(false);
            clueA2.SetActive(false); clueB2.SetActive(false); clueA3.SetActive(false); clueB3.SetActive(false);
            hintLabel.text = "Talk to your partner. Reveal only your own role's evidence.";
            Refresh();
        }
        void OnBlackoutSolved()
        {
            if (PowerController.Instance != null) PowerController.Instance.RestorePower();
            StartGhost();
            hintLabel.text = "Auxiliary supply restored. Ghost Administrator is now online.";
        }
        void OnGhostSolved()
        {
            victoryPanel.SetActive(true); blackoutPanel.SetActive(false); ghostPanel.SetActive(false);
            if (exitDoor != null && !exitDoor.IsOpen) { exitDoor.Open(); openedExitDoor = true; }
            if (GameHUD.Instance != null && !GameHUD.Instance.IsFinished)
            {
                GameHUD.Instance.Win("VAULT AUTHORISED - YOU ESCAPED!"); completedHud = true;
            }
            input.ClearLocalConfirmations(); onAllPuzzlesSolved.Invoke();
        }

        void Update()
        {
            if (!started || ActivePuzzle == 0) return;
            bool a = input.IsRoleHeld(1, padA), b = input.IsRoleHeld(2, padB);
            if (ActivePuzzle == 2) { blackout.SetPlayerConfirmation(1, a); blackout.SetPlayerConfirmation(2, b); }
            else { ghost.SetPlayerConfirmation(1, a); ghost.SetPlayerConfirmation(2, b); }
            if (input.mode == PuzzleInputMode.Table && !victoryPanel.activeSelf) UpdateMarkers();
            padAImage.color = a ? new Color(.08f, .5f, .35f) : new Color(.15f, .22f, .3f);
            padBImage.color = b ? new Color(.08f, .5f, .35f) : new Color(.15f, .22f, .3f);
            Refresh();
        }

        void OnInputTap(Vector2 position)
        {
            // The same OS/TUIO press may finish Puzzle 1 before this adapter sees it.
            // Never let that transition press also operate the newly opened panel.
            if (Time.frameCount <= stageStartedFrame) return;
            HandleTap(position);
        }

        public void HandleTap(Vector2 position)
        {
            if (ActivePuzzle == 0 || controls == null) return;
            // Last-created controls are visually on top (e.g. private evidence overlays).
            for (int i = controls.Length - 1; i >= 0; --i)
                if (controls[i].Contains(position)) { Execute(controls[i].action, controls[i].value); return; }
        }

        public void Execute(string action, string value)
        {
            if (victoryPanel.activeSelf && action != "demo2" && action != "demo3" && action != "mode" && action != "reset") return;
            bool local = input.mode == PuzzleInputMode.LocalMouse;
            switch (action)
            {
                case "demo2": StartBlackout(); break;
                case "demo3": StartGhost(); break;
                case "mode": input.SetMode(local ? PuzzleInputMode.Table : PuzzleInputMode.LocalMouse); ApplySessionMode(); break;
                case "reset": if (ActivePuzzle == 2) StartBlackout(); else StartGhost(); break;
                case "switch": if (ActivePuzzle == 2 && !victoryPanel.activeSelf) blackout.ToggleSwitch(int.Parse(value)); break;
                case "node": if (ActivePuzzle == 3 && !victoryPanel.activeSelf) ghost.TryAppendNode(value); break;
                case "routeClear": ghost.ClearRoute(); typed = ""; input.ClearLocalConfirmations(); break;
                case "validate": ghost.ValidateRoute(); break;
                case "shift": ghost.SetShift(int.Parse(value)); break;
                case "key": if (typed.Length < (ActivePuzzle == 2 ? 3 : 4)) { typed += value; InvalidateEntry(); } break;
                case "back": if (typed.Length > 0) { typed = typed.Substring(0, typed.Length - 1); InvalidateEntry(); } break;
                case "clear": typed = ""; InvalidateEntry(); break;
                case "submit": if (ActivePuzzle == 2) blackout.SubmitCode(typed); else ghost.SubmitPassword(typed); input.ClearLocalConfirmations(); break;
                case "holdA": if (local) input.SetLocalConfirmation(1, !input.GetLocalConfirmation(1)); break;
                case "holdB": if (local) input.SetLocalConfirmation(2, !input.GetLocalConfirmation(2)); break;
                case "clueA": ToggleClue(ActivePuzzle == 2 ? clueA2 : clueA3); break;
                case "clueB": ToggleClue(ActivePuzzle == 2 ? clueB2 : clueB3); break;
                case "closeClues": clueA2.SetActive(false); clueB2.SetActive(false); clueA3.SetActive(false); clueB3.SetActive(false); break;
                case "hint": ShowHint(); break;
            }
            Refresh();
        }
        void InvalidateEntry()
        {
            // Editing a previously accepted entry must revoke its authorisation immediately.
            if (ActivePuzzle == 2 && blackout.CodeAccepted) blackout.SubmitCode("");
            if (ActivePuzzle == 3 && ghost.PasswordAccepted) ghost.SubmitPassword("");
            input.ClearLocalConfirmations();
        }
        void ToggleClue(GameObject target) { bool show = !target.activeSelf; clueA2.SetActive(false); clueB2.SetActive(false); clueA3.SetActive(false); clueB3.SetActive(false); target.SetActive(show); }
        void ApplySessionMode() { if (manageDemoSession && input.session != null) input.session.gameObject.SetActive(input.mode == PuzzleInputMode.Table); }
        void ShowHint()
        {
            string[] hints = ActivePuzzle == 2
                ? new[] { "Colours are not the rule. Discuss the four binary digits on each relay.", "Exactly two 1s is a counting instruction, not a decimal conversion.", "Write each row as four bits; XOR equal bits to 0 and different bits to 1." }
                : new[] { "Enumerate every directed route from S to V and add each link's cost.", "Reject routes using D, then compare remaining routes with the 10 ms limit.", "Use the route latency mod 5; move each letter of STIR backward in the alphabet." };
            hintLabel.text = hints[Mathf.Min(hintIndex++, hints.Length - 1)];
        }
        void Refresh()
        {
            if (entry == null || ActivePuzzle == 0) return;
            entry.text = (ActivePuzzle == 2 ? "CHECKSUM   " : "PASSPHRASE   ") + typed.PadRight(ActivePuzzle == 2 ? 3 : 4, '_');
            status.text = ActivePuzzle == 2 ? blackout.Feedback : ghost.Feedback;
            route.text = "PACKET   " + string.Join(" > ", ghost.Route) + "\nLATENCY   " + ghost.TotalLatency + " ms";
            shift.text = "DECODER SHIFT   " + ghost.Shift;
            bool local = input.mode == PuzzleInputMode.LocalMouse;
            modeLabel.text = local ? "LOCAL MOUSE DEMO  |  click pads to simulate two tokens" : "TABLE MODE  |  " + input.Status;
            holdLabel.text = local ? "SIMULATED COOPERATION: click A, then B. Click again to release." : "Both operators: place distinct bound tokens on the A / B pads for 2 seconds.";
            progress.fillAmount = ActivePuzzle == 2 ? blackout.ConfirmationProgress : ghost.ConfirmationProgress;
            progress.rectTransform.anchorMax = new Vector2(progress.fillAmount, 1f);
        }
        void UpdateMarkers()
        {
            if (clueA2.activeSelf || clueA3.activeSelf) { lastPacket = lastRelay = ""; packetDwell = relayDwell = 0; return; }
            UpdateMarker(input.packetTokenId, "node", ref lastPacket, ref packetDwell, .25f);
            UpdateMarker(input.relaySelectorTokenId, "switch", ref lastRelay, ref relayDwell, .45f);
        }
        void UpdateMarker(int id, string action, ref string last, ref float dwell, float threshold)
        {
            string current = "";
            if (id >= 0 && input.TryGetTokenScreenPosition(id, out var pos))
                foreach (var c in controls) if (c.action == action && c.Contains(pos)) { current = c.value; break; }
            if (current != last) { last = current; dwell = 0; }
            if (current.Length == 0 || dwell < 0) return;
            dwell += Time.unscaledDeltaTime;
            if (dwell >= threshold) { Execute(action, current); dwell = -1; }
        }
        void OnTokenRotated(int id, float delta)
        {
            if (ActivePuzzle != 3 || id != input.decoderTokenId || id < 0) return;
            decoderAngle += delta;
            while (Mathf.Abs(decoderAngle) >= 30f)
            {
                int step = decoderAngle > 0 ? 1 : -1;
                ghost.SetShift((ghost.Shift + step + 5) % 5); decoderAngle -= step * 30f;
            }
        }
    }
}

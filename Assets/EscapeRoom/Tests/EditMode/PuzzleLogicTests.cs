using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Events;
using Object = UnityEngine.Object;

namespace EscapeRoom.Tests
{
    // Reflection deliberately avoids moving teammates' existing Assembly-CSharp scripts
    // into an assembly definition just to make them visible to the test assembly.
    public class PuzzleLogicTests
    {
        private GameObject host;
        private Component puzzle;

        private static Type RuntimeType(string name)
        {
            var result = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("EscapeRoom." + name, false))
                .FirstOrDefault(type => type != null);
            Assert.That(result, Is.Not.Null, "Runtime type must compile and be loaded: " + name);
            return result;
        }

        private void Create(string name)
        {
            host = new GameObject("Puzzle logic test");
            puzzle = host.AddComponent(RuntimeType(name));
            Call("ActivatePuzzle");
        }

        private object Call(string method, params object[] args)
        {
            var info = puzzle.GetType().GetMethod(method, BindingFlags.Public | BindingFlags.Instance);
            Assert.That(info, Is.Not.Null, "Missing method " + method);
            return info.Invoke(puzzle, args);
        }

        private T Get<T>(string property) => (T)puzzle.GetType().GetProperty(property).GetValue(puzzle);
        private void Hold(int player, bool held) => Call("SetPlayerConfirmation", player, held);
        private void Tick(float delta) => Call("TickConfirmation", delta);
        private void BlackoutSolution()
        {
            foreach (int number in new[] { 2, 6, 7, 8 }) Call("SetSwitch", number, true);
        }

        private void Route(string path)
        {
            foreach (string node in path.Split('>')) Assert.That(Call("TryAppendNode", node), Is.True);
        }

        private void GhostSolution()
        {
            Route("S>B>E>V");
            Assert.That(Call("ValidateRoute"), Is.True);
            Call("SetShift", 4);
            Assert.That(Call("SubmitPassword", "OPEN"), Is.True);
        }

        [TearDown]
        public void TearDown()
        {
            if (host != null) Object.DestroyImmediate(host);
        }

        [Test]
        public void Blackout_All256MasksHaveExactlyOneStablePattern()
        {
            Create("BlackoutProtocol");
            int acceptedMasks = 0;
            for (int mask = 0; mask < 256; mask++)
            {
                Call("ResetPuzzle");
                for (int bit = 0; bit < 8; bit++) Call("SetSwitch", bit + 1, (mask & (1 << (7 - bit))) != 0);
                bool stable = (bool)Call("CheckSwitches");
                Assert.That(stable, Is.EqualTo(mask == 0x47), "Mask " + mask);
                Assert.That(Call("SubmitCode", "473"), Is.EqualTo(mask == 0x47));
                if (stable) acceptedMasks++;
            }
            Assert.That(acceptedMasks, Is.EqualTo(1));
        }

        [Test]
        public void Blackout_PdfDiagnosticsProduceTheXorChecksum()
        {
            Create("BlackoutProtocol");
            string[] expected = { "1011", "0110", "1101", "0100", "1110", "1001", "0011", "1010" };
            for (int i = 0; i < expected.Length; i++) Assert.That(Call("GetDiagnostic", i + 1), Is.EqualTo(expected[i]));
            BlackoutSolution();
            Assert.That(Call("GenerateCode"), Is.EqualTo("473"));
            Assert.That(Call("SubmitCode", "473"), Is.True);
            Assert.That(Get<bool>("Solved"), Is.False, "Code alone cannot solve the puzzle.");
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("0473")]
        [TestCase("473 ")]
        [TestCase("474")]
        public void Blackout_RejectsIncorrectOrMalformedCodes(string code)
        {
            Create("BlackoutProtocol");
            BlackoutSolution();
            Assert.That(Call("SubmitCode", code), Is.False);
            Hold(1, true); Hold(2, true); Tick(20f);
            Assert.That(Get<bool>("Solved"), Is.False);
        }

        [Test]
        public void Blackout_ChangingOneSwitchRevokesAcceptedCode()
        {
            Create("BlackoutProtocol");
            BlackoutSolution();
            Call("SubmitCode", "473"); Hold(1, true); Hold(2, true); Tick(1.5f);
            Call("ToggleSwitch", 1);
            Assert.That(Get<bool>("CodeAccepted"), Is.False);
            Assert.That(Get<float>("ConfirmationProgress"), Is.Zero);
            Call("ToggleSwitch", 1);
            Tick(5f);
            Assert.That(Get<bool>("Solved"), Is.False, "Returning to the correct pattern still needs a new submission.");
        }

        [Test]
        public void Blackout_IdempotentStateAndInvalidIndicesDoNotRevokeCode()
        {
            Create("BlackoutProtocol");
            BlackoutSolution(); Call("SubmitCode", "473");
            Call("SetSwitch", 2, true); Call("SetSwitch", 0, true); Call("ToggleSwitch", 9);
            Assert.That(Get<bool>("CodeAccepted"), Is.True);
            Assert.That(Call("GetSwitchState", 0), Is.False);
            Assert.That(Call("GetSwitchState", 9), Is.False);
        }

        [TestCase("BlackoutProtocol")]
        [TestCase("GhostAdministrator")]
        public void Cooperation_RequiresTwoRolesAndUninterruptedTwoSeconds(string name)
        {
            Create(name);
            if (name == "BlackoutProtocol") { BlackoutSolution(); Call("SubmitCode", "473"); }
            else GhostSolution();
            Hold(1, true); Tick(10f);
            Assert.That(Get<bool>("Solved"), Is.False);
            Assert.That(Get<float>("ConfirmationProgress"), Is.Zero);
            Hold(3, true); Tick(10f);
            Assert.That(Get<bool>("Solved"), Is.False, "Unknown roles cannot stand in for B.");
            Hold(2, true); Tick(1.5f);
            Assert.That(Get<float>("ConfirmationProgress"), Is.EqualTo(0.75f).Within(0.001f));
            Hold(1, false); Hold(1, true); Tick(1.5f);
            Assert.That(Get<bool>("Solved"), Is.False, "Lifting one token cancels all elapsed hold time.");
            Tick(0.5f);
            Assert.That(Get<bool>("Solved"), Is.True);
            Assert.That(Get<bool>("IsActive"), Is.False);
        }

        [TestCase("BlackoutProtocol")]
        [TestCase("GhostAdministrator")]
        public void Cooperation_PreheldPadsMustBeReleasedAfterSubmission(string name)
        {
            Create(name);
            Hold(1, true); Hold(2, true);
            if (name == "BlackoutProtocol") { BlackoutSolution(); Call("SubmitCode", "473"); }
            else GhostSolution();
            Tick(10f);
            Assert.That(Get<bool>("Solved"), Is.False);
            Hold(1, false); Hold(1, true); Tick(10f);
            Assert.That(Get<bool>("Solved"), Is.False, "Both preheld pads require release.");
            Hold(2, false); Hold(2, true); Tick(2f);
            Assert.That(Get<bool>("Solved"), Is.True);
        }

        [TestCase("BlackoutProtocol")]
        [TestCase("GhostAdministrator")]
        public void Cooperation_CompletionEventsFireExactlyOncePerAttempt(string name)
        {
            Create(name);
            int managedEvents = 0, unityEvents = 0;
            Action handler = () => managedEvents++;
            puzzle.GetType().GetEvent("OnSolved").AddEventHandler(puzzle, handler);
            var unityEvent = (UnityEvent)puzzle.GetType().GetField("onPuzzleSolved").GetValue(puzzle);
            unityEvent.AddListener(() => unityEvents++);
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                Call("ActivatePuzzle");
                if (name == "BlackoutProtocol") { BlackoutSolution(); Call("SubmitCode", "473"); }
                else GhostSolution();
                Hold(1, true); Hold(2, true); Tick(2f); Tick(10f); Tick(10f);
                Assert.That(managedEvents, Is.EqualTo(attempt));
                Assert.That(unityEvents, Is.EqualTo(attempt));
            }
        }

        [TestCase("BlackoutProtocol")]
        [TestCase("GhostAdministrator")]
        public void Cooperation_DeactivationAndInvalidTimeCannotComplete(string name)
        {
            Create(name);
            if (name == "BlackoutProtocol") { BlackoutSolution(); Call("SubmitCode", "473"); }
            else GhostSolution();
            Hold(1, true); Hold(2, true);
            Tick(float.NaN); Tick(float.PositiveInfinity); Tick(-1f);
            Assert.That(Get<float>("ConfirmationProgress"), Is.Zero);
            Call("DeactivatePuzzle"); Tick(10f);
            Assert.That(Get<bool>("Solved"), Is.False);
            Assert.That(Get<bool>("IsActive"), Is.False);
            Call("ActivatePuzzle");
            Assert.That(Get<bool>("Solved"), Is.False);
            Assert.That(Get<bool>("IsActive"), Is.True);
        }

        [TestCase("S>A>C>V", 12, false)]
        [TestCase("S>A>D>V", 6, false)]
        [TestCase("S>B>D>V", 6, false)]
        [TestCase("S>B>E>V", 9, true)]
        public void Ghost_AllFourPrintedRoutesMatchSecurityPolicy(string route, int latency, bool authorised)
        {
            Create("GhostAdministrator"); Route(route);
            Assert.That(Get<int>("TotalLatency"), Is.EqualTo(latency));
            Assert.That(Call("ValidateRoute"), Is.EqualTo(authorised));
        }

        [Test]
        public void Ghost_RejectsNonEdgesAndReverseTraversalWithoutCorruptingRoute()
        {
            Create("GhostAdministrator");
            Assert.That(Call("TryAppendNode", "B"), Is.False);
            Assert.That(Call("TryAppendNode", "S"), Is.True);
            Assert.That(Call("TryAppendNode", "E"), Is.False);
            Assert.That(Call("TryAppendNode", "A"), Is.True);
            Assert.That(Call("TryAppendNode", "S"), Is.False);
            Assert.That(Get<int>("TotalLatency"), Is.EqualTo(4));
            Assert.That(Call("ValidateRoute"), Is.False);
        }

        [Test]
        public void Ghost_RequiresExplicitPlayerBValidationAndCorrectShift()
        {
            Create("GhostAdministrator"); Route("S>B>E>V");
            Call("SetShift", 4);
            Assert.That(Call("SubmitPassword", "OPEN"), Is.False);
            Assert.That(Call("ValidateRoute"), Is.True);
            for (int shift = 0; shift < 4; shift++)
            {
                Call("SetShift", shift);
                Assert.That(Call("SubmitPassword", "OPEN"), Is.False);
            }
            Call("SetShift", 4);
            Assert.That(Call("SubmitPassword", "OPEN"), Is.True);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("open")]
        [TestCase("OPEN ")]
        [TestCase("STIR")]
        public void Ghost_RejectsWrongOrMalformedPassphrase(string password)
        {
            Create("GhostAdministrator"); GhostSolution();
            Assert.That(Call("SubmitPassword", password), Is.False);
            Hold(1, true); Hold(2, true); Tick(5f);
            Assert.That(Get<bool>("Solved"), Is.False);
        }

        [Test]
        public void Ghost_ChangingShiftOrRouteRevokesPassphrase()
        {
            Create("GhostAdministrator"); GhostSolution();
            Call("SetShift", 3);
            Assert.That(Get<bool>("PasswordAccepted"), Is.False);
            Call("SetShift", 4); Call("SubmitPassword", "OPEN");
            Call("ClearRoute");
            Assert.That(Get<bool>("PasswordAccepted"), Is.False);
            Assert.That(Get<bool>("RouteValidated"), Is.False);
            Assert.That(Get<int>("TotalLatency"), Is.Zero);
        }

        [Test]
        public void Ghost_ResetClearsAllPuzzleAndCooperationState()
        {
            Create("GhostAdministrator"); GhostSolution(); Hold(1, true); Hold(2, true); Tick(1.5f);
            Call("ResetPuzzle");
            Assert.That(Get<int>("Shift"), Is.Zero);
            Assert.That(Get<int>("TotalLatency"), Is.Zero);
            Assert.That(Get<bool>("PasswordAccepted"), Is.False);
            Assert.That(Get<bool>("RouteValidated"), Is.False);
            Assert.That(Get<float>("ConfirmationProgress"), Is.Zero);
            Assert.That(((IEnumerable)Get<object>("Route")).Cast<string>(), Is.Empty);
            Tick(10f);
            Assert.That(Get<bool>("Solved"), Is.False);
        }

        [TestCase("STIR", 4, "OPEN")]
        [TestCase("ABCD", 4, "WXYZ")]
        [TestCase("AZ", 0, "AZ")]
        public void Ghost_BackwardCipherWrapsAlphabet(string encoded, int shift, string expected)
        {
            var decode = RuntimeType("GhostAdministrator").GetMethod("Decode", BindingFlags.Public | BindingFlags.Static);
            Assert.That(decode.Invoke(null, new object[] { encoded, shift }), Is.EqualTo(expected));
        }

        [TestCase(-1, 2, false)]
        [TestCase(3, -1, false)]
        [TestCase(4, 4, false)]
        [TestCase(0, 1, true)]
        [TestCase(23, 91, true)]
        public void Table_ConfirmationBindingsRequireTwoDistinctObservedIds(int a, int b, bool valid)
        {
            var method = RuntimeType("CooperativePuzzleInput").GetMethod("AreRoleBindingsValid", BindingFlags.Public | BindingFlags.Static);
            Assert.That(method.Invoke(null, new object[] { a, b }), Is.EqualTo(valid));
        }

        [TestCase(1920f, 1080f)]
        [TestCase(2560f, 1440f)]
        [TestCase(3840f, 2160f)]
        public void Table_NormalisedCoordinatesAdaptToActualResolution(float width, float height)
        {
            var method = RuntimeType("CooperativePuzzleInput").GetMethod("NormalizedToScreen", BindingFlags.Public | BindingFlags.Static);
            var value = (Vector2)method.Invoke(null, new object[] { new Vector2(0.25f, 0.2f), new Vector2(width, height), false, true });
            Assert.That(value.x, Is.EqualTo(width * 0.25f).Within(0.01f));
            Assert.That(value.y, Is.EqualTo(height * 0.8f).Within(0.01f));
        }

        [Test]
        public void Table_ModeChangeCannotReuseLocalSimulatedConfirmation()
        {
            host = new GameObject("Input adapter test");
            puzzle = host.AddComponent(RuntimeType("CooperativePuzzleInput"));
            Call("SetLocalConfirmation", 1, true);
            Call("SetLocalConfirmation", 2, true);
            Assert.That(Call("GetLocalConfirmation", 1), Is.True);
            Assert.That(Call("GetLocalConfirmation", 2), Is.True);
            var modeType = RuntimeType("PuzzleInputMode");
            Call("SetMode", Enum.Parse(modeType, "Table"));
            Assert.That(Call("GetLocalConfirmation", 1), Is.False);
            Assert.That(Call("GetLocalConfirmation", 2), Is.False);
            Call("SetLocalConfirmation", 1, true);
            Assert.That(Call("GetLocalConfirmation", 1), Is.False, "Mouse simulation cannot authorise in Table mode.");
            Call("SetMode", Enum.Parse(modeType, "LocalMouse"));
            Assert.That(Call("GetLocalConfirmation", 1), Is.False, "Returning to local mode starts with released pads.");
        }

        [Test]
        public void Table_LocalRolesRemainIndependentAndRejectUnknownRoles()
        {
            host = new GameObject("Input adapter test");
            puzzle = host.AddComponent(RuntimeType("CooperativePuzzleInput"));
            Call("SetLocalConfirmation", 1, true);
            Call("SetLocalConfirmation", 0, true);
            Call("SetLocalConfirmation", 3, true);
            Assert.That(Call("GetLocalConfirmation", 1), Is.True);
            Assert.That(Call("GetLocalConfirmation", 2), Is.False);
            Assert.That(Call("GetLocalConfirmation", 0), Is.False);
            Assert.That(Call("GetLocalConfirmation", 3), Is.False);
            Call("ClearLocalConfirmations");
            Assert.That(Call("GetLocalConfirmation", 1), Is.False);
        }

        [Test]
        public void Table_DefaultBindingsCannotAcceptAConfirmation()
        {
            host = new GameObject("Input adapter test");
            puzzle = host.AddComponent(RuntimeType("CooperativePuzzleInput"));
            Call("SetMode", Enum.Parse(RuntimeType("PuzzleInputMode"), "Table"));
            Assert.That(Get<bool>("RoleBindingsValid"), Is.False);
            Assert.That(Call("IsRoleHeld", 1, null), Is.False);
            Assert.That(Call("IsRoleHeld", 2, null), Is.False);
        }
    }
}

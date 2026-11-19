using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace EscapeRoom.Tests
{
    public class MainSceneIntegrationTests
    {
        private const string MainScene = "Assets/EscapeRoom/Scenes/EscapeRoom.unity";
        private const string SetupKey = "EscapeRoom.Tests.OriginalSceneSetup";

        [Serializable]
        private class SavedScene { public string path; public bool loaded; public bool active; }
        [Serializable]
        private class SavedSetup { public SavedScene[] scenes; }

        private static Type TypeOf(string name)
            => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name, false)).First(t => t != null);
        private static Component Find(string name)
            => (Component)Object.FindAnyObjectByType(TypeOf(name), FindObjectsInactive.Include);
        private static T Field<T>(Component component, string name)
            => (T)component.GetType().GetField(name).GetValue(component);
        private static T Property<T>(Component component, string name)
            => (T)component.GetType().GetProperty(name).GetValue(component);
        private static object Call(Component component, string name, params object[] args)
            => component.GetType().GetMethod(name).Invoke(component, args);

        // EditMode's runner owns this enumerator even after EnterPlayMode. Poll
        // actual game time rather than relying on its handling of WaitForSeconds.
        private static IEnumerator WaitForGameSeconds(float seconds)
        {
            float deadline = Time.time + seconds;
            while (Time.time < deadline) yield return null;
        }

        [UnityTest]
        public IEnumerator ExistingPuzzleOneProgressesThroughBlackoutGhostVictoryAndReplay()
        {
            // Save the editor setup in SessionState because EnterPlayMode reloads domains.
            // No fixture modifications below are saved to EscapeRoom.unity.
            Assert.That(Enumerable.Range(0, SceneManager.sceneCount).All(i => !SceneManager.GetSceneAt(i).isDirty), Is.True,
                "Save the user's dirty scenes before running this integration test.");
            SessionState.SetString(SetupKey, JsonUtility.ToJson(new SavedSetup
            {
                scenes = EditorSceneManager.GetSceneManagerSetup().Select(s => new SavedScene
                { path = s.path, loaded = s.isLoaded, active = s.isActive }).ToArray()
            }));
            EditorSceneManager.OpenScene(MainScene, OpenSceneMode.Single);
            Component session = Find("TuioUnity.Common.TuioSessionBehaviour");
            Component legacy = Find("EscapeRoom.TangibleInput");
            Assert.That(session, Is.Not.Null);
            Assert.That(legacy, Is.Not.Null);
            session.gameObject.SetActive(false);
            legacy.gameObject.SetActive(false);
            // EnterPlayMode preserves the unsaved in-memory scene while avoiding the SDK's
            // network constructor in Awake. The source .unity file remains untouched.
            yield return new EnterPlayMode();
            yield return null;
            yield return null;

            var flow = Find("EscapeRoom.CooperativePuzzleFlow");
            var puzzle1 = Find("EscapeRoom.SwitchPuzzle");
            session = Find("TuioUnity.Common.TuioSessionBehaviour");
            legacy = Find("EscapeRoom.TangibleInput");
            Assert.That(Field<Component>(flow, "puzzle1"), Is.SameAs(puzzle1));
            Assert.That(Property<int>(flow, "ActivePuzzle"), Is.Zero);
            Assert.That(Field<GameObject>(flow, "screen").activeSelf, Is.False);
            Assert.That(Field<int[]>(puzzle1, "solution"), Is.EqualTo(new[] { 1, 0, 1, 0, 2, 0, 1, 2 }));
            Assert.That(Field<Array>(puzzle1, "worldKnobs").Length, Is.EqualTo(8));
            Assert.That(Field<Object>(puzzle1, "panelFace"), Is.Not.Null);
            Assert.That(Field<Object>(puzzle1, "panelSprite"), Is.Not.Null);
            Assert.That(Field<Object>(puzzle1, "room"), Is.Not.Null);
            Assert.That(Field<Component>(Field<Component>(flow, "input"), "session"), Is.SameAs(session));
            Assert.That(Field<Component>(legacy, "session"), Is.SameAs(session));
            Assert.That(Object.FindObjectsByType(TypeOf("EscapeRoom.CooperativePuzzleFlow"), FindObjectsInactive.Include, FindObjectsSortMode.None).Length, Is.EqualTo(1));

            // Bring the legacy input up without a session solely to verify the flow suppresses
            // it at transition. Restore its reference immediately; no SDK access occurs here.
            legacy.GetType().GetField("session").SetValue(legacy, null);
            LogAssert.Expect(LogType.Warning, "[EscapeRoom] No TuioSessionBehaviour in the scene - only keyboard/mouse simulation is active.");
            legacy.gameObject.SetActive(true);
            legacy.GetType().GetField("session").SetValue(legacy, session);
            Assert.That(legacy.gameObject.activeSelf, Is.True);
            var solution = Field<int[]>(puzzle1, "solution");
            for (int slot = 0; slot < solution.Length; slot++)
                for (int attempt = 0; attempt < 3 && (int)Call(puzzle1, "GetState", slot) != solution[slot]; attempt++)
                    Call(puzzle1, "Cycle", slot);
            yield return null;
            Assert.That(Property<bool>(puzzle1, "Solved"), Is.True);
            Assert.That(Property<int>(flow, "ActivePuzzle"), Is.EqualTo(2));
            // In-room mode (Escape Room > Puzzles 2 + 3 > Place puzzles + clues in the rooms):
            // the rooms stay playable and the puzzles run on the consoles next to the door.
            var stations = Find("EscapeRoom.RoomPuzzleStations");
            bool roomMode = stations != null && ((Behaviour)stations).isActiveAndEnabled;
            if (roomMode)
            {
                Assert.That(Field<GameObject>(flow, "screen").activeSelf, Is.False, "In-room mode must not cover the rooms.");
                Assert.That(legacy.gameObject.activeSelf, Is.True, "Room navigation stays available in in-room mode.");
                Assert.That(Object.FindObjectsByType(TypeOf("EscapeRoom.RoomInteractable"), FindObjectsSortMode.None).Length, Is.EqualTo(6),
                    "Two consoles and four clue posters are expected in the rooms.");
                Assert.That((bool)Call(stations, "OpenConsole", 1), Is.True);
                Assert.That((bool)Call(stations, "OpenConsole", 2), Is.True);

                // Ring zoom: rest the ring (id 7; id 4 is the mouse-following simulated ring) on Console A
                // for 1 s -> zoomed in. After CLOSE it must not reopen until the ring leaves the console.
                Call(stations, "Close", 1); Call(stations, "Close", 2);
                Call(Find("EscapeRoom.GameHUD"), "BeginGame");
                var labView = Field<Component>(stations, "labView");
                Call(labView, "ShowWall", 1);
                yield return WaitForGameSeconds(1.5f);
                var consoleA = Object.FindObjectsByType(TypeOf("EscapeRoom.RoomInteractable"), FindObjectsSortMode.None)
                    .Cast<Component>().First(c => c.name.StartsWith("Lab Console A"));
                var labCamera = Field<Camera>(labView, "viewCamera");
                Vector2 ringAt = labCamera.WorldToScreenPoint(consoleA.GetComponentInChildren<Collider>().bounds.center);
                Call(legacy, "SimulatePlace", 7, ringAt);
                yield return WaitForGameSeconds(1.3f);
                Assert.That((bool)Call(stations, "IsConsoleOpen", 1), Is.True, "Ring resting 1 s on Console A must open it.");
                Call(stations, "Close", 1);
                yield return WaitForGameSeconds(1.3f);
                Assert.That((bool)Call(stations, "IsConsoleOpen", 1), Is.False, "After CLOSE the resting ring must not reopen it.");
                Call(legacy, "SimulateRemove", 7);
            }
            else
            {
                Assert.That(Field<GameObject>(flow, "screen").activeSelf, Is.True);
                Assert.That(Field<GameObject>(flow, "blackoutPanel").activeSelf, Is.True);
                Assert.That(legacy.gameObject.activeSelf, Is.False, "Cooperative flow must deactivate the old input owner.");
            }
            Assert.That(session.gameObject.activeSelf, Is.False, "The test must never connect the laboratory session.");
            Assert.That(Field<GameObject>(flow, "legacyBlackoutCanvas").activeSelf, Is.False);
            Assert.That(Field<int[]>(puzzle1, "solution"), Is.EqualTo(new[] { 1, 0, 1, 0, 2, 0, 1, 2 }));
            Assert.That(Field<Component>(legacy, "session"), Is.SameAs(session));
            foreach (string number in new[] { "2", "6", "7", "8" }) Call(flow, "Execute", "switch", number);
            foreach (char digit in "473") Call(flow, "Execute", "key", digit.ToString());
            Call(flow, "Execute", "submit", "");
            Assert.That(Property<bool>(Field<Component>(flow, "blackout"), "CodeAccepted"), Is.True);
            Call(flow, "Execute", "holdA", ""); Call(flow, "Execute", "holdB", "");
            yield return WaitForGameSeconds(2.2f);
            Assert.That(Property<int>(flow, "ActivePuzzle"), Is.EqualTo(3),
                "Progress=" + Property<float>(Field<Component>(flow, "blackout"), "ConfirmationProgress") +
                "; A=" + Call(Field<Component>(flow, "input"), "GetLocalConfirmation", 1) +
                "; B=" + Call(Field<Component>(flow, "input"), "GetLocalConfirmation", 2));
            if (roomMode)
                Assert.That(Property<bool>(Field<Component>(stations, "door"), "IsOpen"), Is.True, "Solving Puzzle 2 opens the door between the rooms.");
            foreach (string node in new[] { "S", "B", "E", "V" }) Call(flow, "Execute", "node", node);
            Call(flow, "Execute", "validate", ""); Call(flow, "Execute", "shift", "4");
            foreach (char letter in "OPEN") Call(flow, "Execute", "key", letter.ToString());
            Call(flow, "Execute", "submit", "");
            Call(flow, "Execute", "holdA", ""); Call(flow, "Execute", "holdB", "");
            yield return WaitForGameSeconds(2.2f);
            Assert.That(Field<GameObject>(flow, "victoryPanel").activeSelf, Is.True);
            var door = Field<Component>(flow, "exitDoor");
            var hud = Find("EscapeRoom.GameHUD");
            Assert.That(Property<bool>(door, "IsOpen"), Is.True);
            Assert.That(Property<bool>(hud, "IsFinished"), Is.True);
            Call(flow, "Execute", "demo3", "");
            if (!roomMode) Assert.That(Property<bool>(door, "IsOpen"), Is.False, "Replay closes the door opened by this flow.");
            Assert.That(Property<bool>(hud, "IsFinished"), Is.False, "Replay clears this flow's old HUD victory.");
            Assert.That(Property<bool>(hud, "IsRunning"), Is.False, "Hard-mode demo remains untimed.");
            Assert.That(Field<int[]>(puzzle1, "solution"), Is.EqualTo(new[] { 1, 0, 1, 0, 2, 0, 1, 2 }));
            // Avoid RestoreRoom re-enabling the temporarily disconnected input on scene unload.
            legacy.GetType().GetField("session").SetValue(legacy, null);
            yield return new ExitPlayMode();
        }

        [UnityTearDown]
        public IEnumerator RestoreEditorSetup()
        {
            if (EditorApplication.isPlaying)
            {
                // Also run on assertion failure: RestoreRoom must not initialize
                // the disconnected laboratory session while unloading this fixture.
                var legacy = Find("EscapeRoom.TangibleInput");
                if (legacy != null) legacy.GetType().GetField("session").SetValue(legacy, null);
                yield return new ExitPlayMode();
            }
            string json = SessionState.GetString(SetupKey, "");
            if (string.IsNullOrEmpty(json)) yield break;
            // Discard the unsaved test fixture; never save over the user's main scene.
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var setup = JsonUtility.FromJson<SavedSetup>(json);
            if (setup != null && setup.scenes != null && setup.scenes.Length > 0)
                EditorSceneManager.RestoreSceneManagerSetup(setup.scenes.Select(s => new SceneSetup
                { path = s.path, isLoaded = s.loaded, isActive = s.active }).ToArray());
            SessionState.EraseString(SetupKey);
        }
    }
}

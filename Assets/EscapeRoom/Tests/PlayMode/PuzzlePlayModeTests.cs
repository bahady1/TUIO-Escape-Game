#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace EscapeRoom.Tests
{
    /// <summary>
    /// Loads the generated demo scene and exercises rendered control hit areas.
    /// Uses the same HandleTap route as the mouse/TUIO adapter, without bypassing
    /// entry controls or replacing the required two-second confirmation timer.
    /// </summary>
    public class PuzzlePlayModeTests
    {
        private const string DemoScene = "Assets/EscapeRoom/Scenes/BlackoutGhostDemo.unity";
        private Component flow;
        private Component blackout;
        private Component ghost;
        private Component input;
        private Scene scene;

        private static Type RuntimeType(string name)
        {
            var type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("EscapeRoom." + name, false))
                .FirstOrDefault(candidate => candidate != null);
            Assert.That(type, Is.Not.Null, "Missing compiled runtime type: " + name);
            return type;
        }

        private static object Call(Component target, string method, params object[] arguments)
        {
            return target.GetType().GetMethod(method, BindingFlags.Public | BindingFlags.Instance).Invoke(target, arguments);
        }

        private static T Property<T>(Component target, string name)
            => (T)target.GetType().GetProperty(name).GetValue(target);
        private static T Field<T>(Component target, string name)
            => (T)target.GetType().GetField(name).GetValue(target);
        private Component[] Controls()
            => flow.GetComponentsInChildren(RuntimeType("PuzzleControl"), true).Cast<Component>().ToArray();

        private Component Control(string action, string value = "")
        {
            var candidates = Controls().Where(control => control.gameObject.activeInHierarchy
                && Field<string>(control, "action") == action
                && (Field<string>(control, "value") ?? "") == value).ToArray();
            Assert.That(candidates.Length, Is.EqualTo(1), "Expected one visible control " + action + " " + value);
            return candidates[0];
        }

        private void Tap(string action, string value = "")
        {
            Canvas.ForceUpdateCanvases();
            var rect = (RectTransform)Control(action, value).transform;
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
            Assert.That(screen.x, Is.InRange(0f, (float)Screen.width), action + " must be on screen horizontally.");
            Assert.That(screen.y, Is.InRange(0f, (float)Screen.height), action + " must be on screen vertically.");
            Call(flow, "HandleTap", screen);
        }

        private void Enter(string value)
        {
            foreach (char character in value) Tap("key", character.ToString());
        }

        private void SelectBlackoutSolution()
        {
            foreach (int number in new[] { 2, 6, 7, 8 }) Tap("switch", number.ToString());
        }

        private void Route(string path)
        {
            foreach (string node in path.Split('>')) Tap("node", node);
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Assert.That(File.Exists(DemoScene), Is.True, "Run the cooperative puzzle installer before tests.");
            scene = EditorSceneManager.LoadSceneInPlayMode(DemoScene, new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            yield return null;
            flow = (Component)Object.FindAnyObjectByType(RuntimeType("CooperativePuzzleFlow"));
            Assert.That(flow, Is.Not.Null);
            blackout = Field<Component>(flow, "blackout");
            ghost = Field<Component>(flow, "ghost");
            input = Field<Component>(flow, "input");
            Assert.That(Property<int>(flow, "ActivePuzzle"), Is.EqualTo(2));
            Assert.That(Property<bool>(input, "IsTableMode"), Is.False);
            Canvas.ForceUpdateCanvases();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (!scene.IsValid() || !scene.isLoaded) yield break;
            var empty = SceneManager.CreateScene("Cooperative test cleanup");
            SceneManager.SetActiveScene(empty);
            yield return SceneManager.UnloadSceneAsync(scene);
            flow = blackout = ghost = input = null;
        }

        [UnityTest]
        public IEnumerator SceneReferencesAndSwitchVisualsAreComplete()
        {
            Assert.That(Object.FindObjectsByType(RuntimeType("CooperativePuzzleFlow"), FindObjectsInactive.Include, FindObjectsSortMode.None).Length, Is.EqualTo(1));
            Assert.That(Object.FindObjectsByType(RuntimeType("BlackoutProtocol"), FindObjectsInactive.Include, FindObjectsSortMode.None).Length, Is.EqualTo(1));
            Assert.That(Object.FindObjectsByType(RuntimeType("GhostAdministrator"), FindObjectsInactive.Include, FindObjectsSortMode.None).Length, Is.EqualTo(1));
            Assert.That(Object.FindObjectsByType(RuntimeType("SwitchPuzzle"), FindObjectsInactive.Include, FindObjectsSortMode.None).Length, Is.Zero,
                "Independent demo must not require or duplicate Puzzle 1.");
            foreach (string field in new[] { "screen", "blackoutPanel", "ghostPanel", "victoryPanel", "padA", "padB", "status", "entry", "progress", "clueA2", "clueB2", "clueA3", "clueB3" })
                Assert.That(Field<Object>(flow, field), Is.Not.Null, "Missing scene reference: " + field);
            var visuals = flow.GetComponentsInChildren(RuntimeType("SwitchButtonVisual"), true).Cast<Component>().ToArray();
            Assert.That(visuals.Length, Is.EqualTo(8));
            var switch2 = visuals.Single(visual => Field<int>(visual, "switchNumber") == 2);
            var label = Field<Component>(switch2, "label");
            Assert.That(Property<string>(label, "text"), Does.Contain("OFF  /  0"));
            Tap("switch", "2");
            Assert.That(Call(blackout, "GetSwitchState", 2), Is.True, "One tap must toggle exactly once.");
            Assert.That(Property<string>(label, "text"), Does.Contain("ON  /  1"));
            Tap("switch", "2");
            Assert.That(Call(blackout, "GetSwitchState", 2), Is.False);
            yield return null;
        }

        [UnityTest]
        public IEnumerator BlackoutUiRequiresCorrectEntryAndFreshUninterruptedDualHold()
        {
            SelectBlackoutSolution();
            Enter("474"); Tap("submit");
            Assert.That(Property<bool>(blackout, "CodeAccepted"), Is.False);
            Tap("back"); Enter("3"); Tap("submit");
            Assert.That(Property<string>(flow, "Entry"), Is.EqualTo("473"));
            Assert.That(Property<bool>(blackout, "CodeAccepted"), Is.True);
            Tap("holdA");
            yield return new WaitForSeconds(2.15f);
            Assert.That(Property<int>(flow, "ActivePuzzle"), Is.EqualTo(2), "One role cannot complete the puzzle.");
            Tap("holdB");
            yield return new WaitForSeconds(.6f);
            Assert.That(Property<float>(blackout, "ConfirmationProgress"), Is.GreaterThan(0f));
            Tap("holdA");
            yield return null;
            Assert.That(Property<float>(blackout, "ConfirmationProgress"), Is.Zero, "Lifting a role must clear progress in the same update.");
            Tap("holdA");
            yield return new WaitForSeconds(1.6f);
            Assert.That(Property<int>(flow, "ActivePuzzle"), Is.EqualTo(2));
            yield return new WaitForSeconds(.6f);
            Assert.That(Property<bool>(blackout, "Solved"), Is.True);
            Assert.That(Property<int>(flow, "ActivePuzzle"), Is.EqualTo(3));
            Assert.That(Field<GameObject>(flow, "blackoutPanel").activeSelf, Is.False);
            Assert.That(Field<GameObject>(flow, "ghostPanel").activeSelf, Is.True);
            Assert.That(Property<string>(flow, "Entry"), Is.Empty, "Transition must clear the previous checksum.");
            Assert.That(Call(input, "GetLocalConfirmation", 1), Is.False);
            Assert.That(Call(input, "GetLocalConfirmation", 2), Is.False);
        }

        [UnityTest]
        public IEnumerator GhostUiRejectsUnsafeRoutesThenOpensVaultOnce()
        {
            Tap("demo3");
            Route("S>B>D>V"); Tap("validate");
            Assert.That(Property<bool>(ghost, "RouteValidated"), Is.False);
            Assert.That(Property<string>(ghost, "Feedback"), Does.Contain("compromised"));
            Tap("routeClear"); Route("S>A>C>V"); Tap("validate");
            Assert.That(Property<bool>(ghost, "RouteValidated"), Is.False);
            Assert.That(Property<int>(ghost, "TotalLatency"), Is.EqualTo(12));
            Tap("routeClear"); Route("S>B>E>V"); Tap("validate");
            Assert.That(Property<bool>(ghost, "RouteValidated"), Is.True);
            Assert.That(Property<int>(ghost, "TotalLatency"), Is.EqualTo(9));
            Tap("shift", "3"); Enter("OPEN"); Tap("submit");
            Assert.That(Property<bool>(ghost, "PasswordAccepted"), Is.False, "Correct password cannot bypass a wrong decoder shift.");
            Tap("shift", "4"); Tap("submit");
            Assert.That(Property<bool>(ghost, "PasswordAccepted"), Is.True);
            int completions = 0;
            Field<UnityEvent>(flow, "onAllPuzzlesSolved").AddListener(() => completions++);
            Tap("holdA"); Tap("holdB");
            yield return new WaitForSeconds(1.5f);
            Assert.That(Property<bool>(ghost, "Solved"), Is.False);
            yield return new WaitForSeconds(.7f);
            Assert.That(Property<bool>(ghost, "Solved"), Is.True);
            Assert.That(Field<GameObject>(flow, "victoryPanel").activeSelf, Is.True);
            Assert.That(Field<GameObject>(flow, "ghostPanel").activeSelf, Is.False);
            yield return new WaitForSeconds(.2f);
            Assert.That(completions, Is.EqualTo(1));
            Tap("demo3");
            Assert.That(Field<GameObject>(flow, "victoryPanel").activeSelf, Is.False);
            Assert.That(Property<bool>(ghost, "Solved"), Is.False);
            Assert.That(Property<int>(ghost, "TotalLatency"), Is.Zero);
            Assert.That(Property<int>(ghost, "Shift"), Is.Zero);
            Assert.That(Property<string>(flow, "Entry"), Is.Empty);
        }

        [UnityTest]
        public IEnumerator EditingAcceptedEntryAndResetRevokeAuthorisation()
        {
            SelectBlackoutSolution(); Enter("473"); Tap("submit"); Tap("holdA");
            yield return null;
            Tap("back");
            Assert.That(Property<bool>(blackout, "CodeAccepted"), Is.False);
            Assert.That(Call(input, "GetLocalConfirmation", 1), Is.False);
            Tap("reset");
            for (int number = 1; number <= 8; number++) Assert.That(Call(blackout, "GetSwitchState", number), Is.False);
            Assert.That(Property<string>(flow, "Entry"), Is.Empty);
            Assert.That(Property<float>(blackout, "ConfirmationProgress"), Is.Zero);
            Tap("demo3"); Route("S>B>E>V"); Tap("validate"); Tap("shift", "4"); Enter("OPEN"); Tap("submit");
            Tap("clear");
            Assert.That(Property<bool>(ghost, "PasswordAccepted"), Is.False);
            Tap("reset");
            Assert.That(Property<int>(ghost, "TotalLatency"), Is.Zero);
            Assert.That(Property<bool>(ghost, "RouteValidated"), Is.False);
            yield return null;
        }

        [UnityTest]
        public IEnumerator EvidenceOverlayBlocksCoveredControlsAndOnlyOneRoleSheetIsOpen()
        {
            Tap("clueA");
            Assert.That(Field<GameObject>(flow, "clueA2").activeSelf, Is.True);
            var switch2 = Control("switch", "2");
            var rect = (RectTransform)switch2.transform;
            Call(flow, "HandleTap", RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center)));
            Assert.That(Call(blackout, "GetSwitchState", 2), Is.False, "A clue overlay must consume taps above the relay panel.");
            Assert.That(Field<GameObject>(flow, "clueA2").activeSelf, Is.False);
            Tap("clueA"); Tap("clueB");
            Assert.That(Field<GameObject>(flow, "clueA2").activeSelf, Is.False);
            Assert.That(Field<GameObject>(flow, "clueB2").activeSelf, Is.True);
            yield return null;
        }

        [UnityTest]
        public IEnumerator TransitionPressCannotAlsoToggleTheNewPanel()
        {
            Call(flow, "StartBlackout");
            Canvas.ForceUpdateCanvases();
            var rect = (RectTransform)Control("switch", "2").transform;
            Vector2 point = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
            var listeners = (Action<Vector2>)input.GetType().GetField("Tapped", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(input);
            Assert.That(listeners, Is.Not.Null);
            listeners(point);
            Assert.That(Call(blackout, "GetSwitchState", 2), Is.False, "The opening press is consumed at the transition boundary.");
            yield return null;
            listeners(point);
            Assert.That(Call(blackout, "GetSwitchState", 2), Is.True, "A later physical/mouse press still works.");
        }

        [UnityTest]
        public IEnumerator CaptureDemonstrationScreensWhenGraphicsAreAvailable()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("Screenshot capture requires a graphics device; functional UI tests still run with -nographics.");
            string directory = Path.Combine(Application.dataPath, "..", "Validation", "screenshots");
            Directory.CreateDirectory(directory);
            yield return Capture(Path.Combine(directory, "blackout.png"));
            Tap("clueB");
            yield return Capture(Path.Combine(directory, "blackout-clue-b.png"));
            Tap("demo3");
            yield return Capture(Path.Combine(directory, "ghost.png"));
            Tap("clueB");
            yield return Capture(Path.Combine(directory, "ghost-clue-b.png"));
            Call(flow, "Execute", "closeClues", "");
            Route("S>B>E>V"); Tap("validate"); Tap("shift", "4"); Enter("OPEN"); Tap("submit"); Tap("holdA"); Tap("holdB");
            yield return new WaitForSeconds(2.2f);
            Assert.That(Field<GameObject>(flow, "victoryPanel").activeSelf, Is.True);
            yield return Capture(Path.Combine(directory, "victory.png"));
        }

        private static IEnumerator Capture(string path)
        {
            // Batch mode does not necessarily present a Game-view frame, so ScreenCapture
            // can remain queued indefinitely. Render this same UI through its demo camera.
            // URP's installed Render Requests documentation specifies that SubmitRenderRequest
            // completes synchronously, making ReadPixels deterministic without EndOfFrame.
            const int width = 1920, height = 1080;
            var camera = Object.FindAnyObjectByType<Camera>();
            Assert.That(camera, Is.Not.Null, "The demo requires a capture camera.");
            var canvases = Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            var modes = canvases.Select(canvas => canvas.renderMode).ToArray();
            var cameras = canvases.Select(canvas => canvas.worldCamera).ToArray();
            var distances = canvases.Select(canvas => canvas.planeDistance).ToArray();
            var oldTarget = camera.targetTexture;
            var oldRect = camera.rect;
            float oldAspect = camera.aspect;
            var oldActive = RenderTexture.active;
            var destination = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var pixels = new Texture2D(width, height, TextureFormat.RGB24, false, false);
            try
            {
                destination.Create();
                camera.targetTexture = destination;
                camera.rect = new Rect(0f, 0f, 1f, 1f);
                camera.aspect = (float)width / height;
                foreach (var canvas in canvases)
                {
                    canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    canvas.worldCamera = camera;
                    canvas.planeDistance = Mathf.Max(1f, camera.nearClipPlane + .1f);
                }
                yield return null;
                Canvas.ForceUpdateCanvases();
                if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null)
                {
                    var request = new UnityEngine.Rendering.RenderPipeline.StandardRequest
                    {
                        destination = destination,
                        mipLevel = 0,
                        slice = 0,
                        face = CubemapFace.Unknown
                    };
                    Assert.That(UnityEngine.Rendering.RenderPipeline.SupportsRenderRequest(camera, request), Is.True,
                        "The installed render pipeline must support its documented StandardRequest.");
                    UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera, request);
                }
                else camera.Render();
                RenderTexture.active = destination;
                pixels.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, false);
                pixels.Apply(false, false);
                byte[] png = pixels.EncodeToPNG();
                Assert.That(png.Length, Is.GreaterThan(10000), "Capture must contain a rendered UI, not an empty file.");
                File.WriteAllBytes(path, png);
            }
            finally
            {
                RenderTexture.active = oldActive;
                camera.targetTexture = oldTarget;
                camera.rect = oldRect;
                camera.aspect = oldAspect;
                for (int i = 0; i < canvases.Length; i++)
                {
                    if (canvases[i] == null) continue;
                    canvases[i].renderMode = modes[i];
                    canvases[i].worldCamera = cameras[i];
                    canvases[i].planeDistance = distances[i];
                }
                Canvas.ForceUpdateCanvases();
                destination.Release();
                Object.Destroy(destination);
                Object.Destroy(pixels);
            }
        }
    }
}
#endif

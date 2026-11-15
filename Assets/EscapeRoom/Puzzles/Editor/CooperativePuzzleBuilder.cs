using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TuioUnity.Common;

namespace EscapeRoom.Editor
{
    public static class CooperativePuzzleBuilder
    {
        public const string MainScene = "Assets/EscapeRoom/Scenes/EscapeRoom.unity";
        public const string DemoScene = "Assets/EscapeRoom/Scenes/BlackoutGhostDemo.unity";
        public const string PrefabPath = "Assets/EscapeRoom/Puzzles/Prefabs/CooperativePuzzles.prefab";
        static readonly Color Ink = new Color(.035f, .055f, .09f);
        static readonly Color Panel = new Color(.065f, .095f, .15f);
        static readonly Color Accent = new Color(.22f, .85f, .78f);
        static Font Font => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        [MenuItem("Escape Room/Puzzles 2 + 3/Install or validate (preserves existing objects)")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Directory.CreateDirectory("Validation");
            Directory.CreateDirectory("Assets/EscapeRoom/Puzzles/Prefabs");
            var main = EditorSceneManager.OpenScene(MainScene, OpenSceneMode.Single);
            var existing = UnityEngine.Object.FindAnyObjectByType<CooperativePuzzleFlow>();
            if (existing == null)
            {
                Checkpoint(MainScene);
                var oldBlackout = UnityEngine.Object.FindAnyObjectByType<BlackoutProtocol>();
                var sourceSession = UnityEngine.Object.FindAnyObjectByType<TuioSessionBehaviour>();
                if (oldBlackout == null || sourceSession == null) throw new InvalidOperationException("Expected existing Blackout and TUIO session are missing.");
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                if (prefab == null)
                {
                    var template = BuildUI();
                    prefab = PrefabUtility.SaveAsPrefabAsset(template, PrefabPath);
                    UnityEngine.Object.DestroyImmediate(template);
                }
                var root = (GameObject)PrefabUtility.InstantiatePrefab(prefab, main);
                var flow = root.GetComponent<CooperativePuzzleFlow>();
                // Keep the manually created controller, with its original GUID and events.
                var templateBlackout = flow.blackout;
                flow.blackout = oldBlackout;
                foreach (var visual in root.GetComponentsInChildren<SwitchButtonVisual>(true)) visual.puzzle = oldBlackout;
                UnityEngine.Object.DestroyImmediate(templateBlackout);
                flow.input.session = sourceSession;
                flow.puzzle1 = UnityEngine.Object.FindAnyObjectByType<SwitchPuzzle>();
                flow.exitDoor = UnityEngine.Object.FindAnyObjectByType<Door>();
                flow.legacyBlackoutCanvas = GameObject.Find("Puzzle2Canvas");
                flow.startPuzzle = CooperativePuzzleFlow.StartPuzzle.AfterPuzzle1;
                PrefabUtility.RecordPrefabInstancePropertyModifications(flow);
                PrefabUtility.RecordPrefabInstancePropertyModifications(flow.input);
                foreach (var visual in root.GetComponentsInChildren<SwitchButtonVisual>(true)) PrefabUtility.RecordPrefabInstancePropertyModifications(visual);
                EditorSceneManager.MarkSceneDirty(main);
                if (!EditorSceneManager.SaveScene(main)) throw new IOException("Could not save EscapeRoom scene.");
            }
            if (!File.Exists(DemoScene))
            {
                var session = UnityEngine.Object.FindAnyObjectByType<TuioSessionBehaviour>();
                var demo = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                SceneManager.SetActiveScene(demo);
                var root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath), demo);
                var flow = root.GetComponent<CooperativePuzzleFlow>();
                var clone = UnityEngine.Object.Instantiate(session.gameObject);
                clone.name = "TUIO Session - copied existing configuration (Table mode only)";
                SceneManager.MoveGameObjectToScene(clone, demo);
                flow.input.session = clone.GetComponent<TuioSessionBehaviour>();
                clone.SetActive(false);
                flow.startPuzzle = CooperativePuzzleFlow.StartPuzzle.Blackout;
                flow.manageDemoSession = true;
                var camera = new GameObject("Demo Camera", typeof(Camera));
                camera.GetComponent<Camera>().clearFlags = CameraClearFlags.SolidColor;
                camera.GetComponent<Camera>().backgroundColor = Ink;
                PrefabUtility.RecordPrefabInstancePropertyModifications(flow);
                PrefabUtility.RecordPrefabInstancePropertyModifications(flow.input);
                EditorSceneManager.SaveScene(demo, DemoScene);
                EditorSceneManager.CloseScene(demo, true);
            }
            AssetDatabase.SaveAssets();
            ValidateScene();
            File.WriteAllText("Validation/installation.txt", "Installed and validated with Unity " + Application.unityVersion + "\n" + DateTime.UtcNow.ToString("O"));
        }

        [MenuItem("Escape Room/Puzzles 2 + 3/Open standalone demo")]
        public static void OpenDemo()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) EditorSceneManager.OpenScene(DemoScene);
        }

        public static void ValidateScene()
        {
            var flows = UnityEngine.Object.FindObjectsByType<CooperativePuzzleFlow>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (flows.Length != 1) throw new InvalidOperationException("Expected exactly one cooperative flow.");
            var flow = flows[0];
            if (flow.blackout == null || flow.ghost == null || flow.input == null || flow.input.session == null || flow.padA == null || flow.padB == null)
                throw new InvalidOperationException("Missing required puzzle reference.");
            if (flow.GetComponentsInChildren<SwitchButtonVisual>(true).Length != 8) throw new InvalidOperationException("Expected eight switch visuals.");
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                    if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject) > 0)
                        throw new InvalidOperationException("Missing script on " + transform.name);
            Debug.Log("Cooperative scene references validated.");
        }

        static void Checkpoint(string path)
        {
            string dir = "Backups/editor-install-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
            Directory.CreateDirectory(dir); File.Copy(path, Path.Combine(dir, Path.GetFileName(path)));
        }

        static GameObject BuildUI()
        {
            var root = new GameObject("Cooperative Puzzles 2 + 3");
            var flow = root.AddComponent<CooperativePuzzleFlow>();
            flow.blackout = root.AddComponent<BlackoutProtocol>();
            flow.ghost = root.AddComponent<GhostAdministrator>();
            flow.input = root.AddComponent<CooperativePuzzleInput>();
            var canvasRt = Rect("Cooperative Canvas", root.transform, 0, 0, 1, 1);
            var canvas = canvasRt.gameObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 400;
            var scaler = canvasRt.gameObject.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
            flow.screen = canvasRt.gameObject; Background(canvasRt, Ink);
            Label(canvasRt, "LAB 24   /   AUXILIARY SYSTEMS", .025f, .946f, .56f, .985f, 31, Accent, TextAnchor.MiddleLeft);
            Control(canvasRt, "DEMO 2", "demo2", "", .59f, .947f, .68f, .986f);
            Control(canvasRt, "DEMO 3", "demo3", "", .69f, .947f, .78f, .986f);
            Control(canvasRt, "INPUT MODE", "mode", "", .79f, .947f, .89f, .986f);
            Control(canvasRt, "RESET", "reset", "", .90f, .947f, .975f, .986f);
            flow.modeLabel = Label(canvasRt, "", .025f, .90f, .975f, .939f, 21, new Color(.65f,.75f,.85f), TextAnchor.MiddleLeft);
            var p2 = Rect("Blackout Protocol", canvasRt, .02f, .265f, .98f, .89f); flow.blackoutPanel = p2.gameObject;
            var p3 = Rect("Ghost Administrator", canvasRt, .02f, .265f, .98f, .89f); flow.ghostPanel = p3.gameObject;
            BuildBlackout(p2, flow); BuildGhost(p3, flow);
            // Shared response belongs to B; input is always manual, never auto-filled with solutions.
            flow.entry = Label(canvasRt, "", .525f, .215f, .78f, .258f, 30, Accent, TextAnchor.MiddleLeft);
            Control(canvasRt, "BACK", "back", "", .79f, .216f, .85f, .256f);
            Control(canvasRt, "CLEAR", "clear", "", .855f, .216f, .915f, .256f);
            Control(canvasRt, "ENTER", "submit", "", .92f, .216f, .98f, .256f);
            flow.status = Label(canvasRt, "", .025f, .165f, .975f, .205f, 25, Color.white, TextAnchor.MiddleLeft);
            flow.hintLabel = Label(canvasRt, "", .025f, .115f, .89f, .155f, 22, new Color(.68f,.77f,.86f), TextAnchor.MiddleLeft);
            Control(canvasRt, "HINT", "hint", "", .9f, .115f, .975f, .153f);
            var a = Control(canvasRt, "A  /  AUTHORISE", "holdA", "", .025f, .025f, .235f, .098f);
            var b = Control(canvasRt, "B  /  AUTHORISE", "holdB", "", .765f, .025f, .975f, .098f);
            flow.padA = a.Rect; flow.padB = b.Rect; flow.padAImage = a.GetComponent<Image>(); flow.padBImage = b.GetComponent<Image>();
            flow.holdLabel = Label(canvasRt, "", .25f, .052f, .75f, .1f, 20, Color.white);
            var bar = Rect("Hold progress", canvasRt, .25f, .028f, .75f, .042f); Background(bar, Panel);
            var fill = Rect("Fill", bar, 0, 0, 1, 1); flow.progress = Background(fill, Accent); flow.progress.type = Image.Type.Filled; flow.progress.fillMethod = Image.FillMethod.Horizontal; flow.progress.fillAmount = 0;
            var win = Rect("Vault open", canvasRt, .02f, .215f, .98f, .89f); Background(win, Panel);
            Label(win, "VAULT AUTHORISED\n\nTwo operators. One recovered network.\nLab 24 is open.\n\nUse DEMO 2 or DEMO 3 above to replay.", .1f, .1f, .9f, .9f, 48, Accent);
            flow.victoryPanel = win.gameObject;
            p3.gameObject.SetActive(false); win.gameObject.SetActive(false);
            return root;
        }

        static void BuildBlackout(RectTransform panel, CooperativePuzzleFlow flow)
        {
            var a = Role(panel, "02  /  BLACKOUT PROTOCOL", "PLAYER A  /  RELAY CABINET", 0, .49f);
            var b = Role(panel, "TORN SERVICE MANUAL", "PLAYER B  /  CHECKSUM LOCK", .51f, 1);
            Control(a, "A: REVEAL EVIDENCE", "clueA", "", .04f, .77f, .96f, .85f);
            Label(a, "Set eight relays. Ask B which signatures are stable.", .04f, .66f, .96f, .75f, 23, Color.white);
            string[] colours = { "BLUE", "BLACK", "RED", "YELLOW", "GREEN", "YELLOW", "BLACK", "BLUE" };
            for (int i = 0; i < 8; ++i)
            {
                float x = .04f + (i % 4) * .235f, y = i < 4 ? .35f : .08f;
                var control = Control(a, "", "switch", (i + 1).ToString(), x, y, x + .215f, y + .24f);
                control.role = 1;
                var visual = control.gameObject.AddComponent<SwitchButtonVisual>(); visual.puzzle = flow.blackout; visual.switchNumber = i + 1;
                visual.label = control.GetComponentInChildren<Text>(); visual.face = control.GetComponent<Image>(); visual.colourName = colours[i]; visual.Refresh();
            }
            Control(b, "B: REVEAL EVIDENCE", "clueB", "", .04f, .77f, .96f, .85f);
            Label(b, "Calculate the three-digit checksum from A's relay states.", .04f, .66f, .96f, .75f, 23, Color.white);
            for (int i = 1; i <= 9; i++) { int n = i - 1; float x = .08f + n % 3 * .29f, y = .44f - n / 3 * .15f; Control(b, i.ToString(), "key", i.ToString(), x, y, x + .26f, y + .125f); }
            Control(b, "0", "key", "0", .37f, .01f, .63f, .125f);
            flow.clueA2 = Clue(a, "A / INCIDENT LOG A-17", "Colours were repainted during maintenance.\n\nRelay diagnostics (top row, then bottom):\n1: 1011     2: 0110     3: 1101     4: 0100\n5: 1110     6: 1001     7: 0011     8: 1010\n\nYou control the relays. Describe these signatures to B; ask for the maintenance rule.");
            flow.clueB2 = Clue(b, "B / MAINTENANCE REVISION 04", "A stable relay contains EXACTLY TWO 1s. Turn all other relays OFF. Ignore colours.\n\nRead each row left to right: ON=1, OFF=0. Bit weights are 8, 4, 2, 1.\n\nDigit 1 = top row in decimal.\nDigit 2 = bottom row in decimal.\nDigit 3 = top XOR bottom in decimal.\nXOR: equal bits = 0; different bits = 1.\n\nEnter all three digits; both operators then authorise together for two seconds.");
        }

        static void BuildGhost(RectTransform panel, CooperativePuzzleFlow flow)
        {
            var a = Role(panel, "03  /  GHOST ADMINISTRATOR", "PLAYER A  /  NETWORK TELEMETRY", 0, .49f);
            var b = Role(panel, "SECURITY INCIDENT BULLETIN", "PLAYER B  /  DECODER", .51f, 1);
            Control(a, "A: REVEAL EVIDENCE", "clueA", "", .04f, .77f, .96f, .85f);
            Control(b, "B: REVEAL EVIDENCE", "clueB", "", .04f, .77f, .96f, .85f);
            var graph = Rect("Directed graph", a, .035f, .22f, .965f, .75f);
            string[] ids = { "S", "A", "B", "C", "D", "E", "V" };
            Vector2[] pos = { new Vector2(.09f,.5f), new Vector2(.34f,.78f), new Vector2(.34f,.22f), new Vector2(.61f,.88f), new Vector2(.61f,.5f), new Vector2(.61f,.12f), new Vector2(.91f,.5f) };
            int[,] edges = { {0,1,4},{0,2,3},{1,3,3},{1,4,1},{2,4,2},{2,5,2},{3,6,5},{4,6,1},{5,6,4} };
            for (int i = 0; i < edges.GetLength(0); i++)
            {
                var p = pos[edges[i,0]]; var q = pos[edges[i,1]];
                // Textual directed edge labels stay unambiguous at any aspect ratio.
                var mid = (p + q) * .5f;
                Label(graph, ids[edges[i,0]] + ">" + ids[edges[i,1]] + "\n" + edges[i,2] + " ms", mid.x - .075f, mid.y - .075f, mid.x + .075f, mid.y + .075f, 17, new Color(.6f,.78f,.88f));
            }
            for (int i = 0; i < ids.Length; i++) Control(graph, ids[i], "node", ids[i], pos[i].x-.055f, pos[i].y-.075f, pos[i].x+.055f, pos[i].y+.075f).role = 1;
            flow.route = Label(a, "", .04f, .07f, .70f, .2f, 25, Accent, TextAnchor.MiddleLeft);
            Control(a, "CLEAR ROUTE", "routeClear", "", .72f, .08f, .96f, .18f);
            flow.shift = Label(b, "", .04f, .65f, .58f, .74f, 25, Accent, TextAnchor.MiddleLeft);
            Control(b, "VALIDATE ROUTE", "validate", "", .6f, .65f, .96f, .74f);
            for (int i = 0; i < 5; i++) Control(b, i.ToString(), "shift", i.ToString(), .06f + i*.18f, .53f, .22f+i*.18f, .62f).role = 2;
            Label(b, "CAPTURED MESSAGE:  S T I R", .04f, .435f, .96f, .51f, 24, Color.white);
            for (int i = 0; i < 26; i++)
            {
                float x = .04f + i % 7 * .133f, y = .32f - i / 7 * .095f;
                string letter = ((char)('A' + i)).ToString(); Control(b, letter, "key", letter, x, y, x + .12f, y + .08f).role = 2;
            }
            flow.clueA3 = Clue(a, "A / NETWORK TELEMETRY", "All links are ONE-WAY; costs are milliseconds.\n\nS > A: 4       S > B: 3\nA > C: 3       A > D: 1\nB > D: 2       B > E: 2\nC > V: 5       D > V: 1       E > V: 4\n\nStart at S and guide a packet through connected nodes to V. Discuss link timings with B. B holds the updated security policy.");
            flow.clueB3 = Clue(b, "B / SECURITY DIRECTIVE", "Router D is compromised: NEVER use D.\nAn authorised route starts at S, ends at V, follows directed links, and costs AT MOST 10 ms.\n\nAsk A for the route's total latency T.\nSHIFT = T mod 5 (the remainder after dividing by 5).\n\nDecode STIR by shifting every letter BACKWARD by SHIFT in the alphabet; wrap A back to Z.\nABCDEFGHIJKLMNOPQRSTUVWXYZ\n\nSet the decoder shift, validate the route, enter four letters, then both authorise for two seconds.");
        }

        static RectTransform Role(Transform parent, string title, string subtitle, float left, float right)
        {
            var rt = Rect(title, parent, left, 0, right, 1); Background(rt, Panel);
            Label(rt, title, .04f, .91f, .96f, .98f, 27, Accent, TextAnchor.MiddleLeft);
            Label(rt, subtitle, .04f, .855f, .96f, .91f, 20, Color.white, TextAnchor.MiddleLeft); return rt;
        }
        static GameObject Clue(Transform parent, string title, string body)
        {
            var rt = Rect(title, parent, .025f, .025f, .975f, .855f); Background(rt, new Color(.11f,.16f,.23f));
            var blocker = rt.gameObject.AddComponent<PuzzleControl>(); blocker.action = "closeClues";
            Label(rt, title + "  /  PRIVATE", .04f, .86f, .96f, .98f, 28, Accent);
            Label(rt, body, .055f, .13f, .945f, .85f, 25, Color.white, TextAnchor.UpperLeft);
            Control(rt, "HIDE EVIDENCE", "closeClues", "", .20f, .02f, .80f, .115f); rt.gameObject.SetActive(false); return rt.gameObject;
        }
        static RectTransform Rect(string name, Transform parent, float x1, float y1, float x2, float y2)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(x1,y1); rt.anchorMax = new Vector2(x2,y2); rt.offsetMin = rt.offsetMax = Vector2.zero; return rt;
        }
        static Image Background(RectTransform rt, Color color) { var image = rt.gameObject.AddComponent<Image>(); image.color = color; image.raycastTarget = false; return image; }
        static Text Label(Transform parent, string value, float x1, float y1, float x2, float y2, int size, Color color, TextAnchor align = TextAnchor.MiddleCenter)
        {
            var rt = Rect("Text", parent, x1,y1,x2,y2); var label = rt.gameObject.AddComponent<Text>(); label.font = Font; label.text = value;
            label.fontSize = size; label.color = color; label.alignment = align; label.raycastTarget = false;
            label.resizeTextForBestFit = true; label.resizeTextMinSize = 12; label.resizeTextMaxSize = size; label.supportRichText = false;
            label.horizontalOverflow = HorizontalWrapMode.Wrap; label.verticalOverflow = VerticalWrapMode.Truncate; return label;
        }
        static PuzzleControl Control(Transform parent, string label, string action, string value, float x1, float y1, float x2, float y2)
        {
            var rt = Rect(action + " " + value, parent, x1,y1,x2,y2); var face = Background(rt, new Color(.14f,.22f,.31f));
            var button = rt.gameObject.AddComponent<Button>(); button.targetGraphic = face; button.navigation = new Navigation { mode = Navigation.Mode.None };
            var control = rt.gameObject.AddComponent<PuzzleControl>(); control.action = action; control.value = value;
            Label(rt, label, .04f,.04f,.96f,.96f,26,Color.white); return control;
        }
    }
}

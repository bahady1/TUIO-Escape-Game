using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace EscapeRoom.Editor
{
    /// <summary>
    /// Escape Room > Puzzles 2 + 3 > Place puzzles + clues in the rooms
    ///
    /// Additively places the Puzzle 2/3 consoles and the four clue posters into EscapeRoom.unity and switches
    /// the existing CooperativePuzzleFlow to in-room mode. Nothing else in the scene is rebuilt or moved.
    /// Running it again replaces only the objects it created itself.
    /// </summary>
    public static class RoomPuzzleInstaller
    {
        public const string RootName = "Puzzles 2 + 3 (in rooms)";
        const string MatDir = "Assets/EscapeRoom/Materials/";

        // Room geometry from EscapeRoomBuilder: lab x -6..0, server room x 0..6, z -3..3, walls 0.2 m.
        const float WallInner = 2.9f;     // inner face of the north / south walls (|z|)
        const float DividerFace = 0.1f;   // inner faces of the divider wall (x = -0.1 lab side, +0.1 server side)

        [MenuItem("Escape Room/Place Puzzles 2 + 3 in the Rooms", priority = 1)]
        [MenuItem("Escape Room/Puzzles 2 + 3/Place puzzles + clues in the rooms", priority = 1)]
        [MenuItem("Tools/Escape Room/Place Puzzles 2 + 3 in the Rooms")]
        public static void Place()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.OpenScene(CooperativePuzzleBuilder.MainScene, OpenSceneMode.Single);

            var flow = Object.FindAnyObjectByType<CooperativePuzzleFlow>(FindObjectsInactive.Include);
            if (flow == null)
            {
                CooperativePuzzleBuilder.Install();
                scene = SceneManager.GetActiveScene();
                flow = Object.FindAnyObjectByType<CooperativePuzzleFlow>(FindObjectsInactive.Include);
                if (flow == null) throw new InvalidOperationException("Could not install the cooperative puzzle flow.");
            }

            RemoveExisting(scene);
            var hud = Object.FindAnyObjectByType<GameHUD>(FindObjectsInactive.Include);
            var root = new GameObject(RootName);
            SceneManager.MoveGameObjectToScene(root, scene);
            var stations = root.AddComponent<RoomPuzzleStations>();
            stations.flow = flow;
            stations.labView = hud != null ? hud.leftRoom : null;
            stations.serverView = hud != null ? hud.rightRoom : null;
            stations.door = flow.exitDoor != null ? flow.exitDoor : Object.FindAnyObjectByType<Door>(FindObjectsInactive.Include);

            var plastic = Load("Dark Plastic"); var metal = Load("Metal"); var paper = Load("Whiteboard");

            // ---- Consoles: either side of the door, back to back (z = -2) ----
            Console(root.transform, "Lab Console A (Puzzles 2+3)", 1, new Vector3(-DividerFace, 1.45f, -2.0f), Vector3.left, plastic, metal);
            Console(root.transform, "Server Console B (Puzzles 2+3)", 2, new Vector3(DividerFace, 1.45f, -2.0f), Vector3.right, plastic, metal);

            // ---- Clues: each player's evidence is in their own room ----
            // Puzzle 2 / A: pinned on the lab whiteboard (south wall).
            var board = GameObject.Find("Whiteboard/Board");
            Vector3 boardFront = board != null
                ? new Vector3(board.GetComponent<Renderer>().bounds.center.x, 1.5f, board.GetComponent<Renderer>().bounds.max.z)
                : new Vector3(-2.9f, 1.5f, -2.865f);
            Clue(root.transform, "Clue 2A - Incident Log (whiteboard)", 1, 2, PuzzleClues.BlackoutATitle, PuzzleClues.BlackoutABody,
                boardFront, Vector3.forward, new Vector2(1.05f, 0.72f), paper, plastic);
            // Puzzle 3 / A: lab north wall above the 3D printer table.
            Clue(root.transform, "Clue 3A - Network Telemetry (lab north wall)", 1, 3, PuzzleClues.GhostATitle, PuzzleClues.GhostABody,
                new Vector3(-4.3f, 1.82f, WallInner), Vector3.back, new Vector2(0.85f, 0.72f), paper, plastic);
            // Puzzle 2 / B and Puzzle 3 / B: server-room north wall, either side of the terminal desk.
            Clue(root.transform, "Clue 2B - Maintenance Revision (server north wall)", 2, 2, PuzzleClues.BlackoutBTitle, PuzzleClues.BlackoutBBody,
                new Vector3(1.2f, 1.65f, WallInner), Vector3.back, new Vector2(0.8f, 0.95f), paper, plastic);
            Clue(root.transform, "Clue 3B - Security Directive (server north wall)", 2, 3, PuzzleClues.GhostBTitle, PuzzleClues.GhostBBody,
                new Vector3(4.8f, 1.65f, WallInner), Vector3.back, new Vector2(0.8f, 0.95f), paper, plastic);

            var so = new SerializedObject(flow);
            so.FindProperty("roomStations").objectReferenceValue = stations;
            so.ApplyModifiedProperties();

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new System.IO.IOException("Could not save " + CooperativePuzzleBuilder.MainScene);
            Selection.activeGameObject = root;
            Debug.Log("[Puzzles 2+3] Consoles and clue posters placed in the rooms. Press Play in EscapeRoom.unity.");
        }

        [MenuItem("Escape Room/Puzzles 2 + 3/Remove room puzzles (back to full-screen overlay)", priority = 2)]
        public static void Remove()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.OpenScene(CooperativePuzzleBuilder.MainScene, OpenSceneMode.Single);
            RemoveExisting(scene);
            var flow = Object.FindAnyObjectByType<CooperativePuzzleFlow>(FindObjectsInactive.Include);
            if (flow != null)
            {
                var so = new SerializedObject(flow);
                so.FindProperty("roomStations").objectReferenceValue = null;
                so.ApplyModifiedProperties();
            }
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        static void RemoveExisting(Scene scene)
        {
            foreach (var go in scene.GetRootGameObjects())
                if (go.name == RootName) Object.DestroyImmediate(go);
        }

        static Material Load(string name) => AssetDatabase.LoadAssetAtPath<Material>(MatDir + name + ".mat");

        /// <summary>Wall-mounted console. 'normal' points from the wall into the room.</summary>
        static void Console(Transform parent, string name, int role, Vector3 wallPoint, Vector3 normal, Material body, Material trim)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(wallPoint, Quaternion.LookRotation(-normal)); // local +Z points into the wall
            const float depth = 0.1f;
            Box("Cabinet", go.transform, new Vector3(0f, 0f, -depth / 2f), new Vector3(0.95f, 0.8f, depth), body);
            Box("Shelf", go.transform, new Vector3(0f, -0.45f, -0.12f), new Vector3(0.95f, 0.04f, 0.24f), trim);
            Box("Conduit", go.transform, new Vector3(0.38f, 0.85f, -0.03f), new Vector3(0.05f, 0.9f, 0.05f), trim);

            var item = go.AddComponent<RoomInteractable>();
            item.kind = RoomInteractable.Kind.Console;
            item.role = role;
            item.title = role == 1 ? "Lab console (Player A)" : "Server console (Player B)";
            var canvas = WorldCanvas(go.transform, new Vector3(0f, 0.03f, -depth - 0.004f), new Vector2(0.85f, 0.62f));
            Panel(canvas, new Color(0.02f, 0.05f, 0.04f));
            Label(canvas, role == 1 ? "CONSOLE A" : "CONSOLE B", 0.04f, 0.78f, 0.96f, 0.97f, 80, new Color(0.22f, 0.85f, 0.78f));
            item.worldLabel = Label(canvas, "OFFLINE\nNO POWER", 0.04f, 0.04f, 0.96f, 0.76f, 90, new Color(1f, 0.3f, 0.25f));
        }

        /// <summary>Poster / sheet on a wall with a readable heading and the full text (tap to read it large).</summary>
        static void Clue(Transform parent, string name, int role, int puzzle, string title, string body,
            Vector3 wallPoint, Vector3 normal, Vector2 size, Material paper, Material backing)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(wallPoint, Quaternion.LookRotation(-normal));
            Box("Backing", go.transform, new Vector3(0f, 0f, -0.006f), new Vector3(size.x + 0.03f, size.y + 0.03f, 0.01f), backing);
            Box("Sheet", go.transform, new Vector3(0f, 0f, -0.013f), new Vector3(size.x, size.y, 0.005f), paper);

            var item = go.AddComponent<RoomInteractable>();
            item.kind = RoomInteractable.Kind.Clue;
            item.role = role;
            item.puzzle = puzzle;
            item.title = title;
            item.body = body;
            var canvas = WorldCanvas(go.transform, new Vector3(0f, 0f, -0.0175f), size - new Vector2(0.04f, 0.04f));
            var ink = new Color(0.08f, 0.09f, 0.14f);
            item.worldLabel = Label(canvas, title, 0.04f, 0.80f, 0.96f, 0.98f, 70, ink);
            Label(canvas, (role == 1 ? "PLAYER A" : "PLAYER B") + "  -  PUZZLE " + puzzle + "  -  TAP TO READ", 0.04f, 0.72f, 0.96f, 0.80f, 34, new Color(0.6f, 0.1f, 0.1f));
            var text = Label(canvas, body, 0.05f, 0.03f, 0.95f, 0.71f, 30, ink);
            text.alignment = TextAnchor.UpperLeft;
        }

        static RectTransform WorldCanvas(Transform parent, Vector3 localPos, Vector2 sizeMetres)
        {
            var go = new GameObject("Screen", typeof(RectTransform), typeof(Canvas));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.localPosition = localPos;
            rt.localRotation = Quaternion.identity; // a UI canvas is seen from its -Z side, i.e. from the room
            rt.localScale = Vector3.one * 0.001f;   // 1 canvas unit = 1 mm
            rt.sizeDelta = sizeMetres * 1000f;
            go.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            return rt;
        }

        static void Panel(RectTransform canvas, Color color)
        {
            var img = canvas.gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
        }

        static Text Label(RectTransform canvas, string value, float x1, float y1, float x2, float y2, int size, Color color)
        {
            var rt = new GameObject("Text", typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(canvas, false);
            rt.anchorMin = new Vector2(x1, y1);
            rt.anchorMax = new Vector2(x2, y2);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.text = value;
            t.fontSize = size;
            t.color = color;
            t.alignment = TextAnchor.MiddleCenter;
            t.raycastTarget = false;
            t.supportRichText = false;
            t.resizeTextForBestFit = true;
            t.resizeTextMinSize = 8;
            t.resizeTextMaxSize = size;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            return t;
        }

        static GameObject Box(string name, Transform parent, Vector3 localCenter, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); // keeps its BoxCollider: taps raycast against it
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localCenter;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = size;
            if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }
    }
}

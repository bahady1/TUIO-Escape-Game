using System.Collections.Generic;
using System.IO;
using System.Linq;
using TuioUnity.Common;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace EscapeRoom.EditorTools
{
    /// <summary>
    /// Menu: Escape Room > Build Escape Room Scene
    ///
    /// Builds Assets/EscapeRoom/Scenes/EscapeRoom.unity:
    ///   - Main Lab (x -6..0) and Server Room (x 0..6), each 6 x 6 m, 3.2 m high
    ///   - a wall between them (x = 0) with a door in the middle
    ///   - the uploaded FBX models placed in both rooms + a few extra props
    ///   - two cameras: left half of the table = lab, right half = server room
    ///   - TangibleInput (object 1 turns the lab view, object 2 the server view), TUIO session, bottom HUD
    ///
    /// Running it again rebuilds the scene from scratch (move props afterwards by hand if you like).
    /// </summary>
    public static class EscapeRoomBuilder
    {
        const string Root = "Assets/EscapeRoom";
        const string ModelDir = Root + "/Models/";
        const string MatDir = Root + "/Materials";
        const string ScenePath = Root + "/Scenes/EscapeRoom.unity";

        // Room dimensions in metres.
        const float RoomW = 6f;    // along X, each room
        const float RoomD = 6f;    // along Z
        const float RoomH = 3.2f;
        const float WallT = 0.2f;
        const float DoorW = 1.2f;
        const float DoorH = 2.2f;

        const float BarHeight = 84f; // must match GameHUD.barHeight

        enum Wall { North, East, South, West }
        enum Fit { Height, Longest }

        sealed class Room
        {
            public string Name;
            public float XMin, XMax;
            public Transform Root;
            public Transform Props;
            public float CenterX => (XMin + XMax) * 0.5f;
        }

        // Materials
        static Material _labWall, _labFloor, _labCeiling, _srvWall, _srvFloor, _srvCeiling;
        static Material _door, _doorFrame, _metal, _darkPlastic, _lampWhite, _lampBlue, _screen, _ledRed, _ledGreen,
            _whiteboard, _red, _wood;
        static Material[] _posters;
        static Material _lampOff, _emergencyMat, _clueMat, _panelMat;
        static Material[] _knobMats;

        // Puzzle 1 art (Assets/EscapeRoom/Puzzle1)
        const string PuzzleDir = Root + "/Puzzle1/";
        static readonly string[] SlotKnobColors = { "blue", "black", "red", "yellow", "green", "yellow", "black", "blue" };
        static Sprite _panelSprite;
        static Sprite[] _slotKnobSprites;

        // Collected while building, wired into PowerController / SwitchPuzzle.
        static List<Light> _lamps, _emergencyLights;
        static List<Renderer> _lampRenderers, _emergencyRenderers;
        static Renderer _switchFace;
        static Transform[] _switchKnobs;

        [MenuItem("Escape Room/Build Escape Room Scene", priority = 0)]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            EnsureFolder(Root + "/Scenes");
            EnsureFolder(MatDir);
            ImportPuzzleArt();
            CreateMaterials();
            _lamps = new List<Light>();
            _emergencyLights = new List<Light>();
            _lampRenderers = new List<Renderer>();
            _emergencyRenderers = new List<Renderer>();
            _switchFace = null;
            _switchKnobs = null;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.20f, 0.20f, 0.23f);

            var building = new GameObject("Building").transform;
            var lab = NewRoom("Main Lab", -RoomW, 0f, building);
            var srv = NewRoom("Server Room", 0f, RoomW, building);

            BuildShell(lab, _labFloor, _labWall, _labCeiling, outerWest: true, outerEast: false);
            BuildShell(srv, _srvFloor, _srvWall, _srvCeiling, outerWest: false, outerEast: true);
            var door = BuildDividerAndDoor(building);

            FurnishLab(lab);
            FurnishServerRoom(srv);

            // ----- game objects -----
            var systems = new GameObject("Game").transform;

            var session = CreateTuioSession(systems);
            var inputGo = new GameObject("Tangible Input");
            inputGo.transform.SetParent(systems, false);
            var input = inputGo.AddComponent<TangibleInput>();
            input.session = session;

            var labView = CreateView(lab, "Lab View", tangibleId: 1, startWall: (int)Wall.North, left: true, audioListener: true);
            var srvView = CreateView(srv, "Server View", tangibleId: 2, startWall: (int)Wall.East, left: false, audioListener: false);

            var hudGo = new GameObject("Game HUD");
            hudGo.transform.SetParent(systems, false);
            var hud = hudGo.AddComponent<GameHUD>();
            hud.leftRoom = labView;
            hud.rightRoom = srvView;
            hud.barHeight = BarHeight;
            hud.startTimerOnPlay = false; // the intro dialogue starts the game
            hud.startMessage = "The power is out! Turn object 1 to look around the lab, object 2 for the server room.";
            hud.hints = new[]
            {
                "The switchbox in the server room controls the power. The lab has the clue.",
                "Look closely at the old circuit poster in the lab.",
                "Put the ring on the switchbox for 2 seconds, then turn it 90 degrees.",
                "Each knob can be up, middle or down. Tap a knob to move it.",
                "Turn the ring back 90 degrees to step away from the switchbox.",
            };

            var power = systems.gameObject.AddComponent<PowerController>();
            power.lamps = _lamps;
            power.lampRenderers = _lampRenderers;
            power.lampOffMaterial = _lampOff;
            power.emergencyLights = _emergencyLights;
            power.emergencyRenderers = _emergencyRenderers;
            power.startPowered = true; // the power fails during the intro

            var puzzleGo = new GameObject("Puzzle 1 - Switchbox");
            puzzleGo.transform.SetParent(systems, false);
            var puzzle = puzzleGo.AddComponent<SwitchPuzzle>();
            puzzle.room = srvView;
            puzzle.panelWall = (int)Wall.South;
            puzzle.panelFace = _switchFace;
            puzzle.worldKnobs = _switchKnobs;
            puzzle.worldKnobTravel = SwitchTravelLocal;
            puzzle.panelSprite = _panelSprite;
            puzzle.knobSprites = _slotKnobSprites;
            puzzle.ringIds = new[] { 4, 7 };

            var introGo = new GameObject("Intro Dialogue");
            introGo.transform.SetParent(systems, false);
            introGo.AddComponent<IntroDialogue>();

            door.name = "Door (Lab <-> Server Room)";

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuildSettings(ScenePath);
            ConfigurePlayer();
            AssetDatabase.SaveAssets();

            Selection.activeGameObject = labView.gameObject;
            Debug.Log("[EscapeRoom] Scene built: " + ScenePath +
                      "  |  Play: A/D or wheel on the left half turns the lab, arrows or wheel on the right half turns the server room, H = next hint.");
        }

        [MenuItem("Escape Room/Set Game View hint (QHD 2560x1440)", priority = 20)]
        static void GameViewHint()
        {
            EditorUtility.DisplayDialog("QHD Game View",
                "In the Game view, open the resolution dropdown, press '+', and add a Fixed Resolution of 2560 x 1440 " +
                "(or pick '16:9 Aspect'). The player build already starts at 2560 x 1440 full screen.", "OK");
        }

        // =====================================================================================================
        // Rooms

        static Room NewRoom(string name, float xMin, float xMax, Transform parent)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            var props = new GameObject("Props").transform;
            props.SetParent(root, false);
            return new Room { Name = name, XMin = xMin, XMax = xMax, Root = root, Props = props };
        }

        static void BuildShell(Room r, Material floor, Material wall, Material ceiling, bool outerWest, bool outerEast)
        {
            var shell = new GameObject("Shell").transform;
            shell.SetParent(r.Root, false);
            float cx = r.CenterX;

            Box("Floor", shell, new Vector3(cx, -0.05f, 0f), new Vector3(RoomW, 0.1f, RoomD), floor);
            Box("Ceiling", shell, new Vector3(cx, RoomH + 0.05f, 0f), new Vector3(RoomW, 0.1f, RoomD), ceiling);
            Box("Wall North", shell, new Vector3(cx, RoomH / 2f, RoomD / 2f), new Vector3(RoomW, RoomH, WallT), wall);
            Box("Wall South", shell, new Vector3(cx, RoomH / 2f, -RoomD / 2f), new Vector3(RoomW, RoomH, WallT), wall);
            if (outerWest) Box("Wall West", shell, new Vector3(r.XMin, RoomH / 2f, 0f), new Vector3(WallT, RoomH, RoomD + WallT), wall);
            if (outerEast) Box("Wall East", shell, new Vector3(r.XMax, RoomH / 2f, 0f), new Vector3(WallT, RoomH, RoomD + WallT), wall);

            // Skirting board for a bit of detail.
            var skirt = r.Name.StartsWith("Server") ? _metal : _doorFrame;
            float inZ = RoomD / 2f - WallT / 2f - 0.01f;
            Box("Skirting N", shell, new Vector3(cx, 0.05f, inZ), new Vector3(RoomW, 0.1f, 0.02f), skirt);
            Box("Skirting S", shell, new Vector3(cx, 0.05f, -inZ), new Vector3(RoomW, 0.1f, 0.02f), skirt);

            // Ceiling lamps + lights.
            bool server = r.Name.StartsWith("Server");
            var lampMat = server ? _lampBlue : _lampWhite;
            var lights = new GameObject("Lights").transform;
            lights.SetParent(r.Root, false);
            for (int i = -1; i <= 1; i += 2)
            {
                // Lights without shadows shine through walls, so the lamp next to the divider casts shadows
                // and the far one has a short range. That keeps each room's light inside the room.
                bool nearDivider = server ? i < 0 : i > 0;
                var lamp = Box("Ceiling Lamp", lights, new Vector3(cx + i * 1.4f, RoomH - 0.02f, 0f), new Vector3(1.2f, 0.04f, 0.5f), lampMat);
                var l = PointLight("Lamp Light", lights, new Vector3(cx + i * 1.4f, RoomH - 0.7f, 0f),
                    server ? new Color(0.65f, 0.8f, 1f) : new Color(1f, 0.96f, 0.9f), server ? 1.7f : 2.3f, nearDivider ? 9f : 5.5f);
                l.shadows = nearDivider ? LightShadows.Soft : LightShadows.None;
                _lamps.Add(l);
                _lampRenderers.Add(lamp.GetComponent<Renderer>());
            }

            // Emergency lighting (only on while the power is out): small red lamp on the far wall + a dim red light.
            float farX = server ? r.XMax - WallT / 2f - 0.05f : r.XMin + WallT / 2f + 0.05f;
            var eLamp = Box("Emergency Lamp", lights, new Vector3(farX, RoomH - 0.35f, 0f), new Vector3(0.08f, 0.12f, 0.4f), _emergencyMat);
            _emergencyRenderers.Add(eLamp.GetComponent<Renderer>());
            var e = PointLight("Emergency Light", lights, new Vector3(cx + (server ? 0.6f : -0.6f), RoomH - 0.5f, 0f),
                new Color(1f, 0.35f, 0.25f), 0.9f, 8f);
            e.shadows = LightShadows.Soft; // shadows stop it shining through the divider wall
            _emergencyLights.Add(e);
            // Off in the editor (scene starts powered); PowerController switches them at runtime.
            e.enabled = false;
            eLamp.GetComponent<Renderer>().enabled = false;
        }

        static GameObject BuildDividerAndDoor(Transform parent)
        {
            var div = new GameObject("Divider Wall").transform;
            div.SetParent(parent, false);

            // Two thin layers so each side keeps its room colour. Door opening is centred at z = 0.
            float half = WallT / 2f;
            DividerLayer(div, -half / 2f, half, _labWall, "Lab Side");
            DividerLayer(div, half / 2f, half, _srvWall, "Server Side");

            // Frame around the opening.
            float frameT = WallT + 0.04f;
            Box("Frame Left", div, new Vector3(0f, DoorH / 2f, -DoorW / 2f - 0.04f), new Vector3(frameT, DoorH, 0.08f), _doorFrame);
            Box("Frame Right", div, new Vector3(0f, DoorH / 2f, DoorW / 2f + 0.04f), new Vector3(frameT, DoorH, 0.08f), _doorFrame);
            Box("Frame Top", div, new Vector3(0f, DoorH + 0.04f, 0f), new Vector3(frameT, 0.08f, DoorW + 0.16f), _doorFrame);

            // Door: hinge on the -Z side, panel extends towards +Z.
            var doorRoot = new GameObject("Door");
            doorRoot.transform.SetParent(div, false);
            var hinge = new GameObject("Hinge").transform;
            hinge.SetParent(doorRoot.transform, false);
            hinge.localPosition = new Vector3(0f, 0f, -DoorW / 2f);
            Box("Door Panel", hinge, new Vector3(0f, DoorH / 2f, DoorW / 2f), new Vector3(0.06f, DoorH - 0.02f, DoorW - 0.02f), _door);
            Box("Handle Lab", hinge, new Vector3(-0.06f, 1.05f, DoorW - 0.15f), new Vector3(0.04f, 0.04f, 0.16f), _metal);
            Box("Handle Server", hinge, new Vector3(0.06f, 1.05f, DoorW - 0.15f), new Vector3(0.04f, 0.04f, 0.16f), _metal);
            Box("Window", hinge, new Vector3(0f, 1.65f, DoorW / 2f), new Vector3(0.07f, 0.4f, 0.3f), _darkPlastic);
            var door = doorRoot.AddComponent<Door>();
            door.hinge = hinge;

            // Keypad on the lab side (puzzle device) and card reader + warning lamp on the server side.
            Box("Keypad (Lab)", div, new Vector3(-half - 0.02f, 1.3f, -DoorW / 2f - 0.35f), new Vector3(0.04f, 0.26f, 0.18f), _darkPlastic);
            Box("Keypad Screen", div, new Vector3(-half - 0.045f, 1.37f, -DoorW / 2f - 0.35f), new Vector3(0.01f, 0.06f, 0.14f), _ledRed);
            Box("Card Reader (Server)", div, new Vector3(half + 0.02f, 1.3f, DoorW / 2f + 0.35f), new Vector3(0.04f, 0.2f, 0.12f), _darkPlastic);
            var lamp = Box("Warning Lamp", div, new Vector3(half + 0.05f, DoorH + 0.3f, 0f), new Vector3(0.1f, 0.12f, 0.3f), _ledRed);
            lamp.AddComponent<BlinkingLight>();

            return doorRoot;
        }

        static void DividerLayer(Transform parent, float x, float thickness, Material mat, string name)
        {
            float seg = (RoomD - DoorW) / 2f;
            float zc = DoorW / 2f + seg / 2f;
            Box(name + " A", parent, new Vector3(x, RoomH / 2f, -zc), new Vector3(thickness, RoomH, seg), mat);
            Box(name + " B", parent, new Vector3(x, RoomH / 2f, zc), new Vector3(thickness, RoomH, seg), mat);
            Box(name + " Lintel", parent, new Vector3(x, DoorH + (RoomH - DoorH) / 2f, 0f), new Vector3(thickness, RoomH - DoorH, DoorW), mat);
        }

        // =====================================================================================================
        // Furniture

        static void FurnishLab(Room r)
        {
            var p = r.Props;

            // North wall: two work tables with the 3D printer and a rag, AC above, poster.
            var tableA = OnWall("Table.fbx", "Table A", r, Wall.North, -4.3f, 0f, 0.75f, Fit.Height);
            var tableB = OnWall("Table.fbx", "Table B", r, Wall.North, -1.8f, 0f, 0.75f, Fit.Height);
            OnTop("3dprinter.fbx", "3D Printer", p, tableA, 0f, 0f, 0f, 0.5f, Fit.Height);
            OnTop("Rag.fbx", "Rag", p, tableB, 0.4f, 0.05f, 25f, 0.4f, Fit.Longest);
            OnWall("AC.fbx", "Air Conditioner", r, Wall.North, -4.3f, 2.35f, 1.0f, Fit.Longest);
            CluePoster(r, -1.8f);
            var poster1 = OnWall("Poster1.fbx", "Poster 1", r, Wall.West, 2.2f, 1.35f, 0.9f, Fit.Longest);

            // West wall: poster printer and a poster.
            OnWall("Poster printer.fbx", "Poster Printer", r, Wall.West, 0.7f, 0f, 1.6f, Fit.Longest);
            var poster2 = OnWall("Poster2.fbx", "Poster 2", r, Wall.West, -1.8f, 1.3f, 0.9f, Fit.Longest);

            // South wall: closet, whiteboard, shelf, bin.
            OnWall("Closet.fbx", "Closet", r, Wall.South, -5.0f, 0f, 2.0f, Fit.Height);
            Whiteboard(r, -2.9f);
            OnWall("shelf.fbx", "Shelf", r, Wall.South, -1.0f, 0f, 1.8f, Fit.Height);
            Floor("Bin1.fbx", "Bin", p, -1.9f, -2.5f, 0f, 0.6f, Fit.Height);

            // East wall (divider with the door): poster, blue bin.
            var poster3 = OnWall("Poster3.fbx", "Poster 3", r, Wall.East, 1.9f, 1.3f, 0.9f, Fit.Longest);
            Floor("Bin2Blue.fbx", "Blue Bin", p, -0.5f, -2.4f, 0f, 0.6f, Fit.Height);

            // Free standing armchair.
            Floor("Armchair.fbx", "Armchair", p, -5.0f, -1.0f, 60f, 1.0f, Fit.Height);

            ApplyPosterMaterial(poster1, _posters[0]);
            ApplyPosterMaterial(poster2, _posters[1]);
            ApplyPosterMaterial(poster3, _posters[2]);
        }

        static void FurnishServerRoom(Room r)
        {
            var p = r.Props;

            // East wall: row of server racks (count adapts to the model's width).
            var first = OnWall("server.fbx", "Server Rack 1", r, Wall.East, 0f, 0f, 2.0f, Fit.Height, thinAlongZ: false);
            if (first != null)
            {
                float width = LocalBounds(first).size.x + 0.05f;
                int count = Mathf.Clamp(Mathf.FloorToInt((RoomD - 0.8f) / width), 1, 5);
                float start = -(count - 1) * width / 2f;
                first.transform.position = WallPoint(r, Wall.East, start, 0f, LocalBounds(first).size.z);
                for (int i = 1; i < count; i++)
                {
                    var rack = OnWall("server.fbx", "Server Rack " + (i + 1), r, Wall.East, start + i * width, 0f, 2.0f, Fit.Height, thinAlongZ: false);
                    if (rack != null) AddRackLeds(rack.transform, i);
                }
                AddRackLeds(first.transform, 0);
            }

            // North wall: terminal desk + AC.
            TerminalDesk(r, 3.0f);
            OnWall("AC.fbx", "Air Conditioner", r, Wall.North, 3.0f, 2.35f, 1.0f, Fit.Longest);

            // South wall: closet, shelf, bin.
            OnWall("Closet.fbx", "Closet", r, Wall.South, 4.8f, 0f, 2.0f, Fit.Height);
            OnWall("shelf.fbx", "Shelf", r, Wall.South, 2.1f, 0f, 1.8f, Fit.Height);
            Switchbox(r, 3.55f);
            Floor("Bin1.fbx", "Bin", p, 1.2f, -2.5f, 0f, 0.6f, Fit.Height);

            // Extra props: cable tray under the ceiling, fire extinguisher by the door.
            Box("Cable Tray", p, new Vector3(r.CenterX + 0.3f, RoomH - 0.25f, 2.2f), new Vector3(RoomW - 1.2f, 0.08f, 0.4f), _metal);
            Box("Cable Tray Drop", p, new Vector3(RoomW - 0.4f, RoomH - 0.6f, 2.2f), new Vector3(0.4f, 0.7f, 0.08f), _metal);
            var ext = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ext.name = "Fire Extinguisher";
            ext.transform.SetParent(p, false);
            ext.transform.localPosition = new Vector3(0.3f, 0.3f, -1.4f);
            ext.transform.localScale = new Vector3(0.16f, 0.3f, 0.16f);
            ext.GetComponent<Renderer>().sharedMaterial = _red;
        }

        // ---------- Puzzle 1 ----------

        const float SwitchW = 0.9f;
        const float SwitchH = SwitchW * 1972f / 1437f;       // SwitchPanel.png aspect
        const float SwitchTravelLocal = 112f / 1972f * SwitchH;

        /// <summary>Old circuit drawing (Clue1.png, the clue), on the lab's north wall.</summary>
        static void CluePoster(Room r, float along)
        {
            var root = new GameObject("Clue Poster (Power Sequence)").transform;
            root.SetParent(r.Props, false);
            float z = RoomD / 2f - WallT / 2f;
            const float w = 0.78f, h = w * 2400f / 1792f;
            Box("Backing", root, new Vector3(along, 1.2f + h / 2f, z - 0.006f), new Vector3(w + 0.04f, h + 0.04f, 0.01f), _darkPlastic);
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Poster";
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            quad.transform.SetParent(root, false);
            quad.transform.localPosition = new Vector3(along, 1.2f + h / 2f, z - 0.013f); // quad faces -Z = into the lab
            quad.transform.localScale = new Vector3(w, h, 1f);
            quad.GetComponent<Renderer>().sharedMaterial = _clueMat;
        }

        /// <summary>Wall switchbox with 8 knobs in the server room (puzzle 1).</summary>
        static void Switchbox(Room r, float along)
        {
            var root = new GameObject("Switchbox (Puzzle 1)").transform;
            root.SetParent(r.Props, false);
            float wallZ = -(RoomD / 2f - WallT / 2f);
            const float centerY = 1.35f, depth = 0.12f;
            Box("Cabinet", root, new Vector3(along, centerY, wallZ + depth / 2f), new Vector3(SwitchW + 0.06f, SwitchH + 0.06f, depth), _darkPlastic);
            Box("Conduit", root, new Vector3(along, centerY + SwitchH / 2f + (RoomH - centerY - SwitchH / 2f) / 2f, wallZ + 0.04f),
                new Vector3(0.06f, RoomH - centerY - SwitchH / 2f, 0.06f), _metal);

            // Face: rotated 180 degrees so the quad's front points into the room (+Z).
            var face = new GameObject("Face").transform;
            face.SetParent(root, false);
            face.localPosition = new Vector3(along, centerY, wallZ + depth + 0.002f);
            face.localRotation = Quaternion.Euler(0f, 180f, 0f);

            var panel = GameObject.CreatePrimitive(PrimitiveType.Quad);
            panel.name = "Panel";
            Object.DestroyImmediate(panel.GetComponent<Collider>());
            panel.transform.SetParent(face, false);
            panel.transform.localScale = new Vector3(SwitchW, SwitchH, 1f);
            panel.GetComponent<Renderer>().sharedMaterial = _panelMat;
            _switchFace = panel.GetComponent<Renderer>();

            float[] slotX = { 260f, 568f, 874f, 1182f };
            float[] rowY = { 858f, 1480f };
            _switchKnobs = new Transform[8];
            for (int i = 0; i < 8; i++)
            {
                var knob = GameObject.CreatePrimitive(PrimitiveType.Quad);
                knob.name = $"Knob {i + 1} ({SlotKnobColors[i]})";
                Object.DestroyImmediate(knob.GetComponent<Collider>());
                knob.transform.SetParent(face, false);
                knob.transform.localPosition = new Vector3((slotX[i % 4] / 1437f - 0.5f) * SwitchW, (0.5f - rowY[i / 4] / 1972f) * SwitchH, -0.004f);
                knob.transform.localScale = new Vector3(172f / 1437f * SwitchW, 176f / 1972f * SwitchH, 1f);
                knob.GetComponent<Renderer>().sharedMaterial = _knobMats[i];
                _switchKnobs[i] = knob.transform;
            }
        }

        static void ImportPuzzleArt()
        {
            SetupTexture(PuzzleDir + "SwitchPanel.png", sprite: true);
            foreach (var c in SlotKnobColors.Distinct()) SetupTexture(PuzzleDir + $"Knobs/knob_{c}.png", sprite: true);
            SetupTexture(PuzzleDir + "Clue1.png", sprite: false);

            _panelSprite = AssetDatabase.LoadAssetAtPath<Sprite>(PuzzleDir + "SwitchPanel.png");
            _slotKnobSprites = SlotKnobColors.Select(c => AssetDatabase.LoadAssetAtPath<Sprite>(PuzzleDir + $"Knobs/knob_{c}.png")).ToArray();
            if (_panelSprite == null) Debug.LogWarning("[EscapeRoom] Missing " + PuzzleDir + "SwitchPanel.png");
        }

        static void SetupTexture(string path, bool sprite)
        {
            if (AssetImporter.GetAtPath(path) is not TextureImporter ti)
            {
                Debug.LogWarning("[EscapeRoom] Texture not found: " + path);
                return;
            }
            bool changed = false;
            if (sprite && ti.textureType != TextureImporterType.Sprite)
            {
                ti.textureType = TextureImporterType.Sprite;
                ti.spriteImportMode = SpriteImportMode.Single;
                changed = true;
            }
            if (!ti.alphaIsTransparency) { ti.alphaIsTransparency = true; changed = true; }
            if (ti.maxTextureSize < 2048) { ti.maxTextureSize = 2048; changed = true; }
            if (sprite && ti.mipmapEnabled) { ti.mipmapEnabled = false; changed = true; }
            if (changed) ti.SaveAndReimport();
        }

        static Material TexMat(string name, Texture tex, bool alphaClip, float emission)
        {
            var m = Mat(name, Color.white, 0.25f, 0f, emission > 0f ? (Color?)(Color.white * emission) : null);
            if (tex == null) return m;
            m.SetTexture("_BaseMap", tex);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
            if (emission > 0f) m.SetTexture("_EmissionMap", tex);
            if (alphaClip)
            {
                m.SetFloat("_AlphaClip", 1f);
                m.SetFloat("_Cutoff", 0.5f);
                m.EnableKeyword("_ALPHATEST_ON");
                m.renderQueue = (int)RenderQueue.AlphaTest;
            }
            EditorUtility.SetDirty(m);
            return m;
        }

        static void Whiteboard(Room r, float along)
        {
            var root = new GameObject("Whiteboard").transform;
            root.SetParent(r.Props, false);
            float z = -(RoomD / 2f - WallT / 2f);
            Box("Board", root, new Vector3(along, 1.45f, z + 0.02f), new Vector3(1.8f, 1.1f, 0.03f), _whiteboard);
            Box("Frame", root, new Vector3(along, 1.45f, z + 0.01f), new Vector3(1.86f, 1.16f, 0.02f), _metal);
            Box("Tray", root, new Vector3(along, 0.88f, z + 0.06f), new Vector3(1.2f, 0.03f, 0.08f), _metal);
        }

        static void TerminalDesk(Room r, float along)
        {
            var root = new GameObject("Terminal Desk").transform;
            root.SetParent(r.Props, false);
            float z = RoomD / 2f - WallT / 2f - 0.4f;
            Box("Desk Top", root, new Vector3(along, 0.74f, z), new Vector3(1.6f, 0.04f, 0.7f), _darkPlastic);
            Box("Leg L", root, new Vector3(along - 0.75f, 0.36f, z), new Vector3(0.05f, 0.72f, 0.65f), _metal);
            Box("Leg R", root, new Vector3(along + 0.75f, 0.36f, z), new Vector3(0.05f, 0.72f, 0.65f), _metal);
            Box("Monitor", root, new Vector3(along, 1.08f, z + 0.18f), new Vector3(0.7f, 0.42f, 0.04f), _darkPlastic);
            Box("Screen", root, new Vector3(along, 1.08f, z + 0.155f), new Vector3(0.64f, 0.36f, 0.01f), _screen);
            Box("Stand", root, new Vector3(along, 0.83f, z + 0.2f), new Vector3(0.06f, 0.14f, 0.06f), _metal);
            Box("Keyboard", root, new Vector3(along, 0.77f, z - 0.1f), new Vector3(0.45f, 0.02f, 0.15f), _darkPlastic);
        }

        static void AddRackLeds(Transform rack, int seed)
        {
            var b = LocalBounds(rack.gameObject);
            var rnd = new System.Random(seed * 31 + 7);
            // LEDs on the side of the rack facing the room (holder +Z).
            for (int i = 0; i < 6; i++)
            {
                float y = 0.5f + i * 0.22f;
                float x = (float)(rnd.NextDouble() - 0.5) * b.size.x * 0.6f;
                var led = Box("LED", rack, new Vector3(x, y, b.max.z + 0.01f), new Vector3(0.03f, 0.03f, 0.01f), i % 3 == 0 ? _ledRed : _ledGreen);
                var blink = led.AddComponent<BlinkingLight>();
                blink.onChance = 0.8f;
            }
        }

        // =====================================================================================================
        // Model placement helpers

        /// <summary>Loads a model, scales it to 'size' metres and puts its bottom-centre at the holder origin.</summary>
        static GameObject LoadModel(string file, string label, float size, Fit fit, bool thinAlongZ)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModelDir + file);
            if (prefab == null)
            {
                Debug.LogWarning($"[EscapeRoom] Model not found: {ModelDir}{file}");
                return null;
            }

            var holder = new GameObject(label);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            model.transform.SetParent(holder.transform, false);

            var b = LocalBounds(holder);
            if (thinAlongZ && b.size.z > b.size.x)
            {
                model.transform.localRotation = Quaternion.Euler(0f, 90f, 0f) * model.transform.localRotation;
                b = LocalBounds(holder);
            }

            float current = fit == Fit.Height ? b.size.y : Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            if (current > 1e-5f) model.transform.localScale *= size / current;

            b = LocalBounds(holder);
            model.transform.localPosition -= new Vector3(b.center.x, b.min.y, b.center.z);
            return holder;
        }

        /// <summary>Against a wall, facing into the room. 'along' = world X for North/South walls, world Z for East/West.</summary>
        static GameObject OnWall(string file, string label, Room r, Wall wall, float along, float bottomY, float size, Fit fit,
            bool thinAlongZ = true, bool flip = false)
        {
            var h = LoadModel(file, label, size, fit, thinAlongZ);
            if (h == null) return null;
            float depth = LocalBounds(h).size.z;
            h.transform.SetParent(r.Props, false);
            h.transform.SetPositionAndRotation(WallPoint(r, wall, along, bottomY, depth),
                Quaternion.LookRotation(WallNormal(wall)) * Quaternion.Euler(0f, flip ? 180f : 0f, 0f));
            return h;
        }

        static GameObject Floor(string file, string label, Transform parent, float x, float z, float yaw, float size, Fit fit)
        {
            var h = LoadModel(file, label, size, fit, false);
            if (h == null) return null;
            h.transform.SetParent(parent, false);
            h.transform.SetPositionAndRotation(new Vector3(x, 0f, z), Quaternion.Euler(0f, yaw, 0f));
            return h;
        }

        static GameObject OnTop(string file, string label, Transform parent, GameObject surface, float offsetX, float offsetZ,
            float yaw, float size, Fit fit)
        {
            if (surface == null) return null;
            var h = LoadModel(file, label, size, fit, false);
            if (h == null) return null;
            var sb = WorldBounds(surface);
            h.transform.SetParent(parent, false);
            h.transform.SetPositionAndRotation(new Vector3(sb.center.x + offsetX, sb.max.y, sb.center.z + offsetZ),
                surface.transform.rotation * Quaternion.Euler(0f, yaw, 0f));
            return h;
        }

        static Vector3 WallNormal(Wall w) => w switch
        {
            Wall.North => Vector3.back,
            Wall.South => Vector3.forward,
            Wall.East => Vector3.left,
            _ => Vector3.right,
        };

        static Vector3 WallPoint(Room r, Wall wall, float along, float y, float depth)
        {
            float off = depth / 2f + 0.02f;
            float inner = WallT / 2f;
            return wall switch
            {
                Wall.North => new Vector3(along, y, RoomD / 2f - inner - off),
                Wall.South => new Vector3(along, y, -RoomD / 2f + inner + off),
                Wall.East => new Vector3(r.XMax - inner - off, y, along),
                _ => new Vector3(r.XMin + inner + off, y, along),
            };
        }

        static Bounds WorldBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            return b;
        }

        /// <summary>Bounds in the holder's local space (exact for meshes, via their 8 local corners).</summary>
        static Bounds LocalBounds(GameObject holder)
        {
            var inv = holder.transform.worldToLocalMatrix;
            bool any = false;
            var b = new Bounds();
            foreach (var mf in holder.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                var mb = mf.sharedMesh.bounds;
                var m = inv * mf.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = m.MultiplyPoint3x4(c);
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; }
                    else b.Encapsulate(p);
                }
            }
            foreach (var smr in holder.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (smr.sharedMesh == null) continue;
                var p = inv.MultiplyPoint3x4(smr.bounds.center);
                if (!any) { b = new Bounds(p, smr.bounds.size); any = true; }
                else b.Encapsulate(new Bounds(p, smr.bounds.size));
            }
            return b;
        }

        /// <summary>The poster FBX files reference textures that are not included, so give them a visible clue material.</summary>
        static void ApplyPosterMaterial(GameObject poster, Material posterMat)
        {
            if (poster == null) return;
            foreach (var rend in poster.GetComponentsInChildren<Renderer>())
            {
                var mats = rend.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null) continue;
                    var c = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.HasProperty("_Color") ? m.color : Color.white;
                    bool dark = c.r + c.g + c.b < 0.15f;
                    bool image = m.name.Contains("Monster") || m.name.Contains("Girl") || m.name.Contains("Lght");
                    if (dark || image || mats.Length == 1)
                    {
                        mats[i] = posterMat;
                        changed = true;
                    }
                }
                if (changed) rend.sharedMaterials = mats;
            }
        }

        // =====================================================================================================
        // Cameras, TUIO, primitives

        static WallViewController CreateView(Room r, string name, int tangibleId, int startWall, bool left, bool audioListener)
        {
            var go = new GameObject(name);
            go.transform.SetParent(r.Root, false);
            go.transform.position = new Vector3(r.CenterX, 0f, 0f);

            var camGo = new GameObject(r.Name + " Camera");
            camGo.transform.SetParent(go.transform, false);
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.fieldOfView = 62f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 50f;
            cam.rect = GameHUD.ViewportFor(left, BarHeight);
            if (audioListener) camGo.AddComponent<AudioListener>();

            var view = go.AddComponent<WallViewController>();
            view.roomName = r.Name;
            view.tangibleId = tangibleId;
            view.viewCamera = cam;
            view.startWall = startWall;
            view.ApplyCamera(startWall * 90f);
            return view;
        }

        static TuioSessionBehaviour CreateTuioSession(Transform parent)
        {
            var go = new GameObject("TUIO 1.1 Session");
            go.transform.SetParent(parent, false);
            var session = go.AddComponent<TuioSessionBehaviour>();

            // Same settings as the session in SampleScene (TUIO 1.1, WebSocket, 10.0.0.20).
            var so = new SerializedObject(session);
            SetInt(so, "<TuioVersion>k__BackingField", 0);
            SetInt(so, "<ConnectionType>k__BackingField", 1);
            SetInt(so, "<UdpPort>k__BackingField", 3333);
            var ip = so.FindProperty("_ipAddress");
            if (ip != null) ip.stringValue = "10.0.0.20";
            so.ApplyModifiedPropertiesWithoutUndo();
            return session;
        }

        static void SetInt(SerializedObject so, string path, int value)
        {
            var p = so.FindProperty(path);
            if (p == null) return;
            if (p.propertyType == SerializedPropertyType.Enum) p.enumValueIndex = value;
            else p.intValue = value;
        }

        /// <summary>Cube primitive. 'center' is local to the parent (all room groups sit at the world origin).</summary>
        static GameObject Box(string name, Transform parent, Vector3 center, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        static Light PointLight(string name, Transform parent, Vector3 pos, Color color, float intensity, float range)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = color;
            l.intensity = intensity;
            l.range = range;
            l.shadows = LightShadows.None;
            return l;
        }

        // =====================================================================================================
        // Materials, project settings

        static void CreateMaterials()
        {
            _labWall = Mat("Lab Wall", new Color(0.86f, 0.87f, 0.84f));
            _labFloor = Mat("Lab Floor", new Color(0.55f, 0.57f, 0.6f), 0.45f);
            _labCeiling = Mat("Lab Ceiling", new Color(0.92f, 0.92f, 0.92f));
            _srvWall = Mat("Server Wall", new Color(0.22f, 0.26f, 0.32f));
            _srvFloor = Mat("Server Floor", new Color(0.16f, 0.17f, 0.19f), 0.5f);
            _srvCeiling = Mat("Server Ceiling", new Color(0.3f, 0.32f, 0.35f));
            _door = Mat("Door", new Color(0.55f, 0.36f, 0.2f), 0.35f);
            _doorFrame = Mat("Door Frame", new Color(0.3f, 0.3f, 0.32f), 0.4f);
            _wood = Mat("Wood", new Color(0.6f, 0.45f, 0.3f));
            _metal = Mat("Metal", new Color(0.62f, 0.64f, 0.67f), 0.6f, 0.8f);
            _darkPlastic = Mat("Dark Plastic", new Color(0.08f, 0.08f, 0.09f), 0.5f);
            _whiteboard = Mat("Whiteboard", new Color(0.97f, 0.97f, 0.97f), 0.8f);
            _red = Mat("Red Paint", new Color(0.8f, 0.08f, 0.06f), 0.6f);
            _lampWhite = Mat("Lamp White", Color.white, 0.5f, 0f, new Color(1f, 0.97f, 0.9f) * 2f);
            _lampBlue = Mat("Lamp Blue", new Color(0.8f, 0.9f, 1f), 0.5f, 0f, new Color(0.6f, 0.8f, 1f) * 1.5f);
            _screen = Mat("Screen", new Color(0.05f, 0.2f, 0.25f), 0.9f, 0f, new Color(0.1f, 0.8f, 0.9f) * 1.3f);
            _ledRed = Mat("LED Red", new Color(1f, 0.1f, 0.05f), 0.5f, 0f, new Color(1f, 0.1f, 0.05f) * 3f);
            _ledGreen = Mat("LED Green", new Color(0.1f, 1f, 0.2f), 0.5f, 0f, new Color(0.1f, 1f, 0.2f) * 3f);
            _lampOff = Mat("Lamp Off", new Color(0.32f, 0.33f, 0.35f), 0.4f);
            _emergencyMat = Mat("Emergency Lamp", new Color(1f, 0.15f, 0.1f), 0.5f, 0f, new Color(1f, 0.15f, 0.1f) * 2.5f);

            // Faint emission keeps the clue and the switchbox readable while the power is out.
            _clueMat = TexMat("Clue Poster", AssetDatabase.LoadAssetAtPath<Texture2D>(PuzzleDir + "Clue1.png"), false, 0.35f);
            _panelMat = TexMat("Switch Panel", AssetDatabase.LoadAssetAtPath<Texture2D>(PuzzleDir + "SwitchPanel.png"), true, 0.25f);
            var knobMatByColor = new Dictionary<string, Material>();
            foreach (var c in SlotKnobColors.Distinct())
                knobMatByColor[c] = TexMat("Knob " + c, AssetDatabase.LoadAssetAtPath<Texture2D>(PuzzleDir + $"Knobs/knob_{c}.png"), true, 0.3f);
            _knobMats = SlotKnobColors.Select(c => knobMatByColor[c]).ToArray();

            _posters = new[]
            {
                Mat("Poster A (drop image in Base Map)", new Color(0.95f, 0.75f, 0.2f), 0.2f),
                Mat("Poster B (drop image in Base Map)", new Color(0.25f, 0.55f, 0.95f), 0.2f),
                Mat("Poster C (drop image in Base Map)", new Color(0.85f, 0.3f, 0.45f), 0.2f),
            };
            AssetDatabase.SaveAssets();
        }

        static Material Mat(string name, Color color, float smoothness = 0.25f, float metallic = 0f, Color? emission = null)
        {
            string path = $"{MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Color")) m.SetColor("_Color", color);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            if (emission.HasValue)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emission.Value);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            EditorUtility.SetDirty(m);
            return m;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static void AddSceneToBuildSettings(string path)
        {
            var list = EditorBuildSettings.scenes.Where(s => s.path != path).ToList();
            list.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = list.ToArray();
        }

        static void ConfigurePlayer()
        {
            // QHD table display.
            PlayerSettings.defaultScreenWidth = 2560;
            PlayerSettings.defaultScreenHeight = 1440;
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
        }
    }
}

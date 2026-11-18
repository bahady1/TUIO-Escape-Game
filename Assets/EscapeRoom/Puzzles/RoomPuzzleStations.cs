using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace EscapeRoom
{
    /// <summary>
    /// Puts Puzzles 2 (Blackout Protocol) and 3 (Ghost Administrator) into the two rooms instead of a
    /// full-screen overlay.
    ///
    ///  - Player A (left half, Main Lab) operates the console on the lab side of the door:
    ///    Puzzle 2 relays, Puzzle 3 packet route.
    ///  - Player B (right half, Server Room) operates the console on the server side of the door:
    ///    Puzzle 2 checksum keypad, Puzzle 3 decoder + passphrase.
    ///  - Each player's evidence hangs on the walls of their own room (tap a poster to read it).
    ///  - Solving Puzzle 2 opens the door between the rooms; solving Puzzle 3 wins the game.
    ///
    /// Tapping a console / poster in a room view opens it over that half of the table. All puzzle rules,
    /// the two-player authorisation and TUIO input stay in BlackoutProtocol, GhostAdministrator,
    /// CooperativePuzzleFlow and CooperativePuzzleInput; this component is only the in-room view.
    /// </summary>
    [DefaultExecutionOrder(90)]
    public class RoomPuzzleStations : MonoBehaviour
    {
        public CooperativePuzzleFlow flow;
        [Tooltip("Left half / Player A.")] public WallViewController labView;
        [Tooltip("Right half / Player B.")] public WallViewController serverView;
        [Tooltip("Door between the rooms.")] public Door door;
        [Tooltip("Open the door between the rooms as soon as Puzzle 2 is solved (otherwise after Puzzle 3).")]
        public bool openDoorAfterPuzzle2 = true;

        [Header("Zoom with the ring tangible (same as the Puzzle 1 switchbox)")]
        [Tooltip("Marker ids of the ring(s). Copied from the Puzzle 1 switchbox if it exists.")]
        public int[] ringIds = { 4, 7 };
        [Tooltip("Seconds the ring has to rest on a console / poster to zoom in (no turning needed).")]
        public float holdSeconds = 1f;
        [Tooltip("Extra room around the ring position (pixels) that still counts as 'on the object'.")]
        public float ringMarginPx = 60f;
        [Tooltip("Also open consoles / posters with a plain tap (off = only the ring zooms in).")]
        public bool tapToOpen;

        [Header("Messages (bottom bar)")]
        [TextArea(1, 3)] public string puzzle2Intro =
            "Consoles by the door are online. Rest the ring on a console or poster for 1 s to zoom in; tap CLOSE to leave.";
        [TextArea(1, 3)] public string puzzle3Message =
            "Relays stable - the door between the rooms is open! The consoles now run the Ghost Administrator.";
        [TextArea(1, 3)] public string offlineMessage = "This console has no power yet. Restore the power first.";
        [TextArea(1, 3)] public string wrongConsoleMessage = "That console belongs to the other player.";

        static readonly Color Ink = new(.035f, .055f, .09f, .96f);
        static readonly Color PanelColor = new(.065f, .095f, .15f, 1f);
        static readonly Color Accent = new(.22f, .85f, .78f, 1f);
        static readonly Color ButtonColor = new(.14f, .22f, .31f, 1f);
        static readonly Color Paper = new(.93f, .91f, .84f, 1f);

        public IReadOnlyList<PuzzleControl> Controls => _controls;
        public bool IsConsoleOpen(int role) => Valid(role) && _console[role].activeSelf;
        public bool IsClueOpen(int role) => Valid(role) && _clue[role].activeSelf;

        readonly List<PuzzleControl> _controls = new();
        readonly List<RoomInteractable> _items = new();
        // Index 1 = Player A / lab, 2 = Player B / server room.
        readonly GameObject[] _half = new GameObject[3], _console = new GameObject[3], _clue = new GameObject[3];
        readonly GameObject[] _p2 = new GameObject[3], _p3 = new GameObject[3];
        readonly Text[] _status = new Text[3], _padLabel = new Text[3], _clueTitle = new Text[3], _clueBody = new Text[3];
        readonly Image[] _progress = new Image[3], _pad = new Image[3];
        readonly bool[] _lockedByMe = new bool[3];
        Text _entry, _route, _shift;
        Font _font;
        int _lastStage = -1;
        bool _built, _subscribed;

        // Ring zoom state
        sealed class Ring { public RoomInteractable item, used; public int role; public float dwell; public Vector2 pos; }
        readonly Dictionary<int, Ring> _rings = new();
        readonly RectTransform[] _marker = new RectTransform[3];
        readonly Image[] _markerFill = new Image[3], _markerRing = new Image[3];
        readonly Text[] _markerLabel = new Text[3];
        TangibleInput _legacy;

        static bool Valid(int role) => role == 1 || role == 2;
        WallViewController View(int role) => role == 1 ? labView : serverView;

        // ------------------------------------------------------------------ lifecycle

        void Awake()
        {
            if (flow == null) flow = FindAnyObjectByType<CooperativePuzzleFlow>();
            if (flow == null || flow.blackout == null || flow.ghost == null)
            {
                Debug.LogError("[Puzzles 2+3] RoomPuzzleStations needs a CooperativePuzzleFlow with Blackout and Ghost components.");
                enabled = false;
                return;
            }
            if (labView == null || serverView == null)
            {
                var hud = FindAnyObjectByType<GameHUD>();
                if (hud != null) { if (labView == null) labView = hud.leftRoom; if (serverView == null) serverView = hud.rightRoom; }
            }
            if (door == null) door = flow.exitDoor != null ? flow.exitDoor : FindAnyObjectByType<Door>();
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _items.AddRange(FindObjectsByType<RoomInteractable>(FindObjectsInactive.Include, FindObjectsSortMode.None));
            BuildUI();
            _built = true;

            // The flow keeps doing the two-player authorisation; it just watches our pads.
            flow.padA = (RectTransform)_pad[1].transform;
            flow.padB = (RectTransform)_pad[2].transform;
            flow.padAImage = _pad[1];
            flow.padBImage = _pad[2];
            flow.roomStations = this;
        }

        void Start()
        {
            if (!_built) return;
            flow.blackout.OnSolved += OnBlackoutSolved;
            flow.ghost.OnSolved += OnGhostSolved;
            if (flow.puzzle1 != null && flow.puzzle1.ringIds != null && flow.puzzle1.ringIds.Length > 0)
                ringIds = flow.puzzle1.ringIds;
            // Taps (TUIO touches + mouse clicks, de-duplicated) and the ring come from the room's own input.
            _legacy = TangibleInput.Instance != null ? TangibleInput.Instance : FindAnyObjectByType<TangibleInput>();
            if (_legacy != null)
            {
                _legacy.Tapped += OnTap;
            }
            _subscribed = true;
            OnStageChanged();
        }

        void OnDestroy()
        {
            if (!_subscribed) return;
            if (flow != null && flow.blackout != null) flow.blackout.OnSolved -= OnBlackoutSolved;
            if (flow != null && flow.ghost != null) flow.ghost.OnSolved -= OnGhostSolved;
            if (_legacy != null)
            {
                _legacy.Tapped -= OnTap;
            }
        }

        void OnTap(Vector2 screen) => flow.HandleRoomTap(screen);

        // ------------------------------------------------------------------ ring zoom

        Ring RingState(int id)
        {
            if (!_rings.TryGetValue(id, out var ring)) _rings[id] = ring = new Ring();
            return ring;
        }

        /// <summary>Ring resting on a console / poster for 'holdSeconds' opens it. After CLOSE the ring
        /// has to leave that object before it can open it again.</summary>
        void UpdateRings()
        {
            bool started = GameHUD.Instance == null || GameHUD.Instance.GameStarted;
            var shown = new Ring[3];
            foreach (int id in ringIds)
            {
                var ring = RingState(id);
                if (!started || _legacy == null || !_legacy.TryGetScreenPosition(id, out var p))
                {
                    ring.item = ring.used = null;
                    ring.dwell = 0f;
                    continue;
                }
                int role = RoleAt(p);
                // While that half is zoomed in the ring just rests on the panel: leave its state alone.
                if (role != 0 && (_console[role].activeSelf || _clue[role].activeSelf)) continue;
                var item = role != 0 && !View(role).inputLocked ? ItemUnderRing(role, p) : null;
                if (item != ring.used) ring.used = null;
                if (item == null || item == ring.used)
                {
                    ring.item = null;
                    ring.dwell = 0f;
                    continue;
                }
                if (item != ring.item || role != ring.role) { ring.item = item; ring.role = role; ring.dwell = 0f; }
                ring.pos = p;
                ring.dwell += Time.deltaTime;
                if (ring.dwell >= holdSeconds)
                {
                    ring.used = item;
                    ring.item = null;
                    ring.dwell = 0f;
                    if (item.kind == RoomInteractable.Kind.Clue) OpenClue(role, item);
                    else if (item.role != role) Hint(wrongConsoleMessage);
                    else OpenConsole(role);
                    continue;
                }
                shown[role] = ring;
            }

            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 3f);
            for (int role = 1; role <= 2; role++)
            {
                var ring = shown[role];
                _marker[role].gameObject.SetActive(ring != null);
                if (ring == null) continue;
                _marker[role].position = ring.pos;
                _markerFill[role].fillAmount = Mathf.Clamp01(ring.dwell / holdSeconds);
                _markerRing[role].color = new Color(1f, 1f, 1f, 0.35f + 0.4f * pulse);
                _markerLabel[role].text = "Hold the ring still...\n" + ring.item.title;
            }
        }

        RoomInteractable ItemUnderRing(int role, Vector2 p)
        {
            var cam = View(role).viewCamera;
            Vector2[] offsets = { Vector2.zero, Vector2.up, Vector2.down, Vector2.left, Vector2.right };
            foreach (var o in offsets)
            {
                var point = p + o * ringMarginPx;
                if (!cam.pixelRect.Contains(point)) continue;
                if (Physics.Raycast(cam.ScreenPointToRay(point), out var hit, 40f, ~0, QueryTriggerInteraction.Collide))
                {
                    var item = hit.collider.GetComponentInParent<RoomInteractable>();
                    if (item != null) return item;
                }
            }
            return null;
        }

        void Update()
        {
            if (!_built) return;
            UpdateRings();
            int stage = flow.ActivePuzzle;
            bool playable = (stage == 2 || stage == 3) && !flow.AllSolved;
            for (int role = 1; role <= 2; role++)
            {
                if (_console[role].activeSelf && !playable) Close(role);
                // Keep the room from turning away while a panel is open (other scripts may unlock it).
                if (_lockedByMe[role] && View(role) != null) View(role).inputLocked = true;
                _half[role].SetActive(_console[role].activeSelf || _clue[role].activeSelf);
            }
            if (!playable) return;

            string feedback = stage == 2 ? flow.blackout.Feedback : flow.ghost.Feedback;
            float progress = stage == 2 ? flow.blackout.ConfirmationProgress : flow.ghost.ConfirmationProgress;
            bool local = flow.input == null || flow.input.mode == PuzzleInputMode.LocalMouse;
            for (int role = 1; role <= 2; role++)
            {
                _status[role].text = feedback;
                _progress[role].rectTransform.anchorMax = new Vector2(progress, 1f);
                string who = role == 1 ? "A" : "B";
                _padLabel[role].text = local ? who + "  AUTHORISE\n(tap to hold / release)" : "PLACE TOKEN " + who + "\nHERE";
            }
            int length = stage == 2 ? 3 : 4;
            _entry.text = (stage == 2 ? "CHECKSUM   " : "PASSPHRASE   ") + flow.Entry.PadRight(length, '_');
            _route.text = "PACKET   " + (flow.ghost.Route.Count == 0 ? "-" : string.Join(" > ", flow.ghost.Route)) +
                          "\nLATENCY   " + flow.ghost.TotalLatency + " ms";
            _shift.text = "DECODER SHIFT   " + flow.ghost.Shift + (flow.ghost.RouteValidated ? "      ROUTE OK" : "");
        }

        // ------------------------------------------------------------------ called by CooperativePuzzleFlow

        /// <summary>Puzzle stage changed (0 = waiting for Puzzle 1, 2 = Blackout, 3 = Ghost).</summary>
        public void OnStageChanged()
        {
            if (!_built) return;
            int stage = flow.ActivePuzzle;
            for (int role = 1; role <= 2; role++)
            {
                _p2[role].SetActive(stage == 2);
                _p3[role].SetActive(stage == 3);
            }
            if (stage == 2 && _lastStage != 2) Hint(puzzle2Intro);
            _lastStage = stage;
            RefreshWorldLabels();
        }

        /// <summary>Panel-only actions (open / close). Returns true when handled.</summary>
        public bool HandleUiAction(string action, string value)
        {
            if (action != "roomClose") return false;
            Close(value == "B" ? 2 : 1);
            return true;
        }

        /// <summary>A tap that did not hit a panel control: tap a console or clue in the room view.</summary>
        public void HandleWorldTap(Vector2 screen)
        {
            if (!_built || !tapToOpen || (GameHUD.Instance != null && !GameHUD.Instance.GameStarted)) return;
            int role = RoleAt(screen);
            if (role == 0 || _console[role].activeSelf || _clue[role].activeSelf) return;
            var view = View(role);
            // Someone else owns this half (e.g. the Puzzle 1 switchbox zoom).
            if (view.inputLocked) return;
            if (!Physics.Raycast(view.viewCamera.ScreenPointToRay(screen), out var hit, 40f, ~0, QueryTriggerInteraction.Collide)) return;
            var item = hit.collider.GetComponentInParent<RoomInteractable>();
            if (item == null) return;
            if (item.kind == RoomInteractable.Kind.Clue) OpenClue(role, item);
            else if (item.role != role) Hint(wrongConsoleMessage);
            else OpenConsole(role);
        }

        public bool OpenConsole(int role)
        {
            if (!Valid(role)) return false;
            if (flow.AllSolved) { Hint("Vault authorised - the network is yours."); return false; }
            if (flow.ActivePuzzle != 2 && flow.ActivePuzzle != 3) { Hint(offlineMessage); return false; }
            _clue[role].SetActive(false);
            _console[role].SetActive(true);
            _half[role].SetActive(true);
            Lock(role);
            return true;
        }

        public bool OpenClue(int role, RoomInteractable item)
        {
            if (!Valid(role) || item == null) return false;
            _clueTitle[role].text = item.title + "   /   " + (item.role == 1 ? "PLAYER A" : "PLAYER B") + " EVIDENCE";
            _clueBody[role].text = item.body;
            _console[role].SetActive(false);
            _clue[role].SetActive(true);
            _half[role].SetActive(true);
            Lock(role);
            return true;
        }

        public void Close(int role)
        {
            if (!Valid(role)) return;
            _console[role].SetActive(false);
            _clue[role].SetActive(false);
            _half[role].SetActive(false);
            if (_lockedByMe[role] && View(role) != null) View(role).inputLocked = false;
            _lockedByMe[role] = false;
        }

        void Lock(int role)
        {
            var view = View(role);
            if (view == null || _lockedByMe[role]) return;
            view.inputLocked = true;
            _lockedByMe[role] = true;
        }

        int RoleAt(Vector2 screen)
        {
            if (labView != null && labView.viewCamera != null && labView.viewCamera.pixelRect.Contains(screen)) return 1;
            if (serverView != null && serverView.viewCamera != null && serverView.viewCamera.pixelRect.Contains(screen)) return 2;
            return 0;
        }

        // ------------------------------------------------------------------ puzzle events

        void OnBlackoutSolved()
        {
            if (openDoorAfterPuzzle2 && door != null && !door.IsOpen) door.Open();
            Hint(puzzle3Message);
        }

        void OnGhostSolved()
        {
            Close(1);
            Close(2);
            RefreshWorldLabels();
        }

        void RefreshWorldLabels()
        {
            int stage = flow.ActivePuzzle;
            foreach (var item in _items)
            {
                if (item == null || item.kind != RoomInteractable.Kind.Console || item.worldLabel == null) continue;
                string device = item.role == 1 ? (stage == 3 ? "PACKET ROUTER" : "RELAY CABINET")
                                               : (stage == 3 ? "DECODER" : "CHECKSUM LOCK");
                if (flow.AllSolved) { item.worldLabel.text = "VAULT\nAUTHORISED"; item.worldLabel.color = Accent; }
                else if (stage == 2 || stage == 3)
                {
                    item.worldLabel.text = (stage == 2 ? "02 BLACKOUT" : "03 GHOST ADMIN") + "\n" + device + "\nTAP TO OPEN";
                    item.worldLabel.color = new Color(.5f, 1f, .6f);
                }
                else { item.worldLabel.text = "OFFLINE\nNO POWER"; item.worldLabel.color = new Color(1f, .3f, .25f); }
            }
        }

        static void Hint(string text)
        {
            if (GameHUD.Instance != null && !string.IsNullOrEmpty(text)) GameHUD.Instance.ShowHint(text, false);
        }

        // ------------------------------------------------------------------ UI

        void BuildUI()
        {
            var canvasGo = new GameObject("Room Puzzle Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 300;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(GameHUD.ReferenceWidth, GameHUD.ReferenceHeight);
            scaler.matchWidthOrHeight = 1f;
            var hud = FindAnyObjectByType<GameHUD>();
            float bar = hud != null ? hud.barHeight : 84f;

            for (int role = 1; role <= 2; role++)
            {
                var half = R(role == 1 ? "Player A (Lab)" : "Player B (Server Room)", canvasGo.transform, role == 1 ? 0f : .5f, 0f, role == 1 ? .5f : 1f, 1f);
                half.offsetMin = new Vector2(role == 1 ? 0f : 3f, bar);
                half.offsetMax = new Vector2(role == 1 ? -3f : 0f, 0f);
                half.gameObject.SetActive(false);
                _half[role] = half.gameObject;
                BuildConsole(role, half);
                BuildClueReader(role, half);
                BuildRingMarker(role, canvasGo.transform);
            }
        }

        void BuildRingMarker(int role, Transform canvasRoot)
        {
            var sprite = MakeRingSprite();
            var marker = new GameObject("Ring Marker " + role, typeof(RectTransform)).GetComponent<RectTransform>();
            marker.SetParent(canvasRoot, false);
            marker.anchorMin = marker.anchorMax = Vector2.zero;
            marker.sizeDelta = new Vector2(220f, 220f);
            _markerRing[role] = Bg(marker, Color.white);
            _markerRing[role].sprite = sprite;
            var fill = R("Progress", marker, 0, 0, 1, 1);
            _markerFill[role] = Bg(fill, Accent);
            _markerFill[role].sprite = sprite;
            _markerFill[role].type = Image.Type.Filled;
            _markerFill[role].fillMethod = Image.FillMethod.Radial360;
            _markerFill[role].fillOrigin = (int)Image.Origin360.Top;
            _markerLabel[role] = T(marker, "", -1f, -0.75f, 2f, -0.05f, 30, Color.white, TextAnchor.UpperCenter);
            marker.gameObject.SetActive(false);
            _marker[role] = marker;
        }

        static Sprite MakeRingSprite()
        {
            const int n = 256;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            float c = (n - 1) / 2f, outer = n / 2f - 2f, inner = outer - 16f;
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(c, c));
                float a = Mathf.Clamp01(outer - d) * Mathf.Clamp01(d - inner);
                px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
        }

        void BuildConsole(int role, RectTransform half)
        {
            var panel = R("Console", half, 0, 0, 1, 1);
            panel.gameObject.SetActive(false); // children enable later, after their references are set
            _console[role] = panel.gameObject;
            Bg(panel, Ink);
            string who = role == 1 ? "A" : "B";
            T(panel, role == 1 ? "LAB CONSOLE  /  PLAYER A" : "SERVER CONSOLE  /  PLAYER B", .03f, .935f, .60f, .99f, 40, Accent, TextAnchor.MiddleLeft);
            C(panel, "HINT", "hint", "", .62f, .94f, .78f, .99f, role);
            C(panel, "CLOSE", "roomClose", who, .80f, .94f, .97f, .99f, role);

            var p2 = R("02 Blackout Protocol", panel, 0, 0, 1, 1); p2.gameObject.SetActive(false); _p2[role] = p2.gameObject;
            var p3 = R("03 Ghost Administrator", panel, 0, 0, 1, 1); p3.gameObject.SetActive(false); _p3[role] = p3.gameObject;
            if (role == 1) { BuildRelays(p2); BuildRouter(p3); }
            else { BuildChecksum(p2); BuildDecoder(p3); BuildEntryRow(panel); }

            _status[role] = T(panel, "", .03f, .14f, .97f, .21f, 30, Color.white, TextAnchor.MiddleLeft);
            var pad = C(panel, "", role == 1 ? "holdA" : "holdB", "", .03f, .015f, .40f, .125f, role);
            _pad[role] = pad.GetComponent<Image>();
            _padLabel[role] = pad.GetComponentInChildren<Text>();
            T(panel, "Both players hold A and B together for 2 seconds after a correct entry.", .43f, .065f, .97f, .125f, 24, new Color(.68f, .77f, .86f));
            var barRt = R("Hold progress", panel, .43f, .025f, .97f, .05f); Bg(barRt, PanelColor);
            var fill = R("Fill", barRt, 0, 0, 0, 1); _progress[role] = Bg(fill, Accent);
        }

        void BuildRelays(RectTransform p)
        {
            T(p, "02 / BLACKOUT PROTOCOL  -  RELAY CABINET", .03f, .87f, .97f, .925f, 32, Color.white, TextAnchor.MiddleLeft);
            T(p, "Tap a relay to switch it. Read the incident log on the whiteboard; B has the maintenance rule.", .03f, .82f, .97f, .87f, 24, new Color(.68f, .77f, .86f), TextAnchor.MiddleLeft);
            string[] colours = { "BLUE", "BLACK", "RED", "YELLOW", "GREEN", "YELLOW", "BLACK", "BLUE" };
            for (int i = 0; i < 8; i++)
            {
                float x = .03f + (i % 4) * .2375f, y = i < 4 ? .54f : .25f;
                var control = C(p, "", "switch", (i + 1).ToString(), x, y, x + .2225f, y + .26f, 1);
                var visual = control.gameObject.AddComponent<SwitchButtonVisual>();
                visual.puzzle = flow.blackout;
                visual.switchNumber = i + 1;
                visual.label = control.GetComponentInChildren<Text>();
                visual.face = control.GetComponent<Image>();
                visual.colourName = colours[i];
            }
        }

        void BuildChecksum(RectTransform p)
        {
            T(p, "02 / BLACKOUT PROTOCOL  -  CHECKSUM LOCK", .03f, .87f, .97f, .925f, 32, Color.white, TextAnchor.MiddleLeft);
            T(p, "Ask A for the relay states, apply the maintenance revision on your wall, enter 3 digits.", .03f, .82f, .97f, .87f, 24, new Color(.68f, .77f, .86f), TextAnchor.MiddleLeft);
            for (int i = 1; i <= 9; i++)
            {
                int n = i - 1;
                float x = .17f + n % 3 * .225f, y = .64f - n / 3 * .12f;
                C(p, i.ToString(), "key", i.ToString(), x, y, x + .2f, y + .105f, 2);
            }
            C(p, "0", "key", "0", .395f, .28f, .595f, .385f, 2);
        }

        void BuildRouter(RectTransform p)
        {
            T(p, "03 / GHOST ADMINISTRATOR  -  PACKET ROUTER", .03f, .87f, .97f, .925f, 32, Color.white, TextAnchor.MiddleLeft);
            T(p, "Start at S, tap connected nodes to reach V. Network telemetry hangs above the 3D printer.", .03f, .82f, .97f, .87f, 24, new Color(.68f, .77f, .86f), TextAnchor.MiddleLeft);
            var graph = R("Directed graph", p, .03f, .30f, .97f, .81f);
            Bg(graph, PanelColor);
            string[] ids = { "S", "A", "B", "C", "D", "E", "V" };
            Vector2[] pos = { new(.08f, .5f), new(.33f, .8f), new(.33f, .2f), new(.62f, .88f), new(.62f, .5f), new(.62f, .12f), new(.92f, .5f) };
            int[,] edges = { { 0, 1, 4 }, { 0, 2, 3 }, { 1, 3, 3 }, { 1, 4, 1 }, { 2, 4, 2 }, { 2, 5, 2 }, { 3, 6, 5 }, { 4, 6, 1 }, { 5, 6, 4 } };
            for (int i = 0; i < edges.GetLength(0); i++)
            {
                var mid = (pos[edges[i, 0]] + pos[edges[i, 1]]) * .5f;
                T(graph, ids[edges[i, 0]] + ">" + ids[edges[i, 1]] + "\n" + edges[i, 2] + " ms", mid.x - .07f, mid.y - .07f, mid.x + .07f, mid.y + .07f, 22, new Color(.6f, .78f, .88f));
            }
            for (int i = 0; i < ids.Length; i++)
                C(graph, ids[i], "node", ids[i], pos[i].x - .06f, pos[i].y - .085f, pos[i].x + .06f, pos[i].y + .085f, 1, 44);
            _route = T(p, "", .03f, .22f, .70f, .295f, 28, Accent, TextAnchor.MiddleLeft);
            C(p, "CLEAR ROUTE", "routeClear", "", .72f, .225f, .97f, .29f, 1);
        }

        void BuildDecoder(RectTransform p)
        {
            T(p, "03 / GHOST ADMINISTRATOR  -  DECODER", .03f, .87f, .97f, .925f, 32, Color.white, TextAnchor.MiddleLeft);
            _shift = T(p, "", .03f, .815f, .60f, .865f, 28, Accent, TextAnchor.MiddleLeft);
            C(p, "VALIDATE ROUTE", "validate", "", .62f, .815f, .97f, .865f, 2);
            for (int i = 0; i < 5; i++) C(p, "SHIFT " + i, "shift", i.ToString(), .03f + i * .19f, .745f, .2f + i * .19f, .80f, 2, 28);
            T(p, "CAPTURED MESSAGE:   S  T  I  R", .03f, .69f, .97f, .735f, 30, Color.white);
            for (int i = 0; i < 26; i++)
            {
                float x = .03f + i % 7 * .137f, y = .60f - i / 7 * .1f;
                string letter = ((char)('A' + i)).ToString();
                C(p, letter, "key", letter, x, y, x + .125f, y + .085f, 2);
            }
        }

        void BuildEntryRow(RectTransform panel)
        {
            _entry = T(panel, "", .03f, .22f, .52f, .295f, 34, Accent, TextAnchor.MiddleLeft);
            C(panel, "BACK", "back", "", .54f, .225f, .68f, .29f, 2);
            C(panel, "CLEAR", "clear", "", .69f, .225f, .83f, .29f, 2);
            C(panel, "ENTER", "submit", "", .84f, .225f, .97f, .29f, 2);
        }

        void BuildClueReader(int role, RectTransform half)
        {
            var panel = R("Clue", half, 0, 0, 1, 1);
            panel.gameObject.SetActive(false);
            _clue[role] = panel.gameObject;
            Bg(panel, new Color(0f, 0f, 0f, .85f));
            var sheet = R("Sheet", panel, .05f, .05f, .95f, .95f);
            Bg(sheet, Paper);
            _clueTitle[role] = T(sheet, "", .05f, .88f, .95f, .97f, 40, new Color(.1f, .12f, .18f));
            _clueBody[role] = T(sheet, "", .06f, .14f, .94f, .86f, 40, new Color(.08f, .08f, .1f), TextAnchor.UpperLeft);
            C(sheet, "CLOSE", "roomClose", role == 1 ? "A" : "B", .2f, .025f, .8f, .11f, role);
        }

        // ------------------------------------------------------------------ UI helpers

        static RectTransform R(string name, Transform parent, float x1, float y1, float x2, float y2)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(x1, y1);
            rt.anchorMax = new Vector2(x2, y2);
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        static Image Bg(RectTransform rt, Color color)
        {
            var image = rt.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        Text T(Transform parent, string value, float x1, float y1, float x2, float y2, int size, Color color, TextAnchor align = TextAnchor.MiddleCenter)
        {
            var label = R("Text", parent, x1, y1, x2, y2).gameObject.AddComponent<Text>();
            label.font = _font;
            label.text = value;
            label.fontSize = size;
            label.color = color;
            label.alignment = align;
            label.raycastTarget = false;
            label.supportRichText = false;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 12;
            label.resizeTextMaxSize = size;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            return label;
        }

        PuzzleControl C(Transform parent, string label, string action, string value, float x1, float y1, float x2, float y2, int role, int size = 34)
        {
            var rt = R(action + " " + value, parent, x1, y1, x2, y2);
            Bg(rt, ButtonColor);
            var control = rt.gameObject.AddComponent<PuzzleControl>();
            control.action = action;
            control.value = value;
            control.role = role;
            T(rt, label, .04f, .04f, .96f, .96f, size, Color.white);
            _controls.Add(control);
            return control;
        }
    }
}

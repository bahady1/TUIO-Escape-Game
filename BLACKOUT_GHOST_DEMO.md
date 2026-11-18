# Blackout Protocol and Ghost Administrator

This continues the existing manual implementation through Step 3.25. `GetSwitchState()` and the existing Blackout component are retained. The added Step 3.26 visual component is `Assets/EscapeRoom/Puzzle2/SwitchButtonVisual.cs`.

## Launch a local demonstration

1. Open this existing project in Unity **6000.3.26f1** on branch `feature/blackout-ghost-puzzles`.
2. Open **Assets/EscapeRoom/Scenes/BlackoutGhostDemo.unity**, or use **Escape Room > Puzzles 2 + 3 > Open standalone demo**.
3. Press **Play**. Blackout opens immediately in **LOCAL MOUSE DEMO** mode. Use **DEMO 3** at the top to start Ghost independently, or **DEMO 2** to return to Blackout. **RESET** restarts the current puzzle.
4. Click controls with the mouse. Click each player's evidence button to reveal that role's clue; **HIDE EVIDENCE** closes it. Only one evidence card is open at a time. The hint button reveals the three PDF hints progressively.
5. The two authorisation pads are deliberately latched mouse simulations: click A and B to simulate two simultaneous tokens; click again to release. One pad alone cannot solve either puzzle. The screen explicitly labels this simulation. A real table run requires two distinct physical role tokens.

The UI uses proportional anchors and actual screen coordinates. 1920x1080 is its design reference, **not an assumed table resolution**. Use the lab display's actual resolution and check the layout and marker alignment before the demonstration.

## Facilitator solutions

Keep this section and the facilitator PDF away from players. Give each player only their relevant role pages from the player PDF, or enforce the two marked screen zones.

**Puzzle 2:** B's rule is exactly two 1s in a diagnostic signature. Turn **2, 6, 7 and 8 ON**; leave **1, 3, 4 and 5 OFF**. Top row `0100` = 4; bottom row `0111` = 7; XOR `0011` = 3. Enter **473**, then **ENTER**. Release any already held pads/tokens, then hold both authorisation pads for **two uninterrupted seconds**. Releasing either pad resets progress. Correct completion restores auxiliary power and opens Puzzle 3 automatically.

**Puzzle 3:** Start the packet at **S**, then select **B**, **E**, **V**. B clicks **VALIDATE ROUTE**. The directed costs are 3 + 2 + 4 = **9 ms**. Routes through D are rejected; S-A-C-V is too slow at 12 ms. Set decoder shift **4** because 9 mod 5 = 4. Move each letter of **STIR** backward four places to obtain **OPEN**. Enter **OPEN**, click **ENTER**, release any held pads, and authorise with A and B together for two seconds. The victory screen appears; in the full EscapeRoom scene the existing door opens and the HUD receives Win().

Changing relays, route, decoder shift, or an accepted entry revokes the corresponding authorisation. Wrong attempts have feedback and no hidden penalty. Completing a puzzle fires its C# event and UnityEvent once per attempt.

Replay/reset closes only a door this flow opened and clears its own HUD victory state. A door already open before this flow's completion is preserved.

## Full EscapeRoom integration

Open **Assets/EscapeRoom/Scenes/EscapeRoom.unity** and press Play for the original intro and Puzzle 1. `SwitchPuzzle.OnSolved` starts Blackout, then Blackout starts Ghost. No Puzzle 1 source, objects, configuration, or existing event listeners were replaced.

The new root is **Cooperative Puzzles 2 + 3**. Its `CooperativePuzzleFlow` references the **original** Blackout component. The manual `Puzzle2Canvas` and `Switch1Button` remain in the scene; the flow hides the unfinished canvas at runtime so it cannot overlap or double-trigger the completed UI. During Puzzles 2/3, the original TangibleInput GameObject is temporarily deactivated so its OnDisable unsubscribes legacy listeners; the existing TUIO session stays available to the new adapter. Room overlays/navigation are suspended. Stopping Play restores the saved scene.

The original 15-minute HUD timer is paused during these hard-mode stages. This is an explicit demo assumption based on the PDF warning that each hard puzzle may take 6-10 minutes. The standalone demo has no timer. A final timed game needs the team's agreed overall duration.

Do **not** use the older **Build Escape Room Scene** menu for this work: that existing builder regenerates the entire scene. The new **Install or validate** menu is additive and does not rebuild an existing cooperative root/prefab. It checkpoints the main scene before the first install and validates required references. Existing prefab/layout edits are preserved on later calls.

## In-room mode (puzzles and clues inside the rooms)

Run **Escape Room > Puzzles 2 + 3 > Place puzzles + clues in the rooms** once (Unity open, not in Play mode). It saves
`EscapeRoom.unity` with a new root **Puzzles 2 + 3 (in rooms)** and switches the existing flow to in-room mode.
Nothing else in the scene is rebuilt; running it again only replaces its own objects.
**Remove room puzzles (back to full-screen overlay)** undoes it.

| Object | Room / wall | Player |
|---|---|---|
| Console A (relays, then packet router) | Lab, East wall (divider), right of the door | A, left half |
| Console B (checksum, then decoder + passphrase) | Server Room, West wall (divider), left of the door | B, right half |
| Incident Log A-17 (Puzzle 2) | Lab, South wall, on the whiteboard | A |
| Network Telemetry (Puzzle 3) | Lab, North wall, above the 3D printer | A |
| Maintenance Revision 04 (Puzzle 2) | Server Room, North wall, left of the desk | B |
| Security Directive (Puzzle 3) | Server Room, North wall, right of the desk | B |

Play: finish the intro and Puzzle 1 as before. The consoles switch from *OFFLINE* to *TAP TO OPEN*.
**Zoom with the ring** (tangible 4 or 7, like the Puzzle 1 switchbox): rest it on a console or poster in a room view
for 1 s (the circle fills) and it zooms in over that half of the table; no turning. Tap **CLOSE** to leave; move the
ring off the object before it can open it again. The ring may be lifted while zoomed in. Inside a console, **tap the screen** (TUIO touch or
mouse click) to switch relays, pick route nodes and type the solution. Keyboard simulation: **R** puts ring 4 at the
mouse (it follows the mouse, R again removes it). `Tap To Open` on *RoomPuzzleStations*
re-enables opening with a plain tap. Each console has its own **AUTHORISE** pad and **HINT** button. Solving Puzzle 2 opens the door between the
rooms and brings both consoles to Puzzle 3; solving Puzzle 3 shows *YOU ESCAPED* in the bottom bar. In this mode the
15-minute HUD timer keeps running (raise *Game HUD > Time Limit Minutes* if needed). The standalone demo scene keeps
the full-screen layout.

## Connect the laboratory TUX table

The current saved project configuration is **TUIO 1.1 / WebSocket / 10.0.0.20**. The installed SDK selects WebSocket port **3333** for TUIO 1.1 (3343 for TUIO 2.0); its UDP Port field applies to UDP. These are inspected project values, **not verified live lab settings**. No network endpoint was changed.

1. Open the demo scene. Before Play, inspect **TUIO Session - copied existing configuration (Table mode only)**. Ask the lab operator to confirm protocol, transport, server address and display. Change this session only if the lab's verified settings differ. Do not start another TUIO client or Unity instance.
2. Select **Cooperative Puzzles 2 + 3 > CooperativePuzzleInput**. Set **Mode = Table** before Play, or click **INPUT MODE** while in the running mouse demo. The demo enables the cloned session only for Table mode; the full EscapeRoom uses its existing session.
3. Put each physical token on the table one at a time. The Console logs `Physical marker observed: <id>`. Identify the actual IDs, stop Play, and enter them into the adapter's Inspector fields. All bindings deliberately start at **-1 / unbound**. Use two **different** IDs for **Role A Token Id** and **Role B Token Id**. A duplicate or absent marker cannot authorise.
4. Bind **Relay Selector Token Id**, **Packet Token Id**, and **Decoder Token Id** after observing those tokens. A role token may also be that player's interaction token: move it from the puzzle area to its confirmation pad when ready. Distinct A and B role IDs are mandatory; interactions do not require five unique physical objects.
5. In Blackout, move the relay selector onto a switch and dwell about **0.45 seconds** to toggle it once. Move away before toggling that switch again. In Ghost, move the packet onto S then each connected node, dwelling about **0.25 seconds** per node. B can rotate the decoder: each **30 degrees** steps the five-position shift dial. The five on-screen shift hotspots are also available as allowed by the PDF.
6. Use TUIO touch cursors/pointers for keypad, evidence, validation, reset and menu controls. Table mode ignores OS mouse clicks, avoiding duplicate emulated touches. If the laboratory sends only marker objects and no touch cursors/pointers, keypad controls need the lab's cursor feed or an explicitly agreed input mapping; this has not been verified remotely.
7. Check marker position at the screen corners and both pads. Normalised TUIO coordinates scale to actual `Screen.width/height`. **Flip Y** defaults to the standard TUIO top-left to Unity bottom-left conversion; **Flip X** is configurable. Verify axes, display selection, touch alignment, rotation direction and tracker frame cadence. The adapter refuses physical input when no TUIO frame has arrived for **Stream Timeout Seconds** (default one second); adjust this only after verifying the tracker's actual heartbeat.
8. After a correct entry, lift any confirmation tokens that were already held, then place A on the left pad and B on the right pad together for two seconds. The pads turn green while valid and the progress bar fills. Remove one token or interrupt tracking to verify that progress resets.

To recover after selecting Table mode without hardware, stop Play and set Mode back to **LocalMouse** in the Inspector (or change it live in the Inspector). A mouse cannot click the Table-mode menu. Save verified bindings outside Play so they persist.

Two physical tokens verify simultaneous independent input identities, not the number of humans. Two students and private-role discipline remain facilitator requirements. A shared screen cannot guarantee secret evidence; use the supplied printed A/B sheets or physical shielding when strict information separation matters.

## Files and preservation

- Existing logic updated: `Assets/EscapeRoom/Puzzle2/BlackoutProtocol.cs`.
- Step 3.26 visuals: `Assets/EscapeRoom/Puzzle2/SwitchButtonVisual.cs`.
- Ghost rules: `Assets/EscapeRoom/Puzzle3/GhostAdministrator.cs`.
- Input, controls and flow: `Assets/EscapeRoom/Puzzles/`.
- Editor automation: `Assets/EscapeRoom/Puzzles/Editor/CooperativePuzzleBuilder.cs`.
- Generated prefab: `Assets/EscapeRoom/Puzzles/Prefabs/CooperativePuzzles.prefab`.
- Scene integration: `Assets/EscapeRoom/Scenes/EscapeRoom.unity`; independent scene: `BlackoutGhostDemo.unity`.
- Tests: `Assets/EscapeRoom/Tests/EditMode/` and `PlayMode/`.
- Design audit, math/preservation checks, actual Unity logs/XML and screenshots: `Validation/`.

The pre-edit source checkpoint is **Backups/before-blackout-ghost-20261115/project-source.zip**, with original Git status, HEAD and tracked patch alongside it. It includes all pre-existing Assets, Packages and ProjectSettings, including uncommitted work. No reset, push, merge or commit was performed. Existing TextMesh Pro additions and Mobile_RPAsset changes belong to the original working tree and were preserved.

## Repeat validation

With Unity closed, run from the project folder:

```powershell
python Validation/check_design.py
python Validation/check_preservation.py
powershell -ExecutionPolicy Bypass -File Validation/run-unity-validation.ps1 -Action Install
powershell -ExecutionPolicy Bypass -File Validation/run-unity-validation.ps1 -Action EditMode
powershell -ExecutionPolicy Bypass -File Validation/run-unity-validation.ps1 -Action PlayMode -Graphics
```

With Unity already open, use **Window > General > Test Runner**, run the puzzle EditMode and PlayMode suites, and do not start a batch instance. The wrapper explicitly refuses a second Unity process. Screenshot capture requires graphics; `-nographics` skips only the screenshot test. Unity itself manages its caches when it runs; no task script edits Library, Temp or Logs. Batch logs are redirected to Validation.

The installed Unity Test Framework command-line workflow follows the [Unity 6000.3 documentation](https://docs.unity.com/en-us/engine/6000.3/manual/programming-environment/test-framework-introduction/running-tests/run-tests-from-command-line). Actual run results and any remaining limitations are recorded in `Validation/RESULTS.md`.

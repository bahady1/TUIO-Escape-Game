# Escape Room – Tangible Table (nis_ias_tuio, QHD 2560x1440)

## Open / rebuild
- Scene: `Assets/EscapeRoom/Scenes/EscapeRoom.unity` (first scene in Build Settings).
- Menu **Escape Room > Build Escape Room Scene** rebuilds the whole scene from scratch
  (this overwrites manual changes in that scene, so tweak `EscapeRoomBuilder.cs` or save a copy first).
- Game view: add a fixed resolution of **2560 x 1440**. Player builds start at 2560x1440 full screen.

## Layout
| Screen half | Room | Tangible | Starts facing |
|---|---|---|---|
| Left | Main Lab (x -6..0) | object **1** | North wall (work tables, 3D printer) |
| Right | Server Room (x 0..6) | object **2** | East wall (server racks) |

The rooms are separated by a wall at x = 0 with a door in the middle (`Door` component, call `Open()`).
Each room's wall order is North -> East -> South -> West. The camera follows the tangible continuously:
turning it by `Degrees Per Wall` (default 60°) turns the view a full wall (90°), a small turn moves the view
a little. Clockwise turns right. `Follow Smoothing` sets how snappy it is; tick `Snap To Nearest Wall` if the
view should glide onto the nearest wall when the token stops.

## Game flow
1. **Intro** (`Intro Dialogue` object): a short 5-line story ("you and your teammate") types itself out.
   A tap/click anywhere finishes the line, the next tap goes on. The lights fail on line 3, the security
   system announces the lockdown in red on line 4, and after the last line the timer starts.
   Edit the `Story` list in the inspector: each line has a `Speaker`, `Text`, `Alert` (red, shaking box)
   and `Cuts Power`.
2. **Darkness** (`Game` object > `PowerController`): ceiling lamps off, red emergency lights on.
3. **Puzzle 1 - Switchbox** (`Puzzle 1 - Switchbox` object, `SwitchPuzzle`)
   - Clue: the circuit poster on the lab's north wall (above the right table).
   - Switchbox: server room, south wall.
   - Put the ring (tangible **4 or 7**) on the switchbox for **1 s**, then turn it: the zoom grows with the ring and is complete at about **90°**
     (`Turn Degrees`, default 80). Turning part of the way zooms part of the way; lifting the ring early lets it fall back. No marker shows where the switchbox is (`Show Hotspot Circle` turns one on).
   - Tap a knob: middle -> down -> up -> middle ... (0 = up, 1 = middle, 2 = down)
   - Solution `{ 1, 0, 1, 0, 2, 0, 1, 2 }` (top row L->R, then bottom row) -> **lights turn on**.
   - Turn the ring back to leave; the switchbox shrinks as the ring turns and closes at about 90°.
     The ring can be lifted off while zoomed in (a half-finished turn back then springs open again).
   - `OnSolved` event on `SwitchPuzzle` for the next puzzle.

## Background music
`Background Music` object (`BackgroundMusic` + `AudioSource`) loops `Audio/EscapeRoomLoop.wav`, an original
24 s track (80 BPM drone, Am-F-Dm-E pad, heartbeat, ticking clock, plucked arpeggio). It fades in at start,
speeds up slightly in the last minute and fades out when time runs out. Replace the clip or change `Volume`
in the inspector.

## Bottom edge (GameHUD)
`[MAIN LAB ●○○○ NORTH] [15:00] [HINT ...one line...] [SERVER ROOM ○●○○ EAST]`

From any script:
```csharp
GameHUD.Instance.ShowHint("Check the poster printer.");
GameHUD.Instance.ShowNextHint();      // next entry from the Hints list in the inspector
GameHUD.Instance.Win();               // stops timer, shows "YOU ESCAPED!"
GameHUD.Instance.TimeUp += () => { /* lock everything */ };
```
Timer length, hint lines and colours are on the **Game HUD** object.

## Tangibles / TUIO
- **TUIO 1.1 Session** object: TUIO 1.1, WebSocket, 10.0.0.20 (same as SampleScene). Change there if needed.
- **Tangible Input** logs every marker id that is placed on the table, so you can find which ids
  your physical objects have. Set those ids on **Lab View** / **Server View** (`Tangible Id`).
- If the view turns the wrong way, tick `Invert Direction` on the view.

## Testing without the table
- `A` / `D` or mouse wheel over the left half: turn object 1 (lab)
- `Left` / `Right` arrows or mouse wheel over the right half: turn object 2 (server room)
- `H`: show next hint
- Click: tap (intro, knobs). `Space` / `Enter` also advance the intro.
- `R`: place / remove the ring (object 4) at the mouse, it follows the mouse. `E` / `Q`: turn it +90° / -90°.
  While the ring is placed, the mouse wheel turns the ring in small steps (to try the gradual zoom).

## Assets
- `Models/` – the uploaded FBX files (scaled & placed automatically by the builder).
- `Materials/` – generated materials. The poster FBX files point to textures that were not included,
  so posters use `Poster A/B/C` materials: drop your clue image into their **Base Map**.
- `Puzzle1/` – switchbox panel, knob sprites and `Clue1.png` (the circuit clue poster in the lab).

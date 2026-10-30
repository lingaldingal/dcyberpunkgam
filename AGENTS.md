# Repository notes (persistent memory)

Godot 4 project (`dcyberpunkgam`), **C# (.NET / Godot Mono)**, 2D. Validated with Godot 4.7.2-stable Mono + .NET SDK 8.

## Layout (menu + placeholder turn-based level + cutscene)
- `GAMES.md` — game design outline (OUTDATED: references removed `CharacterData`; trust the code)
- `project.godot` — main scene `res://scenes/main_menu.tscn`; `GameManager` C# autoload; `features` includes `C#`, `dotnet`
- `scenes/main_menu.tscn` — title screen: NEW GAME / CONTINUE / SCENE TEST / RESOLUTION / QUIT
- `scenes/level_01.tscn` — turn-based arena (28×16 grid board, 4 party + 4 enemy tokens, top-right HUD panel w/ END TURN / UNDO / BACK, bottom-left actions menu with END TURN)
- `scenes/scene_test.tscn` — basic 2D cutscene (two placeholder characters L/R, scrolling cheese dialogue, speaker highlight)
- `scripts/GameManager.cs` — autoload: scene flow (StartGame / StartSceneTest / QuitToMenu); exposes `Instance`
- `scripts/MainMenu.cs` — menu buttons incl. `OnResolutionPressed()` (cycles window size); `GetResolution()` test hook
- `scripts/Level01.cs` (turn-based iso arena logic), `scripts/SceneTest.cs`
- `scripts/IsoGrid.cs` — Node2D in the skewed `Iso` layer: draws floor diamonds, wall extrusion, move/shoot highlights + HP readouts in LOGICAL board pixels (the skew does the iso)
- `scripts/TokenNode.cs` — Node2D per combatant under `Board/Iso`, draws a filled diamond at its cell corner; name rendered by Level01 as an unskewed Label overlay
- `scripts/Character.cs` — combatant data (Name, Color, Agility, `IsEnemy`, `MovementRange` = 5 + Agility/2 floored, `Constitution`/`HitPoints`/`MaxHitPoints`, `Equipment`, `Items` inventory, `UseItem`/`GetItemCount`/`Heal`)
- `scripts/Weapon.cs` — `Weapon` (Name, Range, Damage); built-ins `Pistol` (4/2) + `Rifle` (6/3)
- `scripts/Item.cs` — `Item` (Name, HealValue); built-in `BioStim` (heal 6)
- `scripts/Equipment.cs` — `Equipment` (Head, Body, Weapon) + `ToText()`
- `scripts/TurnSystem.cs` — round-based initiative turn order (see below)
- `dcyberpunkgam.csproj` / `dcyberpunkgam.sln` — .NET project (Godot.NET.Sdk 4.7.2, net8.0)
- `run.sh` — builds the C# then launches the game with the Mono Godot binary (the command the user runs to play)
- `tests/flow_test.gd` — headless SceneTree functional test (menu → level → 8-combatant initiative → rounds → movement range → BFS reachability → **camera panning + clamping** → **mouse click-to-move** (incl. click **behind** an obstacle/character via BFS detour) → once-per-turn + UNDO → enemy auto-move + auto turn-end → back). ~60 checks.
- `tests/cutscene_test.gd` — headless functional test (menu → SCENE TEST → name-instant/body-typewriter reveal, speaker alternation, highlight, click-to-advance, back). ~19 checks.
- `tests/resolution_test.gd` — headless test: menu RESOLUTION button cycles 1280x720 → 1600x900 → 1920x1080 → back
- `tests/combat_test.gd` — headless combat test (equipment + random weapon assignment, **circle-based shoot range (any direction, dx²+dy² ≤ range²) + LOS blocking by walls AND characters**, shooting-circle overlay, **weapon-based range/damage: pistol 4/2, rifle 6/3**, once-per-turn, WAIT, **enemy AI (move into shooting circle / hold + shoot)**, **BioStim heal**, death removes token + skips turns, **END TURN button in the actions menu**, action-menu screen clamping). ~100 checks.
- `readme.me` — pre-existing file, untouched

## Cutscene (scene_test)
`SceneTest` (C#) is the basic 2D dialogue cutscene reached from the menu's
SCENE TEST button:
- Two placeholder characters (Vex left = red, Kaito right = cyan) as
  ColorRect "portraits" (glow + frame + body + head + name label), no textures.
- A bottom `PanelContainer` holds a `RichTextLabel` that reveals each line
  character-by-character (`RevealCps` = 60, top of the 35–60 CPS reading
  comfort range) and auto-scrolls. The speaker is highlighted in the text via
  BBCode (`[b][color=#...]Speaker[/color][/b]: ...`); `BbcodeEnabled = true`
  is set in `_Ready` so the tags render as bold/colored text (without it the
  raw `[b][color]` tags show).
- **Name instant, body types**: `ShowLine()` sets `_charsShown = 0` and the
  speaker name appears in full immediately; only the line body types itself
  out (see `UpdateText()`, which builds `SpeakerBbCode(line) + body[..shown]`).
- The active speaker's portrait stays at full brightness with a pulsing glow;
  the inactive one dims to alpha 0.35 and its glow hides. Lines alternate
  Vex/Kaito.
- **Manual advance (single press)**: lines do NOT auto-advance. A left click
  anywhere (`_GuiInput`) or Space/Enter (`_UnhandledInput`, `ui_accept`/
  `ui_select`) calls `Advance()`, which moves straight to the next line — it
  does NOT require a separate "finish reveal" press (the reveal completes
  implicitly). Answering the LAST line ends the scene → `GameManager.QuitToMenu()`.
  A `Hint` label under the text says "Click / Space / Enter to continue".
  `BACK TO MENU` still works via `GameManager.QuitToMenu()`.
- **Clicks must reach the root `_GuiInput`.** The `Background` ColorRect,
  `DialogueBox` PanelContainer, and `Text` RichTextLabel all get
  `MouseFilter.Ignore` in `_Ready` (and the root `Control` has
  `mouse_filter = 2`/Ignore in the .tscn) so a click anywhere bubbles up to
  the root's `gui_input`. If any of them is left at the default `Stop`, it
  swallows the click and dialogue stops advancing. The BACK button keeps its
  own `Pressed` handler (it's top-right and doesn't overlap dialogue clicks).
- Test hooks: `GetLineIndex()`, `GetSpeakerName()`, `GetLineText()`,
  `GetLineCount()`, `GetTextPlain()` (logical text, BBCode-free), `SkipReveal()`,
  `SetLine(int)`, `Advance()`. `cutscene_test.gd` uses these (headless, so no
  render pass) and simulates a click with `Input.parse_input_event` (separate
  press/release events). NOTE: in `-s` headless mode the root Control is sized
  64×64, so before a click test the scene's `.size` must be set back to
  1280×720 (or the text rect lands off-screen and the root's `gui_input`
  hit-test misses).

## Turn system (initiative) — 8 combatants
`TurnSystem` (C#, plain class — not a Godot node) drives turn order in Level01:
- At the start of the encounter (`StartEncounter()`), **every combatant**
  (4 party + 4 enemies) rolls a **d5** (1–5); **total = Agility + roll**.
- Turn order is **descending total**; ties break by higher Agility, then roster order.
- The order is rolled **once** and persists for the rest of the encounter.
  Everyone acts once per round; END TURN advances, and completing the round
  starts a new one with the SAME order (no re-roll).
- Party: Vex 12, Kaito 10, Rook 15, Mira 10. Enemies: Brute 16, Slick 14,
  Gutter 12, Wisp 9 (see `Level01.Enemies`).
- `Level01` builds it in `_Ready` from `Party` + `Enemies` and shows
  `ROUND n — TURN: <name>` + per-character `Agility+roll` in the HUD.
  Test hooks on Level01: `GetRound()`, `GetCurrentName()`, `GetInitiativeTotals()`,
  `GetInitiativeOrderNames()`, `SetInitiativeRolls(int[])` (8 values, party then
  enemies, forces deterministic d5 results), `GetMovementRange()`,
  `IsReachable(int x, int y)`, `GetTokenCell(string) -> Vector2i`,
  `ForceCurrent(string)`. `TurnSystem.ForceCurrent(int index)` sets the pointer
  to that POSITION in the sorted `_order` array (NOT a roster index).
- `flow_test.gd` uses `SetInitiativeRolls` to make order deterministic.

## Movement + pathfinding (Level01)
- Movement range per turn = **5 + Agility/2 (floored)** (`Character.MovementRange`).
- Reachable cells are computed with **BFS** over the walkable grid, blocked by
  obstacles and other tokens; `IsReachable(x, y)` returns whether the *current*
  combatant can reach that cell within range. (Replaced an earlier
  `GetReachableCells()` that returned `Vector2I[]` — the array didn't export
  cleanly to GDScript, so it's a `bool` now.)
- **Character movement is MOUSE-ONLY now.** Clicking a reachable tile jumps
  there (BFS) via `OnBoardGuiInput` → `TryBoardClick` → `TryMoveToward`.
  `TryBoardClick` resolves the board-local click with a single **inverse iso
  projection** (`gx=floor((X+2Y)/T)`, `gy=floor((-X+2Y)/T)` — the exact
  inverse of the cell-center forward projection) to the grid cell,
  so clicks land in the cell the player sees even at cell edges. Clicking a
  reachable tile (even **behind** an obstacle/character, as long as a BFS
  detour stays within movement range) moves there. The first move sets
  `_movedThisTurn`; subsequent moves that turn are ignored. **UNDO** (button
  or `UndoMove()`) restores the turn-start cell and re-arms the move — allowed
  once, before END TURN. The reachable set is frozen to the turn-start
  position after a move (not recomputed from the new position) so the full
  range stays valid for the rest of the turn. (WASD no longer moves tokens —
  it now pans the camera, see below. The old `_Input` handler was removed.)
  Tests emit clicks at `level.IsoCellCenter(Vector2i(x, y))` — the forward
  cell-center projection — so headless click tests resolve to the exact cell.
- **Isometric projection.** `level_01` is 2:1 iso. The `Board` Control holds
  an `Iso` Node2D child with a skew `Transform2D(X=(.5,.25), Y=(-.5,.25))`.
  Everything that must look skewed (floor, walls, highlights, tokens, range
  overlay) is drawn inside `Iso` in **LOGICAL board pixels** (`x*TILE,
  y*TILE`); the skew maps logical `(x,y)` → screen `((x-y)/2*TILE,
  (x+y)/4*TILE)`. Name labels are unskewed `Label` Controls under `Board`,
  repositioned via `IsoCellCenter` each frame. Inverse: `gx=(X+2Y)/T,
  gy=(-X+2Y)/T` (`CellFromIso`). The iso board bounding box (`_isoBounds`,
  board space) is `x:[-H*T/2, W*T/2]`, `y:[0,(W+H)*T/4]` — i.e. width
  `(W+H)*T/2`, height `(W+H)*T/4`. **NOTE the negative left edge**: cell
  (0,H) projects to board x = -H*T/2, so the diamond's left vertex is OFF
  the origin. The camera clamps to THIS box (not a [0,size] box).
- **Camera panning (WASD).** Levels can be far larger than the window
  (up to 2000×2000 grids), so the board is offset by `-camera` and **WASD /
  arrow keys pan the viewport** one tile per frame (`UpdateCamera` in
  `_Process`, `CameraPanSpeed` = one tile/frame). `_cameraPos` is the board
  pixel at the viewport's top-left; `ApplyCamera()` clamps it to
  `[bounds.Min, bounds.Max - view]` per axis (a board smaller than the window
  gets centered, not pinned — the min clamp uses the diamond's NEGATIVE left
  edge so its left third isn't pinned off-screen). The camera **starts
  centered** on the iso diamond (diamond center = board point
  `((W-H)*T/4, (W+H)*T/8)` minus the viewport half-size; done once in the
  first `_Process` where `Size` is valid; in headless test
  mode `Size` is (0,0) so it stays at the origin — flow_test's pan assertions
  rely on that). `Board.Position = -camera`; the board's children keep
  board-local coords; only the board node moves. The info panel is
  constrained (800px + autowrap) so long text never overflows.
- **Enemies act** (move first, then shoot, see the Combat section's "Enemy
  AI") after a short delay (12 frames) in `_Process`, then **automatically
  end their turn**. Guarded by `_enemyActed` so the turn ends exactly once.
  `OnBoardGuiInput` bails when `_turns.Current.IsEnemy` (no mouse control of
  enemies).
- Map is 28 cols × 16 rows, 71 obstacles (rows y=0..2 are a fully clear band;
  the x=0 column is a clear vertical lane y=0..15 — both relied on by the
  shoot tests). `StartCells`: party clustered top-left at (1,1)/(2,1)/(1,2)/
  (2,2); enemies top-right at (26,1)/(25,1)/(26,2)/(25,2).
- Scene has a third action button: **UNDO** (between END TURN and BACK).
  Test hooks: `HasMovedThisTurn() -> bool`, `UndoMove()`, `ForceEnemyMove()`
  (acts for the active enemy now: move + end turn), `GetCameraPos()`,
  `PanCameraBy(Vector2)`, `SetBoardPixelSize(float w, float h)` (treats the
  board as that pixel size — replaces `_isoBounds` with a plain `[0,w]x[0,h]`
  box so the test exercises pan/clamp on a large board),
  `SetPanKey(Key)` (simulates a held WASD key for the next `_Process`,
  `Key.None` clears — used to test panning headless where `Input.IsKeyPressed`
  is never true).

## Combat (HP, Weapons, Shoot, Wait, Items, Actions Menu)
- **Constitution + HP**: every combatant has `Constitution` (7–13) and
  `HitPoints = 5 + Constitution` (`MaxHitPoints`; see `Character.cs`). Party:
  Vex 11, Kaito 9, Rook 12, Mira 8. Enemies: Gutter 10, Slick 13, Brute 12,
  Wisp 7. HP readout floats above each token and updates on damage.
- **Per-turn budget** (one action + one move, latched independently): each
  turn a party member may perform ONE action (SHOOT / WAIT / **BIOSTIM**) AND
  ONE move — each latches itself (`_actionUsed` for the action,
  `_movedThisTurn` for the move) but neither blocks the other. So shoot-then-
  move, move-then-shoot, or just one of them all work. The **actions menu**
  (bottom-left panel: name on top, an equipment readout line, SHOOT / MOVE /
  WAIT / BIOSTIM buttons, and an **END TURN** button on the same row; clamped
  on screen via `ClampActionMenu()`) shows the buttons as disabled once their
  path is spent. Mode switching is free. UNDO re-arms both paths.
- **HUD layout**: the debug/info panel (round/turn, per-char initiative,
  move-range readout, END TURN / UNDO / BACK buttons) is a `PanelContainer`
  pinned to the **top-right** corner (`Center/Panel` — `Center` is a plain
  full-rect `Control`, NOT a CenterContainer: a CenterContainer re-centers
  children and silently overrides the panel's anchors). Small font, ~408px
  wide × 138px tall. The big center title label was removed.
- **Weapons** (`Weapon.cs`): the equipped weapon drives the shoot mechanic —
  the shoot range is a **CIRCLE**: any cell with
  `dx*dx + dy*dy <= Weapon.Range^2` (distance 1..range, **any direction**)
  with a clear line of sight can be hit, and a hit deals `Weapon.Damage`.
  A shoot may be used **before or after the move**; a shot always fires from
  the shooter's **CURRENT position** — moving recomputes the shoot-target set
  from the new cell (`MovePartyTo` for the player's click-to-move and
  `MoveTokenTo` for enemy/undo moves both call `ComputeShootTargets()` +
  `RefreshHighlights()`).
  `Weapon.Pistol` (range 4, dmg 2) and `Weapon.Rifle` (range 6, dmg 3) are the
  two built-in weapons. `IsShootCell` / `ComputeShootTargets` / `TryShootAt`
  are all weapon-aware (range + damage come from the active combatant's
  `Equipment.Weapon`). In **shoot mode** a soft range overlay
  (`_rangeCircle`, in the skewed `Iso` layer, radius = range tiles, centered
  on the cell's LOGICAL center) is drawn around the active PARTY shooter so
  the player can see the range (hidden for enemy turns / other modes). In
  logical space it is a 45° square (corners at distance r); under the iso
  skew it projects to an on-screen diamond with a SQUARE bounding box of side
  `2*range*TILE`. Test hook `GetRangeCircle() -> float[4]` returns the
  **board-space** `[x, y, w, h]` (center = iso cell center, w=h=2*range*TILE)
  or `[-1,-1,-1,-1]` when hidden.
- **Equipment** (`Equipment.cs`): head + body + weapon. Basic gear for now —
  Ballistic Cap (head) + Weave Vest (body) + a **random** Pistol or Rifle for
  ALL 8 combatants (assigned in `AssignStartingGear()`). `Equipment.ToText()`
  → "Rifle · Ballistic Cap · Weave Vest" (shown in the actions menu + HUD).
- **Items** (`Item.cs`): consumables with a `HealValue`. `Item.BioStim`
  (heal 6) — party members start with **2** (enemies 0). Using one heals up to
  `MaxHitPoints` and consumes the action. `Character.UseItem` /
  `GetItemCount`.
- **Line of sight**: a shot is only valid if the segment shooter→target is
  clear — `HasLineOfSight(from, to, range)` walks the intermediate cells with
  an integer **Bresenham** (exact 45° diagonals are walked one-by-one; other
  slopes use the standard two-error algorithm) and fails if any intermediate
  cell is a wall **or occupied by another character** — you cannot shoot
  through obstacles or through other players/enemies. The target's own cell is
  the endpoint, not an obstacle. `ComputeShootTargets` iterates EVERY cell in
  the shooting circle from the CURRENT position and keeps the ones with clear
  line of sight (a weapon hits at any distance 1..range inside the circle).
- **Enemy AI** (`EnemyAct()`), same rules as the player: each enemy turn it
  spends its one **move** via `EnemyMoveToward`, walking the BFS path toward
  the **nearest** living party member, but it **stops as soon as the target is
  inside its own weapon's shooting circle** (it moves INTO shooting range
  rather than right up next to the player; if already inside the circle it
  holds its position and doesn't move), then spends its one **action** via
  `EnemyShoot` — from the new position it fires at that target if it is inside
  its shooting circle with a clear line of sight. The move and shot latch
  `_movedThisTurn` / `_actionUsed` exactly like the player's actions, so the
  actions menu reflects the spent budgets. Auto-end turn (guarded by
  `_enemyActed`). `FindNearestPartyMember` picks the target.
- **Death**: HP ≤ 0 → `IsDead`; the token is hidden from the map, no longer
  blocks movement (`IsOccupiedBy` skips the dead), and `TurnSystem.EndTurn`
  skips dead combatants when advancing (round wraps when everyone is dead).
- Test hooks: `GetHitPoints(string)`, `IsAlive(string)`, `IsShootTarget(int,
  int)`, `SetActionMode(ActionMode)`, `SetTokenCell(string, int, int)`,
  `GetActionMenuPos()`, `ForceCurrent(string)`, `GetWeaponName(string)`,
  `GetWeaponRange(string)`, `SetWeapon(string, bool isRifle)`,
  `GetItemCount(string)`, `UseItem(string)`, `GetEquipment(string)`.
  NOTE: set tokens' cells BEFORE `ForceCurrent` — shoot targets/move range are
  computed from the active token's position in `SetActive()`. Tests pin
  weapons with `SetWeapon` (and `SetInitiativeRolls`) for determinism.

## Resolution selector (MainMenu)
`MainMenu` has a **RESOLUTION** button that cycles the window through three
supported sizes — **1280×720 → 1600×900 → 1920×1080 → back** — via
`DisplayServer.WindowSetSize` (the project's default window is 1280×720, set in
`project.godot`). The button label shows the current size. `GetResolution() ->
Vector2I` is the test hook. NOTE: `DisplayServer.window_get_size()` returns
(0,0) in `--headless`/`-s` test mode (window size isn't tracked headless), so
`resolution_test.gd` asserts the menu's `GetResolution()` state rather than the
window size.

## Build (C#) + running the game
**Preferred: just `./run.sh`** — it builds the C# (`dotnet build`) then launches the
game with the Mono Godot binary. `./run.sh --no-build` skips the build when code is
unchanged. This is the single command the user runs to play.

`dotnet` (SDK 8) is on PATH (`/usr/bin/dotnet`). Build before running:
```
export DOTNET_CLI_TELEMETRY_OPTOUT=1
dotnet build dcyberpunkgam.csproj
```
Godot's SDK redirects output to `.godot/mono/temp/bin/Debug/` (gitignored), NOT `bin/`.
Run the game with the Mono build (loads `.cs`):
`/home/bigal/Applications/GodotMono/Godot_v4.7.2-stable_mono_linux_x86_64/Godot_v4.7.2-stable_mono_linux.x86_64 --path .`

**Gotcha — "buttons don't respond" = C# not built.** The Mono binary loads `.cs`
scripts from the built assembly in `.godot/mono/temp/bin/Debug/dcyberpunkgam.dll`.
If that DLL is missing/stale, every `GetNode<...>` and `Pressed +=` binding is a
no-op and buttons silently do nothing (autoload `GameManager` also fails to
instantiate). Always `dotnet build` before running. The bare `godot` on PATH
(`/usr/local/bin/godot`) is the non-Mono build and errors
"Failed to instantiate an autoload, script ... does not inherit from 'Node'".

## Validation (headless + rendered)
TWO godot builds exist here — you MUST use the **Mono** one for C# (the plain
`godot` on PATH, `/usr/local/bin/godot`, is the non-Mono build and fails to load
`.cs` scripts with "No loader found for resource"). The functional tests
(`flow_test.gd`, `cutscene_test.gd`) call C# methods, so run them with the Mono
binary. Build the C# first.
```
GODOT=/home/bigal/Applications/GodotMono/Godot_v4.7.2-stable_mono_linux_x86_64/Godot_v4.7.2-stable_mono_linux.x86_64
$GODOT --headless --path . --import                    # clean import check
$GODOT --headless --path . --quit-after 120            # smoke run (main scene = menu)
$GODOT --headless --path . -s res://tests/flow_test.gd      # functional flow test (60 checks)
$GODOT --headless --path . -s res://tests/cutscene_test.gd  # cutscene test (19 checks)
$GODOT --headless --path . -s res://tests/combat_test.gd    # combat: weapons/items/enemy AI/shoot/death/actions menu (100 checks)
```
Headless alone is NOT enough for UI: it never renders, so off-screen controls
are invisible to it. There's a real Xorg at `DISPLAY=:0.0` (no Xvfb/xdotool here).
To screenshot a scene, run a tiny `SceneTree` script with the **Mono** binary
(`$GODOT --path . -s res://tests/_shot.gd`, NON-headless) that instantiates the
scene, `await process_frame` a few times, then
`root.get_window().get_texture().get_image().save_png(...)`. For a real click,
build an `InputEventMouseButton` (left, pressed) and `Input.parse_input_event(p)`
at the button's `get_global_rect().get_center()`, then the release event. Check
the PNG has more than a handful of unique colors (flat = black screen); analyze
with PIL. `tests/_iso_shot.gd` (committed) is a ready-made example: it loads
`level_01`, switches to shoot mode (shows the range overlay) and saves
`tests/_iso_shot.png`.

## Menu layout gotcha (the black-screen bug)
The original menu put `anchors_preset = 8` (center) on the Title/Buttons nodes
while ALSO setting explicit `anchor_right = 1.0` / `anchor_bottom = 1.0`.
At runtime only the `anchor_*` values matter (preset is editor metadata), so
the nodes anchored to the bottom-right corner and rendered off-screen —
only the full-rect Background ColorRect showed, i.e. a flat black screen.
Fix: menu/stage content lives in a full-rect `CenterContainer` + `VBoxContainer`;
never mix `anchors_preset` with explicit `anchor_*` values that disagree.

## Gotchas
- **Iso token/label paths**: tokens are `Node2D` `TokenNode`s under
  `Board/Iso` (names `P0`..`P3`, `E0`..`E3`); tests must look them up at
  `Board/Iso/P0`, not `Board/P0` (they're no longer `Control`s). Token
  `position` is the LOGICAL cell corner (`x*TILE, y*TILE`); the iso skew is on
  the parent `Iso` node, so `get_global_position()` already returns the skewed
  screen/board-space point.
- **IsoGrid degenerate polygons**: `Diamond(x,y)` returns corners in the order
  `[right, bottom, left, top]`. Wall side-faces must use ADJACENT corners
  `(right,bottom)` and `(bottom,left)`; using opposite corners makes a
  degenerate line that spams `Invalid polygon data, triangulation failed`
  (16k+ errors) and drags headless tests down to a crawl.
- **Headless window `Size` is (0,0)**: in `--headless`/`-s` mode the viewport
  has no size, so the one-time centered-camera init is skipped and the camera
  stays at the origin (flow_test's pan/clamp assertions depend on that).
- Fresh clones have no `.godot/` cache. A direct `godot --path .` run does NOT
  auto-import textures (`.godot/imported` stays empty), so any scene referencing
  imported assets fails to load on a fresh clone until `--import` is run once.
  Keep scenes texture-free (ColorRect/Label/Button) to avoid this entirely.
- `project.godot` input actions: object types must be UNQUOTED identifiers:
  `Object(InputEventKey, ...)`, and values end `null)` not `null")`.
- Don't commit `.godot/` cache (gitignored). `.import` and `.uid` files ARE committed.
- In `-s res://script.gd` mode the main scene is NOT auto-loaded; instantiate
  it manually in the test. `:=` inference fails on variables initialized `null`.
- C# `partial class` is REQUIRED for Godot C# scripts (e.g. `public partial class Level01 : Control`).
- Board click handling: the `Board` Control uses `MouseFilter.Stop` + the
  `GuiInput` signal (coords are board-local). Every decorative node on top of
  the board (UI panel, labels, tiles, tokens, highlights) must be
  `MouseFilter.Ignore` or it swallows the click. The root Window (a Control in
  Godot 4) also consumes in-window clicks, so `_UnhandledInput` is NOT a
  reliable click path; use `GuiInput` on the board.
- `Walkable` is row-major `int[,]` indexed `[y, x]` (y = row, x = column).
- **Headless `RichTextLabel` doesn't parse BBCode**: in `--headless` / `-s`
  test mode there's no render pass, so `GetParsedText()` returns the RAW BBCode
  (`[b][color=...]...`) instead of plain text. For tests, expose a logical
  `GetTextPlain()` built from state rather than relying on `GetParsedText()`.
- **Mono Godot binary (this container)**: the Mono build that loads `.cs`
  scripts is `/home/bigal/Applications/GodotMono/Godot_v4.7.2-stable_mono_linux_x86_64/Godot_v4.7.2-stable_mono_linux.x86_64`.
  The bare `godot` on PATH (`/usr/local/bin/godot`) is the GDScript build and
  fails with "No loader found for resource ... .cs". `dotnet` is on PATH
  (`/usr/bin/dotnet`), so plain `dotnet build dcyberpunkgam.csproj` works.
  The simplest path: just `./run.sh`.
- **Rendering verification**: see the "Validation" section — real Xorg at
  `DISPLAY=:0.0`, screenshot via a NON-headless `SceneTree` script with the
  Mono binary, analyze the PNG with PIL.

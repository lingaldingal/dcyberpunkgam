# Repository notes (persistent memory)

Godot 4 project (`dcyberpunkgam`), **C# (.NET / Godot Mono)**, 2D. Validated with Godot 4.7.2-stable Mono + .NET SDK 8.

## Layout (menu + placeholder turn-based level)
- `GAMES.md` — game design outline (OUTDATED: references removed `CharacterData`; trust the code)
- `project.godot` — main scene `res://scenes/main_menu.tscn`; `GameManager` C# autoload; `features` includes `C#`, `dotnet`
- `scenes/main_menu.tscn` — title screen: NEW GAME / CONTINUE / QUIT
- `scenes/level_01.tscn` — turn-based arena (20×12 grid board, 4 party + 4 enemy tokens, HUD, END TURN / BACK)
- `scripts/GameManager.cs` — autoload: scene flow only (StartGame / QuitToMenu); exposes `Instance`
- `scripts/MainMenu.cs`, `scripts/Level01.cs`
- `scripts/Character.cs` — combatant data (Name, Color, Agility, `IsEnemy`, `MovementRange` = 5 + Agility/2 floored)
- `scripts/TurnSystem.cs` — round-based initiative turn order (see below)
- `dcyberpunkgam.csproj` / `dcyberpunkgam.sln` — .NET project (Godot.NET.Sdk 4.7.2, net8.0)
- `tests/flow_test.gd` — headless SceneTree functional test (menu → level → 8-combatant initiative → rounds → movement range → BFS reachability → WASD/click movement → once-per-turn + UNDO → enemy auto-move + auto turn-end → back). 36 checks.
- `readme.me` — pre-existing file, untouched

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
- **Party members move once per turn.** WASD nudges one tile; clicking a
  reachable tile jumps there (BFS). The first move sets `_movedThisTurn`;
  subsequent moves that turn are ignored. **UNDO** (button or `UndoMove()`)
  restores the turn-start cell and re-arms the move — allowed once, before
  END TURN. The reachable set is frozen to the turn-start position after a
  move (not recomputed from the new position) so the full range stays valid
  for the rest of the turn.
- **Enemies auto-move** one tile in a random walkable direction after a short
  delay (12 frames) in `_Process`, then **automatically end their turn**
  (enemies have no other actions yet). Guarded by `_enemyActed` so the turn
  ends exactly once. The player cannot control enemies: `_Input` and
  `OnBoardGuiInput` both bail when `_turns.Current.IsEnemy`.
- Map is 20 cols × 12 rows; obstacles + token positions mean some short
  straight-line targets need a detour (e.g. Vex at (1,1) can't reach (1,11)
  directly because Rook at (1,2) blocks the column — the left column detour is
  12 steps, just over Vex's range 11).
- Scene has a third action button: **UNDO** (between END TURN and BACK).
  Test hooks: `HasMovedThisTurn() -> bool`, `UndoMove()`, `ForceEnemyMove()`
  (acts for the active enemy now: move + end turn).

## Build (C#)
`dotnet` (SDK 8) is on PATH (`/usr/bin/dotnet`). Build before running:
```
export DOTNET_CLI_TELEMETRY_OPTOUT=1
dotnet build dcyberpunkgam.csproj
```
Godot's SDK redirects output to `.godot/mono/temp/bin/Debug/` (gitignored), NOT `bin/`.

## Validation (headless + rendered)
TWO godot builds exist here — you MUST use the **Mono** one for C# (the plain
`godot` on PATH, `/usr/local/bin/godot`, is the non-Mono build and fails to load
`.cs` scripts with "No loader found for resource"):
```
GODOT=/home/bigal/Applications/GodotMono/Godot_v4.7.2-stable_mono_linux_x86_64/Godot_v4.7.2-stable_mono_linux.x86_64
```
```
$GODOT --headless --path . --import                    # clean import check
$GODOT --headless --path . --quit-after 120            # smoke run (main scene = menu)
$GODOT --headless --path . -s res://tests/flow_test.gd # functional flow test
```
Headless alone is NOT enough for UI: it never renders, so off-screen controls
are invisible to it. To verify a menu actually displays, run on a virtual
display and screenshot:
```
Xvfb :99 -screen 0 1280x720x24 &
DISPLAY=:99 <bin> --path . &            # then: import -window root out.png
```
Check the PNG has more than a handful of unique colors (flat = black screen).
For real click testing use `xdotool windowfocus <id>` then `mousemove`/`click`
(no WM on Xvfb, so `windowactivate` fails — plain focus works).

## Menu layout gotcha (the black-screen bug)
The original menu put `anchors_preset = 8` (center) on the Title/Buttons nodes
while ALSO setting explicit `anchor_right = 1.0` / `anchor_bottom = 1.0`.
At runtime only the `anchor_*` values matter (preset is editor metadata), so
the nodes anchored to the bottom-right corner and rendered off-screen —
only the full-rect Background ColorRect showed, i.e. a flat black screen.
Fix: menu/stage content lives in a full-rect `CenterContainer` + `VBoxContainer`;
never mix `anchors_preset` with explicit `anchor_*` values that disagree.

## Gotchas
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

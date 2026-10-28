# Repository notes (persistent memory)

Godot 4 project (`dcyberpunkgam`), **C# (.NET / Godot Mono)**, 2D. Validated with Godot 4.7.2-stable Mono + .NET SDK 8.

## Layout (menu + placeholder turn-based level + cutscene)
- `GAMES.md` — game design outline (OUTDATED: references removed `CharacterData`; trust the code)
- `project.godot` — main scene `res://scenes/main_menu.tscn`; `GameManager` C# autoload; `features` includes `C#`, `dotnet`
- `scenes/main_menu.tscn` — title screen: NEW GAME / CONTINUE / SCENE TEST / QUIT
- `scenes/level_01.tscn` — placeholder turn-based arena (grid board, 4 party tokens, HUD, END TURN / BACK)
- `scenes/scene_test.tscn` — basic 2D cutscene (two placeholder characters L/R, scrolling cheese dialogue, speaker highlight)
- `scripts/GameManager.cs` — autoload: scene flow (StartGame / StartSceneTest / QuitToMenu); exposes `Instance`
- `scripts/MainMenu.cs`, `scripts/Level01.cs`, `scripts/SceneTest.cs`
- `scripts/Character.cs` — combatant data (Name, Color, Agility)
- `scripts/TurnSystem.cs` — round-based initiative turn order (see below)
- `dcyberpunkgam.csproj` / `dcyberpunkgam.sln` — .NET project (Godot.NET.Sdk 4.7.2, net8.0)
- `tests/flow_test.gd` — headless SceneTree functional test (menu → level → initiative order → rounds → movement → back)
- `tests/cutscene_test.gd` — headless functional test (menu → SCENE TEST → reveal, speaker alternation, highlight, back)
- `readme.me` — pre-existing file, untouched

## Cutscene (scene_test)
`SceneTest` (C#) is the basic 2D dialogue cutscene reached from the menu's
SCENE TEST button:
- Two placeholder characters (Vex left = red, Kaito right = cyan) as
  ColorRect "portraits" (glow + frame + body + head + name label), no textures.
- A bottom `PanelContainer` holds a `RichTextLabel` that reveals each line
  character-by-character (`RevealCps`) and auto-scrolls; the speaker is
  highlighted in the text via BBCode (`[b][color=#...]Speaker[/color][/b]: ...`).
- The active speaker's portrait stays at full brightness with a pulsing glow;
  the inactive one dims to alpha 0.35 and its glow hides. Lines alternate
  Vex/Kaito and loop. `BACK TO MENU` returns via `GameManager.QuitToMenu()`.
- Test hooks: `GetLineIndex()`, `GetSpeakerName()`, `GetLineText()`,
  `GetLineCount()`, `GetTextPlain()` (logical text, BBCode-free), `SkipReveal()`,
  `SetLine(int)`. `cutscene_test.gd` uses these (headless, so no render pass).

## Turn system (initiative)
`TurnSystem` (C#, plain class — not a Godot node) drives turn order in Level01:
- At the start of the encounter (`StartEncounter()`), every combatant rolls a
  **d5** (1–5); **total = Agility + roll**.
- Turn order is **descending total**; ties break by higher Agility, then party order.
- The order is rolled **once** and persists for the rest of the encounter.
  Everyone acts once per round; END TURN advances, and completing the round
  starts a new one with the SAME order (no re-roll). Agility: 10 = normal, 20 = max.
- `Level01` builds it in `_Ready` from its `Party` (Vex 12, Kaito 10, Rook 15,
  Mira 10) and shows `ROUND n — TURN: <name>` + per-character `Agility+roll`
  in the HUD. Test hooks on Level01: `GetRound()`, `GetCurrentName()`,
  `GetInitiativeTotals()`, `GetInitiativeOrderNames()`, `SetInitiativeRolls(int[])`
  (forces deterministic d5 results for tests).
- `flow_test.gd` uses `SetInitiativeRolls` to make order deterministic
  (e.g. roll `[1,1,1,1]` → Rook 16, Vex 13, Kaito/Mira 11 tie → Kaito before Mira).

## Build (C#)
The .NET SDK in this container is `/tmp/dotnet` (not on PATH). Build before running:
```
export PATH=/tmp/dotnet:$PATH DOTNET_ROOT=/tmp/dotnet DOTNET_CLI_TELEMETRY_OPTOUT=1
/tmp/dotnet/dotnet build dcyberpunkgam.csproj
```
Godot's SDK redirects output to `.godot/mono/temp/bin/Debug/` (gitignored), NOT `bin/`.
Run the game with the Mono build: `/tmp/godot/Godot_v4.7.2-stable_mono_linux_x86_64/Godot_v4.7.2-stable_mono_linux.x86_64`.

## Validation (headless + rendered)
Binary in this container: `/tmp/godot/godot` (Godot 4.7.2).
```
<bin> --headless --path . --import                    # clean import check
<bin> --headless --path . --quit-after 120            # smoke run (main scene = menu)
<bin> --headless --path . -s res://tests/flow_test.gd # functional flow test (all 28 checks)
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
- **Headless `RichTextLabel` doesn't parse BBCode**: in `--headless` / `-s`
  test mode there's no render pass, so `GetParsedText()` returns the RAW BBCode
  (`[b][color=...]...`) instead of plain text. For tests, expose a logical
  `GetTextPlain()` built from state rather than relying on `GetParsedText()`.
- **Mono Godot binary (this container)**: the Mono build that loads `.cs`
  scripts is `/home/bigal/Applications/GodotMono/Godot_v4.7.2-stable_mono_linux_x86_64/Godot_v4.7.2-stable_mono_linux.x86_64`.
  The bare `godot` on PATH (`/usr/local/bin/godot`) is the GDScript build and
  fails with "No loader found for resource ... .cs". `dotnet` is on PATH
  (`/usr/bin/dotnet`), so plain `dotnet build dcyberpunkgam.csproj` works.
  (The `/tmp/dotnet` + `/tmp/godot` paths above were from an earlier container.)
- **Rendering verification without ImageMagick**: there's a real Xorg at
  `DISPLAY=:0.0`. To screenshot a scene, run a tiny `SceneTree` script
  (`godot --path . -s res://tests/_shot.gd`, NON-headless) that instantiates
  the scene, `await process_frame` a few times, then
  `root.get_texture().get_image().save_png(...)`. Analyze the PNG with PIL.

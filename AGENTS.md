# Repository notes (persistent memory)

Godot 4 project (`dcyberpunkgam`), **C# (.NET / Godot Mono)**, 2D. Validated with Godot 4.7.2-stable Mono + .NET SDK 8.

## Layout (menu + placeholder turn-based level)
- `GAMES.md` — game design outline (OUTDATED: references removed `CharacterData`; trust the code)
- `project.godot` — main scene `res://scenes/main_menu.tscn`; `GameManager` C# autoload; `features` includes `C#`, `dotnet`
- `scenes/main_menu.tscn` — title screen: NEW GAME / CONTINUE / QUIT
- `scenes/level_01.tscn` — placeholder turn-based arena (grid board, 4 party tokens, HUD, END TURN / BACK)
- `scripts/GameManager.cs` — autoload: scene flow only (StartGame / QuitToMenu); exposes `Instance`
- `scripts/MainMenu.cs`, `scripts/Level01.cs`
- `dcyberpunkgam.csproj` / `dcyberpunkgam.sln` — .NET project (Godot.NET.Sdk 4.7.2, net8.0)
- `tests/flow_test.gd` — headless SceneTree functional test (menu → level → turns → movement → back)
- `readme.me` — pre-existing file, untouched

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
<bin> --headless --path . -s res://tests/flow_test.gd # functional flow test (all 18 checks)
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

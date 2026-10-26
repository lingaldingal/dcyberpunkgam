# Repository notes (persistent memory)

Godot 4 project (`dcyberpunkgam`), GDScript, 2D. Validated with Godot 4.7.2-stable.

## Layout (minimal, menu-first)
- `GAMES.md` — game design outline (core loop, planned architecture)
- `project.godot` — main scene `res://scenes/main_menu.tscn`; `GameManager` autoload
- `scenes/main_menu.tscn` — title screen: NEW GAME / CONTINUE / QUIT
- `scenes/stage_01.tscn` — minimal texture-free placeholder stage, Back to Menu
- `scripts/game_manager.gd` — autoload: scene flow only (start_game / quit_to_menu)
- `scripts/main_menu.gd`, `scripts/stage_01.gd`
- `assets/` — (removed; stage is texture-free so fresh clones need no import step)
- `readme.me` — pre-existing file, untouched

## Menu layout gotcha (the black-screen bug)
The original menu put `anchors_preset = 8` (center) on the Title/Buttons nodes
while ALSO setting explicit `anchor_right = 1.0` / `anchor_bottom = 1.0`.
At runtime only the `anchor_*` values matter (preset is editor metadata), so
the nodes anchored to the bottom-right corner and rendered off-screen —
only the full-rect Background ColorRect showed, i.e. a flat black screen.
Fix: menu/stage content lives in a full-rect `CenterContainer` + `VBoxContainer`;
never mix `anchors_preset` with explicit `anchor_*` values that disagree.

## Validation (headless + rendered)
Binary in this container: `/tmp/godot/godot` (Godot 4.7.2).
```
<bin> --headless --path . --import                    # clean import check
<bin> --headless --path . --quit-after 120            # smoke run (main scene = menu)
```
Headless alone is NOT enough for UI: it never renders, so off-screen controls
are invisible to it. To verify a menu actually displays, run on a virtual
display and screenshot:
```
Xvfb :99 -screen 0 1280x720x24 &
DISPLAY=:99 <bin> --path . &            # then: import -window root out.png
```
Check the PNG has more than a handful of unique colors (flat = black screen).
A functional flow test (temporary SceneTree script, `-s res://x.gd`) can
instantiate the menu, emit `Button.pressed`, await frames, and assert the
stage node appears and Back returns to the menu.

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

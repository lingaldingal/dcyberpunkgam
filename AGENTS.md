# Repository notes (persistent memory)

Godot 4 project (`dcyberpunkgam`), GDScript, 2D. Validated with Godot 4.7.2-stable.

## Layout
- `GAMES.md` — game design outline (core loop, scaffold status, planned architecture)
- `project.godot` — main scene `res://scenes/main_menu.tscn`; `GameManager` autoload; input actions `move_left/right/up/down` (WASD + arrows, bound via `physical_keycode`)
- `scenes/main_menu.tscn` — title screen, Start Game / Quit
- `scenes/stage_01.tscn` — former test arena (walls, 3 obstacles, HUD, player), now stage 01 with party HUD
- `scenes/player.tscn` — CharacterBody2D + follow Camera2D
- `scripts/game_manager.gd` — autoload: party (max 5), active character, scene flow
- `scripts/character_data.gd` — Resource: stats, level, EXP, level-up rule
- `scripts/main_menu.gd`, `scripts/stage_01.gd`, `scripts/player.gd`
- `assets/` — placeholder PNGs (player/obstacle/floor/wall), `icon.svg`
- `readme.me` — pre-existing file, untouched

## Validation (headless)
Binary in this container: `/tmp/godot/godot` (Godot 4.7.2).
```
<bin> --headless --path . --import                    # clean import check
<bin> --headless --path . --quit-after 120            # smoke run (main scene = menu)
```
Both pass with zero errors/warnings. Functional test pattern (temporary SceneTree script, `-s res://x.gd`): call `GameManager.start_game()`, await frames, assert `Stage01` loaded and HUD shows party; inject `InputEventKey` with `physical_keycode` set (NOT `keycode` — actions are bound to physical keys) via `Input.parse_input_event` (4.7 API; was `push_event` in older 4.x), await physics frames, assert player moved.

## Gotchas
- `project.godot` input actions: object types must be UNQUOTED identifiers: `Object(InputEventKey, ...)`, and values end `null)` not `null")`.
- Don't commit `.godot/` cache (gitignored). `.import` files ARE committed.
- First run on a fresh clone needs `--import` once.

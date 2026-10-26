# Repository notes (persistent memory)

Godot 4 project (`dcyberpunkgam`), GDScript, 2D. Validated with Godot 4.7.2-stable.

## Layout
- `project.godot` — main scene `res://scenes/main.tscn`; input actions `move_left/right/up/down` (WASD + arrows, bound via `physical_keycode`)
- `scenes/main.tscn` — test arena: walls (StaticBody2D), 3 obstacles, HUD label, player instance
- `scenes/player.tscn` — CharacterBody2D + follow Camera2D
- `scripts/player.gd` — movement via `Input.get_axis` + `move_and_slide`, exported `speed`
- `assets/` — placeholder PNGs (player/obstacle/floor/wall), `icon.svg`
- `readme.me` — pre-existing file, untouched

## Validation (headless)
Binary in this container: `/home/bigal/Gamedev/godot` (Godot 4.7.2).
```
<bin> --path . --import                    # clean import check
<bin> --path . --headless --quit-after 120 # smoke run
```
Both pass with zero errors/warnings. Functional test pattern (temporary SceneTree script, `-s res://x.gd`): instantiate `scenes/main.tscn`, inject `InputEventKey` with `physical_keycode` set (NOT `keycode` — actions are bound to physical keys), await physics frames, assert player moved.

## Gotchas
- `project.godot` input actions: object types must be UNQUOTED identifiers: `Object(InputEventKey, ...)`, and values end `null)` not `null")`.
- Don't commit `.godot/` cache (gitignored). `.import` files ARE committed.
- First run on a fresh clone needs `--import` once.

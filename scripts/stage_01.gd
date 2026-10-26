extends Node2D
## Stage 01: the test arena, now wired to the party.

var _hud_label: Label


func _ready() -> void:
	_hud_label = $HUD/Label
	_hud_label.text = "STAGE 01 — the test arena"
	_update_hud()


func _update_hud() -> void:
	var c := GameManager.get_active()
	_hud_label.text = (
		"STAGE 01 — %s L%d HP %d/%d ATK %d DEF %d EXP %d/%d  (WASD/arrows to move)"
		% [c.name, c.level, c.hp, c.max_hp, c.attack, c.defense, c.exp, c.exp_to_next]
	)

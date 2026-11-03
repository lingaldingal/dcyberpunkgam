extends SceneTree
# Overwatch test: a combatant (party or enemy) designates a far-end cell within
# its weapon's range; the straight (Bresenham) line from its CURRENT cell to
# that target — capped at the weapon's range, diagonals included — is "watched".
# If, on an enemy's turn, that enemy walks into a watched cell, the
# overwatcher fires a reaction shot (weapon damage), the game pauses 0.5s, and
# the attack is logged to the console. The overwatch persists until the
# overwatcher's next turn.
#
# Run: godot --headless --path . -s res://tests/overwatch_test.gd

const SHOOT := 1
const OVERWATCH := 4  # Level01.ActionMode.Overwatch

var _failures := 0
var level: Node = null


func _initialize() -> void:
	_run()


func _check(condition: bool, message: String) -> void:
	if condition:
		print("  ok: ", message)
	else:
		_failures += 1
		printerr("  FAIL: ", message)


# Emit a left mouse click at the iso screen position of board cell (x, y)
# through the board's gui_input (the same path the shoot/melee tests use).
func _board_click(board: Node, cell: Vector2i) -> void:
	var click := InputEventMouseButton.new()
	click.button_index = MOUSE_BUTTON_LEFT
	click.pressed = true
	click.position = level.IsoCellCenter(cell)
	board.gui_input.emit(click)


func _run() -> void:
	var menu: Node = load("res://scenes/main_menu.tscn").instantiate()
	root.add_child(menu)
	await process_frame
	menu.get_node("Center/Panel/NewGame").pressed.emit()
	level = null
	for i in 60:
		await process_frame
		level = root.get_node_or_null("Level01")
		if level != null:
			break
	_check(level != null, "NEW GAME loads level_01")
	if level == null:
		_finish()
		return
	await process_frame
	var board: Node = level.get_node("Board")
	await process_frame

	# Pin Vex to the rifle (range 6, 3 dmg) and every attack to a guaranteed
	# HIT so the damage expectations stay deterministic.
	level.SetWeapon("Vex", true)
	level.SetForcedHit(1)
	await process_frame

	# --- The overwatch line is the Bresenham path from the CURRENT cell to the
	# target, capped at the weapon's range (rifle = 6), diagonals included. ---
	level.ForceCurrent("Vex")
	await process_frame
	# Vex starts at (1,1). Target (5,1): a horizontal line, 4 cells.
	level.SetOverwatch("Vex", 5, 1)
	await process_frame
	_check(level.GetOverwatchTarget("Vex") == Vector2i(5, 1),
			"Vex overwatch target is (5,1) (got %s)" % level.GetOverwatchTarget("Vex"))
	_check(level.GetOverwatchLine("Vex") == "2,1 3,1 4,1 5,1",
			"horizontal overwatch line is 2,1 3,1 4,1 5,1 (got '%s')" % level.GetOverwatchLine("Vex"))
	_check(level.IsOverwatchCell(2, 1), "cell (2,1) is watched")
	_check(level.IsOverwatchCell(5, 1), "cell (5,1) is watched")
	_check(not level.IsOverwatchCell(1, 1), "Vex's own cell (1,1) is NOT watched")
	_check(not level.IsOverwatchCell(6, 1), "cell (6,1) past the target is NOT watched")

	# A 45-degree diagonal works too: (1,1) -> (5,5) is a clean diagonal.
	level.SetOverwatch("Vex", 5, 5)
	await process_frame
	_check(level.GetOverwatchLine("Vex") == "2,2 3,3 4,4 5,5",
			"diagonal overwatch line is 2,2 3,3 4,4 5,5 (got '%s')" % level.GetOverwatchLine("Vex"))
	_check(level.IsOverwatchCell(3, 3), "diagonal cell (3,3) is watched")
	_check(not level.IsOverwatchCell(3, 2), "off-diagonal cell (3,2) is NOT watched")

	# The line is capped at the weapon's range: a target beyond rifle range (6)
	# still only watches 6 cells from the current position.
	level.SetOverwatch("Vex", 10, 1)
	await process_frame
	_check(level.GetOverwatchLine("Vex") == "2,1 3,1 4,1 5,1 6,1 7,1",
			"overwatch line is capped at rifle range 6 (got '%s')" % level.GetOverwatchLine("Vex"))
	_check(not level.IsOverwatchCell(8, 1), "cell (8,1) beyond range 6 is NOT watched")

	# A pistol (range 4) caps the line at 4 cells.
	level.SetWeapon("Vex", false)
	await process_frame
	level.SetOverwatch("Vex", 10, 1)
	await process_frame
	_check(level.GetOverwatchLine("Vex") == "2,1 3,1 4,1 5,1",
			"pistol overwatch line is capped at range 4 (got '%s')" % level.GetOverwatchLine("Vex"))
	# Back to the rifle for the trigger tests.
	level.SetWeapon("Vex", true)
	await process_frame

	# --- The overwatch line is anchored to the CURRENT cell: a move shifts it. ---
	level.SetTokenCell("Vex", 3, 1)
	await process_frame
	level.SetOverwatch("Vex", 7, 1)
	await process_frame
	_check(level.GetOverwatchLine("Vex") == "4,1 5,1 6,1 7,1",
			"overwatch line re-anchors to the new position (got '%s')" % level.GetOverwatchLine("Vex"))
	level.SetTokenCell("Vex", 1, 1)
	await process_frame

	# --- Trigger: an enemy that walks into a watched cell takes a reaction
	# shot, the game pauses 0.5s, the attack is logged, and the enemy stops.
	# Vex (rifle, at (1,1)) overwatches (8,1) -> watches 2,1..7,1. Brute
	# (pistol, at (12,1)) walks west toward Kaito (2,1); it steps onto the
	# watched cell (7,1) BEFORE its pistol shooting range (from (6,1)), so the
	# overwatch fires and Brute stops on (7,1). ---
	level.SetOverwatch("Vex", 8, 1)
	await process_frame
	_check(level.GetOverwatchLine("Vex") == "2,1 3,1 4,1 5,1 6,1 7,1",
			"Vex overwatches (8,1) -> watches 2,1..7,1 (got '%s')" % level.GetOverwatchLine("Vex"))
	level.SetTokenCell("Brute", 12, 1)
	await process_frame
	level.SetWeapon("Brute", false)  # pistol: range 4 (stops at (6,1))
	await process_frame
	level.SetForcedHit(1)
	level.ForceCurrent("Brute")
	await process_frame
	level.ForceEnemyMove()
	await process_frame
	_check(level.GetTokenCell("Brute") == Vector2i(7, 1),
			"Brute stopped on the watched cell (7,1) (got %s)" % level.GetTokenCell("Brute"))
	_check(level.GetOverwatchPause() > 0.0,
			"the 0.5s overwatch pause is armed (got %f)" % level.GetOverwatchPause())
	_check(String(level.GetInfoText()).contains("OVERWATCHES"),
			"info panel logs the overwatch attack (got '%s')" % String(level.GetInfoText()))
	_check(level.GetLastAttackResult() == "HIT",
			"overwatch reaction reports HIT (got '%s')" % level.GetLastAttackResult())
	level.SetOverwatchPause(0.0)  # clear the pause (headless delta may be 0)
	await process_frame

	# --- The overwatch persists until the overwatcher's NEXT turn. ---
	# Vex's turn starts: its overwatch clears (it may set a fresh one).
	level.ForceCurrent("Vex")
	await process_frame
	_check(level.GetOverwatchTarget("Vex") == Vector2i(-1, -1),
			"Vex's overwatch cleared at the start of its own turn")

	# --- Activating overwatch via a board click spends the one action. ---
	level.SetTokenCell("Vex", 1, 1)
	await process_frame
	level.ForceCurrent("Vex")
	await process_frame
	level.ForceOverwatchMode()
	await process_frame
	_check(level.GetActionMode() == OVERWATCH,
			"overwatch mode is active (got %d)" % level.GetActionMode())
	# Commit via a board click on a valid target cell (spends the action).
	_board_click(board, Vector2i(4, 1))
	await process_frame
	_check(level.GetOverwatchTarget("Vex") == Vector2i(4, 1),
			"board click committed the overwatch on (4,1) (got %s)" % level.GetOverwatchTarget("Vex"))
	_check(String(level.GetInfoText()).contains("takes overwatch"),
			"info panel logs the overwatch commit (got '%s')" % String(level.GetInfoText()))

	_finish()


func _finish() -> void:
	if _failures > 0:
		printerr("OVERWATCH TEST FAILED: %d check(s) failed" % _failures)
	else:
		print("OVERWATCH TEST PASSED")
	quit(1 if _failures > 0 else 0)

extends SceneTree
# Combat test: Constitution/HP values, Shoot action (exactly 4 straight
# squares, 8 directions, 2 damage), death (token removed + turn skipped),
# and the bottom-left actions menu (name on top, always on screen).
# Run: godot --headless --path . -s res://tests/combat_test.gd

const TILE := 48
const SHOOT := 1  # Level01.ActionMode.Shoot
const MOVE := 0   # Level01.ActionMode.Move

var _failures := 0


func _initialize() -> void:
	_run()


func _run() -> void:
	var menu: Node = load("res://scenes/main_menu.tscn").instantiate()
	root.add_child(menu)
	await process_frame
	menu.get_node("Center/Panel/NewGame").pressed.emit()
	var level: Node = null
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

	# --- HP = 5 + Constitution for everyone; Constitution in [7, 13]. ---
	_check(level.GetHitPoints("Vex") == 16, "Vex HP = 5 + Con 11 = 16 (got %d)" % level.GetHitPoints("Vex"))
	_check(level.GetHitPoints("Kaito") == 14, "Kaito HP = 5 + Con 9 = 14 (got %d)" % level.GetHitPoints("Kaito"))
	_check(level.GetHitPoints("Rook") == 17, "Rook HP = 5 + Con 12 = 17 (got %d)" % level.GetHitPoints("Rook"))
	_check(level.GetHitPoints("Mira") == 13, "Mira HP = 5 + Con 8 = 13 (got %d)" % level.GetHitPoints("Mira"))
	_check(level.GetHitPoints("Gutter") == 15, "Gutter HP = 5 + Con 10 = 15 (got %d)" % level.GetHitPoints("Gutter"))
	_check(level.GetHitPoints("Slick") == 18, "Slick HP = 5 + Con 13 = 18 (got %d)" % level.GetHitPoints("Slick"))
	_check(level.GetHitPoints("Brute") == 17, "Brute HP = 5 + Con 12 = 17 (got %d)" % level.GetHitPoints("Brute"))
	_check(level.GetHitPoints("Wisp") == 12, "Wisp HP = 5 + Con 7 = 12 (got %d)" % level.GetHitPoints("Wisp"))

	# --- Actions menu: bottom-left, on screen, active character's name on top. ---
	var menu_node: Node = level.get_node("ActionMenu")
	var menu_size: Vector2 = menu_node.size
	var name_label: Node = level.get_node("ActionMenu/VBox/Name")
	_check(name_label != null, "actions menu has the character name on top")

	# Force a realistic viewport so the menu's true bottom-left placement is
	# exercised (headless default is a 64x64 window where it clamps).
	level.size = Vector2(1280, 720)
	for i in 3:
		await process_frame
	var menu_pos: Vector2 = level.GetActionMenuPos()
	var view: Vector2 = level.size
	_check(menu_pos.x == 12.0, "actions menu hugs the left edge (got %s)" % str(menu_pos))
	_check(abs(menu_pos.y - (view.y - menu_size.y - 12.0)) < 0.5,
			"actions menu is bottom-left (got y=%s, want %s)" % [str(menu_pos.y), str(view.y - menu_size.y - 12.0)])
	_check(menu_pos.x >= 0.0 and menu_pos.y >= 0.0
			and menu_pos.x + menu_size.x <= view.x and menu_pos.y + menu_size.y <= view.y,
			"actions menu is fully on screen (got %s, size %s, view %s)"
			% [str(menu_pos), str(menu_size), str(view)])
	if name_label != null:
		_check(String(name_label.text) == level.GetCurrentName(),
				"menu name matches active character (got '%s', active '%s')" %
				[String(name_label.text), level.GetCurrentName()])
	# Restore the small headless view (the menu clamps back on screen).
	level.size = Vector2(64, 64)
	for i in 3:
		await process_frame
	var tiny_pos: Vector2 = level.GetActionMenuPos()
	_check(tiny_pos.x >= 0.0 and tiny_pos.y >= 0.0,
			"menu stays on screen on a tiny viewport (got %s)" % str(tiny_pos))

	# --- Shoot range: exactly 4 straight squares, any of the 8 directions. ---
	# Set the cells BEFORE ForceCurrent: shoot targets are computed from the
	# active combatant's position when the turn is set. Vex is centered so all
	# 8 directions fall in-bounds.
	level.SetTokenCell("Vex", 9, 6)
	level.SetTokenCell("Wisp", 13, 6)
	level.ForceCurrent("Vex")
	level.SetActionMode(SHOOT)
	_check(level.IsShootTarget(13, 6), "enemy 4 squares east is a shoot target")
	_check(level.IsShootTarget(9, 10), "cell 4 squares south is a shoot target")
	_check(level.IsShootTarget(13, 10), "cell 4 squares southeast (diagonal) is a shoot target")
	_check(level.IsShootTarget(13, 2), "cell 4 squares northeast (diagonal) is a shoot target")
	_check(level.IsShootTarget(5, 6), "cell 4 squares west is a shoot target")
	_check(level.IsShootTarget(9, 2), "cell 4 squares north is a shoot target")
	_check(not level.IsShootTarget(12, 6), "3 squares away is not a shoot target")
	_check(not level.IsShootTarget(14, 6), "5 squares away is not a shoot target")
	_check(not level.IsShootTarget(12, 7), "knight's move (3,1) is not a shoot target")
	_check(not level.IsShootTarget(9, 6), "own cell is not a shoot target")

	# Clicking a non-target cell in shoot mode deals no damage (12,6 is 3 away).
	var click_wrong_cell := InputEventMouseButton.new()
	click_wrong_cell.button_index = MOUSE_BUTTON_LEFT
	click_wrong_cell.pressed = true
	click_wrong_cell.position = Vector2(12 * TILE + TILE / 2, 6 * TILE + TILE / 2)
	board.gui_input.emit(click_wrong_cell)
	await process_frame
	_check(level.GetHitPoints("Wisp") == 12, "clicking a non-target in shoot mode deals no damage")

	# A real shot: click Wisp's cell (13,6). 2 damage, and the action is consumed.
	var click_shoot := InputEventMouseButton.new()
	click_shoot.button_index = MOUSE_BUTTON_LEFT
	click_shoot.pressed = true
	click_shoot.position = Vector2(13 * TILE + TILE / 2, 6 * TILE + TILE / 2)
	board.gui_input.emit(click_shoot)
	await process_frame
	_check(level.GetHitPoints("Wisp") == 10, "shoot deals 2 damage (got %d HP)" % level.GetHitPoints("Wisp"))
	# Second click: action already used this turn -> no extra damage.
	board.gui_input.emit(click_shoot)
	await process_frame
	_check(level.GetHitPoints("Wisp") == 10, "second click does not shoot again (once per turn)")
	# A valid diagonal target with no enemy there does no damage.
	level.ForceCurrent("Vex")
	level.SetActionMode(SHOOT)
	var click_diag := InputEventMouseButton.new()
	click_diag.button_index = MOUSE_BUTTON_LEFT
	click_diag.pressed = true
	click_diag.position = Vector2(13 * TILE + TILE / 2, 10 * TILE + TILE / 2)
	board.gui_input.emit(click_diag)
	await process_frame
	_check(level.GetHitPoints("Wisp") == 10, "diagonal click at empty target does no damage")

	# --- WAIT consumes the action (no damage, no move). ---
	level.ForceCurrent("Vex")
	level.SetActionMode(SHOOT)
	level.get_node("ActionMenu/VBox/Actions/Wait").pressed.emit()
	await process_frame
	var click_after_wait := InputEventMouseButton.new()
	click_after_wait.button_index = MOUSE_BUTTON_LEFT
	click_after_wait.pressed = true
	click_after_wait.position = Vector2(13 * TILE + TILE / 2, 6 * TILE + TILE / 2)
	board.gui_input.emit(click_after_wait)
	await process_frame
	_check(level.GetHitPoints("Wisp") == 10, "WAIT consumes the action (no shot after)")

	# --- Death: HP hits 0 -> token removed from the map, no more turns. ---
	# Rook (party) shoots Wisp (12 HP) to death: 6 shots, one per turn.
	# Brute is forced current first: it is an enemy, so it auto-moves and
	# auto-ends, handing the turn to Rook in the [1,1,1,1,1,1,1,1] order.
	level.SetInitiativeRolls([1, 1, 1, 1, 1, 1, 1, 1])
	level.ForceCurrent("Brute")
	for i in 24:
		await process_frame
	_check(level.GetCurrentName() == "Rook",
			"Brute auto-ended, Rook is active (got '%s')" % level.GetCurrentName())
	var end_turn: Button = level.get_node("Center/Panel/Actions/EndTurn")
	var token_wisp: Node = board.get_node("E3")
	var shots := 0
	while level.IsAlive("Wisp") and shots < 8:
		level.SetTokenCell("Rook", 8, 1)
		level.SetTokenCell("Wisp", 12, 1)
		level.ForceCurrent("Rook")
		level.SetActionMode(SHOOT)
		var click := InputEventMouseButton.new()
		click.button_index = MOUSE_BUTTON_LEFT
		click.pressed = true
		click.position = Vector2(12 * TILE + TILE / 2, 1 * TILE + TILE / 2)
		board.gui_input.emit(click)
		await process_frame
		end_turn.pressed.emit()
		await process_frame
		shots += 1
	_check(level.GetHitPoints("Wisp") == 0, "Wisp HP reached 0 (got %d)" % level.GetHitPoints("Wisp"))
	_check(not level.IsAlive("Wisp"), "Wisp is dead")
	_check(token_wisp.visible == false, "dead Wisp's token is removed from the map")
	# The turn order (rolls all 1) is Brute, Rook, Slick, Vex, Gutter, Kaito,
	# Mira, Wisp. Mira is second-to-last, so ending its turn must land on the
	# dead Wisp slot and skip straight to Brute (round wraps).
	level.ForceCurrent("Mira")
	end_turn.pressed.emit()
	await process_frame
	_check(level.GetCurrentName() == "Brute",
			"dead Wisp no longer gets a turn (Mira -> Brute, now '%s')"
			% level.GetCurrentName())
	if name_label != null:
		_check(String(name_label.text) == level.GetCurrentName(),
				"menu name follows the active character (got '%s')" % String(name_label.text))

	_finish()


func _check(condition: bool, message: String) -> void:
	if condition:
		print("  ok: ", message)
	else:
		_failures += 1
		printerr("  FAIL: ", message)


func _finish() -> void:
	if _failures > 0:
		printerr("COMBAT TEST FAILED: %d check(s) failed" % _failures)
	else:
		print("COMBAT TEST PASSED")
	quit(1 if _failures > 0 else 0)

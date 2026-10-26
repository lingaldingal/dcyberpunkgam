extends SceneTree
# Temporary functional flow test: menu -> level -> turns -> movement -> back.
# Run: godot --headless --path . -s res://tests/flow_test.gd

const TILE := 64

var _failures := 0


func _initialize() -> void:
	_run_flow()


func _run_flow() -> void:
	var menu: Node = load("res://scenes/main_menu.tscn").instantiate()
	root.add_child(menu)
	await process_frame

	# NEW GAME transitions to level 01.
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

	# Level hosts exactly 4 characters.
	var tokens := 0
	for c in level.get_node("Board").get_children():
		if c is Control and String(c.name).begins_with("P"):
			tokens += 1
	_check(tokens == 4, "level has 4 characters (got %d)" % tokens)

	# Turn order cycles Vex -> Kaito -> Rook -> Mira -> Vex ... endlessly.
	var turn := level.get_node("Center/Panel/HUD/Turn")
	var end_turn := level.get_node("Center/Panel/Actions/EndTurn")
	var expected := ["Vex", "Kaito", "Rook", "Mira"]
	for i in 8:
		_check(turn.text == "TURN: " + expected[i % 4],
				"turn %d belongs to %s (got '%s')" % [i, expected[i % 4], turn.text])
		end_turn.pressed.emit()
		await process_frame

	# Keyboard movement: P0 starts at (1,1), flanked by P1 (right) and
	# P2 (down). Left (0,1) is open.
	var p0 := level.get_node("Board/P0")
	_press_key(KEY_A)
	await process_frame
	_check(p0.position == Vector2(0, TILE), "P0 moved left to (0,1)")

	# Down to (0,2).
	_press_key(KEY_S)
	await process_frame
	_check(p0.position == Vector2(0, 2 * TILE), "P0 moved down to (0,2)")

	# Right is blocked: P2 stands at (1,2).
	_press_key(KEY_D)
	await process_frame
	_check(p0.position == Vector2(0, 2 * TILE), "P0 blocked by party member at (1,2)")

	# Clicking the highlighted tile at (0,3) moves P0 there. The board
	# handles clicks via its gui_input signal (board-local coords); emit
	# it directly since headless skips the window's mouse hit-test.
	var board := level.get_node("Board")
	var click := InputEventMouseButton.new()
	click.button_index = MOUSE_BUTTON_LEFT
	click.pressed = true
	click.position = Vector2(0 * TILE + TILE / 2, 3 * TILE + TILE / 2)
	board.gui_input.emit(click)
	await process_frame
	_check(p0.position == Vector2(0, 3 * TILE), "click moved P0 to (0,3)")

	# P3 (Mira) starts at (2,2); (3,2) is an obstacle. End three turns
	# to make P3 active, then verify the obstacle blocks and S moves.
	for i in 3:
		end_turn.pressed.emit()
		await process_frame
	_check(turn.text == "TURN: Mira", "turn passed to Mira (got '%s')" % turn.text)
	var p3 := level.get_node("Board/P3")
	_press_key(KEY_D)
	await process_frame
	_check(p3.position == Vector2(2 * TILE, 2 * TILE), "P3 blocked by obstacle at (3,2)")
	_press_key(KEY_S)
	await process_frame
	_check(p3.position == Vector2(2 * TILE, 3 * TILE), "P3 moved down to (2,3)")

	# BACK returns to the menu.
	level.get_node("Center/Panel/Actions/Back").pressed.emit()
	await process_frame
	_check(root.get_node_or_null("MainMenu") != null, "BACK returns to menu")

	_finish()


func _press_key(code: int) -> void:
	var key := InputEventKey.new()
	key.keycode = code
	key.pressed = true
	get_root().push_input(key)


func _check(condition: bool, message: String) -> void:
	if condition:
		print("  ok: ", message)
	else:
		_failures += 1
		printerr("  FAIL: ", message)


func _finish() -> void:
	if _failures > 0:
		printerr("FLOW TEST FAILED: %d check(s) failed" % _failures)
	else:
		print("FLOW TEST PASSED")
	quit(1 if _failures > 0 else 0)

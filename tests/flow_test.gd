extends SceneTree
# Functional flow test: menu -> level -> initiative turn order -> rounds ->
# movement -> back. Turn order is set once at the start of the encounter by
# (Agility + d5 roll), descending, and persists for the rest of it, so the
# test forces deterministic rolls via SetInitiativeRolls.
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
	await process_frame  # ensure Level01._Ready (turn-system init) completed

	# Level hosts exactly 4 characters.
	var tokens := 0
	for c in level.get_node("Board").get_children():
		if c is Control and String(c.name).begins_with("P"):
			tokens += 1
	_check(tokens == 4, "level has 4 characters (got %d)" % tokens)

	# The level opens on round 1.
	_check(level.GetRound() == 1, "level starts on round 1 (got %d)" % level.GetRound())

	var turn := level.get_node("Center/Panel/HUD/Turn")
	var end_turn := level.get_node("Center/Panel/Actions/EndTurn")

	# --- Initiative: total = Agility + d5 roll; order = descending total. ---
	# Agility: Vex 12, Kaito 10, Rook 15, Mira 10.
	# Force roll [1,1,1,1] -> totals [13,11,16,11].
	level.SetInitiativeRolls([1, 1, 1, 1])
	await process_frame
	var totals := Array(level.GetInitiativeTotals())
	_check(totals == [13, 11, 16, 11],
			"totals are Agility + d5 roll (got %s)" % str(totals))
	# Rook 16 > Vex 13 > Kaito 11 == Mira 11 (tie -> party order: Kaito, Mira).
	_check(Array(level.GetInitiativeOrderNames()) == ["Rook", "Vex", "Kaito", "Mira"],
			"turn order is descending total w/ tiebreak (got %s)" % str(level.GetInitiativeOrderNames()))
	_check(turn.text == "ROUND 1 — TURN: Rook", "round 1 starts with Rook (got '%s')" % turn.text)
	_check(level.GetCurrentName() == "Rook", "current character is Rook")

	# A high d5 roll can overtake a higher-Agility character:
	# roll [5,1,1,1] -> Vex 17, Rook 16 -> Vex leads despite lower Agility.
	level.SetInitiativeRolls([5, 1, 1, 1])
	await process_frame
	_check(Array(level.GetInitiativeOrderNames()) == ["Vex", "Rook", "Kaito", "Mira"],
			"high d5 roll lets Vex (12) beat Rook (15) (got %s)" % str(level.GetInitiativeOrderNames()))

	# --- Turn advancement: everyone acts once, then a new round begins. ---
	level.SetInitiativeRolls([1, 1, 1, 1])
	await process_frame
	var expected := ["Rook", "Vex", "Kaito", "Mira"]
	for i in 4:
		_check(level.GetCurrentName() == expected[i],
				"turn %d belongs to %s (got '%s')" % [i, expected[i], level.GetCurrentName()])
		end_turn.pressed.emit()
		await process_frame
	# After all 4 acted, a new round begins. Initiative was rolled once, so
	# the order persists into round 2 (no re-roll) and round 3.
	_check(level.GetRound() == 2, "new round starts after everyone acted (got %d)" % level.GetRound())
	_check(turn.text.begins_with("ROUND 2"), "HUD shows round 2 (got '%s')" % turn.text)
	_check(Array(level.GetInitiativeOrderNames()) == expected,
			"initiative order persists into round 2 (got %s)" % str(level.GetInitiativeOrderNames()))
	_check(Array(level.GetInitiativeTotals()) == [13, 11, 16, 11],
			"initiative totals persist into round 2 (got %s)" % str(level.GetInitiativeTotals()))
	for i in 4:
		_check(level.GetCurrentName() == expected[i],
				"round 2 turn %d belongs to %s (got '%s')" % [i, expected[i], level.GetCurrentName()])
		end_turn.pressed.emit()
		await process_frame
	_check(level.GetRound() == 3, "round 3 starts after second pass (got %d)" % level.GetRound())
	_check(Array(level.GetInitiativeOrderNames()) == expected,
			"initiative order persists into round 3 (got %s)" % str(level.GetInitiativeOrderNames()))

	# --- Movement: make Vex active, then verify WASD + click movement. ---
	level.SetInitiativeRolls([1, 1, 1, 1])  # order Rook, Vex, ... -> Rook active
	await process_frame
	end_turn.pressed.emit()  # -> Vex active
	await process_frame
	_check(level.GetCurrentName() == "Vex", "Vex is now active for movement test")

	var p0 := level.get_node("Board/P0")
	_press_key(KEY_A)
	await process_frame
	_check(p0.position == Vector2(0, TILE), "Vex moved left to (0,1)")

	_press_key(KEY_S)
	await process_frame
	_check(p0.position == Vector2(0, 2 * TILE), "Vex moved down to (0,2)")

	# Right is blocked: Rook (P2) stands at (1,2).
	_press_key(KEY_D)
	await process_frame
	_check(p0.position == Vector2(0, 2 * TILE), "Vex blocked by Rook at (1,2)")

	# Click the highlighted tile at (0,3) to move Vex there. The board
	# handles clicks via its gui_input signal (board-local coords); emit
	# it directly since headless skips the window's mouse hit-test.
	var board := level.get_node("Board")
	var click := InputEventMouseButton.new()
	click.button_index = MOUSE_BUTTON_LEFT
	click.pressed = true
	click.position = Vector2(0 * TILE + TILE / 2, 3 * TILE + TILE / 2)
	board.gui_input.emit(click)
	await process_frame
	_check(p0.position == Vector2(0, 3 * TILE), "click moved Vex to (0,3)")

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

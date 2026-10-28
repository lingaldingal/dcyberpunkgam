extends SceneTree
# Functional flow test: menu -> level -> initiative turn order (party +
# enemies) -> rounds -> movement range + BFS pathfinding -> enemy auto-move
# -> back. Turn order is set once at the start of the encounter by
# (Agility + d5 roll), descending, and persists for the rest of it, so the
# test forces deterministic rolls via SetInitiativeRolls.
# Run: godot --headless --path . -s res://tests/flow_test.gd

const TILE := 48

# Roster order (index -> name, agility, is_enemy).
const ROSTER := [
	["Vex", 12, false], ["Kaito", 10, false], ["Rook", 15, false], ["Mira", 10, false],
	["Gutter", 11, true], ["Slick", 13, true], ["Brute", 16, true], ["Wisp", 9, true],
]

var _failures := 0


func _initialize() -> void:
	_run_flow()


# Set <rolls> on the turn system, then let Level01 settle (including any
# armed enemy auto-move) so the test observes a stable state.
func _set_rolls(level: Node, rolls: Array) -> void:
	level.SetInitiativeRolls(rolls)
	for i in 16:
		await process_frame


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

	# Level hosts exactly 8 tokens: 4 party (P*) + 4 enemies (E*).
	var party := 0
	var enemies := 0
	for c in level.get_node("Board").get_children():
		var n := String(c.name)
		if c is Control and n.begins_with("P"):
			party += 1
		elif c is Control and n.begins_with("E"):
			enemies += 1
	_check(party == 4, "level has 4 party tokens (got %d)" % party)
	_check(enemies == 4, "level has 4 enemy tokens (got %d)" % enemies)

	# The level opens on round 1.
	_check(level.GetRound() == 1, "level starts on round 1 (got %d)" % level.GetRound())

	var turn := level.get_node("Center/Panel/HUD/Turn")
	var end_turn := level.get_node("Center/Panel/Actions/EndTurn")

	# --- Initiative: total = Agility + d5 roll; order = descending total. ---
	# Roster agility: Vex 12, Kaito 10, Rook 15, Mira 10, Gutter 11, Slick 13,
	# Brute 16, Wisp 9. Force roll [1,1,1,1,1,1,1,1] -> totals = agi + 1.
	await _set_rolls(level, [1, 1, 1, 1, 1, 1, 1, 1])
	var totals := Array(level.GetInitiativeTotals())
	_check(totals == [13, 11, 16, 11, 12, 14, 17, 10],
			"totals are Agility + d5 roll (got %s)" % str(totals))
	# Descending total: Brute 17 > Rook 16 > Slick 14 > Vex 13 > Gutter 12 >
	# Kaito 11 > Mira 11 (tie -> party order: Kaito, Mira) > Wisp 10.
	_check(Array(level.GetInitiativeOrderNames()) ==
			["Brute", "Rook", "Slick", "Vex", "Gutter", "Kaito", "Mira", "Wisp"],
			"turn order is descending total w/ tiebreak (got %s)" % str(level.GetInitiativeOrderNames()))
	_check(level.GetCurrentName() == "Brute", "round 1 starts with Brute (got '%s')" % level.GetCurrentName())

	# A high d5 roll can overtake a higher-Agility character:
	# roll [5,1,1,1,1,1,1,1] -> Vex 17, Brute 17 (tie -> Brute higher Agi),
	# so Vex lands just behind Brute despite lower Agility than Rook/Slick.
	await _set_rolls(level, [5, 1, 1, 1, 1, 1, 1, 1])
	_check(Array(level.GetInitiativeOrderNames()) ==
			["Brute", "Vex", "Rook", "Slick", "Gutter", "Kaito", "Mira", "Wisp"],
			"high d5 roll lifts Vex to 2nd (got %s)" % str(level.GetInitiativeOrderNames()))

	# --- Turn advancement: everyone acts once, then a new round begins. ---
	await _set_rolls(level, [1, 1, 1, 1, 1, 1, 1, 1])
	var expected := ["Brute", "Rook", "Slick", "Vex", "Gutter", "Kaito", "Mira", "Wisp"]
	for i in 8:
		_check(level.GetCurrentName() == expected[i],
				"turn %d belongs to %s (got '%s')" % [i, expected[i], level.GetCurrentName()])
		end_turn.pressed.emit()
		await process_frame
	# After all 8 acted, a new round begins. Initiative was rolled once, so
	# the order persists into round 2 (no re-roll) and round 3.
	_check(level.GetRound() == 2, "new round starts after everyone acted (got %d)" % level.GetRound())
	_check(turn.text.begins_with("ROUND 2"), "HUD shows round 2 (got '%s')" % turn.text)
	_check(Array(level.GetInitiativeOrderNames()) == expected,
			"initiative order persists into round 2 (got %s)" % str(level.GetInitiativeOrderNames()))
	_check(Array(level.GetInitiativeTotals()) == [13, 11, 16, 11, 12, 14, 17, 10],
			"initiative totals persist into round 2 (got %s)" % str(level.GetInitiativeTotals()))
	for i in 8:
		_check(level.GetCurrentName() == expected[i],
				"round 2 turn %d belongs to %s (got '%s')" % [i, expected[i], level.GetCurrentName()])
		end_turn.pressed.emit()
		await process_frame
	_check(level.GetRound() == 3, "round 3 starts after second pass (got %d)" % level.GetRound())
	_check(Array(level.GetInitiativeOrderNames()) == expected,
			"initiative order persists into round 3 (got %s)" % str(level.GetInitiativeOrderNames()))

	# --- Movement range: 5 + Agility/2 (floored), per active combatant. ---
	await _set_rolls(level, [1, 1, 1, 1, 1, 1, 1, 1])
	# Force Vex (agi 12) active -> range 5 + 6 = 11.
	level.ForceCurrent("Vex")
	await process_frame
	_check(level.GetCurrentName() == "Vex", "Vex is active for movement test")
	_check(level.GetMovementRange() == 11, "Vex (12) moves 11 (got %d)" % level.GetMovementRange())
	# Force Wisp (agi 9) active -> range 5 + 4 = 9 (floor of 9/2).
	level.ForceCurrent("Wisp")
	await process_frame
	_check(level.GetMovementRange() == 9, "Wisp (9) moves 9 (got %d)" % level.GetMovementRange())
	# Force Brute (agi 16) active -> range 5 + 8 = 13.
	level.ForceCurrent("Brute")
	await process_frame
	_check(level.GetMovementRange() == 13, "Brute (16) moves 13 (got %d)" % level.GetMovementRange())

	# --- Pathfinding: reachable set respects range + obstacles + tokens. ---
	# Vex starts at (1,1), agi 12 -> range 11. Rook sits at (1,2), so the
	# straight-down route is blocked and the left column (x=0) is the detour.
	level.ForceCurrent("Vex")
	await process_frame
	# Own cell is reachable.
	_check(level.IsReachable(1, 1), "Vex's own cell is reachable")
	# One step left is reachable.
	_check(level.IsReachable(0, 1), "Vex reaches (0,1) one step left")
	# Down the open left column to (0,11) is exactly 11 steps -> boundary hit.
	_check(level.IsReachable(0, 11), "Vex reaches (0,11) at the 11-step boundary")
	# (1,11) needs a 12-step detour around Rook -> just out of range.
	_check(not level.IsReachable(1, 11), "Vex cannot reach (1,11) (12-step detour)")
	# Obstacles are never reachable.
	_check(not level.IsReachable(5, 1), "obstacle (5,1) not reachable")

	# --- WASD movement nudges one tile within range. ---
	level.ForceCurrent("Vex")
	await process_frame
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

	# --- Click-to-move: click a reachable tile and Vex jumps there (BFS). ---
	# Vex is at (0,2). Click (0,6): 4 tiles down, reachable, straight path.
	var board := level.get_node("Board")
	var click := InputEventMouseButton.new()
	click.button_index = MOUSE_BUTTON_LEFT
	click.pressed = true
	click.position = Vector2(0 * TILE + TILE / 2, 6 * TILE + TILE / 2)
	board.gui_input.emit(click)
	await process_frame
	_check(p0.position == Vector2(0, 6 * TILE), "click moved Vex to (0,6)")

	# --- Enemy auto-move: on an enemy's turn it steps one random tile. ---
	level.ForceCurrent("Brute")  # Brute at (17,2); open neighbours exist
	await process_frame
	var brute_before: Vector2i = level.GetTokenCell("Brute")
	# Wait for the enemy auto-move (armed for 12 frames) to fire.
	for i in 20:
		await process_frame
	var brute_after: Vector2i = level.GetTokenCell("Brute")
	var moved := Vector2i(brute_after.x - brute_before.x, brute_after.y - brute_before.y)
	_check(abs(moved.x) + abs(moved.y) == 1,
			"enemy Brute auto-moved one tile (delta %s)" % str(moved))

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

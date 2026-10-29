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


# Let Level01 settle: if the active combatant is an enemy, wait for its
# auto-move to fire and its turn to auto-end (~12 frames); otherwise a single
# frame is enough.
func _settle(level: Node) -> void:
	if level.GetCurrentName() in ["Gutter", "Slick", "Brute", "Wisp"]:
		for i in 20:
			await process_frame
	else:
		await process_frame


# Set <rolls> on the turn system, then let the level settle (including any
# armed enemy auto-move + auto turn-end).
func _set_rolls(level: Node, rolls: Array) -> void:
	level.SetInitiativeRolls(rolls)
	await _settle(level)


# Verify <expected> is the current combatant, then advance past their turn:
# enemies auto-move + auto-end (wait for it); party members use END TURN.
func _advance_turn(level: Node, end_turn: Button, expected: String) -> void:
	_check(level.GetCurrentName() == expected,
			"turn belongs to %s (got '%s')" % [expected, level.GetCurrentName()])
	if expected in ["Gutter", "Slick", "Brute", "Wisp"]:
		# Enemy: wait for the auto-move to fire and the turn to auto-end.
		for i in 20:
			await process_frame
	else:
		end_turn.pressed.emit()
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
	# Weapons are rolled randomly at encounter start; pin everyone to the
	# pistol (range 4) so the enemy AI (shoot-or-approach) is deterministic.
	for n in ["Vex", "Kaito", "Rook", "Mira", "Gutter", "Slick", "Brute", "Wisp"]:
		level.SetWeapon(n, false)
	await process_frame

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
	# Brute (enemy) auto-ends its turn during _settle, so Rook is now current.
	_check(level.GetCurrentName() == "Rook",
			"Brute auto-ended, Rook is next (got '%s')" % level.GetCurrentName())

	# A high d5 roll can overtake a higher-Agility character:
	# roll [5,1,1,1,1,1,1,1] -> Vex 17, Brute 17 (tie -> Brute higher Agi),
	# so Vex lands just behind Brute despite lower Agility than Rook/Slick.
	await _set_rolls(level, [5, 1, 1, 1, 1, 1, 1, 1])
	_check(Array(level.GetInitiativeOrderNames()) ==
			["Brute", "Vex", "Rook", "Slick", "Gutter", "Kaito", "Mira", "Wisp"],
			"high d5 roll lifts Vex to 2nd (got %s)" % str(level.GetInitiativeOrderNames()))

	# --- Turn advancement: everyone acts once, then a new round begins. ---
	# Enemies auto-move + auto-end; party members use END TURN.
	await _set_rolls(level, [1, 1, 1, 1, 1, 1, 1, 1])
	# Brute auto-ended during settle. Remaining order: Rook..Wisp.
	var expected := ["Brute", "Rook", "Slick", "Vex", "Gutter", "Kaito", "Mira", "Wisp"]
	var remaining := ["Rook", "Slick", "Vex", "Gutter", "Kaito", "Mira", "Wisp"]
	for i in remaining.size():
		await _advance_turn(level, end_turn, remaining[i])
	# After all 8 acted, a new round begins. Initiative was rolled once, so
	# the order persists into round 2 (no re-roll) and round 3.
	_check(level.GetRound() == 2, "new round starts after everyone acted (got %d)" % level.GetRound())
	_check(turn.text.begins_with("ROUND 2"), "HUD shows round 2 (got '%s')" % turn.text)
	_check(Array(level.GetInitiativeOrderNames()) == expected,
			"initiative order persists into round 2 (got %s)" % str(level.GetInitiativeOrderNames()))
	_check(Array(level.GetInitiativeTotals()) == [13, 11, 16, 11, 12, 14, 17, 10],
			"initiative totals persist into round 2 (got %s)" % str(level.GetInitiativeTotals()))
	# Round 2: Brute is current (enemy, will auto-end).
	await _advance_turn(level, end_turn, "Brute")
	var round2_rest := ["Rook", "Slick", "Vex", "Gutter", "Kaito", "Mira", "Wisp"]
	for i in round2_rest.size():
		await _advance_turn(level, end_turn, round2_rest[i])
	_check(level.GetRound() == 3, "round 3 starts after second pass (got %d)" % level.GetRound())
	_check(Array(level.GetInitiativeOrderNames()) == expected,
			"initiative order persists into round 3 (got %s)" % str(level.GetInitiativeOrderNames()))

	# --- Movement range: 5 + Agility/2 (floored), per active combatant. ---
	# Range is read right after ForceCurrent (before any settle), because an
	# enemy's auto-move would end its turn and hand the range to the next
	# combatant. Party members don't auto-end, so Vex is safe to settle.
	await _set_rolls(level, [1, 1, 1, 1, 1, 1, 1, 1])
	# Force Vex (agi 12) active -> range 5 + 6 = 11.
	level.ForceCurrent("Vex")
	await _settle(level)
	_check(level.GetCurrentName() == "Vex", "Vex is active for movement test")
	_check(level.GetMovementRange() == 11, "Vex (12) moves 11 (got %d)" % level.GetMovementRange())
	# Force Wisp (agi 9) active -> range 5 + 4 = 9 (floor of 9/2).
	level.ForceCurrent("Wisp")
	_check(level.GetMovementRange() == 9, "Wisp (9) moves 9 (got %d)" % level.GetMovementRange())
	# Force Brute (agi 16) active -> range 5 + 8 = 13.
	level.ForceCurrent("Brute")
	_check(level.GetMovementRange() == 13, "Brute (16) moves 13 (got %d)" % level.GetMovementRange())

	# --- Pathfinding: reachable set respects range + obstacles + tokens. ---
	# Vex starts at (1,1), agi 12 -> range 11. Rook sits at (1,2), so the
	# straight-down route is blocked and the left column (x=0) is the detour.
	level.ForceCurrent("Vex")
	await _settle(level)
	# Own cell is reachable.
	_check(level.IsReachable(1, 1), "Vex's own cell is reachable")
	# One step left is reachable.
	_check(level.IsReachable(0, 1), "Vex reaches (0,1) one step left")
	# Down the open left column (clear to the bottom edge, row 15) (0,15) is
	# exactly 14 steps -> out of range.
	_check(not level.IsReachable(0, 15), "Vex cannot reach (0,15) (14 steps, range 11)")
	_check(level.IsReachable(0, 11), "Vex reaches (0,11) (11 steps down the open lane)")
	# (1,15) needs a 14-step detour around Rook -> out of range.
	_check(not level.IsReachable(1, 15), "Vex cannot reach (1,15) (14-step detour)")
	# Obstacles are never reachable (6,3 is a wall on the 28x16 map).
	_check(not level.IsReachable(6, 3), "obstacle (6,3) not reachable")

	# --- Camera panning: WASD pans the viewport over the board. ---
	# Levels can be far larger than the window (up to 2000x2000 grids), so the
	# camera clamps to the board's edges. Test the pan + clamp on a large board.
	var p0 := level.get_node("Board/P0")
	var board := level.get_node("Board")
	var viewport: Vector2 = level.size
	level.SetBoardPixelSize(10000, 10000)
	await process_frame
	_check(level.GetCameraPos() == Vector2(0, 0), "camera starts at the board origin")
	_check(board.position == Vector2(0, 0), "board offset is zero at the origin")
	# Panning right/down moves the viewport into the board.
	level.PanCameraBy(Vector2(500, 300))
	_check(level.GetCameraPos() == Vector2(500, 300), "camera pans to (500,300)")
	_check(board.position == Vector2(-500, -300), "board offset tracks the camera")
	# Panning far past the bottom-right edge clamps to the board's edge.
	level.PanCameraBy(Vector2(99999, 99999))
	_check(level.GetCameraPos() == Vector2(10000 - viewport.x, 10000 - viewport.y),
			"camera clamps to the board's bottom-right edge")
	# Panning far past the top-left edge clamps back to the origin.
	level.PanCameraBy(Vector2(-99999, -99999))
	_check(level.GetCameraPos() == Vector2(0, 0), "camera clamps back to the origin")
	_check(board.position == Vector2(0, 0), "board offset returns to zero")
	# Holding a WASD key pans the camera (movement is mouse-only now, so WASD
	# no longer moves any token). SetPanKey simulates holding D.
	level.SetPanKey(KEY_D)
	await process_frame
	_check(level.GetCameraPos().x > 0, "holding D pans the camera right")
	_check(p0.position == Vector2(1 * TILE, 1 * TILE),
			"WASD no longer moves the token")
	level.SetPanKey(KEY_NONE)
	await process_frame
	level.SetBoardPixelSize(28 * TILE, 16 * TILE)
	await process_frame

	# --- Click-to-move: mouse-only. Click a reachable tile, Vex jumps there. ---
	# Vex is at (1,1). Click (0,6): 6 tiles via left column, reachable.
	var click := InputEventMouseButton.new()
	click.button_index = MOUSE_BUTTON_LEFT
	click.pressed = true
	click.position = Vector2(0 * TILE + TILE / 2, 6 * TILE + TILE / 2)
	board.gui_input.emit(click)
	await process_frame
	_check(p0.position == Vector2(0, 6 * TILE), "click moved Vex to (0,6)")
	_check(level.HasMovedThisTurn(), "Vex has moved this turn")

	# A second click is ignored (a party member moves once per turn).
	var click_again := InputEventMouseButton.new()
	click_again.button_index = MOUSE_BUTTON_LEFT
	click_again.pressed = true
	click_again.position = Vector2(0 * TILE + TILE / 2, 5 * TILE + TILE / 2)
	board.gui_input.emit(click_again)
	await process_frame
	_check(p0.position == Vector2(0, 6 * TILE), "second click ignored (once per turn)")

	# --- UNDO restores the turn-start cell and re-arms the move. ---
	level.UndoMove()
	await process_frame
	_check(p0.position == Vector2(1 * TILE, 1 * TILE), "undo restored Vex to (1,1)")
	_check(not level.HasMovedThisTurn(), "undo re-armed the move")
	# Move again by click.
	var click2 := InputEventMouseButton.new()
	click2.button_index = MOUSE_BUTTON_LEFT
	click2.pressed = true
	click2.position = Vector2(0 * TILE + TILE / 2, 6 * TILE + TILE / 2)
	board.gui_input.emit(click2)
	await process_frame
	_check(p0.position == Vector2(0, 6 * TILE), "Vex moved to (0,6) after undo")

	# --- Enemy AI: on an enemy's turn it moves toward the nearest party
	# member (no party member is at weapon range from Brute here), then its
	# turn ends automatically (no player input needed). Brute at (26,1) with
	# pistol range 4 and movement 13: nearest player is Vex at (1,1); row y=1
	# is clear from x=1 to x=26, so it walks west 13 tiles to (13,1) — 12 from
	# Vex, still out of range — deterministic under the pinned pistols. ---
	# Reset to a known layout: party clustered left, enemies spread on the
	# right so Brute's westward lane along row y=1 is unobstructed.
	level.SetTokenCell("Vex", 1, 1)
	level.SetTokenCell("Kaito", 2, 1)
	level.SetTokenCell("Rook", 1, 2)
	level.SetTokenCell("Mira", 2, 2)
	level.SetTokenCell("Brute", 26, 1)
	level.SetTokenCell("Slick", 26, 3)
	level.SetTokenCell("Gutter", 25, 3)
	level.SetTokenCell("Wisp", 25, 4)
	level.ForceCurrent("Brute")
	await process_frame
	var brute_before: Vector2i = level.GetTokenCell("Brute")
	_check(brute_before == Vector2i(26, 1), "Brute starts at (26,1) (got %s)" % str(brute_before))
	# Wait for the enemy AI (armed for 12 frames) to fire.
	for i in 20:
		await process_frame
	var brute_after: Vector2i = level.GetTokenCell("Brute")
	var moved := Vector2i(brute_after.x - brute_before.x, brute_after.y - brute_before.y)
	_check(abs(moved.x) + abs(moved.y) == 13,
			"enemy Brute moved its full 13-tile range toward the players (delta %s)" % str(moved))
	_check(brute_after == Vector2i(13, 1),
			"enemy Brute moved toward the players (got %s, want (13,1))" % str(brute_after))
	_check(level.GetCurrentName() != "Brute",
			"enemy turn auto-ended after acting (now '%s')" % level.GetCurrentName())

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

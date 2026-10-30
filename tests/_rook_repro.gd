extends SceneTree
# Faithful click oracle: routes REAL clicks through the level root gui_input
# (window-local coords) exactly like the player, so the full coordinate
# transform (OnLevelGuiInput -> board-local -> CellFromIso) and the menu
# guard are exercised. The window is sized to 1280x720 so the level (full-rect
# anchored) and the action menu sit where they do in the real game.
var _fails := 0

func _initialize() -> void:
	var level: Node = load("res://scenes/level_01.tscn").instantiate()
	root.add_child(level)
	root.size = Vector2(1280, 720)
	await process_frame
	await process_frame
	var board: Node = level.get_node("Board")
	level.ResetCamera()
	await process_frame
	print("window=", root.size, " level.rect=", level.get_global_rect())
	print("board.global=", board.get_global_position(), " camera=", level.GetCameraPos())
	var menu: Node = level.get_node("ActionMenu")
	print("menu rect=", menu.get_global_rect())
	level.ForceCurrent("Rook")
	await process_frame
	assert(level.GetCurrentName() == "Rook", "turn is Rook")
	print("rook=", level.GetTokenCell("Rook"), " range=", level.GetMovementRange())

	var total := 0
	var click_fails := []
	var menu_blocked := 0
	for y in 16:
		for x in 28:
			if not level.IsReachable(x, y):
				continue
			total += 1
			var screen: Vector2 = level.IsoCellCenter(Vector2i(x, y)) + board.get_global_position()
			# Is the cell center under the action menu? (informational only)
			if menu.get_global_rect().has_point(screen):
				menu_blocked += 1
			_click(level, screen)
			await process_frame
			if level.GetTokenCell("Rook") != Vector2i(x, y):
				click_fails.append([x, y, level.GetTokenCell("Rook")])
			level.UndoMove()
			await process_frame
	print("reachable cells: ", total, " (under-menu: ", menu_blocked, ")")
	print("CLICK fails: ", click_fails.size())
	for f in click_fails:
		_fails += 1
		print("  click fail at (%d,%d) ended at %s" % [f[0], f[1], str(f[2])])
	if _fails == 0:
		print("ALL OK: every reachable cell is clickable for Rook")
	else:
		printerr("TOTAL FAILURES: ", _fails)
	quit(1 if _fails > 0 else 0)

func _click(level: Node, screen_pos: Vector2) -> void:
	var e := InputEventMouseButton.new()
	e.button_index = MOUSE_BUTTON_LEFT
	e.pressed = true
	e.position = screen_pos
	level.gui_input.emit(e)

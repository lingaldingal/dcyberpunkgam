extends SceneTree
# Functional cutscene test: menu -> SCENE TEST scene -> dialogue reveals one
# character at a time, speakers alternate, and the active speaker's portrait
# glows while the other dims.
# Run: godot --headless --path . -s res://tests/cutscene_test.gd

var _failures := 0


func _initialize() -> void:
	_run_cutscene()


func _run_cutscene() -> void:
	var menu: Node = load("res://scenes/main_menu.tscn").instantiate()
	root.add_child(menu)
	await process_frame

	# SCENE TEST button transitions to the cutscene.
	menu.get_node("Center/Panel/SceneTest").pressed.emit()
	var scene: Node = null
	for i in 60:
		await process_frame
		scene = root.get_node_or_null("SceneTest")
		if scene != null:
			break
	_check(scene != null, "SCENE TEST loads the cutscene")
	if scene == null:
		_finish()
		return
	await process_frame

	# The dialogue is the cheese conversation with alternating speakers.
	_check(scene.GetLineCount() == 6, "cutscene has 6 dialogue lines (got %d)" % scene.GetLineCount())

	# --- Reveal: text grows one character at a time. ---
	scene.SetLine(0)
	for i in 3:
		await process_frame
	var full_line: String = scene.GetLineText()
	var plain: String = scene.GetTextPlain()
	_check(plain.begins_with("Vex:"), "text starts with speaker name (got '%s')" % plain.left(12))
	_check(plain.length() < full_line.length() + 5, "line starts partial, not fully revealed (got %d chars shown)" % (plain.length() - 5))
	# Finish the reveal: the full line must then be present.
	scene.SkipReveal()
	await process_frame
	_check(scene.GetTextPlain() == "Vex: " + full_line, "SkipReveal shows the full line")

	# The line holds (no auto-advance); an explicit Advance() moves on.
	for i in 30:
		await process_frame
	_check(scene.GetLineIndex() == 0, "line holds until advanced (auto-advance removed)")
	scene.Advance()
	await process_frame
	_check(scene.GetLineIndex() == 1, "Advance() advances to the next line (got %d)" % scene.GetLineIndex())
	_check(scene.GetSpeakerName() == "Kaito", "next line is spoken by Kaito (got '%s')" % scene.GetSpeakerName())
	_check(scene.GetTextPlain().begins_with("Kaito:"), "Kaito's line text is showing")

	# --- Speaker highlight: active portrait full + glow on, other dimmed. ---
	await process_frame
	var left: Node = scene.get_node("LeftCharacter")
	var right: Node = scene.get_node("RightCharacter")
	_check(right.modulate.a >= 0.99 and left.modulate.a < 0.6,
			"active (right) highlighted, other dimmed (left a=%s, right a=%s)" % [str(left.modulate.a), str(right.modulate.a)])
	_check(right.get_node("Glow").visible, "active character's glow is visible")
	_check(not left.get_node("Glow").visible, "inactive character's glow is hidden")

	# --- Speakers alternate across all six lines. ---
	var speakers := []
	for i in 6:
		scene.SetLine(i)
		await process_frame
		speakers.append(scene.GetSpeakerName())
	_check(speakers == ["Vex", "Kaito", "Vex", "Kaito", "Vex", "Kaito"],
			"speakers alternate Vex/Kaito (got %s)" % str(speakers))

	# --- Answering the last line ends the scene and returns to the menu. ---
	scene.SetLine(5)
	for i in 30:
		await process_frame
	scene.SkipReveal()
	await process_frame
	# Click an empty area (top, between the portraits) so no button swallows it.
	var pos: Vector2 = Vector2(640, 30)
	var down := InputEventMouseButton.new()
	down.button_index = MOUSE_BUTTON_LEFT
	down.pressed = true
	down.position = pos
	Input.parse_input_event(down)
	var up := InputEventMouseButton.new()
	up.button_index = MOUSE_BUTTON_LEFT
	up.pressed = false
	up.position = pos
	Input.parse_input_event(up)
	var ended := false
	for i in 60:
		await process_frame
		if root.get_node_or_null("MainMenu") != null:
			ended = true
			break
	_check(ended, "clicking on the last line ends the scene and returns to the menu")

	# BACK returns to the menu from a fresh cutscene.
	var menu2: Node = root.get_node("MainMenu")
	menu2.get_node("Center/Panel/SceneTest").pressed.emit()
	var scene2: Node = null
	for i in 60:
		await process_frame
		scene2 = root.get_node_or_null("SceneTest")
		if scene2 != null:
			break
	_check(scene2 != null, "cutscene reloaded for BACK check")
	scene2.get_node("Back").pressed.emit()
	await process_frame
	_check(root.get_node_or_null("MainMenu") != null, "BACK returns to menu")

	_finish()


func _check(condition: bool, message: String) -> void:
	if condition:
		print("  ok: ", message)
	else:
		_failures += 1
		printerr("  FAIL: ", message)


func _finish() -> void:
	if _failures > 0:
		printerr("CUTSCENE TEST FAILED: %d check(s) failed" % _failures)
	else:
		print("CUTSCENE TEST PASSED")
	quit(1 if _failures > 0 else 0)

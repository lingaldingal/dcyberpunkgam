extends SceneTree
# Resolution selector test: the menu's RESOLUTION button cycles the window
# through the three supported sizes (1280x720 -> 1600x900 -> 1920x1080 -> ...).
# The menu's GetResolution() reports the selected size; pressing the button
# advances to the next one and resizes the window via DisplayServer.
# Run: godot --headless --path . -s res://tests/resolution_test.gd

var _failures := 0


func _initialize() -> void:
	var menu: Node = load("res://scenes/main_menu.tscn").instantiate()
	root.add_child(menu)
	await process_frame

	# The menu opens on 1280x720 (the project default).
	_check(menu.GetResolution() == Vector2i(1280, 720),
			"menu opens on 1280x720 (got %s)" % str(menu.GetResolution()))

	var button: Button = menu.get_node("Center/Panel/Resolution")

	# Each press advances to the next supported resolution, cycling back to the
	# start after 1920x1080. (DisplayServer.window_get_size() is not tracked in
	# headless mode, so we assert the menu's selected resolution instead.)
	var expected := [Vector2i(1600, 900), Vector2i(1920, 1080), Vector2i(1280, 720)]
	for i in expected.size():
		button.pressed.emit()
		await process_frame
		_check(menu.GetResolution() == expected[i],
				"press %d selects %s (got %s)" % [i + 1, str(expected[i]), str(menu.GetResolution())])

	_finish()


func _check(condition: bool, message: String) -> void:
	if condition:
		print("  ok: ", message)
	else:
		_failures += 1
		printerr("  FAIL: ", message)


func _finish() -> void:
	if _failures > 0:
		printerr("RESOLUTION TEST FAILED: %d check(s) failed" % _failures)
	else:
		print("RESOLUTION TEST PASSED")
	quit(1 if _failures > 0 else 0)

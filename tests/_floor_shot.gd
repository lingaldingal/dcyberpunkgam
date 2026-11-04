extends SceneTree
# Rendered check of the full-board junkyard floor: load level_01 in move
# mode (no overlay), let it settle, save a PNG (run NON-headless, DISPLAY).
func _initialize():
	root.size = Vector2(1280, 720)
	var level: Node = load("res://scenes/level_01.tscn").instantiate()
	root.add_child(level)
	for i in 40:
		await process_frame
	level.ResetCamera()
	for i in 8:
		await process_frame
	var img = root.get_texture().get_image()
	img.save_png("res://tests/_floor_shot.png")
	print("SAVED floor screenshot ", img.get_width(), "x", img.get_height())
	quit(0)

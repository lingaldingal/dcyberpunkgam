extends SceneTree
# Rendered check of the isometric level: load level_01, let it settle,
# save a PNG for visual inspection (run NON-headless with DISPLAY set).
func _initialize():
	var level: Node = load("res://scenes/level_01.tscn").instantiate()
	root.add_child(level)
	for i in 30:
		await process_frame
	level.SetActionMode(1)  # shoot mode: shows the range overlay
	for i in 5:
		await process_frame
	var img = root.get_texture().get_image()
	img.save_png("res://tests/_iso_shot.png")
	print("SAVED screenshot ", img.get_width(), "x", img.get_height())
	quit(0)

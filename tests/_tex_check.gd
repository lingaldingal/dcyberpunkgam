extends SceneTree
# Check if floor texture is loaded and visible in the level.
func _initialize():
	var level: Node = load("res://scenes/level_01.tscn").instantiate()
	root.add_child(level)
	for i in 10:
		await process_frame
	
	# Check the texture file exists
	var path = "res://assets/textures/floor/stone_floor_0.png"
	if FileAccess.file_exists(path):
		print("OK: File exists: ", path)
	else:
		print("FAIL: File NOT found: ", path)
	
	# Check FloorTextureName on the level
	print("FloorTextureName: ", level.FloorTextureName)
	
	# Take a screenshot
	for i in 10:
		await process_frame
	var img = root.get_texture().get_image()
	img.save_png("res://tests/_tex_check.png")
	print("SAVED screenshot ", img.get_width(), "x", img.get_height())
	quit(0)

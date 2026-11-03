extends SceneTree
# Simulate the actual game flow: load the level scene directly (same as
# GameManager.StartGame does) and take a screenshot of the textured floor.
func _initialize():
	var level: Node = load("res://scenes/level_01.tscn").instantiate()
	root.add_child(level)
	# Let it settle (camera centering, grid build, texture load)
	for i in 30:
		await process_frame
	
	# Verify texture is loaded
	var grid = level.get_node("Board/Iso/Grid")
	var tex = grid.FloorTexture
	print("FloorTexture: ", "LOADED " + str(tex.get_size()) if tex != null else "NULL")
	
	# Take full view screenshot
	for i in 5:
		await process_frame
	var img = root.get_texture().get_image()
	img.save_png("res://tests/_game_shot_full.png")
	print("SAVED full view: ", img.get_width(), "x", img.get_height())
	
	# Also take a zoomed-in view by panning to center and zooming
	level.ZoomBy(1)  # zoom in one notch
	level.ZoomBy(1)  # zoom in another notch
	for i in 5:
		await process_frame
	img = root.get_texture().get_image()
	img.save_png("res://tests/_game_shot_zoom.png")
	print("SAVED zoomed view: ", img.get_width(), "x", img.get_height())
	
	quit(0)

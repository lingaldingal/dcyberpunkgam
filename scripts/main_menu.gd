extends Control


func _ready() -> void:
	$Center/Panel/NewGame.pressed.connect(_on_new_game_pressed)
	$Center/Panel/Continue.pressed.connect(_on_continue_pressed)
	$Center/Panel/Quit.pressed.connect(_on_quit_pressed)


func _on_new_game_pressed() -> void:
	GameManager.start_game()


func _on_continue_pressed() -> void:
	# Placeholder: no save system yet.
	$Center/Panel/Status.text = "No saved game yet."


func _on_quit_pressed() -> void:
	get_tree().quit()

extends Control


func _ready() -> void:
	$Buttons/StartGame.pressed.connect(_on_start_game_pressed)
	$Buttons/QuitGame.pressed.connect(_on_quit_game_pressed)


func _on_start_game_pressed() -> void:
	GameManager.start_game()


func _on_quit_game_pressed() -> void:
	get_tree().quit()

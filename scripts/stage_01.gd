extends Control


func _ready() -> void:
	$Center/Panel/Back.pressed.connect(_on_back_pressed)


func _on_back_pressed() -> void:
	GameManager.quit_to_menu()

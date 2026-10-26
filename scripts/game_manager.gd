extends Node
## Autoload: scene flow between the menu and stages.

const STAGE_01_SCENE := "res://scenes/stage_01.tscn"
const MAIN_MENU_SCENE := "res://scenes/main_menu.tscn"


func start_game() -> void:
	get_tree().change_scene_to_file(STAGE_01_SCENE)


func quit_to_menu() -> void:
	get_tree().change_scene_to_file(MAIN_MENU_SCENE)

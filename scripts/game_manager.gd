extends Node
## Autoload. Owns the party and scene flow between menu and stages.

const MAX_PARTY_SIZE := 5
const STAGE_01_SCENE := "res://scenes/stage_01.tscn"
const MAIN_MENU_SCENE := "res://scenes/main_menu.tscn"

var party: Array[CharacterData] = []
var active_index: int = 0


func _ready() -> void:
	if party.is_empty():
		_create_default_party()


func _create_default_party() -> void:
	party = []
	party.append(CharacterData.new())
	party[0].name = "Riko"
	party.append(CharacterData.new())
	party[1].name = "Vex"
	party.append(CharacterData.new())
	party[2].name = "Nyx"


func get_active() -> CharacterData:
	return party[active_index]


func set_active(index: int) -> void:
	if index >= 0 and index < party.size():
		active_index = index


func start_game() -> void:
	get_tree().change_scene_to_file(STAGE_01_SCENE)


func quit_to_menu() -> void:
	get_tree().change_scene_to_file(MAIN_MENU_SCENE)

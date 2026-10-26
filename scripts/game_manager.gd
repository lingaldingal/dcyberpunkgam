extends Node
## Autoload. Owns the party and scene flow between menu and stages.

const MAX_PARTY_SIZE := 5
const STAGE_01_SCENE := "res://scenes/stage_01.tscn"
const MAIN_MENU_SCENE := "res://scenes/main_menu.tscn"
# Preloaded (a const, evaluated at compile time) so the CharacterData class_name
# is registered even on a fresh clone, before the first editor import builds
# the .godot/ cache.
const CharacterDataScript := preload("res://scripts/character_data.gd")

var party: Array = []
var active_index: int = 0


func _ready() -> void:
	if party.is_empty():
		_create_default_party()


func _create_default_party() -> void:
	party = []
	var riko = CharacterDataScript.new()
	riko.name = "Riko"
	party.append(riko)
	var vex = CharacterDataScript.new()
	vex.name = "Vex"
	party.append(vex)
	var nyx = CharacterDataScript.new()
	nyx.name = "Nyx"
	party.append(nyx)


func get_active():
	return party[active_index]


func set_active(index: int) -> void:
	if index >= 0 and index < party.size():
		active_index = index


func start_game() -> void:
	get_tree().change_scene_to_file(STAGE_01_SCENE)


func quit_to_menu() -> void:
	get_tree().change_scene_to_file(MAIN_MENU_SCENE)

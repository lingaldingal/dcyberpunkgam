class_name CharacterData
extends Resource
## Stats for one party member.

@export var name: String = "Riko"
@export var max_hp: int = 100
@export var hp: int = 100
@export var attack: int = 10
@export var defense: int = 5
@export var level: int = 1
@export var exp: int = 0
@export var exp_to_next: int = 100


func add_exp(amount: int) -> int:
	"""Adds exp, handles level ups. Returns the number of levels gained."""
	exp += amount
	var levels_gained := 0
	while exp >= exp_to_next:
		exp -= exp_to_next
		level += 1
		levels_gained += 1
		max_hp += 10
		hp = max_hp
		attack += 2
		defense += 1
		exp_to_next = int(exp_to_next * 1.5)
	return levels_gained

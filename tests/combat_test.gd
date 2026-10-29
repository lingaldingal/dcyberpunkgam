extends SceneTree
# Combat test: Constitution/HP values, Shoot action (exactly 4 straight
# squares, 8 directions, 2 damage), death (token removed + turn skipped),
# and the bottom-left actions menu (name on top, always on screen).
# Run: godot --headless --path . -s res://tests/combat_test.gd

const TILE := 48
const SHOOT := 1  # Level01.ActionMode.Shoot
const MOVE := 0   # Level01.ActionMode.Move

var _failures := 0
var level: Node = null

# Weapons are rolled randomly at encounter start; the AI also shoots, so pin
# everyone to the pistol (range 4, 2 dmg) for deterministic expectations.
const NAMES := ["Vex", "Kaito", "Rook", "Mira", "Gutter", "Slick", "Brute", "Wisp"]


func _pin_weapons() -> void:
	for n in NAMES:
		level.SetWeapon(n, false)
	await process_frame


func _initialize() -> void:
	_run()


func _run() -> void:
	var menu: Node = load("res://scenes/main_menu.tscn").instantiate()
	root.add_child(menu)
	await process_frame
	menu.get_node("Center/Panel/NewGame").pressed.emit()
	level = null
	for i in 60:
		await process_frame
		level = root.get_node_or_null("Level01")
		if level != null:
			break
	_check(level != null, "NEW GAME loads level_01")
	if level == null:
		_finish()
		return
	await process_frame
	var board: Node = level.get_node("Board")
	await _pin_weapons()

	# --- HP = 5 + Constitution for everyone; Constitution in [7, 13]. ---
	_check(level.GetHitPoints("Vex") == 16, "Vex HP = 5 + Con 11 = 16 (got %d)" % level.GetHitPoints("Vex"))
	_check(level.GetHitPoints("Kaito") == 14, "Kaito HP = 5 + Con 9 = 14 (got %d)" % level.GetHitPoints("Kaito"))
	_check(level.GetHitPoints("Rook") == 17, "Rook HP = 5 + Con 12 = 17 (got %d)" % level.GetHitPoints("Rook"))
	_check(level.GetHitPoints("Mira") == 13, "Mira HP = 5 + Con 8 = 13 (got %d)" % level.GetHitPoints("Mira"))
	_check(level.GetHitPoints("Gutter") == 15, "Gutter HP = 5 + Con 10 = 15 (got %d)" % level.GetHitPoints("Gutter"))
	_check(level.GetHitPoints("Slick") == 18, "Slick HP = 5 + Con 13 = 18 (got %d)" % level.GetHitPoints("Slick"))
	_check(level.GetHitPoints("Brute") == 17, "Brute HP = 5 + Con 12 = 17 (got %d)" % level.GetHitPoints("Brute"))
	_check(level.GetHitPoints("Wisp") == 12, "Wisp HP = 5 + Con 7 = 12 (got %d)" % level.GetHitPoints("Wisp"))

	# --- Actions menu: bottom-left, on screen, active character's name on top. ---
	var menu_node: Node = level.get_node("ActionMenu")
	var menu_size: Vector2 = menu_node.size
	var name_label: Node = level.get_node("ActionMenu/VBox/Name")
	_check(name_label != null, "actions menu has the character name on top")

	# Force a realistic viewport so the menu's true bottom-left placement is
	# exercised (headless default is a 64x64 window where it clamps).
	level.size = Vector2(1280, 720)
	for i in 3:
		await process_frame
	var menu_pos: Vector2 = level.GetActionMenuPos()
	var view: Vector2 = level.size
	_check(menu_pos.x == 12.0, "actions menu hugs the left edge (got %s)" % str(menu_pos))
	_check(abs(menu_pos.y - (view.y - menu_size.y - 12.0)) < 0.5,
			"actions menu is bottom-left (got y=%s, want %s)" % [str(menu_pos.y), str(view.y - menu_size.y - 12.0)])
	_check(menu_pos.x >= 0.0 and menu_pos.y >= 0.0
			and menu_pos.x + menu_size.x <= view.x and menu_pos.y + menu_size.y <= view.y,
			"actions menu is fully on screen (got %s, size %s, view %s)"
			% [str(menu_pos), str(menu_size), str(view)])
	if name_label != null:
		_check(String(name_label.text) == level.GetCurrentName(),
				"menu name matches active character (got '%s', active '%s')" %
				[String(name_label.text), level.GetCurrentName()])
	# Restore the small headless view (the menu clamps back on screen).
	level.size = Vector2(64, 64)
	for i in 3:
		await process_frame
	var tiny_pos: Vector2 = level.GetActionMenuPos()
	_check(tiny_pos.x >= 0.0 and tiny_pos.y >= 0.0,
			"menu stays on screen on a tiny viewport (got %s)" % str(tiny_pos))

	# --- Equipment: everyone has head + body gear + a pistol/rifle. ---
	_check(level.GetEquipment("Vex") == "Pistol · Ballistic Cap · Weave Vest",
			"Vex equipped with basic gear + pistol (got '%s')" % level.GetEquipment("Vex"))
	for n in NAMES:
		var w: String = level.GetWeaponName(n)
		_check(w == "Pistol" or w == "Rifle",
				"%s has a pistol or rifle (got '%s')" % [n, w])
		_check(level.GetEquipment(n).ends_with("· Ballistic Cap · Weave Vest"),
				"%s has the basic head/body gear (got '%s')" % [n, level.GetEquipment(n)])
	# Party members start with BioStims; enemies don't.
	_check(level.GetItemCount("Vex") == 2, "party members start with 2 BioStims (got %d)" % level.GetItemCount("Vex"))
	_check(level.GetItemCount("Brute") == 0, "enemies start with no BioStims")
	# Weapons drive the shoot mechanic: pistol 4/2, rifle 6/3.
	_check(level.GetWeaponRange("Vex") == 4 and level.GetWeaponDamage("Vex") == 2,
			"pistol is range 4, 2 dmg (got %d/%d)" % [level.GetWeaponRange("Vex"), level.GetWeaponDamage("Vex")])
	level.SetWeapon("Vex", true)
	_check(level.GetWeaponName("Vex") == "Rifle", "SetWeapon swaps Vex to the rifle")
	_check(level.GetWeaponRange("Vex") == 6 and level.GetWeaponDamage("Vex") == 3,
			"rifle is range 6, 3 dmg (got %d/%d)" % [level.GetWeaponRange("Vex"), level.GetWeaponDamage("Vex")])
	level.SetWeapon("Vex", false)
	await process_frame

	# --- BioStim: heals 6 HP (capped at max), consumes the item + the action. ---
	level.ForceCurrent("Vex")
	var hp_before: int = level.GetHitPoints("Vex")
	level.UseItem("Vex")
	_check(level.GetHitPoints("Vex") == hp_before,
			"BioStim at full HP does nothing (got %d, was %d)" % [level.GetHitPoints("Vex"), hp_before])
	# Damage Vex: enemy Brute (pistol) sits at exactly weapon range 4 and
	# auto-shoots Vex for 2. Row y=1 is completely clear, so the line of
	# sight is open.
	level.SetTokenCell("Vex", 5, 1)
	level.SetTokenCell("Brute", 9, 1)
	level.ForceCurrent("Brute")
	for i in 24:
		await process_frame
	var vex_wounded: int = level.GetHitPoints("Vex")
	_check(vex_wounded == 14, "enemy AI shot Vex for pistol damage 2 (got %d HP, want 14)" % vex_wounded)
	# Use the BioStim via the action menu: Vex is active now (after Brute's turn).
	level.ForceCurrent("Vex")
	level.get_node("ActionMenu/VBox/Actions/Heal").pressed.emit()
	await process_frame
	_check(level.GetHitPoints("Vex") == 16, "BioStim healed Vex back to 16 (got %d)" % level.GetHitPoints("Vex"))
	_check(level.GetItemCount("Vex") == 1, "BioStim count dropped to 1 (got %d)" % level.GetItemCount("Vex"))
	# Heal again after damage; count 1 -> 0.
	level.SetTokenCell("Vex", 5, 1)
	level.SetTokenCell("Brute", 9, 1)
	level.ForceCurrent("Brute")
	for i in 24:
		await process_frame
	level.ForceCurrent("Vex")
	level.get_node("ActionMenu/VBox/Actions/Heal").pressed.emit()
	await process_frame
	_check(level.GetHitPoints("Vex") == 16 and level.GetItemCount("Vex") == 0,
			"second BioStim used, none left (got %d HP, %d stims)" % [level.GetHitPoints("Vex"), level.GetItemCount("Vex")])
	# No stims left: the menu's heal button is disabled.
	var heal_btn: Button = level.get_node("ActionMenu/VBox/Actions/Heal")
	_check(heal_btn.disabled == true, "BIOSTIM button disabled at 0 stims")
	_check(heal_btn.text == "BIOSTIM x0", "BIOSTIM button shows the count (got '%s')" % heal_btn.text)

	# --- Enemy AI: shoots a party member exactly at weapon range. ---
	# (Brute's shots above already prove it: Vex lost exactly pistol damage
	# each turn instead of being ignored.)
	_check(level.GetHitPoints("Kaito") == 14, "AI only shot the in-range target (Kaito untouched, got %d)" % level.GetHitPoints("Kaito"))

	# --- Enemy AI: moves toward the nearest party member when out of range. ---
	# Vex far west, Brute east; force Brute active and let it act. Brute
	# (pistol range 4, move 13) walks the clear row y=1 west 13 tiles to
	# (13,1) — 12 from Vex, so no shot is possible after the move either.
	level.SetTokenCell("Vex", 0, 1)
	level.SetTokenCell("Kaito", 0, 2)
	level.SetTokenCell("Rook", 0, 3)
	level.SetTokenCell("Mira", 0, 4)
	level.SetTokenCell("Slick", 26, 3)
	level.SetTokenCell("Gutter", 25, 3)
	level.SetTokenCell("Wisp", 25, 4)
	level.SetTokenCell("Brute", 26, 1)
	level.ForceCurrent("Brute")
	var brute_before: Vector2i = level.GetTokenCell("Brute")
	for i in 24:
		await process_frame
	var brute_after: Vector2i = level.GetTokenCell("Brute")
	_check(brute_after == Vector2i(13, 1),
			"enemy moved its full 13-tile range toward the players (got %s)" % str(brute_after))

	# --- Rifle shoot range: exactly 6 straight squares, 3 damage. ---
	# Vex sits at (9,1) on the clear top band (rows 0-2 have no walls). The
	# AI test above left Brute at (13,1); move the whole enemy cluster far
	# east so no enemy token overlaps the shoot cells being tested.
	level.SetTokenCell("Brute", 26, 1)
	level.SetTokenCell("Slick", 26, 3)
	level.SetTokenCell("Gutter", 25, 3)
	level.SetTokenCell("Wisp", 15, 1)
	level.SetTokenCell("Vex", 9, 1)
	level.SetWeapon("Vex", true)
	level.ForceCurrent("Vex")
	level.SetActionMode(SHOOT)
	_check(level.IsShootTarget(15, 1), "rifle: 6 squares east is a shoot target")
	_check(level.IsShootTarget(3, 1), "rifle: 6 squares west is a shoot target")
	_check(level.IsShootTarget(9, 7), "rifle: 6 squares south is a shoot target (clear column)")
	_check(not level.IsShootTarget(15, 7), "rifle: 6-square south-east diagonal blocked (wall at 12,4)")
	_check(not level.IsShootTarget(3, 7), "rifle: 6-square south-west diagonal blocked (wall at 7,3)")
	_check(not level.IsShootTarget(13, 1), "rifle: 4 squares east is NOT a target (pistol-only)")
	_check(not level.IsShootTarget(14, 1), "rifle: 5 squares east is not a target")
	_check(not level.IsShootTarget(16, 1), "rifle: 7 squares east is not a target")
	var click_rifle := InputEventMouseButton.new()
	click_rifle.button_index = MOUSE_BUTTON_LEFT
	click_rifle.pressed = true
	click_rifle.position = Vector2(15 * TILE + TILE / 2, 1 * TILE + TILE / 2)
	board.gui_input.emit(click_rifle)
	await process_frame
	_check(level.GetHitPoints("Wisp") == 9, "rifle shot deals 3 damage (got %d HP, was 12)" % level.GetHitPoints("Wisp"))
	level.SetWeapon("Vex", false)
	await process_frame

	# --- Pistol shoot range: exactly 4 straight squares, any of the 8 directions. ---
	# (Wisp is at 9 HP here after the rifle test.)
	# Set the cells BEFORE ForceCurrent: shoot targets are computed from the
	# active combatant's position when the turn is set. Vex at (9,1) has clear
	# lines to 4-away cells in every direction except south-west (wall at
	# 7,3) and south-east (wall at 12,4), which doubles as the line-of-sight
	# (cannot shoot through obstacles) check.
	level.SetTokenCell("Vex", 9, 1)
	level.SetTokenCell("Wisp", 13, 1)
	# Keep the other enemies far away so the cell (13,1) holds only Wisp.
	level.SetTokenCell("Brute", 26, 1)
	level.SetTokenCell("Slick", 26, 3)
	level.SetTokenCell("Gutter", 25, 3)
	level.ForceCurrent("Vex")
	level.SetActionMode(SHOOT)
	_check(level.IsShootTarget(13, 1), "enemy 4 squares east is a shoot target")
	_check(level.IsShootTarget(9, 5), "cell 4 squares south is a shoot target")
	_check(level.IsShootTarget(5, 1), "cell 4 squares west is a shoot target")
	_check(not level.IsShootTarget(13, 5), "4 squares southeast is blocked (wall at 12,4) — no shooting through obstacles")
	_check(not level.IsShootTarget(5, 5), "4 squares southwest is blocked (wall at 7,3) — no shooting through obstacles")
	_check(not level.IsShootTarget(12, 1), "3 squares away is not a shoot target")
	_check(not level.IsShootTarget(14, 1), "5 squares away is not a shoot target")
	_check(not level.IsShootTarget(12, 2), "knight's move (3,1) is not a shoot target")
	_check(not level.IsShootTarget(9, 1), "own cell is not a shoot target")

	# Clicking a non-target cell in shoot mode deals no damage (12,1 is 3 away).
	var click_wrong_cell := InputEventMouseButton.new()
	click_wrong_cell.button_index = MOUSE_BUTTON_LEFT
	click_wrong_cell.pressed = true
	click_wrong_cell.position = Vector2(12 * TILE + TILE / 2, 1 * TILE + TILE / 2)
	board.gui_input.emit(click_wrong_cell)
	await process_frame
	_check(level.GetHitPoints("Wisp") == 9, "clicking a non-target in shoot mode deals no damage (Wisp at 9 from rifle test)")

	# A real shot: click Wisp's cell (13,1). 2 damage, and the action is consumed.
	var click_shoot := InputEventMouseButton.new()
	click_shoot.button_index = MOUSE_BUTTON_LEFT
	click_shoot.pressed = true
	click_shoot.position = Vector2(13 * TILE + TILE / 2, 1 * TILE + TILE / 2)
	board.gui_input.emit(click_shoot)
	await process_frame
	_check(level.GetHitPoints("Wisp") == 7, "shoot deals 2 damage (got %d HP, was 9)" % level.GetHitPoints("Wisp"))
	# Second click: action already used this turn -> no extra damage.
	board.gui_input.emit(click_shoot)
	await process_frame
	_check(level.GetHitPoints("Wisp") == 7, "second click does not shoot again (once per turn)")
	# A valid diagonal target with no enemy there does no damage.
	level.ForceCurrent("Vex")
	level.SetActionMode(SHOOT)
	var click_diag := InputEventMouseButton.new()
	click_diag.button_index = MOUSE_BUTTON_LEFT
	click_diag.pressed = true
	click_diag.position = Vector2(9 * TILE + TILE / 2, 5 * TILE + TILE / 2)
	board.gui_input.emit(click_diag)
	await process_frame
	_check(level.GetHitPoints("Wisp") == 7, "click at empty south target does no damage")

	# --- WAIT consumes the action (no damage, no move). ---
	level.ForceCurrent("Vex")
	level.SetActionMode(SHOOT)
	level.get_node("ActionMenu/VBox/Actions/Wait").pressed.emit()
	await process_frame
	var click_after_wait := InputEventMouseButton.new()
	click_after_wait.button_index = MOUSE_BUTTON_LEFT
	click_after_wait.pressed = true
	click_after_wait.position = Vector2(13 * TILE + TILE / 2, 1 * TILE + TILE / 2)
	board.gui_input.emit(click_after_wait)
	await process_frame
	_check(level.GetHitPoints("Wisp") == 7, "WAIT consumes the action (no shot after)")

	# --- Death: HP hits 0 (or below) -> token removed from the map, no more turns. ---
	# Rook (party, pistol) shoots Wisp (7 HP here) to death: 4 shots, one per turn.
	# 7 -> 5 -> 3 -> 1 -> -1 (overshoot; death is HP <= 0).
	# Brute is forced current first: it is an enemy, so it auto-moves and
	# auto-ends, handing the turn to Rook in the [1,1,1,1,1,1,1,1] order.
	# The pistol test left Vex and Brute overlapping at (9,1); move the party
	# to the far west so Brute's turn is a deterministic 13-tile walk to
	# (13,1) with no shot possible (Vex ends up 12 away).
	level.SetTokenCell("Vex", 0, 1)
	level.SetTokenCell("Kaito", 0, 2)
	level.SetTokenCell("Rook", 0, 3)
	level.SetTokenCell("Mira", 0, 4)
	level.SetTokenCell("Slick", 26, 3)
	level.SetTokenCell("Gutter", 25, 3)
	level.SetTokenCell("Wisp", 25, 4)
	level.SetInitiativeRolls([1, 1, 1, 1, 1, 1, 1, 1])
	level.ForceCurrent("Brute")
	for i in 24:
		await process_frame
	_check(level.GetCurrentName() == "Rook",
			"Brute auto-ended, Rook is active (got '%s')" % level.GetCurrentName())
	var end_turn: Button = level.get_node("Center/Panel/Actions/EndTurn")
	var token_wisp: Node = board.get_node("E3")
	var shots := 0
	while level.IsAlive("Wisp") and shots < 8:
		level.SetTokenCell("Rook", 8, 1)
		level.SetTokenCell("Wisp", 12, 1)
		level.ForceCurrent("Rook")
		level.SetActionMode(SHOOT)
		var click := InputEventMouseButton.new()
		click.button_index = MOUSE_BUTTON_LEFT
		click.pressed = true
		click.position = Vector2(12 * TILE + TILE / 2, 1 * TILE + TILE / 2)
		board.gui_input.emit(click)
		await process_frame
		end_turn.pressed.emit()
		await process_frame
		shots += 1
	_check(level.GetHitPoints("Wisp") <= 0, "Wisp HP reached 0 (got %d)" % level.GetHitPoints("Wisp"))
	_check(not level.IsAlive("Wisp"), "Wisp is dead")
	_check(token_wisp.visible == false, "dead Wisp's token is removed from the map")
	# The turn order (rolls all 1) is Brute, Rook, Slick, Vex, Gutter, Kaito,
	# Mira, Wisp. Mira is second-to-last, so ending its turn must land on the
	# dead Wisp slot and skip straight to Brute (round wraps).
	level.ForceCurrent("Mira")
	end_turn.pressed.emit()
	await process_frame
	_check(level.GetCurrentName() == "Brute",
			"dead Wisp no longer gets a turn (Mira -> Brute, now '%s')"
			% level.GetCurrentName())
	if name_label != null:
		_check(String(name_label.text) == level.GetCurrentName(),
				"menu name follows the active character (got '%s')" % String(name_label.text))

	_finish()


func _check(condition: bool, message: String) -> void:
	if condition:
		print("  ok: ", message)
	else:
		_failures += 1
		printerr("  FAIL: ", message)


func _finish() -> void:
	if _failures > 0:
		printerr("COMBAT TEST FAILED: %d check(s) failed" % _failures)
	else:
		print("COMBAT TEST PASSED")
	quit(1 if _failures > 0 else 0)

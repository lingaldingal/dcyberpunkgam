extends SceneTree
# Combat test: Constitution/HP values, Shoot action (shooting CIRCLE: any
# cell with dx^2+dy^2 <= range^2 in any direction, line of sight blocked by
# walls AND other characters, weapon damage), the shooting-range circle
# overlay (drawn in shoot mode, radius = weapon range), shoot-after-move
# (aims from the CURRENT position), death (token removed + turn skipped), and
# the bottom-left actions menu (name on top, always on screen).
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

	# --- END TURN in the actions menu: pressing it advances the turn, same as
	# the top-right debug panel's END TURN.
	# Roll order [1..]: Brute, Rook, Slick, Vex, Gutter, Kaito, Mira, Wisp.
	# Vex -> menu END TURN -> Gutter, and Gutter (enemy) auto-moves and
	# auto-ends within ~12 frames, landing the turn on Kaito.
	level.ForceCurrent("Vex")
	await process_frame
	level.get_node("ActionMenu/VBox/TurnRow/EndTurn").pressed.emit()
	for i in 20:
		await process_frame
	_check(level.GetCurrentName() == "Kaito",
			"menu END TURN advances the turn (Vex -> Gutter auto -> Kaito, got '%s')"
			% level.GetCurrentName())
	level.ForceCurrent("Vex")
	await process_frame

	# --- Equipment: everyone has head + body gear + a pistol/rifle AND a
	# knife/katana melee weapon. ---
	var vex_equipment: String = level.GetEquipment("Vex")
	var vex_melee: String = level.GetMeleeWeaponName("Vex")
	_check(vex_melee == "Knife" or vex_melee == "Katana",
			"Vex has a knife or katana (got '%s')" % vex_melee)
	_check(vex_equipment == "%s · %s · Ballistic Cap · Weave Vest" % [level.GetWeaponName("Vex"), vex_melee],
			"Vex equipped with basic gear + weapons (got '%s')" % vex_equipment)
	for n in NAMES:
		var w: String = level.GetWeaponName(n)
		var mw: String = level.GetMeleeWeaponName(n)
		_check(w == "Pistol" or w == "Rifle",
				"%s has a pistol or rifle (got '%s')" % [n, w])
		_check(mw == "Knife" or mw == "Katana",
				"%s has a knife or katana (got '%s')" % [n, mw])
		_check(level.GetEquipment(n).ends_with("· Ballistic Cap · Weave Vest"),
				"%s has the basic head/body gear (got '%s')" % [n, level.GetEquipment(n)])
	# Melee weapons: knife 3 dmg, katana 5 dmg.
	level.SetMeleeWeapon("Vex", false)
	_check(level.GetMeleeWeaponName("Vex") == "Knife", "SetMeleeWeapon swaps Vex to the knife")
	_check(level.GetMeleeWeaponDamage("Vex") == 3, "knife does 3 damage (got %d)" % level.GetMeleeWeaponDamage("Vex"))
	level.SetMeleeWeapon("Vex", true)
	_check(level.GetMeleeWeaponName("Vex") == "Katana", "SetMeleeWeapon swaps Vex to the katana")
	_check(level.GetMeleeWeaponDamage("Vex") == 5, "katana does 5 damage (got %d)" % level.GetMeleeWeaponDamage("Vex"))
	# Pin Vex's melee weapon to the knife (3 dmg) for the melee combat checks below.
	level.SetMeleeWeapon("Vex", false)
	# The actions menu has a MELEE button next to SHOOT.
	var melee_btn: Button = level.get_node("ActionMenu/VBox/Actions/Melee")
	_check(melee_btn != null, "actions menu has a MELEE button")
	_check(String(melee_btn.text) == "MELEE", "MELEE button label (got '%s')" % String(melee_btn.text))
	_check(melee_btn.disabled == false, "MELEE button enabled on a fresh turn")
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
	# Damage Vex: Brute (pistol range 4, move 13) always spends its move
	# first, then shoots from its NEW position. The party is clustered far
	# west (x=0) and Brute sits far east at (26,1); on the clear row y=1 its
	# 13-tile walk only gets it to (13,1) (21 steps would be needed to reach
	# Vex's shooting circle), so Vex at (0,1) is 13 away -> NO shot, proving
	# move-then-shoot (a shoot-then-move AI would have fired before walking).
	level.SetTokenCell("Vex", 0, 1)
	level.SetTokenCell("Kaito", 0, 2)
	level.SetTokenCell("Rook", 0, 3)
	level.SetTokenCell("Mira", 0, 4)
	level.SetTokenCell("Brute", 26, 1)
	level.SetTokenCell("Slick", 26, 3)
	level.SetTokenCell("Gutter", 25, 3)
	level.SetTokenCell("Wisp", 25, 4)
	level.SetInitiativeRolls([1, 1, 1, 1, 1, 1, 1, 1])
	level.ForceCurrent("Brute")
	for i in 24:
		await process_frame
	_check(level.GetTokenCell("Brute") == Vector2i(13, 1),
			"enemy Brute moved its full range to (13,1) before shooting (got %s)" % str(level.GetTokenCell("Brute")))
	_check(level.GetHitPoints("Vex") == 16,
			"Vex 13 away from (13,1) is out of pistol range after the move (got %d HP)" % level.GetHitPoints("Vex"))
	# Now Vex is in range of Brute's new position: Vex at (9,1) is 4 away,
	# inside Brute's pistol shooting circle on the clear row y=1 (others stay
	# far west). Brute is already in shooting range, so it HOLDS its position
	# at (13,1) (no move needed) and shoots Vex for 2 from where it stands.
	level.SetTokenCell("Vex", 9, 1)
	level.ForceCurrent("Brute")
	for i in 24:
		await process_frame
	var vex_wounded: int = level.GetHitPoints("Vex")
	_check(vex_wounded == 14, "enemy AI shot Vex for pistol damage 2 from its moved position (got %d HP, want 14)" % vex_wounded)
	# Brute never moved this turn: it was ALREADY inside the shooting circle
	# (Vex 4 away, clear row), so the AI held its position and just shot.
	_check(level.GetTokenCell("Brute") == Vector2i(13, 1),
			"enemy already in range holds position (no move) and shoots (got %s)" % str(level.GetTokenCell("Brute")))
	# Use the BioStim via the items select list: Vex is active now (after
	# Brute's turn). The BioStim button lives in the ITEMS panel.
	level.ForceCurrent("Vex")
	level.get_node("ActionMenu/VBox/ItemsMenu/ItemsVBox/BioStim").pressed.emit()
	await process_frame
	_check(level.GetHitPoints("Vex") == 16, "BioStim healed Vex back to 16 (got %d)" % level.GetHitPoints("Vex"))
	_check(level.GetItemCount("Vex") == 1, "BioStim count dropped to 1 (got %d)" % level.GetItemCount("Vex"))
	# Heal again after damage; count 1 -> 0. Brute stands at (13,1) and Vex
	# is 4 away on the clear row — inside the pistol circle — so Brute holds
	# its position and shoots Vex at (9,1) again (14 -> 12), then the BioStim
	# brings 12 + 6 back to 16.
	level.SetTokenCell("Vex", 9, 1)
	level.SetInitiativeRolls([1, 1, 1, 1, 1, 1, 1, 1])
	level.ForceCurrent("Brute")
	for i in 24:
		await process_frame
	level.ForceCurrent("Vex")
	level.get_node("ActionMenu/VBox/ItemsMenu/ItemsVBox/BioStim").pressed.emit()
	await process_frame
	_check(level.GetHitPoints("Vex") == 16 and level.GetItemCount("Vex") == 0,
			"second BioStim used, none left (got %d HP, %d stims)" % [level.GetHitPoints("Vex"), level.GetItemCount("Vex")])
	# No stims left: the items menu's BioStim button is disabled.
	var heal_btn: Button = level.get_node("ActionMenu/VBox/ItemsMenu/ItemsVBox/BioStim")
	_check(heal_btn.disabled == true, "BIOSTIM button disabled at 0 stims")
	_check(heal_btn.text == "BIOSTIM x0", "BIOSTIM button shows the count (got '%s')" % heal_btn.text)

	# --- Enemy AI: shoots a party member within weapon range. ---
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

	# --- Rifle shooting circle: radius 6 (dx^2+dy^2 <= 36), 3 damage. ---
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
	# The shooting-range overlay is drawn in shoot mode: a range diamond of
	# 2*range pixels (6 tiles = 288px) centered on Vex at (9,1). Its center is
	# the CELL CENTER's ISO board-space position: iso(x,y) = ((x-y)/2, (x+y)/4)
	# in board pixels, cell center (9.5, 1.5) -> ((9.5-1.5)/2, (9.5+1.5)/4)*48
	# = (192, 132).
	var circle: Array = level.GetRangeCircle()
	_check(circle[2] > 0.0 and circle[3] > 0.0,
			"shooting circle is drawn when shoot is selected (got %s)" % str(circle))
	_check(absf(circle[2] - 12 * TILE) < 0.5 and absf(circle[3] - 12 * TILE) < 0.5,
			"circle diameter = 2x weapon range in tiles (got %dx%d px)" % [int(circle[2]), int(circle[3])])
	# The origin = iso cell center - radius.
	var cx: float = ((9.5 - 1.5) / 2.0) * TILE - 6 * TILE
	var cy: float = ((9.5 + 1.5) / 4.0) * TILE - 6 * TILE
	_check(absf(circle[0] - cx) < 0.5 and absf(circle[1] - cy) < 0.5,
			"circle is centered on the character's cell (got origin %s)" % str(circle))
	_check(level.IsShootTarget(15, 1), "rifle: 6 squares east is a shoot target")
	_check(level.IsShootTarget(14, 1), "rifle: 5 squares east is a shoot target (within range)")
	_check(level.IsShootTarget(13, 1), "rifle: 4 squares east is a shoot target (within range)")
	_check(level.IsShootTarget(10, 1), "rifle: 1 square east is a shoot target (closest cell)")
	_check(level.IsShootTarget(3, 1), "rifle: 6 squares west is a shoot target")
	_check(level.IsShootTarget(8, 1), "rifle: 1 square west is a shoot target (closest cell)")
	_check(level.IsShootTarget(9, 7), "rifle: 6 squares south is a shoot target (clear column)")
	_check(level.IsShootTarget(13, 5), "rifle: (13,5) is INSIDE the circle — the old straight-line rule excluded it")
	_check(not level.IsShootTarget(5, 5), "rifle: (5,5) is in the circle but line of sight is blocked by the wall at (7,3)")
	_check(not level.IsShootTarget(15, 7), "rifle: (15,7) is outside the circle (dist^2 = 72 > 36)")
	_check(not level.IsShootTarget(3, 7), "rifle: (3,7) is outside the circle (dist^2 = 72 > 36)")
	_check(not level.IsShootTarget(16, 1), "rifle: 7 squares east is outside the circle (not a target)")
	var click_rifle := InputEventMouseButton.new()
	click_rifle.button_index = MOUSE_BUTTON_LEFT
	click_rifle.pressed = true
	click_rifle.position = level.IsoCellCenter(Vector2i(15, 1))
	board.gui_input.emit(click_rifle)
	await process_frame
	_check(level.GetHitPoints("Wisp") == 9, "rifle shot deals 3 damage (got %d HP, was 12)" % level.GetHitPoints("Wisp"))
	level.SetWeapon("Vex", false)
	await process_frame

	# --- Pistol shooting circle: radius 4 (dx^2+dy^2 <= 16), any direction,
	# 2 damage. (Wisp is at 9 HP here after the rifle test.)
	# Set the cells BEFORE ForceCurrent: shoot targets are computed from the
	# active combatant's position when the turn is set. Vex at (9,1): walls at
	# (7,3) and (12,4) block line of sight into the south-east / south-west
	# corners of the circle — the "cannot shoot through obstacles" check.
	level.SetTokenCell("Vex", 9, 1)
	level.SetTokenCell("Wisp", 13, 1)
	# Keep the other enemies far away so the cell (13,1) holds only Wisp.
	level.SetTokenCell("Brute", 26, 1)
	level.SetTokenCell("Slick", 26, 3)
	level.SetTokenCell("Gutter", 25, 3)
	level.ForceCurrent("Vex")
	level.SetActionMode(SHOOT)
	# Pistol circle overlay: radius 4 tiles = 192px, centered on (9,1).
	var pcircle: Array = level.GetRangeCircle()
	_check(absf(pcircle[2] - 8 * TILE) < 0.5 and absf(pcircle[3] - 8 * TILE) < 0.5,
			"circle follows the pistol range (got %dx%d px)" % [int(pcircle[2]), int(pcircle[3])])
	_check(level.IsShootTarget(13, 1), "enemy 4 squares east is a shoot target")
	_check(level.IsShootTarget(12, 1), "cell 3 squares east is a shoot target (within range)")
	_check(level.IsShootTarget(10, 1), "cell 1 square east is a shoot target (closest cell)")
	_check(level.IsShootTarget(9, 5), "cell 4 squares south is a shoot target")
	_check(level.IsShootTarget(9, 2), "cell 1 square south is a shoot target (closest cell)")
	_check(level.IsShootTarget(5, 1), "cell 4 squares west is a shoot target")
	_check(level.IsShootTarget(12, 3), "pistol: (12,3) is INSIDE the circle — the old straight-line rule excluded it")
	_check(level.IsShootTarget(11, 3), "pistol: (11,3) is inside the circle with clear line of sight")
	_check(not level.IsShootTarget(13, 5), "pistol: (13,5) is in the circle but line of sight is blocked (wall at 12,4)")
	_check(not level.IsShootTarget(5, 5), "pistol: (5,5) is in the circle but line of sight is blocked (wall at 7,3)")
	_check(not level.IsShootTarget(14, 1), "5 squares away is outside the circle (not a target)")
	_check(not level.IsShootTarget(12, 4), "pistol: (12,4) is outside the circle (dist^2 = 18 > 16)")
	_check(not level.IsShootTarget(9, 1), "own cell is not a shoot target")

	# Clicking a non-target cell in shoot mode deals no damage (12,4 is outside
	# the pistol circle, so it was never a valid target).
	var click_wrong_cell := InputEventMouseButton.new()
	click_wrong_cell.button_index = MOUSE_BUTTON_LEFT
	click_wrong_cell.pressed = true
	click_wrong_cell.position = level.IsoCellCenter(Vector2i(12, 4))
	board.gui_input.emit(click_wrong_cell)
	await process_frame
	_check(level.GetHitPoints("Wisp") == 9, "clicking a non-target in shoot mode deals no damage (Wisp at 9 from rifle test)")

	# A real shot: click Wisp's cell (13,1). 2 damage, and the action is consumed.
	var click_shoot := InputEventMouseButton.new()
	click_shoot.button_index = MOUSE_BUTTON_LEFT
	click_shoot.pressed = true
	click_shoot.position = level.IsoCellCenter(Vector2i(13, 1))
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
	click_diag.position = level.IsoCellCenter(Vector2i(9, 5))
	board.gui_input.emit(click_diag)
	await process_frame
	_check(level.GetHitPoints("Wisp") == 7, "click at empty south target does no damage")

	# --- Shoot AFTER move: a shot fires from the CURRENT position, not the
	# turn-start cell. Vex (pistol, range 4) starts at (9,1); Wisp is at
	# (13,1) — 4 away, in range from (9,1) but 8 away from (5,1). ---
	# Move WEST to (5,1) first (4 tiles, within Vex's move range, clear row).
	level.ForceCurrent("Vex")
	level.SetActionMode(MOVE)
	_check(level.IsReachable(5, 1), "Vex can move west to (5,1)")
	var click_move := InputEventMouseButton.new()
	click_move.button_index = MOUSE_BUTTON_LEFT
	click_move.pressed = true
	click_move.position = level.IsoCellCenter(Vector2i(5, 1))
	board.gui_input.emit(click_move)
	await process_frame
	_check(level.HasMovedThisTurn(), "Vex moved to (5,1) this turn")
	_check(level.GetTokenCell("Vex") == Vector2i(5, 1), "Vex is at (5,1) after the move")
	# From (5,1) Wisp at (13,1) is 8 away -> OUT of pistol range: the old
	# turn-start-based rule would still have allowed the shot, so this proves
	# the target set is recomputed from the current position.
	level.SetActionMode(SHOOT)
	_check(not level.IsShootTarget(13, 1),
			"after moving to (5,1), Wisp 8 away is out of pistol range (shoot aims from current position)")
	_check(level.IsShootTarget(9, 1), "after moving, cell 4 east is a shoot target")
	var click_shoot_moved := InputEventMouseButton.new()
	click_shoot_moved.button_index = MOUSE_BUTTON_LEFT
	click_shoot_moved.pressed = true
	click_shoot_moved.position = level.IsoCellCenter(Vector2i(13, 1))
	board.gui_input.emit(click_shoot_moved)
	await process_frame
	_check(level.GetHitPoints("Wisp") == 7,
			"shoot-after-move cannot reach a target now out of range (got %d HP)" % level.GetHitPoints("Wisp"))

	# New turn from (5,1): move EAST to (9,1), putting Wisp back in range,
	# then shoot it for 2 damage.
	level.ForceCurrent("Vex")
	level.SetActionMode(MOVE)
	_check(level.IsReachable(9, 1), "Vex can move east to (9,1)")
	var click_move2 := InputEventMouseButton.new()
	click_move2.button_index = MOUSE_BUTTON_LEFT
	click_move2.pressed = true
	click_move2.position = level.IsoCellCenter(Vector2i(9, 1))
	board.gui_input.emit(click_move2)
	await process_frame
	_check(level.GetTokenCell("Vex") == Vector2i(9, 1), "Vex moved back to (9,1)")
	level.SetActionMode(SHOOT)
	_check(level.IsShootTarget(13, 1), "from (9,1) Wisp 4 east is a shoot target")
	var click_shoot_moved2 := InputEventMouseButton.new()
	click_shoot_moved2.button_index = MOUSE_BUTTON_LEFT
	click_shoot_moved2.pressed = true
	click_shoot_moved2.position = level.IsoCellCenter(Vector2i(13, 1))
	board.gui_input.emit(click_shoot_moved2)
	await process_frame
	_check(level.GetHitPoints("Wisp") == 5,
			"shoot-after-move deals 2 damage from the new position (got %d HP)" % level.GetHitPoints("Wisp"))

	# --- WAIT consumes the action (no damage, no move). ---
	level.ForceCurrent("Vex")
	level.SetActionMode(SHOOT)
	level.get_node("ActionMenu/VBox/Actions/Wait").pressed.emit()
	await process_frame
	var click_after_wait := InputEventMouseButton.new()
	click_after_wait.button_index = MOUSE_BUTTON_LEFT
	click_after_wait.pressed = true
	click_after_wait.position = level.IsoCellCenter(Vector2i(13, 1))
	board.gui_input.emit(click_after_wait)
	await process_frame
	_check(level.GetHitPoints("Wisp") == 5, "WAIT consumes the action (no shot after)")

	# --- Melee: one square left/right/up/down, knife 3 dmg. ---
	# Vex (knife, 3 dmg) is adjacent to Wisp (5 HP) at (11,1).
	const MELEE := 2  # Level01.ActionMode.Melee
	level.SetTokenCell("Vex", 10, 1)
	level.SetTokenCell("Wisp", 11, 1)
	level.ForceCurrent("Vex")
	level.SetActionMode(MELEE)
	_check(level.GetActionMode() == MELEE, "melee mode is active")
	_check(level.IsMeleeTarget(11, 1), "enemy 1 square east is a melee target")
	_check(not level.IsMeleeTarget(10, 0), "adjacent cell with NO token is not a melee target")
	_check(not level.IsMeleeTarget(11, 0), "diagonal cell is NOT a melee target")
	_check(not level.IsMeleeTarget(10, 2), "cell 2 squares south is NOT a melee target")
	_check(not level.IsMeleeTarget(9, 1), "cell 2 squares west is NOT a melee target")
	_check(not level.IsMeleeTarget(10, 1), "own cell is not a melee target")
	# Strike Wisp with the knife.
	var click_melee := InputEventMouseButton.new()
	click_melee.button_index = MOUSE_BUTTON_LEFT
	click_melee.pressed = true
	click_melee.position = level.IsoCellCenter(Vector2i(11, 1))
	board.gui_input.emit(click_melee)
	await process_frame
	_check(level.GetHitPoints("Wisp") == 2, "melee strike deals 3 knife damage (got %d HP, was 5)" % level.GetHitPoints("Wisp"))
	_check(melee_btn.disabled == true, "MELEE button disabled after the action is spent")
	_check(level.get_node("ActionMenu/VBox/Actions/Shoot").disabled == true,
			"SHOOT also disabled: melee and shoot share the one action")
	_check(level.get_node("ActionMenu/VBox/Actions/Move").disabled == false,
			"MOVE stays available: the move is a separate budget")
	# Second click: action already used this turn -> no extra damage.
	board.gui_input.emit(click_melee)
	await process_frame
	_check(level.GetHitPoints("Wisp") == 2, "second melee click does not strike again (once per turn)")
	# A SHOOT after the melee must not fire: the action budget is shared.
	level.SetActionMode(SHOOT)
	_check(level.IsShootTarget(11, 1), "Wisp is inside the pistol circle (sanity)")
	var click_shoot_after_melee := InputEventMouseButton.new()
	click_shoot_after_melee.button_index = MOUSE_BUTTON_LEFT
	click_shoot_after_melee.pressed = true
	click_shoot_after_melee.position = level.IsoCellCenter(Vector2i(11, 1))
	board.gui_input.emit(click_shoot_after_melee)
	await process_frame
	_check(level.GetHitPoints("Wisp") == 2, "shoot after melee does not fire (action shared, got %d HP)" % level.GetHitPoints("Wisp"))
	# New turn: move, then the melee target set is recomputed from the new
	# position (a melee-after-move aims from the CURRENT cell).
	level.ForceCurrent("Vex")
	level.SetActionMode(MOVE)
	var click_move_melee := InputEventMouseButton.new()
	click_move_melee.button_index = MOUSE_BUTTON_LEFT
	click_move_melee.pressed = true
	click_move_melee.position = level.IsoCellCenter(Vector2i(12, 1))
	board.gui_input.emit(click_move_melee)
	await process_frame
	_check(level.GetTokenCell("Vex") == Vector2i(12, 1), "Vex moved to (12,1)")
	# The target set is recomputed from the NEW position: Wisp at (11,1) is
	# now one WEST of Vex — still a valid melee target.
	_check(level.IsMeleeTarget(11, 1), "melee targets recomputed from the new position (Wisp west is adjacent)")
	_check(not level.IsMeleeTarget(13, 1), "cell 1 EAST of the new position is not a target (no token there)")
	# Katana: 5 damage in melee.
	level.SetMeleeWeapon("Vex", true)
	_check(level.GetMeleeWeaponDamage("Vex") == 5, "Vex now wields the katana (5 dmg)")
	level.ForceCurrent("Vex")
	level.SetActionMode(MELEE)
	board.gui_input.emit(click_melee)
	await process_frame
	_check(level.GetHitPoints("Wisp") <= -2, "katana strike deals 5 damage (got %d HP, was 2)" % level.GetHitPoints("Wisp"))
	level.SetMeleeWeapon("Vex", false)
	await process_frame

	# --- Enemy AI prefers to shoot, falls back to melee when in range. ---
	# Brute (pistol + knife, pinned) stands at (25,1); Vex is ADJACENT at
	# (25,2), inside the pistol circle, but the WALL at (24,1) blocks the line
	# of sight — so the enemy cannot shoot and must swing the knife instead.
	level.SetTokenCell("Vex", 25, 2)
	level.SetTokenCell("Kaito", 0, 2)
	level.SetTokenCell("Rook", 0, 3)
	level.SetTokenCell("Mira", 0, 4)
	level.SetTokenCell("Brute", 25, 1)
	level.SetTokenCell("Slick", 26, 3)
	level.SetTokenCell("Gutter", 25, 4)
	level.SetWeapon("Brute", false)
	level.SetMeleeWeapon("Brute", false)
	level.SetInitiativeRolls([1, 1, 1, 1, 1, 1, 1, 1])
	level.ForceCurrent("Brute")
	for i in 24:
		await process_frame
	_check(level.GetTokenCell("Brute") == Vector2i(25, 1),
			"enemy holds its position when already in range (got %s)" % str(level.GetTokenCell("Brute")))
	_check(level.GetHitPoints("Vex") == 14,
			"enemy melee-fallback struck adjacent Vex for knife 3 (got %d HP, want 14)" % level.GetHitPoints("Vex"))
	# Same spot, but now Vex is 5 away on the clear row y=1: the enemy walks
	# east into the pistol circle and SHOOTS (prefers the ranged attack over
	# the knife it could also use).
	level.SetTokenCell("Vex", 20, 1)
	level.ForceCurrent("Brute")
	for i in 24:
		await process_frame
	_check(level.GetHitPoints("Vex") == 12,
			"enemy prefers to shoot over melee (pistol 2 from the walked position, got %d HP, want 12)"
			% level.GetHitPoints("Vex"))

	# --- Death: HP hits 0 (or below) -> token removed from the map, no more turns. ---
	# Rook (party, pistol) shoots Wisp (5 HP here) to death: 3 shots, one per turn.
	# 5 -> 3 -> 1 -> -1 (overshoot; death is HP <= 0).
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
	var token_wisp: Node = board.get_node("Iso/E3")
	var shots := 0
	while level.IsAlive("Wisp") and shots < 8:
		level.SetTokenCell("Rook", 8, 1)
		level.SetTokenCell("Wisp", 12, 1)
		level.ForceCurrent("Rook")
		level.SetActionMode(SHOOT)
		var click := InputEventMouseButton.new()
		click.button_index = MOUSE_BUTTON_LEFT
		click.pressed = true
		click.position = level.IsoCellCenter(Vector2i(12, 1))
		board.gui_input.emit(click)
		await process_frame
		end_turn.pressed.emit()
		await process_frame
		shots += 1
	_check(level.GetHitPoints("Wisp") <= 0, "Wisp HP reached 0 (got %d)" % level.GetHitPoints("Wisp"))
	_check(not level.IsAlive("Wisp"), "Wisp is dead")
	_check(token_wisp.visible == false, "dead Wisp's token is removed from the map")
	var label_wisp: Control = null
	for child in board.get_children():
		if child is Label and (child as Label).text == "Wisp":
			label_wisp = child
			break
	_check(label_wisp != null and label_wisp.visible == false,
		"dead Wisp's name label is removed from the map")
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

	# --- Items menu + Grenade ------------------------------------------------
	# Every combatant starts with exactly one grenade.
	for n in NAMES:
		_check(level.GetItemCount(n, "Grenade") == 1,
				"%s starts with 1 grenade (got %d)" % [n, level.GetItemCount(n, "Grenade")])
	# The actions menu has an ITEMS button (replacing the old BIOSTIM button).
	var items_btn: Button = level.get_node("ActionMenu/VBox/Actions/Items")
	_check(items_btn != null, "actions menu has an ITEMS button")
	_check(String(items_btn.text) == "ITEMS", "ITEMS button label (got '%s')" % String(items_btn.text))
	# The items select list holds a BioStim and a Grenade button.
	var biostim_btn: Button = level.get_node("ActionMenu/VBox/ItemsMenu/ItemsVBox/BioStim")
	var grenade_btn: Button = level.get_node("ActionMenu/VBox/ItemsMenu/ItemsVBox/Grenade")
	_check(biostim_btn != null and grenade_btn != null,
			"items menu lists BioStim and Grenade")
	# The items list is hidden until ITEMS is pressed.
	level.ForceCurrent("Vex")
	await process_frame
	_check(level.GetItemsMenuOpen() == false, "items list starts closed")
	items_btn.pressed.emit()
	await process_frame
	_check(level.GetItemsMenuOpen() == true, "pressing ITEMS opens the items list")
	_check(level.GetActionMode() == 0, "opening items does not change the action mode")

	# --- Grenade throw range: a square of side 2*4+1 = 9 centered on the
	# thrower (Chebyshev distance <= 4, any direction). ---
	# Vex sits at (1,1). The throw range is 4 squares away in any direction.
	level.SetTokenCell("Vex", 1, 1)
	await process_frame
	# Enter grenade mode via the items menu's GRENADE button.
	level.ForceCurrent("Vex")
	await process_frame
	grenade_btn.pressed.emit()
	await process_frame
	_check(level.GetActionMode() == 3, "GRENADE button enters grenade mode (got %d)" % level.GetActionMode())
	# Within range (Chebyshev <= 4): 4 east, 4 west, 4 south, 4 north.
	_check(level.IsGrenadeTarget(5, 1), "4 east is throwable (got %s)" % level.IsGrenadeTarget(5, 1))
	_check(level.IsGrenadeTarget(1, 5), "4 south is throwable (got %s)" % level.IsGrenadeTarget(1, 5))
	_check(level.IsGrenadeTarget(5, 5), "4 east + 4 south (diagonal) is throwable (got %s)" % level.IsGrenadeTarget(5, 5))
	# Out of range: 5 away.
	_check(level.IsGrenadeTarget(6, 1) == false, "5 east is NOT throwable")
	_check(level.IsGrenadeTarget(1, 6) == false, "5 south is NOT throwable")
	_check(level.IsGrenadeTarget(6, 6) == false, "5 east + 5 south is NOT throwable")
	# The thrower's own cell is throwable.
	_check(level.IsGrenadeTarget(1, 1), "own cell is throwable")

	# --- Grenade blast: cross shape (landing cell + 4 orthogonal neighbours),
	# 6 damage to every combatant standing on those cells. ---
	# Party cluster: Vex (1,1), Kaito (2,1), Rook (1,2), Mira (2,2). Enemies are
	# far east at (26,1)/(25,1)/(26,2)/(25,2) — outside the blast.
	level.SetTokenCell("Kaito", 2, 1)
	level.SetTokenCell("Rook", 1, 2)
	level.SetTokenCell("Mira", 2, 2)
	await process_frame
	var kaito_hp: int = level.GetHitPoints("Kaito")
	var rook_hp: int = level.GetHitPoints("Rook")
	var mira_hp: int = level.GetHitPoints("Mira")
	var vex_hp: int = level.GetHitPoints("Vex")
	var brute_hp: int = level.GetHitPoints("Brute")
	# Throw on (2,1): blast = (2,1),(1,1),(3,1),(2,0),(2,2). Kaito (2,1), Rook
	# (1,2) is NOT in the cross, Mira (2,2) IS, Vex (1,1) IS. So Kaito, Vex and
	# Mira each take 6.
	var threw: bool = level.UseGrenade(2, 1)
	_check(threw == true, "UseGrenade on a valid cell succeeds")
	_check(level.GetHitPoints("Kaito") == kaito_hp - 6, "Kaito (on landing cell) took 6 (got %d)" % level.GetHitPoints("Kaito"))
	_check(level.GetHitPoints("Vex") == vex_hp - 6, "Vex (orthogonal neighbour) took 6 (got %d)" % level.GetHitPoints("Vex"))
	_check(level.GetHitPoints("Mira") == mira_hp - 6, "Mira (orthogonal neighbour) took 6 (got %d)" % level.GetHitPoints("Mira"))
	_check(level.GetHitPoints("Rook") == rook_hp, "Rook (diagonal, not in cross) unharmed (got %d)" % level.GetHitPoints("Rook"))
	_check(level.GetHitPoints("Brute") == brute_hp, "Brute (far away) unharmed (got %d)" % level.GetHitPoints("Brute"))
	# The grenade was consumed and the action is spent.
	_check(level.GetItemCount("Vex", "Grenade") == 0, "Vex's grenade was consumed (got %d)" % level.GetItemCount("Vex", "Grenade"))
	# A second throw on the same turn fails (action already spent).
	_check(level.UseGrenade(2, 1) == false, "second grenade throw this turn fails (action spent)")
	# The action menu reflects the spent action: the grenade count shows 0.
	_check(grenade_btn.disabled == true, "GRENADE button disabled after throwing")
	_check(String(grenade_btn.text) == "GRENADE x0", "GRENADE button shows the count (got '%s')" % String(grenade_btn.text))

	# --- Fresh turn, 0 grenades: the GRENADE button is disabled because there
	# is nothing to throw (the action is re-armed, so only the count gates). ---
	level.ForceCurrent("Vex")
	await process_frame
	_check(grenade_btn.disabled == true, "GRENADE button disabled at 0 grenades on a fresh turn")
	_check(String(grenade_btn.text) == "GRENADE x0", "GRENADE button shows 0 on a fresh turn (got '%s')" % String(grenade_btn.text))

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

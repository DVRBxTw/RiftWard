extends "res://beta_game.gd"
# Weapons & armory, enemy archetypes, boss phases, pickups, grenade skill,
# rarity buffs, revives and combat feedback layered on top of the beta game.

const WEAPON_ORDER = ["pistol", "rifle", "shotgun", "sniper"]
const WEAPONS = {
	"pistol": {"name":"P-9 制式手槍", "short":"手槍", "desc":"初始武器。彈匣小、換彈快，穩定可靠。", "dmg":28.0, "interval":0.24, "mag":12, "reload":1.1, "pellets":1, "spread":0.0, "head":2.0, "zoom":0.85, "price":0, "recoil":0.022, "sway":0.006},
	"rifle": {"name":"AR-27 突擊步槍", "short":"步槍", "desc":"全自動高射速，適合壓制大群敵人。", "dmg":17.0, "interval":0.1, "mag":30, "reload":1.8, "pellets":1, "spread":0.012, "head":2.0, "zoom":0.75, "price":400, "recoil":0.011, "sway":0.007},
	"shotgun": {"name":"SG-8 霰彈槍", "short":"霰彈", "desc":"一次射出 7 顆彈丸，近距離威力驚人。", "dmg":11.0, "interval":0.8, "mag":6, "reload":2.2, "pellets":7, "spread":0.07, "head":1.5, "zoom":0.85, "price":600, "recoil":0.065, "sway":0.015},
	"sniper": {"name":"LR-50 狙擊槍", "short":"狙擊", "desc":"單發高傷害，爆頭 3 倍。右鍵開鏡。", "dmg":95.0, "interval":1.0, "mag":5, "reload":2.4, "pellets":1, "spread":0.0, "head":3.0, "zoom":0.35, "price":800, "recoil":0.09, "sway":0.01}
}
const UPGRADE_KEYS = ["damage", "mag", "reload"]
const UPGRADE_NAMES = {"damage":"傷害", "mag":"彈匣", "reload":"換彈"}
const UPGRADE_FACTOR = {"pistol":1.0, "rifle":1.3, "shotgun":1.4, "sniper":1.6}
const MAX_UPGRADE_LEVEL = 5

const KINDS = [
	["突擊兵", Color("ff8855"), 1.0],
	["衝鋒兵", Color("ff4d6d"), 0.9],
	["狙擊兵", Color("b47cff"), 1.0],
	["自爆蟲", Color("ffd23f"), 0.75],
	["護盾兵", Color("5aa9ff"), 1.0],
	["帝國重裝兵", Color("ff5533"), 2.2],
	["帝國指揮官", Color("ffb347"), 2.6]
]
const SNIPER_BULLET_SPEED = 14.0
const SNIPER_WARNING = 0.9
const MAX_ENEMIES = 34

const PLUS_BUFFS = [
	["超載彈芯", "傷害 +%d%%", 20, "✦"],
	["急速扳機", "射速 +%d%%", 15, "⚡"],
	["動能靴", "移動速度 +%d%%", 12, "➤"],
	["強化護盾", "護盾上限 +%d，並立即恢復", 30, "⬢"],
	["修復協議", "每波開始恢復 %d 護盾", 15, "✚"],
	["弱點分析", "%d%% 機率造成雙倍傷害", 20, "◎"],
	["擴充彈匣", "彈匣 +%d 發，換彈加快 15%%", 4, "▤"],
	["戰術冷卻", "手榴彈冷卻 -%d%%", 20, "⟳"],
	["吸能模組", "每次擊殺恢復 %d 護盾", 4, "♥"],
	["爆裂彈頭", "擊殺時爆炸，造成 %d 範圍傷害", 35, "✸"]
]
const RARITY = [["普通", Color("a7a9ac"), 1.0], ["稀有", Color("7fb2e5"), 1.6], ["傳說", Color("f0b24a"), 2.5]]

const SKILL_COOLDOWN = 12.0
const GRENADE_RADIUS = 4.5
const GRENADE_DAMAGE = 130.0
const REVIVE_TIME = 3.0
const PICKUP_CHANCE = 0.16

var enemy_variety = true
var pickups: Dictionary = {}
var next_pickup = 0
var pickup_visuals: Dictionary = {}
var floaters: Array = []
var combo = 0
var combo_timer = 0.0
var damage_flash = 0.0
var last_local_hp = -1.0
var banner_timer = 0.0
var shown_slot = -1
var armory_busy = false
# HUD nodes
var slot_boxes: Array = []
var weapon_name_label: Label
var skill_bar: ProgressBar
var skill_label: Label
var combo_label: Label
var banner_label: Label
var vignette: TextureRect
var revive_label: Label
var armory_status: Label
var reload_audio: AudioStreamPlayer
var reload_sounds: Dictionary = {}
var last_reload_ratio = 0.0
var last_reload_slot = -1
var reload_sfx_log: Array = []
var recoil_pending = 0.0
var last_recoil_time = -1.0
var admin_settings = {"dmg":1.0,"speed":1.0,"god":false,"ammo":false,"fly":false}
var admin_by_id: Dictionary = {}
var admin_status: Label
var admin_players: VBoxContainer

# ---------------------------------------------------------------- weapons

static func weapon_stats(wid: String, levels: Dictionary) -> Dictionary:
	var w = WEAPONS[wid]
	var lv_damage = int(levels.get("damage",0))
	var lv_mag = int(levels.get("mag",0))
	var lv_reload = int(levels.get("reload",0))
	return {
		"damage": w.dmg*(1.0+0.12*lv_damage),
		"interval": w.interval,
		"mag": int(ceil(w.mag*(1.0+0.15*lv_mag))),
		"reload": w.reload*(1.0-0.08*lv_reload),
		"pellets": w.pellets, "spread": w.spread, "head": w.head, "zoom": w.zoom
	}

static func upgrade_cost(wid: String, lv: int) -> int:
	return int(round(120.0*(lv+1)*UPGRADE_FACTOR[wid]))

func _loadout_for(id: int) -> Dictionary:
	var source: Dictionary = verified[id] if verified.has(id) else profile
	var lo = source.get("loadout",{})
	if not lo is Dictionary: lo = {}
	var owned: Array = []
	for w in lo.get("owned",["pistol"]):
		if WEAPONS.has(str(w)) and not str(w) in owned: owned.append(str(w))
	if not "pistol" in owned: owned.push_front("pistol")
	var levels = lo.get("levels",{})
	var equipped: Array = []
	for w in lo.get("equipped",[]):
		if str(w) in owned and not str(w) in equipped and equipped.size() < 2: equipped.append(str(w))
	if equipped.is_empty(): equipped = owned.slice(0,2)
	return {"owned":owned,"levels":(levels.duplicate(true) if levels is Dictionary else {}),"equipped":equipped}

func _weapon_id(a: Dictionary) -> String:
	var kit: Array = a.get("equipped",["pistol"])
	return str(kit[clampi(int(a.get("slot",0)),0,kit.size()-1)])

func _slot_weapon(slot: int) -> String:
	# Client-side view of the local kit (from the account profile).
	var kit: Array = _loadout_for(-1).equipped
	return str(kit[clampi(slot,0,kit.size()-1)])

func _cycle_slot(step: int) -> int:
	return wrapi(desired_slot+step,0,maxi(1,_loadout_for(-1).equipped.size()))

func _set_actor_stats(id: int):
	super._set_actor_stats(id)
	var a = actors[id]
	var lo = _loadout_for(id)
	a.merge({"owned":lo.owned,"levels":lo.levels,"equipped":lo.equipped,"slot":0,"ammo_by":{},"dmg_mult":1.0,"rate_mult":1.0,"mag_bonus":0,"reload_mult":1.0,"skill_mult":1.0,"lifesteal":0,"explode":0,"skill_cd":0.0,"revive":0.0,"admin_speed":1.0,"admin_dmg":1.0,"god":false,"inf_ammo":false,"fly":false},true)
	_refresh_weapon(a)
	a["ammo"] = a.mag
	a["reload"] = 0.0
	_apply_admin(id)

func _refresh_weapon(a: Dictionary):
	var wid = _weapon_id(a)
	var st = weapon_stats(wid,a.get("levels",{}).get(wid,{}))
	a["damage"] = st.damage*float(a.get("dmg_mult",1.0))*float(a.get("admin_dmg",1.0))
	a["fire_interval"] = maxf(0.045,st.interval*float(a.get("rate_mult",1.0)))
	a["mag"] = st.mag+int(a.get("mag_bonus",0))
	a["reload_time"] = maxf(0.35,st.reload*float(a.get("reload_mult",1.0)))
	a["pellets"] = st.pellets
	a["spread"] = st.spread
	a["head_mult"] = st.head
	a["zoom"] = st.zoom
	a["ammo"] = mini(int(a.get("ammo",a.mag)),a.mag)

# ---------------------------------------------------------------- buffs with rarity

func _offer_to(id: int):
	var pool = range(PLUS_BUFFS.size())
	pool.shuffle()
	var offer = []
	for index in pool.slice(0,3):
		var roll = randf()
		var tier = 2 if roll < 0.06 else (1 if roll < 0.32 else 0)
		offer.append(index+10*tier)
	buff_choices[id] = offer
	if id == 1: _buff_offer(offer,level)
	else: _buff_offer.rpc_id(id,offer,level)

func _buff_text(code: int) -> Array:
	var index = code%10
	var tier = clampi(code/10,0,2)
	var b = PLUS_BUFFS[index]
	var value = int(round(b[2]*RARITY[tier][2]))
	return [b[3],b[0],b[1] % value,tier]

@rpc("authority", "call_remote", "reliable")
func _buff_offer(offer: Array, new_level: int):
	if dedicated: return
	modal_open = true
	Input.mouse_mode = Input.MOUSE_MODE_VISIBLE
	if is_instance_valid(buff_overlay): buff_overlay.queue_free()
	buff_overlay = Control.new()
	buff_overlay.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	screen.add_child(buff_overlay)
	var shade = ColorRect.new()
	shade.color = Color(0.03,0.031,0.034,0.86)
	shade.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	buff_overlay.add_child(shade)
	var center = CenterContainer.new()
	center.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	buff_overlay.add_child(center)
	var v = VBoxContainer.new()
	v.add_theme_constant_override("separation",16)
	center.add_child(v)
	for line in [["LEVEL UP",13,CYAN],["等級 %d" % new_level,44,WHITE],["選擇一項加成 · 可疊加 · 敵人也會隨等級變強 · 30 秒後自動選擇",15,MUTED]]:
		var l = _label(line[0],line[1],line[2])
		l.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		v.add_child(l)
	var cards = HBoxContainer.new()
	cards.add_theme_constant_override("separation",20)
	cards.alignment = BoxContainer.ALIGNMENT_CENTER
	v.add_child(cards)
	var buttons: Array = []
	for code in offer:
		var info = _buff_text(int(code))
		var rarity = RARITY[info[3]]
		var b = _button("%s\n\n%s\n\n%s" % [rarity[0],info[1],info[2]],func():
			for other in buttons: other.disabled = true
			if network == "client": _choose_buff.rpc_id(1,code)
			else:
				_apply_buff(1,code)
				if buff_choices.is_empty(): _resolve_buffs(),cards,280)
		b.custom_minimum_size.x = 250
		b.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		b.add_theme_font_size_override("font_size",19)
		var c: Color = rarity[1]
		b.add_theme_stylebox_override("normal",_button_style(Color(c.r*0.15,c.g*0.15,c.b*0.18,0.95),c,info[3] > 0))
		b.add_theme_stylebox_override("hover",_button_style(Color(c.r*0.28,c.g*0.28,c.b*0.3,0.98),c,true))
		b.add_theme_color_override("font_color",c.lightened(0.35) if info[3] > 0 else WHITE)
		buttons.append(b)
	var waiting = _label("選擇後等待隊友；所有人選完就繼續作戰。",14,MUTED)
	waiting.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	v.add_child(waiting)

func _apply_buff(id: int, code: int):
	if not actors.has(id):
		buff_choices.erase(id)
		return
	var a = actors[id]
	var index = code%10
	var m = RARITY[clampi(code/10,0,2)][2]
	match index:
		0: a["dmg_mult"] = float(a.get("dmg_mult",1.0))+0.2*m
		1: a["rate_mult"] = maxf(0.3,float(a.get("rate_mult",1.0))*(1.0-0.15*m))
		2: a["speed"] = minf(12.0,float(a.get("speed",6.5))*(1.0+0.12*m))
		3:
			var bonus = round(30*m)
			a["max_hp"] = float(a.get("max_hp",100))+bonus
			a["hp"] = minf(a.max_hp,a.hp+bonus)
		4: a["regen"] = float(a.get("regen",0))+round(15*m)
		5: a["critical"] = minf(0.8,float(a.get("critical",0.0))+0.2*m)
		6:
			a["mag_bonus"] = int(a.get("mag_bonus",0))+int(round(4*m))
			a["reload_mult"] = maxf(0.4,float(a.get("reload_mult",1.0))*0.85)
		7: a["skill_mult"] = maxf(0.35,float(a.get("skill_mult",1.0))*(1.0-0.2*m))
		8: a["lifesteal"] = float(a.get("lifesteal",0))+round(4*m)
		9: a["explode"] = float(a.get("explode",0))+round(35*m)
	_refresh_weapon(a)
	if index == 6 and float(a.get("reload",0)) <= 0: a["ammo"] = a.mag
	buff_choices.erase(id)
	if id != 1: _buff_applied.rpc_id(id,code,a.get("max_hp",100))
	else: _buff_applied(code,a.get("max_hp",100))

# ---------------------------------------------------------------- enemies

func _spawn_wave():
	super._spawn_wave()
	if test_mode: return
	if enemy_variety:
		var pool = []
		if wave >= 2: pool.append(1)
		if wave >= 3: pool.append(3)
		if wave >= 4: pool.append(2)
		if wave >= 5: pool.append(4)
		for e in enemies.values():
			if e.boss: continue
			var kind = 0
			if not pool.is_empty() and randf() < 0.55: kind = pool.pick_random()
			_apply_kind(e,kind)
	var has_boss = false
	for e in enemies.values():
		if e.boss:
			has_boss = true
			e["kind_id"] = 6 if wave >= 20 else 5
			e["scale"] = KINDS[e.kind_id][2]
			e["phase"] = 1
	_announce_all("BOSS 來襲" if has_boss else "第 %d 波" % wave)

func _apply_kind(e: Dictionary, kind: int):
	e["kind_id"] = kind
	e["scale"] = KINDS[kind][2]
	var base_xp = 20+5*selected
	match kind:
		1:
			e.hp *= 0.7
			e.speed *= 1.7
			e["shoots"] = false
			e["xp"] = base_xp+2
		2:
			e.hp *= 0.8
			e.speed *= 0.9
			e["keep"] = 13.0
			e["ranged_only"] = true
			e["shot_cd"] = randf_range(2.5,4.0)
			e["xp"] = base_xp+10
		3:
			e.hp *= 0.45
			e.speed *= 1.9
			e["shoots"] = false
			e["ranged_only"] = true
			e["xp"] = base_xp
		4:
			e.hp *= 1.6
			e.speed *= 0.85
			e["xp"] = base_xp+12
	e["max_hp"] = e.hp

func _enemy_shoot(e: Dictionary, origin: Vector3, aim: Vector3):
	if int(e.get("kind_id",0)) == 2:
		_spawn_bullet(origin,aim*SNIPER_BULLET_SPEED,float(e.get("damage",10))*1.3,1)
		e["shot_cd"] = randf_range(3.5,5.0)
		return
	super._enemy_shoot(e,origin,aim)

func _damage_multiplier(e: Dictionary, direction: Vector3, head: bool, _shooter: int) -> float:
	# Shield troopers block body shots from the front; flank them or aim for the head.
	if int(e.get("kind_id",0)) == 4 and not head:
		var facing = Vector3(-sin(e.y),0,-cos(e.y))
		if direction.dot(facing) < -0.3: return 0.25
	return 1.0

func _on_enemy_killed(eid: int, e: Dictionary, shooter: int):
	super._on_enemy_killed(eid,e,shooter)
	if test_mode: return
	if actors.has(shooter) and actors[shooter].hp > 0:
		var a = actors[shooter]
		if float(a.get("lifesteal",0)) > 0: a["hp"] = minf(float(a.get("max_hp",100)),a.hp+float(a.lifesteal))
		if float(a.get("explode",0)) > 0: _explode(e.p+Vector3.UP,3.2,float(a.explode),-1)
	if e.get("boss",false): _spawn_pickup(e.p,0)
	elif randf() < PICKUP_CHANCE: _spawn_pickup(e.p,0 if randf() < 0.4 else 1)

func _kill_enemy(eid: int, shooter: int):
	if not enemies.has(eid): return
	var e = enemies[eid]
	kills += 1
	enemies.erase(eid)
	_on_enemy_killed(eid,e,shooter)

func _explode(pos: Vector3, radius: float, damage: float, owner: int):
	for eid in enemies.keys():
		if not enemies.has(eid): continue
		var e = enemies[eid]
		if e.hp <= 0: continue
		var d = (e.p+Vector3.UP).distance_to(pos)
		if d < radius:
			e.hp -= damage*(1.0-0.5*d/radius)
			if e.hp <= 0: _kill_enemy(eid,owner)
	_explosion_all(pos,radius,Color("ffb347"))

func _new_enemy(pos: Vector3) -> Dictionary:
	var region = float(selected)
	return {"p":pos,"hp":(55.0+10.0*region)*_level_scale(),"speed":2.1+0.3*region,"cd":0.6,"y":0.0,"boss":false,"damage":(10.0+2.0*region)*(1.0+0.05*(level-1)),"shot_cd":randf_range(1.5,3.0)}

func _simulate_bullets(dt: float):
	super._simulate_bullets(dt)
	if test_mode: return
	_simulate_players(dt)
	_simulate_enemy_kinds(dt)
	_simulate_grenades(dt)
	_simulate_pickups(dt)

func _simulate_enemy_kinds(dt: float):
	var alive = []
	for id in actors:
		if actors[id].hp > 0: alive.append(id)
	for eid in enemies.keys():
		if not enemies.has(eid): continue
		var e = enemies[eid]
		var kind = int(e.get("kind_id",0))
		if kind == 2:
			e["state"] = 1 if float(e.get("shot_cd",9.0)) < SNIPER_WARNING else 0
		elif kind == 3:
			for pid in alive:
				if actors[pid].p.distance_to(e.p) < 1.8:
					for other in alive:
						if actors[other].p.distance_to(e.p) < 3.2:
							actors[other].hp = maxf(0,actors[other].hp-float(e.get("damage",10))*2.2)
					enemies.erase(eid)
					_explosion_all(e.p+Vector3.UP,3.2,Color("ffd23f"))
					break
		if e.boss and enemies.has(eid):
			if int(e.get("phase",1)) == 1 and e.hp < float(e.get("max_hp",e.hp))*0.5:
				e["phase"] = 2
				e["state"] = 2
				e.speed *= 1.3
				for i in range(4):
					if enemies.size() >= MAX_ENEMIES: break
					var angle = TAU*i/4.0
					var minion = _new_enemy(e.p+Vector3(sin(angle)*3,0,cos(angle)*3))
					_apply_kind(minion,1)
					enemies[next_enemy] = minion
					next_enemy += 1
				_announce_all("BOSS 進入狂暴階段！")
			if int(e.get("phase",1)) == 2:
				e["ring_cd"] = float(e.get("ring_cd",2.0))-dt
				if e.ring_cd <= 0 and bullets.size() < MAX_BULLETS-16:
					e["ring_cd"] = 3.5
					var offset = randf()*TAU
					for k in range(14):
						var a = offset+TAU*k/14.0
						_spawn_bullet(e.p+Vector3(0,1.6,0),Vector3(sin(a),0,cos(a))*5.5,float(e.get("damage",20))*0.45)

# ---------------------------------------------------------------- players: weapons, skill, revive

func _simulate_players(dt: float):
	for id in actors:
		var a = actors[id]
		var p: Dictionary = inputs.get(id,{})
		a["skill_cd"] = maxf(0,float(a.get("skill_cd",0))-dt)
		if a.get("god",false):
			a["hp"] = float(a.get("max_hp",100))
		if a.get("inf_ammo",false) and a.hp > 0:
			a["ammo"] = int(a.get("mag",MAG_SIZE))
			a["reload"] = 0.0
		if a.hp <= 0:
			var helped = false
			for other in actors:
				if other != id and actors[other].hp > 0 and actors[other].p.distance_to(a.p) < 2.4 and inputs.get(other,{}).get("interact",false):
					helped = true
			a["revive"] = clampf(float(a.get("revive",0))+(dt if helped else -dt*0.5),0,REVIVE_TIME)
			if a.revive >= REVIVE_TIME:
				a["hp"] = float(a.get("max_hp",100))*0.4
				a["revive"] = 0.0
			continue
		a["revive"] = 0.0
		if not a.has("ammo_by"): a["ammo_by"] = {}
		var want = int(p.get("slot",a.get("slot",0)))
		if want != int(a.get("slot",0)) and want >= 0 and want < a.get("equipped",["pistol"]).size():
			a.ammo_by[_weapon_id(a)] = a.ammo
			a["slot"] = want
			_refresh_weapon(a)
			a["ammo"] = int(a.ammo_by.get(_weapon_id(a),a.mag))
			a["reload"] = 0.0
			a["cd"] = maxf(float(a.cd),0.25)
		if p.get("skill",false) and a.skill_cd <= 0:
			a["skill_cd"] = SKILL_COOLDOWN*float(a.get("skill_mult",1.0))
			var origin: Vector3 = a.p+Vector3.UP*EYE_HEIGHT
			var aim = p.get("aim",origin+Vector3(0,0,-30))
			var direction: Vector3 = (aim-origin).normalized()
			var gid = _spawn_bullet(origin+direction*0.6,direction*15.0+Vector3.UP*3.5,0.0,2)
			bullets[gid]["grenade"] = true
			bullets[gid]["owner"] = id
			bullets[gid]["life"] = 1.8

func _simulate_grenades(dt: float):
	for bid in bullets.keys():
		var b = bullets[bid]
		if not b.get("grenade",false): continue
		b.v.y -= 20.0*dt
		b.p += b.v*dt
		b.life -= dt
		if b.p.y <= 0.15 or b.life <= 0 or _in_cover(b.p) or absf(b.p.x) > 22.5 or absf(b.p.z) > 22.5:
			var owner = int(b.get("owner",-1))
			var mult = float(actors[owner].get("dmg_mult",1.0)) if actors.has(owner) else 1.0
			bullets.erase(bid)
			_explode(b.p+Vector3.UP*0.5,GRENADE_RADIUS,GRENADE_DAMAGE*(1.0+0.15*selected)*mult,owner)

# ---------------------------------------------------------------- pickups

func _spawn_pickup(pos: Vector3, kind: int):
	pickups[next_pickup] = {"p":Vector3(pos.x,0,pos.z),"kind":kind,"life":20.0}
	next_pickup += 1

func _simulate_pickups(dt: float):
	for pid in pickups.keys():
		var item = pickups[pid]
		item.life -= dt
		if item.life <= 0:
			pickups.erase(pid)
			continue
		for id in actors:
			var a = actors[id]
			if a.hp <= 0 or Vector2(a.p.x-item.p.x,a.p.z-item.p.z).length() > 1.4: continue
			if item.kind == 0:
				if a.hp >= float(a.get("max_hp",100)): continue
				a["hp"] = minf(float(a.get("max_hp",100)),a.hp+30)
			else:
				a["ammo_by"] = {}
				a["ammo"] = a.mag
				a["reload"] = 0.0
			pickups.erase(pid)
			break

@rpc("authority", "call_remote", "unreliable_ordered")
func _pickups_sync(data: PackedFloat32Array):
	if data.size()%4 != 0: return
	pickups.clear()
	for j in range(0,data.size(),4):
		pickups[int(data[j])] = {"p":Vector3(data[j+1],0,data[j+2]),"kind":int(data[j+3]),"life":20.0}

func _broadcast_state():
	super._broadcast_state()
	if test_mode: return
	var data = PackedFloat32Array()
	for pid in pickups:
		data.append_array(PackedFloat32Array([pid,pickups[pid].p.x,pickups[pid].p.z,pickups[pid].kind]))
	_pickups_sync.rpc(data)

# ---------------------------------------------------------------- mission flow & profile

func _start_mission(index: int):
	pickups.clear()
	super._start_mission(index)

@rpc("authority", "call_remote", "reliable")
func _deploy(index: int):
	pickups.clear()
	desired_slot = 0
	shown_slot = -1
	combo = 0
	last_local_hp = -1.0
	super._deploy(index)
	if not dedicated and is_instance_valid(hud) and not test_mode:
		_build_plus_hud()
		# Outlines keep frameless HUD text readable over bright skies.
		for l in hud.find_children("*","Label",true,false):
			l.add_theme_color_override("font_outline_color",Color(0,0,0,0.75))
			if l.get_theme_constant("outline_size") < 4: l.add_theme_constant_override("outline_size",4)

func _clean_models():
	super._clean_models()
	for v in pickup_visuals.values(): v.queue_free()
	pickup_visuals.clear()
	for f in floaters:
		if is_instance_valid(f[0]): f[0].queue_free()
	floaters.clear()

@rpc("any_peer", "call_remote", "reliable")
func _refresh_profile():
	if not multiplayer.is_server() or test_mode: return
	var id = multiplayer.get_remote_sender_id()
	if not verified.has(id): return
	var result = await _api("/api/server/profile",{"account_id":int(verified[id].id)},true)
	if result.get("ok",false) and verified.has(id):
		verified[id] = result.profile
		if mode != "playing" and actors.has(id): _set_actor_stats(id)

func _announce_all(text: String):
	if network == "host": _announce.rpc(text)
	_announce(text)

@rpc("authority", "call_remote", "reliable")
func _announce(text: String):
	if dedicated or not is_instance_valid(banner_label): return
	banner_label.text = text
	banner_timer = 2.2

func _explosion_all(pos: Vector3, radius: float, color: Color):
	if network == "host": _explosion_fx.rpc(pos,radius,color)
	_explosion_fx(pos,radius,color)

@rpc("authority", "call_remote", "unreliable")
func _explosion_fx(pos: Vector3, radius: float, color: Color):
	if dedicated: return
	var ball = SphereMesh.new()
	ball.radius = radius*0.6
	ball.height = radius*1.2
	var mat = _mat(color,3)
	mat.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
	mat.albedo_color.a = 0.55
	var fx = _mesh(self,ball,pos,mat)
	fx.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	effects.append([fx,0.22])
	if actors.has(local_id) and actors[local_id].p.distance_to(pos) < radius*3: shake = 0.08
	_tone(120,0.18,0.18)

# ---------------------------------------------------------------- client visuals

func _enemy_model(e: Dictionary) -> Node3D:
	if e.get("boss",false): return _boss_model(int(e.get("kind_id",5)))
	var kind = clampi(int(e.get("kind_id",0)),0,KINDS.size()-1)
	var color: Color = KINDS[kind][1]
	var root = _actor_model(color)
	match kind:
		1:
			for x in [-0.22,0.22]:
				var horn = _cube(root,Vector3(x,1.95,-0.1),Vector3(0.08,0.28,0.08),color,2)
				horn.rotation_degrees.x = -25
		2:
			_cube(root,Vector3(0.48,1.15,-1.05),Vector3(0.07,0.07,0.9),Color("2a2140"))
			var laser = _cube(root,Vector3(0.48,1.15,-13.5),Vector3(0.025,0.025,24),Color("ff2244"),4)
			laser.name = "Laser"
			laser.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
			laser.visible = false
		3:
			var core = SphereMesh.new()
			core.radius = 0.32
			core.height = 0.64
			_mesh(root,core,Vector3(0,1.25,0.35),_mat(color,1.5))
		4:
			var plate = _cube(root,Vector3(0,1.05,-0.62),Vector3(1.05,1.35,0.1),color,0.8)
			var mat: StandardMaterial3D = plate.material_override
			mat.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
			mat.albedo_color.a = 0.55
	root.scale = Vector3.ONE*float(KINDS[kind][2])
	return root

func _render_models(dt: float):
	if network == "client":
		for b in bullets.values():
			if b.get("grenade",false): b.v.y -= 20.0*dt
	super._render_models(dt)
	if dedicated: return
	for bid in bullet_visuals:
		var vis = bullet_visuals[bid]
		if vis.has_meta("styled") or not bullets.has(bid): continue
		vis.set_meta("styled",true)
		match int(bullets[bid].get("kind",0)):
			1:
				vis.material_override = _mat(Color("c78bff"),1.5)
				vis.scale = Vector3(0.6,0.6,0.6)
			2:
				vis.material_override = _mat(Color("7dff9a"),1.2)
				vis.scale = Vector3(1.3,1.3,1.3)
	for id in actors:
		if not visuals.has(id) or id == local_id: continue
		if actors[id].hp <= 0:
			visuals[id].visible = true
			visuals[id].rotation.x = -PI*0.45
	for eid in enemy_visuals:
		if not enemies.has(eid): continue
		var laser = enemy_visuals[eid].get_node_or_null("Laser")
		if laser: laser.visible = int(enemies[eid].get("state",0)) == 1
	for pid in pickups:
		if not pickup_visuals.has(pid):
			var color = Color("5dff8f") if pickups[pid].kind == 0 else Color("ffd166")
			var holder = Node3D.new()
			add_child(holder)
			_cube(holder,Vector3.ZERO,Vector3(0.45,0.45,0.45),color,0.9)
			if pickups[pid].kind == 0:
				_cube(holder,Vector3.ZERO,Vector3(0.55,0.14,0.14),WHITE,0.6)
				_cube(holder,Vector3.ZERO,Vector3(0.14,0.55,0.14),WHITE,0.6)
			pickup_visuals[pid] = holder
		var node = pickup_visuals[pid]
		node.position = pickups[pid].p+Vector3(0,0.6+sin(clock_time*3+pid)*0.12,0)
		node.rotation.y += dt*2.0
	for pid in pickup_visuals.keys():
		if not pickups.has(pid):
			pickup_visuals[pid].queue_free()
			pickup_visuals.erase(pid)
	for i in range(floaters.size()-1,-1,-1):
		var f = floaters[i]
		f[1] -= dt
		if f[1] <= 0 or not is_instance_valid(f[0]):
			if is_instance_valid(f[0]): f[0].queue_free()
			floaters.remove_at(i)
			continue
		f[0].position.y += dt*1.4
		f[0].modulate.a = clampf(f[1]/0.4,0,1)
	combo_timer = maxf(0,combo_timer-dt)
	if combo_timer <= 0: combo = 0
	damage_flash = maxf(0,damage_flash-dt)
	banner_timer = maxf(0,banner_timer-dt)
	if actors.has(local_id):
		var slot = int(actors[local_id].get("slot",0))
		if slot != shown_slot:
			shown_slot = slot
			desired_slot = slot
			_build_viewmodel(_slot_weapon(slot))

func _damage_number(at: Vector3, amount: float, head: bool):
	var label = Label3D.new()
	label.text = str(int(round(amount)))
	label.billboard = BaseMaterial3D.BILLBOARD_ENABLED
	label.no_depth_test = true
	label.fixed_size = true
	label.pixel_size = 0.0011
	label.font_size = 44 if head else 32
	label.outline_size = 10
	label.modulate = GOLD if head else WHITE
	label.position = at+Vector3(randf_range(-0.2,0.2),0.3,randf_range(-0.2,0.2))
	add_child(label)
	floaters.append([label,0.7])

func _on_local_kill():
	combo = combo+1 if combo_timer > 0 else 1
	combo_timer = 2.5

func _build_viewmodel(wid: String):
	if not is_instance_valid(weapon): return
	for child in weapon.get_children():
		if child != muzzle: child.queue_free()
	var accent = CYAN
	match wid:
		"pistol":
			_cube(weapon,Vector3(0,0.01,-0.02),Vector3(0.07,0.09,0.26),INK)
			_cube(weapon,Vector3(0,-0.1,0.06),Vector3(0.06,0.16,0.08),Color("334d61"))
			_cube(weapon,Vector3(0,0.06,-0.04),Vector3(0.035,0.012,0.18),accent,0.8)
			muzzle.position = Vector3(0,0.01,-0.16)
		"rifle":
			_cube(weapon,Vector3.ZERO,Vector3(0.1,0.12,0.5),INK)
			_cube(weapon,Vector3(0,0,-0.36),Vector3(0.05,0.05,0.3),Color("34485a"))
			_cube(weapon,Vector3(0,-0.14,-0.06),Vector3(0.07,0.18,0.1),Color("2a3b4d"))
			_cube(weapon,Vector3(0,-0.1,0.16),Vector3(0.07,0.14,0.08),Color("334d61"))
			_cube(weapon,Vector3(0,0.07,-0.1),Vector3(0.04,0.018,0.26),accent,0.6)
			muzzle.position = Vector3(0,0,-0.52)
		"shotgun":
			_cube(weapon,Vector3.ZERO,Vector3(0.13,0.13,0.46),Color("3b2a22"))
			for x in [-0.03,0.03]:
				_cube(weapon,Vector3(x,0.02,-0.34),Vector3(0.05,0.05,0.28),Color("1c242c"))
			_cube(weapon,Vector3(0,-0.07,-0.2),Vector3(0.1,0.06,0.16),Color("5a4030"))
			_cube(weapon,Vector3(0,-0.1,0.15),Vector3(0.08,0.15,0.09),Color("334d61"))
			_cube(weapon,Vector3(0,0.075,-0.05),Vector3(0.05,0.015,0.2),ORANGE,0.8)
			muzzle.position = Vector3(0,0.02,-0.5)
		"sniper":
			_cube(weapon,Vector3(0,0,0.02),Vector3(0.08,0.1,0.6),INK)
			_cube(weapon,Vector3(0,0.01,-0.48),Vector3(0.035,0.035,0.4),Color("2a3b4d"))
			var scope = CylinderMesh.new()
			scope.top_radius = 0.035
			scope.bottom_radius = 0.035
			scope.height = 0.22
			var sc = _mesh(weapon,scope,Vector3(0,0.1,-0.04),_mat(Color("1a2430")))
			sc.rotation_degrees.x = 90
			_cube(weapon,Vector3(0,0.1,-0.155),Vector3(0.05,0.05,0.01),Color("b47cff"),2)
			_cube(weapon,Vector3(0,-0.1,0.18),Vector3(0.07,0.15,0.08),Color("334d61"))
			muzzle.position = Vector3(0,0.01,-0.7)
	for mesh in weapon.get_children():
		if mesh is GeometryInstance3D: mesh.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF

# ---------------------------------------------------------------- HUD

func _build_plus_hud():
	var g = Gradient.new()
	g.offsets = PackedFloat32Array([0.0,0.55,1.0])
	g.colors = PackedColorArray([Color(0.9,0.05,0.1,0.0),Color(0.9,0.05,0.1,0.0),Color(0.9,0.05,0.1,0.75)])
	var tex = GradientTexture2D.new()
	tex.gradient = g
	tex.fill = GradientTexture2D.FILL_RADIAL
	tex.fill_from = Vector2(0.5,0.5)
	tex.fill_to = Vector2(1.05,1.05)
	vignette = TextureRect.new()
	vignette.texture = tex
	vignette.stretch_mode = TextureRect.STRETCH_SCALE
	vignette.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	vignette.mouse_filter = Control.MOUSE_FILTER_IGNORE
	vignette.modulate.a = 0
	hud.add_child(vignette)
	hud.move_child(vignette,0)
	# Weapon slots above the ammo panel.
	var strip = HBoxContainer.new()
	strip.add_theme_constant_override("separation",6)
	strip.alignment = BoxContainer.ALIGNMENT_END
	strip.anchor_left = 1.0
	strip.anchor_right = 1.0
	strip.anchor_top = 1.0
	strip.anchor_bottom = 1.0
	strip.offset_left = -404
	strip.offset_right = -24
	strip.offset_top = -196
	strip.offset_bottom = -142
	strip.mouse_filter = Control.MOUSE_FILTER_IGNORE
	hud.add_child(strip)
	slot_boxes.clear()
	for i in range(2):
		var box = PanelContainer.new()
		box.mouse_filter = Control.MOUSE_FILTER_IGNORE
		box.custom_minimum_size = Vector2(88,0)
		var l = _label("",13,WHITE)
		l.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		box.add_child(l)
		strip.add_child(box)
		slot_boxes.append([box,l])
	weapon_name_label = _label("",15,WHITE)
	weapon_name_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	weapon_name_label.anchor_left = 1.0
	weapon_name_label.anchor_right = 1.0
	weapon_name_label.anchor_top = 1.0
	weapon_name_label.anchor_bottom = 1.0
	weapon_name_label.offset_left = -404
	weapon_name_label.offset_right = -28
	weapon_name_label.offset_top = -222
	weapon_name_label.offset_bottom = -200
	weapon_name_label.add_theme_color_override("font_outline_color",Color(0,0,0,0.7))
	weapon_name_label.add_theme_constant_override("outline_size",4)
	hud.add_child(weapon_name_label)
	# Grenade skill above the vitals panel.
	var skill = _hud_block(hud)
	skill.anchor_top = 1.0
	skill.anchor_bottom = 1.0
	skill.offset_left = 24
	skill.offset_right = 240
	skill.offset_top = -204
	skill.offset_bottom = -146
	var sv = VBoxContainer.new()
	sv.add_theme_constant_override("separation",4)
	skill.add_child(sv)
	skill_label = _label("Q  手榴彈  就緒",13,WHITE)
	sv.add_child(skill_label)
	skill_bar = _bar(WHITE,2)
	skill_bar.max_value = 1.0
	sv.add_child(skill_bar)
	combo_label = _label("",30,GOLD)
	combo_label.anchor_left = 1.0
	combo_label.anchor_right = 1.0
	combo_label.offset_left = -300
	combo_label.offset_right = -40
	combo_label.offset_top = 300
	combo_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	combo_label.add_theme_color_override("font_outline_color",Color(0,0,0,0.8))
	combo_label.add_theme_constant_override("outline_size",6)
	hud.add_child(combo_label)
	banner_label = _label("",38,WHITE)
	banner_label.anchor_left = 0.5
	banner_label.anchor_right = 0.5
	banner_label.offset_left = -400
	banner_label.offset_right = 400
	banner_label.offset_top = 210
	banner_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	banner_label.add_theme_color_override("font_outline_color",Color(0,0,0,0.85))
	banner_label.add_theme_constant_override("outline_size",8)
	hud.add_child(banner_label)
	revive_label = _label("",20,Color("7dff9a"))
	revive_label.anchor_left = 0.5
	revive_label.anchor_right = 0.5
	revive_label.anchor_top = 1.0
	revive_label.anchor_bottom = 1.0
	revive_label.offset_left = -360
	revive_label.offset_right = 360
	revive_label.offset_top = -250
	revive_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	revive_label.add_theme_color_override("font_outline_color",Color(0,0,0,0.85))
	revive_label.add_theme_constant_override("outline_size",6)
	hud.add_child(revive_label)

func _update_hud():
	super._update_hud()
	if test_mode or dedicated or not is_instance_valid(hud) or not is_instance_valid(skill_bar) or not actors.has(local_id): return
	var a = actors[local_id]
	if last_local_hp >= 0 and a.hp < last_local_hp: damage_flash = 0.45
	last_local_hp = a.hp
	var low = a.hp > 0 and a.hp <= float(a.get("max_hp",100))*0.3
	vignette.modulate.a = clampf(damage_flash*1.8,0,1) if not low else 0.45+0.25*sin(clock_time*6)
	var kit: Array = _loadout_for(-1).equipped
	var slot = clampi(int(a.get("slot",0)),0,kit.size()-1)
	for i in range(slot_boxes.size()):
		var has = i < kit.size()
		var box: PanelContainer = slot_boxes[i][0]
		var l: Label = slot_boxes[i][1]
		box.visible = has
		if not has: continue
		l.text = "%d   %s" % [i+1,WEAPONS[kit[i]].short]
		var style = StyleBoxFlat.new()
		style.bg_color = Color(0,0,0,0)
		style.border_color = CYAN if i == slot else Color(1,1,1,0.18)
		style.border_width_bottom = 2 if i == slot else 1
		style.content_margin_left = 6
		style.content_margin_right = 6
		style.content_margin_top = 4
		style.content_margin_bottom = 6
		box.add_theme_stylebox_override("panel",style)
		l.modulate = Color(1,1,1,1.0 if has else 0.4)
		l.add_theme_color_override("font_color",WHITE if i == slot else MUTED)
	weapon_name_label.text = WEAPONS[kit[slot]].name
	var cd = float(a.get("skill_cd",0))
	var max_cd = SKILL_COOLDOWN*float(a.get("skill_mult",1.0))
	skill_bar.value = 1.0-clampf(cd/maxf(0.1,max_cd),0,1)
	skill_label.text = "Q  手榴彈  就緒" if cd <= 0 else "Q  手榴彈  %.1f s" % cd
	combo_label.text = "連殺 ×%d" % combo if combo >= 2 else ""
	combo_label.scale = Vector2.ONE*(1.0+0.25*clampf(combo_timer-2.2,0,0.3)/0.3)
	banner_label.text = banner_label.text if banner_timer > 0 else ""
	banner_label.modulate.a = clampf(banner_timer/0.4,0,1)
	revive_label.text = ""
	if a.hp <= 0 and actors.size() > 1:
		revive_label.text = "你已倒地 · 等待隊友按住 E 救援  %d%%" % int(100*float(a.get("revive",0))/REVIVE_TIME)
	else:
		for id in actors:
			if id != local_id and actors[id].hp <= 0 and actors[id].p.distance_to(a.p) < 2.4:
				revive_label.text = "按住 E 救援隊友  %d%%" % int(100*float(actors[id].get("revive",0))/REVIVE_TIME)
	if a.hp <= 0 and actors.size() > 1 and is_instance_valid(toast): toast.text = ""
	_reload_audio_tick(a)

# ---------------------------------------------------------------- reload sound

func _reload_audio_tick(a: Dictionary):
	# Remaining reload fraction: 1 when a reload starts, 0 when finished or idle.
	var ratio = float(a.get("reload_ratio",float(a.get("reload",0))/maxf(0.01,float(a.get("reload_time",RELOAD_TIME)))))
	var slot = int(a.get("slot",0))
	if a.hp > 0 and slot == last_reload_slot:
		if last_reload_ratio <= 0 and ratio > 0: _play_reload("out")
		elif last_reload_ratio > 0.4 and ratio <= 0.4 and ratio > 0: _play_reload("in")
		elif last_reload_ratio > 0 and ratio <= 0 and int(a.get("ammo",0)) >= int(a.get("mag",1)): _play_reload("rack")
	last_reload_ratio = ratio
	last_reload_slot = slot

func _play_reload(kind: String):
	reload_sfx_log.append(kind)
	if dedicated or DisplayServer.get_name() == "headless": return
	if not is_instance_valid(reload_audio):
		reload_audio = AudioStreamPlayer.new()
		reload_audio.volume_db = -4.0
		add_child(reload_audio)
	if not reload_sounds.has(kind): reload_sounds[kind] = _make_reload_sound(kind)
	reload_audio.stream = reload_sounds[kind]
	reload_audio.play()

func _make_reload_sound(kind: String) -> AudioStreamWAV:
	var rate = 22050
	# Each event: [start seconds, length, tone Hz, noise mix, volume]
	var events = {
		"out": [[0.0,0.05,520.0,0.7,0.5],[0.06,0.12,180.0,0.85,0.35]],
		"in": [[0.0,0.035,1500.0,0.5,0.6],[0.04,0.06,300.0,0.6,0.5]],
		"rack": [[0.0,0.05,900.0,0.75,0.5],[0.12,0.04,1900.0,0.45,0.65]]
	}[kind]
	var total = 0.0
	for e in events: total = maxf(total,e[0]+e[1])
	var count = int(rate*(total+0.02))
	var samples = PackedFloat32Array()
	samples.resize(count)
	var rng = RandomNumberGenerator.new()
	rng.seed = hash(kind)
	for e in events:
		var start = int(e[0]*rate)
		var length = int(e[1]*rate)
		var smooth = 0.0
		for i in range(length):
			var t = float(i)/rate
			var env = exp(-t*55.0)*minf(1.0,i/40.0)
			smooth = lerpf(smooth,rng.randf_range(-1,1),0.35)
			var value = (sin(TAU*e[2]*t)*(1.0-e[3])+smooth*e[3])*env*e[4]
			if start+i < count: samples[start+i] += value
	var data = PackedByteArray()
	data.resize(count*2)
	for i in range(count): data.encode_s16(i*2,int(clampf(samples[i],-1,1)*32767))
	var wav = AudioStreamWAV.new()
	wav.format = AudioStreamWAV.FORMAT_16_BITS
	wav.mix_rate = rate
	wav.data = data
	return wav

# ---------------------------------------------------------------- armory

func _show_armory():
	if test_mode: return
	mode = "armory"
	Input.mouse_mode = Input.MOUSE_MODE_VISIBLE
	_clear_ui()
	_backdrop(screen,0.97,0.75)
	var gold = int(profile.get("gold",0))
	var lo = _loadout_for(-1)
	var header = _box(screen,Vector2(48,24),700)
	header.add_theme_constant_override("separation",0)
	header.add_child(_label("ARMORY  ·  用任務金幣購買與升級武器",14,CYAN))
	header.add_child(_label("軍械庫",36))
	var wallet = _panel(screen,GOLD)
	wallet.anchor_left = 1.0
	wallet.anchor_right = 1.0
	wallet.offset_left = -300
	wallet.offset_right = -48
	wallet.offset_top = 32
	var wl = _label("%d G" % gold,26,GOLD)
	wl.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	wallet.add_child(wl)
	var grid = GridContainer.new()
	grid.columns = 2
	grid.add_theme_constant_override("h_separation",16)
	grid.add_theme_constant_override("v_separation",16)
	grid.position = Vector2(48,112)
	screen.add_child(grid)
	for wid in WEAPON_ORDER:
		_armory_card(grid,wid,lo,gold)
	var footer = HBoxContainer.new()
	footer.add_theme_constant_override("separation",16)
	footer.anchor_top = 1.0
	footer.anchor_bottom = 1.0
	footer.offset_left = 48
	footer.offset_right = 1232
	footer.offset_top = -64
	footer.offset_bottom = -20
	screen.add_child(footer)
	var back = _button("←  返回基地",_home,footer,44)
	back.custom_minimum_size.x = 200
	armory_status = _label("升級效果：傷害 +12% / 級 · 彈匣 +15% / 級 · 換彈 -8% / 級（最高 5 級）",14,MUTED)
	armory_status.size_flags_vertical = Control.SIZE_SHRINK_CENTER
	footer.add_child(armory_status)

func _armory_card(parent: Node, wid: String, lo: Dictionary, gold: int):
	var w = WEAPONS[wid]
	var owned = wid in lo.owned
	var levels: Dictionary = lo.levels.get(wid,{}) if lo.levels.get(wid,{}) is Dictionary else {}
	var card = _panel(parent,CYAN if owned else PANEL_EDGE,Color(0.07,0.072,0.078,0.94))
	card.mouse_filter = Control.MOUSE_FILTER_PASS
	card.custom_minimum_size = Vector2(584,0)
	var v = VBoxContainer.new()
	v.add_theme_constant_override("separation",6)
	card.add_child(v)
	var top = HBoxContainer.new()
	v.add_child(top)
	top.add_child(_label(w.name,21,WHITE if owned else MUTED))
	var spacer = Control.new()
	spacer.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	top.add_child(spacer)
	top.add_child(_label("已擁有" if owned else "未解鎖",14,CYAN if owned else ORANGE))
	v.add_child(_label(w.desc,14,MUTED))
	var st = weapon_stats(wid,levels)
	var stats = GridContainer.new()
	stats.columns = 3
	stats.add_theme_constant_override("h_separation",10)
	stats.add_theme_constant_override("v_separation",3)
	v.add_child(stats)
	var dps = st.damage*st.pellets/st.interval
	for row in [["傷害","%d%s" % [round(st.damage),(" ×%d" % st.pellets) if st.pellets > 1 else ""],st.damage*st.pellets/700.0],["每秒傷害","%d" % round(dps),dps/280.0],["彈匣","%d 發" % st.mag],["換彈","%.2f 秒" % st.reload]]:
		stats.add_child(_label(row[0],13,MUTED))
		var bar = _bar(CYAN,6)
		bar.max_value = 1.0
		bar.custom_minimum_size.x = 260
		bar.size_flags_vertical = Control.SIZE_SHRINK_CENTER
		if row.size() > 2: bar.value = clampf(row[2],0.04,1)
		elif row[0] == "彈匣": bar.value = clampf(st.mag/45.0,0.04,1)
		else: bar.value = clampf(1.0-st.reload/3.0,0.04,1)
		stats.add_child(bar)
		stats.add_child(_label(row[1],13))
	if not owned:
		var buy = _button("購買    %d G" % w.price,func(): _armory_request("/api/shop/buy",{"weapon":wid}),v,42)
		buy.disabled = gold < w.price or armory_busy
		buy.add_theme_color_override("font_color",GOLD)
		return
	var kit: Array = lo.equipped
	var equip_row = HBoxContainer.new()
	equip_row.add_theme_constant_override("separation",8)
	v.add_child(equip_row)
	for slot_index in range(2):
		var on = slot_index < kit.size() and kit[slot_index] == wid
		var eb = _button(("● %d 號武器" if on else "裝備到 %d 號") % (slot_index+1),func(): _armory_equip(wid,slot_index),equip_row,36)
		eb.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		eb.add_theme_font_size_override("font_size",14)
		eb.disabled = on or armory_busy
		if on: eb.add_theme_color_override("font_disabled_color",CYAN)
	var ups = HBoxContainer.new()
	ups.add_theme_constant_override("separation",8)
	v.add_child(ups)
	for key in UPGRADE_KEYS:
		var lv = int(levels.get(key,0))
		var maxed = lv >= MAX_UPGRADE_LEVEL
		var cost = upgrade_cost(wid,lv)
		var label = "%s  Lv.%d\n%s" % [UPGRADE_NAMES[key],lv,"已滿級" if maxed else "%d G" % cost]
		var b = _button(label,func(): _armory_request("/api/shop/upgrade",{"weapon":wid,"stat":key}),ups,52)
		b.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		b.add_theme_font_size_override("font_size",15)
		b.disabled = maxed or gold < cost or armory_busy

func _armory_equip(wid: String, slot_index: int):
	var kit: Array = _loadout_for(-1).equipped.duplicate()
	var other = kit.find(wid)
	while kit.size() <= slot_index: kit.append(wid)
	if other != -1 and other != slot_index:
		kit[other] = kit[slot_index]
	kit[slot_index] = wid
	var clean: Array = []
	for w in kit:
		if not w in clean: clean.append(w)
	_armory_request("/api/shop/equip",{"weapons":clean})

func _armory_request(path: String, data: Dictionary):
	if armory_busy: return
	armory_busy = true
	if is_instance_valid(armory_status): armory_status.text = "處理中…"
	var result = await _api(path,data)
	armory_busy = false
	if mode != "armory": return
	if result.get("ok",false):
		profile = result.profile
		if network == "client": _refresh_profile.rpc_id(1)
		_show_armory()
		armory_status.text = "完成！裝備已更新。"
		armory_status.add_theme_color_override("font_color",CYAN)
	else:
		_show_armory()
		armory_status.text = str(result.get("error","操作失敗"))
		armory_status.add_theme_color_override("font_color",ORANGE)


# ---------------------------------------------------------------- recoil

@rpc("authority", "call_remote", "unreliable")
func _shot_fx(origin: Vector3, end: Vector3, hit: bool, shooter: int, head: bool = false, dealt: float = 0.0, killed: bool = false):
	super._shot_fx(origin,end,hit,shooter,head,dealt,killed)
	if dedicated or shooter != local_id or not actors.has(local_id): return
	# One kick per trigger pull, even for multi-pellet weapons.
	if clock_time-last_recoil_time < 0.03: return
	last_recoil_time = clock_time
	var w = WEAPONS[_slot_weapon(int(actors[local_id].get("slot",0)))]
	var kick = float(w.recoil)*(0.85+randf()*0.3)
	pitch = clampf(pitch+kick,-LOOK_LIMIT,LOOK_LIMIT)
	yaw = wrapf(yaw+randf_range(-1.0,1.0)*float(w.sway),-PI,PI)
	recoil_pending += kick*0.65
	shake = 0.02+kick*0.9

func _update_camera(dt: float):
	if recoil_pending > 0.0001 and mode == "playing":
		var back = recoil_pending*minf(1.0,dt*9.0)
		pitch = clampf(pitch-back,-LOOK_LIMIT,LOOK_LIMIT)
		recoil_pending -= back
	super._update_camera(dt)

# ---------------------------------------------------------------- boss soldiers

func _boss_model(kind: int) -> Node3D:
	var root = Node3D.new()
	var commander = kind == 6
	var armor = Color("3a2a2a") if not commander else Color("2c2438")
	var plate = Color("6b3a30") if not commander else Color("8a6a3a")
	var glow: Color = KINDS[clampi(kind,5,6)][1]
	for x in [-0.18,0.18]:
		_cube(root,Vector3(x,0.42,0),Vector3(0.26,0.84,0.3),armor)
		_cube(root,Vector3(x,0.08,-0.05),Vector3(0.3,0.16,0.42),Color("1c1a1f"))
		_cube(root,Vector3(x,0.55,-0.16),Vector3(0.2,0.18,0.04),plate)
	_cube(root,Vector3(0,0.9,0),Vector3(0.62,0.2,0.34),Color("221d24"))
	_cube(root,Vector3(0,1.25,0),Vector3(0.78,0.62,0.44),armor)
	_cube(root,Vector3(0,1.28,-0.23),Vector3(0.6,0.46,0.04),plate)
	_cube(root,Vector3(0,1.3,-0.255),Vector3(0.36,0.05,0.02),glow,2.5)
	for x in [-0.56,0.56]:
		_cube(root,Vector3(x,1.55,0),Vector3(0.36,0.22,0.5),plate)
		_cube(root,Vector3(x,1.18,0),Vector3(0.22,0.6,0.24),armor)
		_cube(root,Vector3(x,0.86,-0.06),Vector3(0.24,0.16,0.28),Color("1c1a1f"))
	_cube(root,Vector3(0,1.6,0),Vector3(0.22,0.12,0.22),Color("1c1a1f"))
	_cube(root,Vector3(0,1.72,0),Vector3(0.42,0.36,0.44),plate)
	_cube(root,Vector3(0,1.72,-0.225),Vector3(0.34,0.08,0.02),glow,3)
	# Heavy cannon held at the hip.
	_cube(root,Vector3(0.56,1.0,-0.42),Vector3(0.24,0.26,0.95),Color("1a1a20"))
	_cube(root,Vector3(0.56,1.0,-0.95),Vector3(0.16,0.16,0.2),Color("2e2e36"))
	_cube(root,Vector3(0.56,1.0,-1.06),Vector3(0.12,0.12,0.03),glow,3)
	_cube(root,Vector3(0.56,1.15,-0.4),Vector3(0.06,0.04,0.6),glow,1.5)
	if commander:
		_cube(root,Vector3(0,2.0,0.02),Vector3(0.06,0.24,0.46),glow,1.2)
		var cape = _cube(root,Vector3(0,1.05,0.3),Vector3(0.74,1.1,0.04),Color("5a1f2a"))
		cape.rotation_degrees.x = 8
		for x in [-0.56,0.56]:
			_cube(root,Vector3(x,1.68,0),Vector3(0.3,0.06,0.44),glow,1.2)
	var ring = TorusMesh.new()
	ring.inner_radius = 0.55
	ring.outer_radius = 0.6
	_mesh(root,ring,Vector3(0,0.03,0),_mat(glow,1.2))
	root.scale = Vector3.ONE*float(KINDS[clampi(kind,5,6)][2])
	return root

func _update_boss_hud():
	super._update_boss_hud()
	if not is_instance_valid(boss_panel) or not boss_panel.visible: return
	for enemy in enemies.values():
		if not enemy.get("boss",false): continue
		var kind = clampi(int(enemy.get("kind_id",5)),5,6)
		var maximum = maxf(1.0,float(enemy.get("max_hp",enemy.hp)))
		boss_name_label.text = "%s%s   %d / %d" % [KINDS[kind][0],"  ·  狂暴" if int(enemy.get("state",0)) == 2 else "",ceili(float(enemy.hp)),ceili(maximum)]
		return

# ---------------------------------------------------------------- admin

func _is_admin(id: int) -> bool:
	if verified.has(id): return bool(verified[id].get("admin",false))
	return id == local_id and not dedicated and bool(profile.get("admin",false))

@rpc("any_peer", "call_remote", "reliable")
func _admin_apply(settings: Dictionary):
	var id = multiplayer.get_remote_sender_id() if multiplayer.get_remote_sender_id() != 0 else local_id
	if not _is_admin(id): return
	admin_by_id[id] = {
		"dmg": clampf(float(settings.get("dmg",1.0)),1.0,20.0),
		"speed": clampf(float(settings.get("speed",1.0)),0.5,3.0),
		"god": bool(settings.get("god",false)),
		"ammo": bool(settings.get("ammo",false)),
		"fly": bool(settings.get("fly",false))
	}
	_apply_admin(id)

func _apply_admin(id: int):
	if not actors.has(id) or not admin_by_id.has(id) or not _is_admin(id): return
	var a = actors[id]
	var s = admin_by_id[id]
	var old_speed = float(a.get("admin_speed",1.0))
	a["speed"] = float(a.get("speed",6.5))/old_speed*float(s.speed)
	a["admin_speed"] = float(s.speed)
	a["admin_dmg"] = float(s.dmg)
	a["god"] = s.god
	a["inf_ammo"] = s.ammo
	a["fly"] = s.fly
	_refresh_weapon(a)

func _send_admin():
	if not bool(profile.get("admin",false)): return
	if network == "client": _admin_apply.rpc_id(1,admin_settings)
	else: _admin_apply(admin_settings)

@rpc("authority", "call_remote", "reliable")
func _account_ok(user: Dictionary):
	super._account_ok(user)
	_send_admin()

func _home():
	super._home()
	if test_mode or dedicated or mode != "home": return
	# The lobby backdrop previews the selected region.
	_apply_region(selected)
	if is_instance_valid(showcase): showcase.visible = true
	if not bool(profile.get("admin",false)): return
	var admin_button = _button("管理員",_show_admin,screen,40)
	admin_button.anchor_left = 1.0
	admin_button.anchor_right = 1.0
	admin_button.offset_left = -200
	admin_button.offset_right = -48
	admin_button.offset_top = 136
	admin_button.offset_bottom = 176
	admin_button.add_theme_color_override("font_color",ORANGE)
	admin_button.add_theme_font_size_override("font_size",16)

func _show_pause_menu():
	super._show_pause_menu()
	if not bool(profile.get("admin",false)) or not is_instance_valid(pause_overlay): return
	var buttons = pause_overlay.find_children("*","VBoxContainer",true,false)
	if buttons.is_empty(): return
	var b = _button("管理員能力",func():
		var overlay = Control.new()
		overlay.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
		pause_overlay.add_child(overlay)
		var center = CenterContainer.new()
		center.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
		overlay.add_child(center)
		var card = _panel(center,ORANGE,Color(0.07,0.072,0.078,0.98))
		card.mouse_filter = Control.MOUSE_FILTER_PASS
		card.custom_minimum_size.x = 460
		var v = VBoxContainer.new()
		v.add_theme_constant_override("separation",10)
		card.add_child(v)
		_admin_ability_panel(v)
		_button("關閉",func(): overlay.queue_free(),v,44),buttons[0],44)
	b.add_theme_color_override("font_color",ORANGE)

func _admin_ability_panel(v: VBoxContainer):
	v.add_child(_label("ADMIN  ·  管理員能力",14,ORANGE))
	v.add_child(_label("只對你自己的角色生效，立即同步到伺服器。",14,MUTED))
	for spec in [["dmg","攻擊力倍率",1.0,20.0,0.5,"×%.1f"],["speed","移動速度倍率",0.5,3.0,0.1,"×%.1f"]]:
		var row = HBoxContainer.new()
		row.add_theme_constant_override("separation",10)
		v.add_child(row)
		var name_label = _label(spec[1],16)
		name_label.custom_minimum_size.x = 130
		row.add_child(name_label)
		var slider = HSlider.new()
		slider.min_value = spec[2]
		slider.max_value = spec[3]
		slider.step = spec[4]
		slider.value = float(admin_settings[spec[0]])
		slider.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		slider.size_flags_vertical = Control.SIZE_SHRINK_CENTER
		slider.custom_minimum_size.x = 180
		row.add_child(slider)
		var value_label = _label(spec[5] % slider.value,16,GOLD)
		value_label.custom_minimum_size.x = 56
		row.add_child(value_label)
		var key = spec[0]
		var fmt = spec[5]
		slider.value_changed.connect(func(value):
			admin_settings[key] = value
			value_label.text = fmt % value
			_send_admin())
	for spec in [["god","無敵（不會受到傷害）"],["ammo","無限彈藥（不需換彈）"],["fly","飛行（Space 上升 · Ctrl 下降）"]]:
		var toggle = CheckButton.new()
		toggle.text = spec[1]
		toggle.button_pressed = bool(admin_settings[spec[0]])
		var key = spec[0]
		toggle.toggled.connect(func(on):
			admin_settings[key] = on
			_send_admin())
		v.add_child(toggle)

func _show_admin():
	if test_mode or not bool(profile.get("admin",false)): return
	mode = "admin"
	Input.mouse_mode = Input.MOUSE_MODE_VISIBLE
	_clear_ui()
	_backdrop(screen,0.97,0.8)
	var header = _box(screen,Vector2(48,24),700)
	header.add_theme_constant_override("separation",0)
	header.add_child(_label("ADMIN CONSOLE",14,ORANGE))
	header.add_child(_label("管理員控制台",36))
	var row = HBoxContainer.new()
	row.add_theme_constant_override("separation",20)
	row.position = Vector2(48,112)
	screen.add_child(row)
	# Left: grant rewards.
	var grant = _panel(row,GOLD,Color(0.07,0.072,0.078,0.95))
	grant.mouse_filter = Control.MOUSE_FILTER_PASS
	grant.custom_minimum_size = Vector2(640,600)
	var gv = VBoxContainer.new()
	gv.add_theme_constant_override("separation",10)
	grant.add_child(gv)
	gv.add_child(_label("發放福利",22,GOLD))
	gv.add_child(_label("輸入玩家帳號，或從下方名單點選；留空並按「發給全部玩家」即可全服發放。",14,MUTED))
	var target = LineEdit.new()
	target.placeholder_text = "玩家帳號"
	target.custom_minimum_size.y = 44
	gv.add_child(target)
	var fields = HBoxContainer.new()
	fields.add_theme_constant_override("separation",10)
	gv.add_child(fields)
	var gold_input = SpinBox.new()
	gold_input.min_value = 0
	gold_input.max_value = 100000
	gold_input.step = 50
	gold_input.value = 500
	gold_input.suffix = "G"
	gold_input.custom_minimum_size = Vector2(200,44)
	fields.add_child(gold_input)
	var weapon_pick = OptionButton.new()
	weapon_pick.add_item("不發放武器")
	for wid in WEAPON_ORDER.slice(1):
		weapon_pick.add_item("解鎖 "+WEAPONS[wid].name)
	weapon_pick.custom_minimum_size = Vector2(260,44)
	weapon_pick.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	fields.add_child(weapon_pick)
	var actions = HBoxContainer.new()
	actions.add_theme_constant_override("separation",10)
	gv.add_child(actions)
	var send = func(all: bool):
		var weapon_id = "" if weapon_pick.selected <= 0 else WEAPON_ORDER[weapon_pick.selected]
		var who = "*" if all else target.text.strip_edges()
		_admin_grant(who,int(gold_input.value),weapon_id)
	var one = _button("發給指定玩家",func(): send.call(false),actions,46)
	var everyone = _button("發給全部玩家",func(): send.call(true),actions,46)
	for b in [one,everyone]: b.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	everyone.add_theme_color_override("font_color",GOLD)
	admin_status = _label("",15,CYAN)
	admin_status.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	gv.add_child(admin_status)
	gv.add_child(_label("玩家名單",16,MUTED))
	var scroll = ScrollContainer.new()
	scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	scroll.custom_minimum_size.y = 220
	gv.add_child(scroll)
	admin_players = VBoxContainer.new()
	admin_players.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	scroll.add_child(admin_players)
	admin_players.set_meta("target",target)
	# Right: self abilities.
	var self_panel = _panel(row,ORANGE,Color(0.07,0.072,0.078,0.95))
	self_panel.mouse_filter = Control.MOUSE_FILTER_PASS
	self_panel.custom_minimum_size = Vector2(500,0)
	self_panel.size_flags_vertical = Control.SIZE_SHRINK_BEGIN
	var sv = VBoxContainer.new()
	sv.add_theme_constant_override("separation",12)
	self_panel.add_child(sv)
	_admin_ability_panel(sv)
	var back = _button("←  返回基地",_home,screen,44)
	back.anchor_top = 1.0
	back.anchor_bottom = 1.0
	back.offset_left = 48
	back.offset_right = 248
	back.offset_top = -64
	back.offset_bottom = -20
	_admin_load_players()

func _admin_load_players():
	var result = await _api("/api/admin/players",{})
	if mode != "admin" or not is_instance_valid(admin_players): return
	for child in admin_players.get_children(): child.queue_free()
	if not result.get("ok",false):
		admin_status.text = str(result.get("error","無法讀取玩家名單"))
		return
	var target: LineEdit = admin_players.get_meta("target")
	for player in result.players:
		var name = str(player.get("username",""))
		var b = _button("%s      %d G      已解鎖 %d 區" % [name,int(player.get("gold",0)),int(player.get("unlocked",0))+1],func(): target.text = name,admin_players,38)
		b.alignment = HORIZONTAL_ALIGNMENT_LEFT
		b.add_theme_font_size_override("font_size",15)

func _admin_grant(username: String, gold: int, weapon_id: String):
	if username.is_empty():
		admin_status.text = "請輸入玩家帳號，或按「發給全部玩家」。"
		admin_status.add_theme_color_override("font_color",ORANGE)
		return
	admin_status.text = "發放中…"
	var result = await _api("/api/admin/grant",{"username":username,"gold":gold,"weapon":weapon_id})
	if mode != "admin" or not is_instance_valid(admin_status): return
	if result.get("ok",false):
		profile = result.profile
		if network == "client": _refresh_profile.rpc_id(1)
		admin_status.add_theme_color_override("font_color",CYAN)
		admin_status.text = "已發放給 %d 位玩家。（玩家在任務結束或重新登入後看到最新金幣）" % int(result.count)
		_admin_load_players()
	else:
		admin_status.add_theme_color_override("font_color",ORANGE)
		admin_status.text = str(result.get("error","發放失敗"))


# ---------------------------------------------------------------- region looks

const REGION_LOOKS = [
	{"sky":Color("6fa8d6"),"fog":Color("a9c8d8"),"fog_density":0.003,"ambient":Color("b9d4b0"),"ambient_energy":0.32,"sun":Color("ffe9c4"),"sun_energy":0.95,"scifi":false,"cover":Color("8a8270"),"wall":Color("6e5c46")},
	{"sky":Color("2a1510"),"fog":Color("3a1d12"),"fog_density":0.009,"ambient":Color("c08a6a"),"ambient_energy":0.45,"sun":Color("ffc9a0"),"sun_energy":0.9,"scifi":true,"cover":Color("4a3a32"),"wall":Color("3e3029")},
	{"sky":Color("160f28"),"fog":Color("24183d"),"fog_density":0.009,"ambient":Color("9c8ad1"),"ambient_energy":0.45,"sun":Color("d9c9ff"),"sun_energy":0.85,"scifi":true,"cover":Color("3a3450"),"wall":Color("332d48")}
]

func _apply_region(index: int):
	super._apply_region(index)
	if dedicated or not is_instance_valid(world_environment): return
	var look = REGION_LOOKS[clampi(index,0,2)]
	world_environment.background_color = look.sky
	world_environment.fog_light_color = look.fog
	world_environment.fog_density = look.fog_density
	world_environment.ambient_light_color = look.ambient
	world_environment.ambient_light_energy = look.ambient_energy
	sun_light.light_color = look.sun
	sun_light.light_energy = look.sun_energy
	scifi_set.visible = look.scifi
	for m in cover_meshes: m.material_override = _mat(look.cover)
	for m in perimeter_meshes: m.material_override = _mat(look.wall)
	if index == 0:
		for child in region_props.get_children(): child.queue_free()
		_build_grassland(region_props)

func _build_grassland(root: Node3D):
	var rng = RandomNumberGenerator.new()
	rng.seed = 2189
	var greens = [Color("355f24"),Color("41702b"),Color("4e8033"),Color("5e8f3c")]
	# Rolling hills on the horizon.
	for i in range(14):
		var angle = TAU*i/14.0+rng.randf_range(-0.2,0.2)
		var radius = rng.randf_range(55,80)
		var hill = SphereMesh.new()
		hill.radius = rng.randf_range(14,24)
		hill.height = hill.radius*0.9
		var h = _mesh(root,hill,Vector3(sin(angle)*radius,-hill.height*0.25,cos(angle)*radius),_mat(greens[rng.randi()%2].darkened(0.1)))
		h.scale = Vector3(1.6,1,1.6)
	# Outer ground so the arena edge does not float in the sky.
	_cube(root,Vector3(0,-0.32,0),Vector3(160,0.5,160),Color("3f6a2b"))
	# Trees outside the fence.
	for i in range(46):
		var angle = rng.randf_range(0,TAU)
		var radius = rng.randf_range(26,48)
		_tree(root,Vector3(sin(angle)*radius,0,cos(angle)*radius),rng.randf_range(0.8,1.5),greens[rng.randi()%greens.size()])
	# Grass tufts and flowers inside the arena.
	for i in range(220):
		var p = Vector3(rng.randf_range(-22,22),0,rng.randf_range(-22,22))
		if _in_cover(p+Vector3.UP*0.1): continue
		var h = rng.randf_range(0.18,0.45)
		var blade = _cube(root,p+Vector3(0,h/2,0),Vector3(0.06,h,0.06),greens[rng.randi()%greens.size()].lightened(0.1))
		blade.rotation = Vector3(rng.randf_range(-0.3,0.3),rng.randf_range(0,TAU),rng.randf_range(-0.3,0.3))
		blade.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	var petals = [Color("fff6e0"),Color("ffd84d"),Color("ff9ec4"),Color("c9a6ff")]
	for i in range(70):
		var p = Vector3(rng.randf_range(-22,22),0.12,rng.randf_range(-22,22))
		var f = _cube(root,p,Vector3(0.1,0.1,0.1),petals[rng.randi()%petals.size()],0.2)
		f.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	# Rocks.
	for i in range(14):
		var rock = SphereMesh.new()
		rock.radius = rng.randf_range(0.4,1.1)
		rock.height = rock.radius*1.3
		var angle = rng.randf_range(0,TAU)
		var radius = rng.randf_range(20,34)
		var r = _mesh(root,rock,Vector3(sin(angle)*radius,rock.radius*0.25,cos(angle)*radius),_mat(Color("8d8a80")))
		r.scale = Vector3(1.3,0.7,1.0)
	_crashed_dropship(root,Vector3(7,0,31))

func _tree(root: Node3D, pos: Vector3, size: float, leaf: Color):
	var trunk = CylinderMesh.new()
	trunk.top_radius = 0.18*size
	trunk.bottom_radius = 0.26*size
	trunk.height = 2.4*size
	_mesh(root,trunk,pos+Vector3(0,1.2*size,0),_mat(Color("6b4a2f")))
	for k in range(3):
		var cone = CylinderMesh.new()
		cone.top_radius = 0.0
		cone.bottom_radius = (1.6-0.35*k)*size
		cone.height = 1.8*size
		_mesh(root,cone,pos+Vector3(0,(2.4+0.9*k)*size,0),_mat(leaf.darkened(0.08*k)))

func _crashed_dropship(root: Node3D, pos: Vector3):
	var ship = Node3D.new()
	ship.position = pos
	ship.rotation = Vector3(0.08,-0.6,0.18)
	root.add_child(ship)
	var hull = Color("5a6068")
	var burnt = Color("2b2a2a")
	_cube(ship,Vector3(0,1.2,0),Vector3(3.2,2.2,9),hull)
	_cube(ship,Vector3(0,1.4,-5),Vector3(2.4,1.6,1.6),hull.darkened(0.1))
	_cube(ship,Vector3(0,1.9,-5.6),Vector3(1.8,0.6,0.1),Color("8ec5ff"),0.6)
	_cube(ship,Vector3(-3.4,0.9,0.6),Vector3(4,0.25,2.6),hull.darkened(0.15))
	var broken = _cube(ship,Vector3(3.2,0.4,1.8),Vector3(3.4,0.25,2.4),burnt)
	broken.rotation_degrees = Vector3(0,20,-28)
	_cube(ship,Vector3(0,2.0,3.6),Vector3(3.3,0.9,1.4),burnt)
	_cube(ship,Vector3(0,1.2,4.6),Vector3(1.4,1.4,0.6),Color("ff8a3d"),1.6)
	# Smoke column.
	for k in range(6):
		var puff = SphereMesh.new()
		puff.radius = 0.8+0.35*k
		puff.height = puff.radius*2
		var mat = _mat(Color(0.18,0.18,0.2))
		mat.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
		mat.albedo_color.a = 0.55-0.07*k
		var s = _mesh(root,puff,pos+Vector3(0.6*k,3.2+1.7*k,1.2+0.4*k),mat)
		s.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	# Scorched ground.
	var scorch = _cube(root,pos+Vector3(0,0.005,0),Vector3(7,0.01,13),Color("3a3326"))
	scorch.rotation.y = -0.6

# ---------------------------------------------------------------- lobby music

const MUSIC_MODES = ["home","armory","admin","story","account","connecting"]
var music_player: AudioStreamPlayer
var music_target_db = -60.0
var music_task = -1
var music_stream: AudioStreamWAV

func _process(dt):
	super._process(dt)
	_music_tick(dt)

func _music_tick(dt: float):
	if dedicated or DisplayServer.get_name() == "headless": return
	var want = mode in MUSIC_MODES
	# Synthesize the loop on a worker thread so the menu never hitches.
	if music_task == -1 and music_stream == null:
		music_task = WorkerThreadPool.add_task(func(): self.music_stream = _make_lobby_music())
	if music_task >= 0 and WorkerThreadPool.is_task_completed(music_task):
		WorkerThreadPool.wait_for_task_completion(music_task)
		music_task = -2
	if want and not is_instance_valid(music_player) and music_task == -2 and music_stream != null:
		music_player = AudioStreamPlayer.new()
		music_player.stream = music_stream
		music_player.volume_db = -40.0
		add_child(music_player)
		music_player.play()
	if not is_instance_valid(music_player): return
	music_target_db = -14.0 if want else -60.0
	music_player.volume_db = lerpf(music_player.volume_db,music_target_db,1.0-exp(-dt*(1.2 if want else 2.5)))
	if not want and music_player.volume_db < -55.0 and music_player.playing: music_player.stream_paused = true
	if want and music_player.stream_paused: music_player.stream_paused = false

func _make_lobby_music() -> AudioStreamWAV:
	# A slow, looping ambient pad: four chords, soft arpeggio, distant pulse.
	var rate = 16000
	var bar = 4.0
	var chords = [[57,60,64,69],[53,57,60,65],[48,52,55,60],[55,59,62,67]]
	var length = int(rate*bar*chords.size())
	var samples = PackedFloat32Array()
	samples.resize(length)
	for c in range(chords.size()):
		var start = int(c*bar*rate)
		var chord = chords[c]
		for i in range(int(bar*rate)):
			var t = float(i)/rate
			var env = minf(1.0,t/1.2)*minf(1.0,(bar-t)/1.0)
			var v = 0.0
			for n in chord:
				var f = 440.0*pow(2.0,(n-69)/12.0)
				v += sin(TAU*f*t)*0.13+sin(TAU*f*1.003*t)*0.09
			# Arpeggio: one note every half second, gently decaying.
			var step = int(t/0.5)
			var at = t-step*0.5
			var arp_f = 440.0*pow(2.0,(chord[step%4]+12-69)/12.0)
			v += sin(TAU*arp_f*at)*exp(-at*5.0)*0.18
			# Low pulse on each beat.
			var bt = fmod(t,1.0)
			v += sin(TAU*55.0*bt)*exp(-bt*7.0)*0.22
			samples[start+i] = v*env*0.55
	var data = PackedByteArray()
	data.resize(length*2)
	for i in range(length): data.encode_s16(i*2,int(clampf(samples[i],-1,1)*32767))
	var wav = AudioStreamWAV.new()
	wav.format = AudioStreamWAV.FORMAT_16_BITS
	wav.mix_rate = rate
	wav.data = data
	wav.loop_mode = AudioStreamWAV.LOOP_FORWARD
	wav.loop_end = length
	return wav

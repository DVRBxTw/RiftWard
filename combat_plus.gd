extends "res://beta_game.gd"
# Weapons & armory, enemy archetypes, boss phases, pickups, grenade skill,
# rarity buffs, revives and combat feedback layered on top of the beta game.

const WEAPON_ORDER = ["pistol", "rifle", "shotgun", "sniper"]
const WEAPONS = {
	"pistol": {"name":"P-9 制式手槍", "short":"手槍", "desc":"初始武器。彈匣小、換彈快，穩定可靠。", "dmg":28.0, "interval":0.24, "mag":12, "reload":1.1, "pellets":1, "spread":0.0, "head":2.0, "zoom":0.85, "price":0},
	"rifle": {"name":"AR-27 突擊步槍", "short":"步槍", "desc":"全自動高射速，適合壓制大群敵人。", "dmg":17.0, "interval":0.1, "mag":30, "reload":1.8, "pellets":1, "spread":0.012, "head":2.0, "zoom":0.75, "price":400},
	"shotgun": {"name":"SG-8 霰彈槍", "short":"霰彈", "desc":"一次射出 7 顆彈丸，近距離威力驚人。", "dmg":11.0, "interval":0.8, "mag":6, "reload":2.2, "pellets":7, "spread":0.07, "head":1.5, "zoom":0.85, "price":600},
	"sniper": {"name":"LR-50 狙擊槍", "short":"狙擊", "desc":"單發高傷害，爆頭 3 倍。右鍵開鏡。", "dmg":95.0, "interval":1.0, "mag":5, "reload":2.4, "pellets":1, "spread":0.0, "head":3.0, "zoom":0.35, "price":800}
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
	["護盾兵", Color("5aa9ff"), 1.0]
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
const RARITY = [["普通", Color("9fb3c8"), 1.0], ["稀有", Color("5aa9ff"), 1.6], ["傳說", Color("ffd166"), 2.5]]

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
	return {"owned":owned,"levels":(levels.duplicate(true) if levels is Dictionary else {})}

func _set_actor_stats(id: int):
	super._set_actor_stats(id)
	var a = actors[id]
	var lo = _loadout_for(id)
	a.merge({"owned":lo.owned,"levels":lo.levels,"slot":0,"ammo_by":{},"dmg_mult":1.0,"rate_mult":1.0,"mag_bonus":0,"reload_mult":1.0,"skill_mult":1.0,"lifesteal":0,"explode":0,"skill_cd":0.0,"revive":0.0},true)
	_refresh_weapon(a)
	a["ammo"] = a.mag
	a["reload"] = 0.0

func _refresh_weapon(a: Dictionary):
	var wid = WEAPON_ORDER[clampi(int(a.get("slot",0)),0,3)]
	var st = weapon_stats(wid,a.get("levels",{}).get(wid,{}))
	a["damage"] = st.damage*float(a.get("dmg_mult",1.0))
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
	shade.color = Color(0.015,0.03,0.06,0.84)
	shade.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	buff_overlay.add_child(shade)
	var center = CenterContainer.new()
	center.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	buff_overlay.add_child(center)
	var v = VBoxContainer.new()
	v.add_theme_constant_override("separation",16)
	center.add_child(v)
	for line in [["LEVEL UP",16,GOLD],["等級提升 · Lv. %d" % new_level,40,WHITE],["選擇一項加成 · 可疊加 · 敵人也會隨等級變強 · 30 秒後自動選擇",15,MUTED]]:
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
		var b = _button("%s\n\n%s\n%s\n\n%s" % [info[0],info[1],"【%s】" % rarity[0],info[2]],func():
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
	var waiting = _label("選擇後等待隊友；所有人選完就繼續作戰。",15,CYAN)
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
			if e.boss:
				e["kind_id"] = 0
				e["phase"] = 1
				continue
			var kind = 0
			if not pool.is_empty() and randf() < 0.55: kind = pool.pick_random()
			_apply_kind(e,kind)
	var has_boss = false
	for e in enemies.values(): has_boss = has_boss or e.boss
	_announce_all("⚠  BOSS 來襲" if has_boss else "第 %d 波" % wave)

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
		if want != int(a.get("slot",0)) and want >= 0 and want < 4 and WEAPON_ORDER[want] in a.get("owned",["pistol"]):
			a.ammo_by[WEAPON_ORDER[a.slot]] = a.ammo
			a["slot"] = want
			_refresh_weapon(a)
			a["ammo"] = int(a.ammo_by.get(WEAPON_ORDER[want],a.mag))
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
	if not dedicated and is_instance_valid(hud) and not test_mode: _build_plus_hud()

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
	var result = await _api("/api/server/profile",{"account_id":verified[id].id},true)
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
	if e.get("boss",false): return _actor_model(Color("ff5533"),true)
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
			_build_viewmodel(WEAPON_ORDER[clampi(slot,0,3)])

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
	for i in range(4):
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
	var skill = _panel(hud,Color("7dff9a"))
	skill.anchor_top = 1.0
	skill.anchor_bottom = 1.0
	skill.offset_left = 24
	skill.offset_right = 240
	skill.offset_top = -204
	skill.offset_bottom = -146
	var sv = VBoxContainer.new()
	sv.add_theme_constant_override("separation",4)
	skill.add_child(sv)
	skill_label = _label("Q  手榴彈  就緒",14,Color("7dff9a"))
	sv.add_child(skill_label)
	skill_bar = _bar(Color("7dff9a"),5)
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
	var slot = clampi(int(a.get("slot",0)),0,3)
	var owned = _loadout_for(local_id).owned
	for i in range(4):
		var wid = WEAPON_ORDER[i]
		var has = wid in owned
		var box: PanelContainer = slot_boxes[i][0]
		var l: Label = slot_boxes[i][1]
		l.text = "%d  %s" % [i+1,WEAPONS[wid].short] if has else "%d  🔒" % (i+1)
		var style = _panel_style(Color(0.12,0.28,0.32,0.92) if i == slot else Color(0.035,0.07,0.115,0.7),CYAN if i == slot else PANEL_EDGE,6)
		style.content_margin_left = 6
		style.content_margin_right = 6
		style.content_margin_top = 4
		style.content_margin_bottom = 4
		style.shadow_size = 0
		box.add_theme_stylebox_override("panel",style)
		l.modulate = Color(1,1,1,1.0 if has else 0.4)
		l.add_theme_color_override("font_color",CYAN if i == slot else WHITE)
	weapon_name_label.text = WEAPONS[WEAPON_ORDER[slot]].name
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
	var wl = _label("◆  %d G" % gold,26,GOLD)
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
	var card = _panel(parent,CYAN if owned else PANEL_EDGE,Color(0.035,0.07,0.115,0.92))
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
		var buy = _button("購買  ◆ %d G" % w.price,func(): _armory_request("/api/shop/buy",{"weapon":wid}),v,42)
		buy.disabled = gold < w.price or armory_busy
		buy.add_theme_color_override("font_color",GOLD)
		return
	var ups = HBoxContainer.new()
	ups.add_theme_constant_override("separation",8)
	v.add_child(ups)
	for key in UPGRADE_KEYS:
		var lv = int(levels.get(key,0))
		var maxed = lv >= MAX_UPGRADE_LEVEL
		var cost = upgrade_cost(wid,lv)
		var label = "%s  Lv.%d\n%s" % [UPGRADE_NAMES[key],lv,"已滿級" if maxed else "◆ %d G" % cost]
		var b = _button(label,func(): _armory_request("/api/shop/upgrade",{"weapon":wid,"stat":key}),ups,52)
		b.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		b.add_theme_font_size_override("font_size",15)
		b.disabled = maxed or gold < cost or armory_busy

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

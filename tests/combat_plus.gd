extends SceneTree

func _initialize():
	_run.call_deferred()

func _run():
	var game = load("res://main.tscn").instantiate()
	root.add_child(game)
	game.set_physics_process(false)
	game.set_process(false)
	game.test_mode = false
	game.profile = {"id":1,"username":"Test","unlocked":0,"tutorial_complete":true,"gold":0,
		"loadout":{"owned":["pistol","shotgun"],"levels":{"pistol":{"damage":2,"mag":1}}}}
	game._start_mission(0)
	var a = game.actors[1]
	# Pistol is the starting weapon and armory upgrades apply.
	assert(a.slot == 0 and is_equal_approx(a.damage,28.0*1.24) and a.mag == 14)
	# Owned weapons can be switched to; unowned ones cannot.
	game.inputs[1] = {"slot":3,"yaw":0.0}
	game._simulate_players(0.01)
	assert(a.slot == 0)
	game.inputs[1] = {"slot":2,"yaw":0.0}
	game._simulate_players(0.01)
	assert(a.slot == 2 and a.pellets == 7 and a.mag == 6)
	# Shotgun pellets each register hits.
	game.enemies.clear()
	var eye = a.p+Vector3.UP*game.EYE_HEIGHT
	game.enemies[1] = {"p":eye+Vector3(0,-game.EYE_HEIGHT,-3),"hp":1000.0,"max_hp":1000.0,"speed":0.0,"cd":INF,"y":0.0,"boss":false}
	a.critical = 0.0
	game._fire(1,game.enemies[1].p+Vector3(0,1.0,0))
	assert(game.enemies[1].hp < 1000.0-11.0*3)
	# Shield trooper blocks body shots from the front, not from behind.
	game.inputs[1] = {"slot":0,"yaw":0.0}
	game._simulate_players(0.01)
	a.cd = 0
	var facing_player = {"p":eye+Vector3(0,-game.EYE_HEIGHT,-8),"hp":500.0,"max_hp":500.0,"speed":0.0,"cd":INF,"y":PI,"boss":false}
	game._apply_kind(facing_player,4)
	facing_player.hp = 500.0
	game.enemies = {2:facing_player}
	game._fire(1,facing_player.p+Vector3(0,0.9,0))
	var front_damage = 500.0-facing_player.hp
	facing_player.hp = 500.0
	facing_player.y = 0.0
	game._fire(1,facing_player.p+Vector3(0,0.9,0))
	assert(front_damage < (500.0-facing_player.hp)*0.5)
	# Grenade explodes on landing and damages nearby enemies.
	game.enemies = {3:{"p":a.p+Vector3(0,0,-8),"hp":100.0,"max_hp":100.0,"speed":0.0,"cd":INF,"y":0.0,"boss":false}}
	game.inputs[1] = {"slot":0,"yaw":0.0,"skill":true,"aim":a.p+Vector3(0,0,-8)}
	game._simulate_players(0.01)
	assert(a.skill_cd > 0)
	game.inputs[1] = {"slot":0,"yaw":0.0}
	for i in range(100): game._simulate_grenades(0.02)
	assert(not game.enemies.has(3) or game.enemies[3].hp < 100.0)
	# Pickups heal the player.
	a.hp = 40
	game.pickups.clear()
	game._spawn_pickup(a.p,0)
	game._simulate_pickups(0.01)
	assert(a.hp == 70 and game.pickups.is_empty())
	# Rarity scales buff strength.
	var before = a.dmg_mult
	game._apply_buff(1,20)
	assert(is_equal_approx(a.dmg_mult,before+0.5))
	# Downed teammates are revived by holding E nearby.
	game._add_actor(2)
	game._set_actor_stats(2)
	game.actors[2].p = a.p+Vector3(1,0,0)
	game.actors[2].hp = 0
	game.inputs[1] = {"interact":true,"yaw":0.0}
	for i in range(40): game._simulate_players(0.1)
	assert(game.actors[2].hp > 0)
	# Boss enters an enraged phase below half health and summons chargers.
	game.enemies.clear()
	game.wave = 10
	game._spawn_wave()
	var boss_id = -1
	for id in game.enemies:
		if game.enemies[id].boss: boss_id = id
	var count = game.enemies.size()
	game.enemies[boss_id].hp = game.enemies[boss_id].max_hp*0.4
	game._simulate_enemy_kinds(0.01)
	assert(game.enemies[boss_id].phase == 2 and game.enemies.size() == count+4)
	# Bosses are giant soldiers: wave 10 heavy trooper, wave 20 commander.
	assert(game.enemies[boss_id].kind_id == 5 and game.enemies[boss_id].scale > 2.0)
	game.enemies.clear()
	game.wave = 20
	game._spawn_wave()
	for e in game.enemies.values():
		if e.boss: assert(e.kind_id == 6)
	# Admin abilities only apply to admins.
	game.enemies.clear()
	game.profile["admin"] = false
	game.admin_settings = {"dmg":5.0,"speed":2.0,"god":true,"ammo":true}
	var base_damage = a.damage
	game._admin_apply(game.admin_settings)
	assert(is_equal_approx(a.damage,base_damage) and not a.get("god",false))
	game.profile["admin"] = true
	var base_speed = a.speed
	game._admin_apply(game.admin_settings)
	assert(is_equal_approx(a.damage,base_damage*5.0) and is_equal_approx(a.speed,base_speed*2.0))
	a.hp = 10
	a.ammo = 0
	game.inputs[1] = {"yaw":0.0}
	game._simulate_players(0.01)
	assert(a.hp == a.max_hp and a.ammo == a.mag)
	game._set_actor_stats(1)
	assert(game.actors[1].god and is_equal_approx(game.actors[1].speed,6.5*2.0))
	# Recoil kicks the view up and then recovers part of it.
	game.mode = "playing"
	game.pitch = 0.0
	game.recoil_pending = 0.0
	game.last_recoil_time = -1.0
	game._shot_fx(Vector3.ZERO,Vector3(0,0,-5),false,1)
	var kicked = game.pitch
	assert(kicked > 0.01)
	for i in range(30): game._update_camera(0.016)
	assert(game.pitch < kicked and game.pitch > 0)
	# Reload sound plays mag-out, mag-in and rack in step with the reload bar.
	var r = {"hp":100.0,"slot":0,"ammo":0,"mag":12,"reload_ratio":0.0}
	game._reload_audio_tick(r)
	for value in [1.0,0.7,0.3,0.0]:
		r.reload_ratio = value
		if value == 0.0: r.ammo = 12
		game._reload_audio_tick(r)
	assert(game.reload_sfx_log == ["out","in","rack"])
	var wav = game._make_reload_sound("rack")
	assert(wav.data.size() > 1000)
	game.queue_free()
	await process_frame
	print("COMBAT_PLUS_PASS loadout switch pellets shield grenade pickup rarity revive boss_phase")
	quit()

extends SceneTree

func _initialize():
	_run.call_deferred()

func _run():
	var game = load("res://main.tscn").instantiate()
	root.add_child(game)
	game.set_physics_process(false)
	game.set_process(false)
	game.test_mode = false
	game.profile = {"id":123,"username":"Test","unlocked":0,"tutorial_complete":false}
	game._show_story()
	assert(game.mode == "story")
	game._start_training()
	assert(game.tutorial_running and game.enemies.has(9999))
	game.yaw = 0.5
	game._simulate(0.01)
	assert(game.tutorial_step == 1)
	game.actors[1].p += Vector3(3,0,0)
	game._simulate(0.01)
	assert(game.tutorial_step == 2)
	# Exercise server-side gameplay without HTTP tutorial persistence (covered by service tests).
	game.tutorial_running = false
	game._start_mission(0)
	assert(game.actors[1].damage == 28)
	game.wave = 1
	game.enemies.clear()
	# Ammo: emptying the magazine triggers an automatic reload.
	game.actors[1].ammo = 1
	game.inputs[1] = {"fire":true,"aim":Vector3(0,1,-30),"yaw":0.0}
	game._simulate(0.01)
	assert(game.actors[1].ammo == 0 and game.actors[1].reload > 0)
	game.inputs[1] = {"yaw":0.0}
	game._simulate(2.0)
	assert(game.actors[1].ammo == game.MAG_SIZE)
	game.wave = 1
	game.wave_delay = 99
	game.enemies.clear()
	# Experience: crossing the threshold queues a level-up buff and raises the next threshold.
	game._gain_xp(game.xp_need)
	assert(game.level == 2 and game.xp_need > 100)
	game._simulate(0.01)
	assert(game.buff_pending and game.buff_choices[1].size() == 3)
	assert(game.modal_open and is_instance_valid(game.buff_overlay))
	var old_wave = game.wave
	game._simulate(0.1)
	assert(game.wave == old_wave)
	game._apply_buff(1,0)
	assert(is_equal_approx(game.actors[1].damage,33.6))
	game._resolve_buffs()
	assert(not game.buff_pending and not game.modal_open)
	game.wave_delay = 0
	game._simulate(0.01)
	assert(game.wave == 2 and game.enemies.size()>0)
	var stronger = game.enemies.values()[0]
	assert(stronger.hp > (55.0*1.08)*1.07 and stronger.damage > 10)
	game._apply_buff(1,3)
	assert(game.actors[1].max_hp == 130)
	game._apply_buff(1,4)
	game.actors[1].hp = 50
	game.enemies.clear()
	game._spawn_wave()
	assert(game.actors[1].hp == 65)
	# Headshots deal double damage, body shots deal normal damage.
	game._start_mission(0)
	game.actors[1].critical = 0.0
	game.enemies.clear()
	var origin = game.actors[1].p+Vector3.UP*game.EYE_HEIGHT
	game.enemies[500] = {"p":origin+Vector3(0,-game.EYE_HEIGHT,-10),"hp":500.0,"max_hp":500.0,"speed":0.0,"cd":INF,"y":0.0,"boss":false}
	var target = game.enemies[500].p
	game._fire(1,target+Vector3(0,game.HEAD_HEIGHT,0))
	assert(is_equal_approx(game.enemies[500].hp,500.0-56.0))
	game._fire(1,target+Vector3(0,0.8,0))
	assert(is_equal_approx(game.enemies[500].hp,500.0-56.0-28.0))
	# Enemies fire slow bullets that damage players on contact.
	game.enemies[500].shot_cd = 0.0
	game.enemies[500].damage = 10.0
	game._simulate_bullets(0.01)
	assert(game.bullets.size() == 1)
	var bullet = game.bullets.values()[0]
	assert(bullet.v.length() <= game.BULLET_SPEED+0.01)
	game.enemies.clear()
	var hp_before = game.actors[1].hp
	for step in range(200): game._simulate_bullets(0.02)
	assert(game.actors[1].hp < hp_before and game.bullets.is_empty())
	game._start_mission(0)
	assert(game.actors[1].damage == 28 and game.actors[1].max_hp == 100)
	game.profile.tutorial_complete = true
	game._return_home()
	assert(game.mode == "home")
	game.queue_free()
	await process_frame
	print("BETA_GAMEPLAY_PASS story training buff_pause damage_stack shield regen difficulty reset return_home")
	quit()

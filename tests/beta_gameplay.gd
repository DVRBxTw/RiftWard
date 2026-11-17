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
	game._simulate(1.1)
	assert(game.wave == 2 and game.enemies.size()>0)
	var stronger = game.enemies.values()[0]
	assert(stronger.hp > 55 and stronger.damage > 10)
	game._apply_buff(1,3)
	assert(game.actors[1].max_hp == 130)
	game._apply_buff(1,4)
	game.actors[1].hp = 50
	game.enemies.clear()
	game._spawn_wave()
	assert(game.actors[1].hp == 65)
	game._start_mission(0)
	assert(game.actors[1].damage == 28 and game.actors[1].max_hp == 100)
	game.profile.tutorial_complete = true
	game._return_home()
	assert(game.mode == "home")
	game.queue_free()
	await process_frame
	print("BETA_GAMEPLAY_PASS story training buff_pause damage_stack shield regen difficulty reset return_home")
	quit()

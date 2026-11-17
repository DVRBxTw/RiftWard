extends SceneTree

func _initialize():
	_run.call_deferred()

func _capture(game: Node, name: String):
	for frame in range(3): await process_frame
	await RenderingServer.frame_post_draw
	var path = "res://beta-"+name+".png"
	game.get_viewport().get_texture().get_image().save_png(path)
	print("BETA_VIEW_CAPTURED ",name)

func _run():
	var game = load("res://main.tscn").instantiate()
	root.add_child(game)
	game.set_physics_process(false)
	game.set_process(false)
	game.test_mode = false
	game._show_account()
	await _capture(game,"account")
	game.profile = {"id":999,"username":"Beta_Demo","unlocked":2,"tutorial_complete":true,"admin":true,"gold":1240,"loadout":{"owned":["pistol","rifle"],"levels":{"pistol":{"damage":3,"mag":1},"rifle":{"reload":2}}}}
	game._show_story()
	await _capture(game,"story")
	game._home()
	game._update_camera(0)
	await _capture(game,"home")
	game._show_armory()
	await _capture(game,"armory")
	game._show_admin()
	await _capture(game,"admin")
	game._start_mission(1)
	game.wave = 1
	game.enemies.clear()
	game._update_camera(0)
	game._update_hud()
	game._begin_buffs()
	await _capture(game,"buff")
	game._apply_buff(1,3)
	game._resolve_buffs()
	game.wave = 6
	game._spawn_wave()
	game.inputs[1] = {"slot":1,"yaw":0.0}
	game._simulate_players(0.01)
	var target = game.enemies.values()[0]
	game._spawn_pickup(game.actors[1].p+Vector3(1.5,0,-4),0)
	game._spawn_pickup(game.actors[1].p+Vector3(-2,0,-5),1)
	for e in game.enemies.values():
		e.p = game.actors[1].p+Vector3(randf_range(-7,7),0,randf_range(-14,-6))
		if int(e.get("kind_id",0)) == 2: e["state"] = 1
	game._spawn_bullet(game.actors[1].p+Vector3(0.6,1.3,-5),Vector3(0,0,1),5)
	game._spawn_bullet(game.actors[1].p+Vector3(-1.2,1.1,-7),Vector3(0,0,1),5)
	game._announce("第 6 波")
	game.combo = 4
	game.combo_timer = 2
	game.actors[1].hp = 60
	game.last_local_hp = 80
	game._render_models(1)
	game._render_models(0.01)
	game._damage_number(game.actors[1].p+Vector3(0,1.6,-6),56,true)
	game._update_hud()
	await _capture(game,"combat")
	game._start_mission(0)
	game._deploy(0)
	game.wave = 3
	game._spawn_wave()
	for e in game.enemies.values(): e.p = game.actors[1].p+Vector3(randf_range(-8,8),0,randf_range(-16,-7))
	game.yaw = 0.25
	game._update_camera(0)
	game._render_models(1)
	game._update_hud()
	await _capture(game,"grass")
	game.yaw = PI
	game._update_camera(0)
	await _capture(game,"crash")
	game.yaw = 0
	game.wave = 10
	game.enemies.clear()
	game._spawn_wave()
	for e in game.enemies.values():
		if e.boss: e.p = game.actors[1].p+Vector3(0,0,-12)
		else: e.p = Vector3(30,0,30)
	game._render_models(1)
	game._update_hud()
	await _capture(game,"boss")
	game.queue_free()
	await process_frame
	quit()

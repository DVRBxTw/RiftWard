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
	game.profile = {"id":999,"username":"Beta_Demo","unlocked":2,"tutorial_complete":true}
	game._show_story()
	await _capture(game,"story")
	game._home()
	game._update_camera(0)
	await _capture(game,"home")
	game._start_mission(1)
	game.wave = 1
	game.enemies.clear()
	game._update_camera(0)
	game._update_hud()
	game._begin_buffs()
	await _capture(game,"buff")
	game._apply_buff(1,3)
	game._resolve_buffs()
	game.wave = 2
	game._spawn_wave()
	game._render_models(1)
	game._update_hud()
	await _capture(game,"combat")
	game.wave = 10
	game.enemies.clear()
	game._spawn_wave()
	game._render_models(1)
	game._update_hud()
	await _capture(game,"boss")
	game.queue_free()
	await process_frame
	quit()

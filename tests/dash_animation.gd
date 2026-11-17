extends SceneTree

func _initialize():
	create_timer(15).timeout.connect(func(): quit(1))
	_run.call_deferred()

func _actor() -> Dictionary:
	return {"p":Vector3.ZERO,"hp":100.0,"cd":0.0,"dash":0.0,"v":0.0,"y":0.0,"speed":0.0}

func _capture_pose(game: Node, pose: String):
	if not "--capture-dash" in OS.get_cmdline_user_args(): return
	game._update_hud()
	for frame in range(3): await process_frame
	await RenderingServer.frame_post_draw
	game.get_viewport().get_texture().get_image().save_png("res://beta-dash-"+pose+".png")

func _run():
	var game = load("res://main.tscn").instantiate()
	root.add_child(game)
	game.set_process(false)
	game.set_physics_process(false)
	game.test_mode = false
	game._start_mission(0)
	var action = {"m":Vector2(0,-1),"yaw":0.0,"dash":true}
	# Integrating the dash at different fixed rates preserves its distance.
	for rate in [30,60,120]:
		var actor = _actor()
		game._move_actor(actor,action,1.0/rate)
		assert(actor.p.length() > 0 and actor.p.length() < game.DASH_DISTANCE)
		for tick in range(rate/2-1): game._move_actor(actor,action,1.0/rate)
		assert(is_equal_approx(actor.p.z,-game.DASH_DISTANCE))
		assert(is_zero_approx(actor.dash_left))
		assert(actor.dash > 2.0)
	var predicted = _actor()
	var authoritative = _actor()
	game.pending_movement.clear()
	for seq in range(1,9):
		var packet = action.duplicate()
		packet.seq = seq
		game.pending_movement.append({"packet":packet,"dt":1.0/60})
		game._move_actor(predicted,packet,1.0/60)
		if seq <= 5: game._move_actor(authoritative,packet,1.0/60)
	game.actors[1] = predicted
	var direction: Vector3 = authoritative.dash_direction
	var state = PackedFloat32Array([authoritative.p.x,authoritative.p.y,authoritative.p.z,100,0,authoritative.dash,0,authoritative.v,100,0,0.16,5,authoritative.dash_left,direction.x,direction.y,direction.z])
	game._snapshot(PackedInt32Array([1]),state,PackedFloat32Array(),0,0,0)
	assert(game.actors[1].p.distance_to(predicted.p) < 0.0001)
	# Peak pose changes FOV and weapon, never the camera's aim rotation.
	game.actors[1].dash = game.DASH_COOLDOWN-game.DASH_VISUAL_DURATION/2
	game.pitch = 0.2
	game.yaw = 0.4
	game._update_camera(1.0/60)
	assert(absf(game.camera.fov-(game.BASE_FOV+game.DASH_FOV_BOOST)) < 0.2)
	assert(game.weapon.position.distance_to(game.WEAPON_REST_POSITION) > 0.05)
	assert(game.camera.rotation.is_equal_approx(Vector3(0.2,0.4,0)))
	await _capture_pose(game,"active")
	game.actors[1].dash = game.DASH_COOLDOWN-game.DASH_VISUAL_DURATION-0.01
	game._update_camera(1.0/60)
	assert(is_equal_approx(game.camera.fov,game.BASE_FOV))
	assert(game.weapon.position.is_equal_approx(game.WEAPON_REST_POSITION))
	assert(game.weapon.rotation.is_zero_approx())
	await _capture_pose(game,"recovered")
	game.actors[1].dash = game.DASH_COOLDOWN-0.1
	game.modal_open = true
	game._update_camera(1.0/60)
	assert(is_equal_approx(game.camera.fov,game.BASE_FOV))
	game.modal_open = false
	game.mode = "home"
	game._update_camera(1.0/60)
	assert(game.weapon.rotation.is_zero_approx() and is_equal_approx(game.camera.fov,game.BASE_FOV))
	game.queue_free()
	await process_frame
	print("DASH_ANIMATION_PASS timed_distance cooldown frame_rates mid_dash_reconciliation pose aim return pause menu")
	quit()

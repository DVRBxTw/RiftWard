extends SceneTree

var game: Node3D
var failures: Array[String] = []

func _initialize():
	_run.call_deferred()

func _check(condition: bool, message: String):
	if not condition:
		failures.append(message)
		push_error(message)

func _motion(delta: Vector2):
	var event = InputEventMouseMotion.new()
	event.relative = delta * 0.25
	event.screen_relative = delta
	root.push_input(event)

func _click(pressed: bool):
	var event = InputEventMouseButton.new()
	event.button_index = MOUSE_BUTTON_LEFT
	event.pressed = pressed
	root.push_input(event)

func _run():
	if DisplayServer.get_name() == "headless":
		push_error("FPS mouse capture checks require a native display; run without --headless")
		quit(2)
		return
	game = load("res://main.tscn").instantiate()
	root.add_child(game)
	game.set_physics_process(false)
	game.set_process(false)
	game._start_mission(0)
	await process_frame
	Input.mouse_mode = Input.MOUSE_MODE_CAPTURED
	# Exercise the real viewport dispatch, including the full-screen HUD.
	_motion(Vector2(240, -160))
	_check(is_equal_approx(game.yaw, -0.6), "HUD must not consume mouse yaw; use unscaled screen delta")
	_check(is_equal_approx(game.pitch, 0.4), "Mouse upward motion must aim upward")
	_motion(Vector2(6000, -10000))
	_check(game.yaw >= -PI and game.yaw < PI, "Yaw must wrap across multiple complete rotations")
	_check(is_equal_approx(game.pitch, game.LOOK_LIMIT), "Upward pitch limit must be 85 degrees")
	_motion(Vector2(0, 20000))
	_check(is_equal_approx(game.pitch, -game.LOOK_LIMIT), "Downward pitch limit must be 85 degrees")
	game.yaw = PI / 2
	game.pitch = 0.3
	game._update_camera(0)
	var eye = game.actors[1].p + Vector3.UP * game.EYE_HEIGHT
	_check(game.camera.position.is_equal_approx(eye), "Camera must sit at the player's eyes")
	_check((-game.camera.global_basis.z).is_equal_approx(game._look_direction()), "Camera center and weapon aim must agree")
	game._render_models(1)
	_check(not game.visuals[1].visible, "Own body must not obscure the first-person camera")
	_check(game.weapon.visible, "View weapon must be visible during live combat")
	var previous_yaw = game.yaw
	game.modal_open = true
	_motion(Vector2(160,160))
	_check(is_equal_approx(game.yaw, previous_yaw), "Exit confirmation must freeze mouse look")
	game.modal_open = false
	game._notification(Node.NOTIFICATION_APPLICATION_FOCUS_OUT)
	_check(Input.mouse_mode == Input.MOUSE_MODE_VISIBLE, "Switching away must release the cursor")
	_motion(Vector2(160,160))
	_check(is_equal_approx(game.yaw, previous_yaw), "Released cursor must not rotate the camera")
	_click(true)
	_check(Input.mouse_mode == Input.MOUSE_MODE_CAPTURED, "Clicking combat must recapture mouse")
	_check(game.release_fire_to_resume, "Recapture click must not fire a shot")
	_click(false)
	_check(not game.release_fire_to_resume, "Releasing mouse must re-enable shooting")
	# A target behind the spawn must be hittable after a 180-degree turn.
	game.yaw = PI
	game.pitch = 0
	game._update_camera(0)
	game.enemies.clear()
	game.enemies[900] = {"p":game.actors[1].p + Vector3(0,0,10),"hp":56.0,"boss":false}
	var target = eye + game._look_direction() * game.SHOT_RANGE
	game._fire(1,target)
	game._fire(1,target)
	_check(not game.enemies.has(900), "Centered shots must hit a target behind the original facing")
	game.enemies[901] = {"p":game.actors[1].p + Vector3(0,0,10),"hp":56.0,"boss":false}
	game.pitch = deg_to_rad(75)
	game._fire(1,eye + game._look_direction() * game.SHOT_RANGE)
	_check(is_equal_approx(game.enemies[901].hp,56), "Aiming into the sky must not snap shots onto the ground plane")
	game._return_home()
	game._update_camera(0)
	_check(Input.mouse_mode == Input.MOUSE_MODE_VISIBLE and not game.weapon.visible, "Home must release cursor and hide weapon")
	await process_frame
	if failures.is_empty(): print("FPS_CONTROLS_PASS viewport_mouse yaw_wrap pitch_limits eye_camera aim_ray body_visibility focus_recapture rear_target sky_miss return_home")
	game.queue_free()
	await process_frame
	quit(0 if failures.is_empty() else 1)

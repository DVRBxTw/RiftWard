extends SceneTree

var game: Node3D
var stage = 0
var runtime = 0.0
var picks = 0

func _initialize():
	_start.call_deferred()

func _start():
	game = load("res://main.tscn").instantiate()
	root.add_child(game)
	game.auth_token = OS.get_environment("RIFTWARD_TEST_TOKEN")
	var result = await game._api("/api/me",{})
	assert(result.get("ok",false))
	game.profile = result.profile
	game._connect_base()

func _process(dt):
	runtime += dt
	if not is_instance_valid(game): return false
	if stage == 0 and game.mode == "playing" and game.actors.has(game.local_id):
		assert(game.network == "client")
		stage = 1
	if stage == 1 and is_instance_valid(game.buff_overlay):
		var buttons = game.buff_overlay.find_children("*","Button",true,false)
		if not buttons.is_empty() and not buttons[0].disabled:
			buttons[0].pressed.emit()
			picks += 1
	if stage == 1 and game.mode == "result":
		assert(game.result_success and game.profile.unlocked == 1 and picks == 2)
		print("BETA_CLIENT_PASS authenticated two_buff_choices persisted_progress")
		game._request_home()
		stage = 2
	if stage == 2 and game.mode == "home":
		print("BETA_CLIENT_RETURN_PASS")
		quit()
	if runtime>30:
		push_error("BETA_CLIENT_TIMEOUT stage="+str(stage)+" mode="+game.mode)
		quit(1)
	return false

extends SceneTree

var game: Node3D
var stage = 0
var runtime = 0.0
var selections = 0

func _initialize():
	_start.call_deferred()

func _start():
	game = load("res://main.tscn").instantiate()
	root.add_child(game)

func _process(dt):
	runtime += dt
	if not is_instance_valid(game): return false
	if stage == 0 and game.verified.size() == 2:
		assert(game.actors.size() == 2)
		game._start_mission(0)
		stage = 1
	if stage == 1 and game.mode == "playing" and not game.buff_pending and not game.enemies.is_empty():
		game.enemies.clear()
	if stage == 1 and game.mode == "result":
		assert(game.result_success)
		for user in game.verified.values(): assert(user.unlocked == 1)
		stage = 2
		print("BETA_SERVER_PASS verified_accounts waves buffs progress")
	if stage == 2 and game.mode == "home" and game.actors.is_empty():
		print("BETA_SERVER_RETURN_PASS")
		quit()
	if runtime>35:
		push_error("BETA_SERVER_TIMEOUT stage="+str(stage))
		quit(1)
	return false

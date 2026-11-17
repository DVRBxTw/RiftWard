extends SceneTree

func _initialize():
	var probe = load("res://tests/probe.gd").new()
	root.add_child.call_deferred(probe)
	get_tree_timeout.call_deferred()

func get_tree_timeout():
	await create_timer(15).timeout
	push_error("Launcher HTTP test timed out")
	quit(1)

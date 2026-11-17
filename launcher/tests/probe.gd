extends "res://main.gd"

func _launch(path: String) -> void:
	assert(verify_pack(path,int(manifest.size),manifest.sha256).is_empty())
	if OS.get_environment("RIFTWARD_EXPECT_DOWNLOAD") == "1":
		assert(path.begins_with(UPDATE_DIR))
	print("LAUNCHER_HTTP_E2E_PASS sha256 size atomic_install "+manifest.version)
	get_tree().quit()

func _fail(message: String) -> void:
	push_error(message)
	get_tree().quit(1)

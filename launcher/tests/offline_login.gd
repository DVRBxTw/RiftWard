extends SceneTree

class Probe extends "res://main.gd":
	var launched = ""
	func _launch(path: String) -> void:
		launched = path

func _initialize():
	var probe = Probe.new()
	probe.install_dir = "user://qa-offline-" + str(Time.get_ticks_usec())
	var directory = probe.install_dir.path_join("game")
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(directory))
	var pack = directory.path_join("RiftWardGame.pck")
	var file = FileAccess.open(pack,FileAccess.WRITE)
	file.store_string("abc")
	file.close()
	assert(not probe._launch_login_offline())
	var metadata = directory.path_join("manifest.json")
	var values = {"version":"0.3.0-beta.2","size":3,"sha256":FileAccess.get_sha256(pack)}
	file = FileAccess.open(metadata,FileAccess.WRITE)
	file.store_string(JSON.stringify(values))
	file.close()
	assert(probe._launch_login_offline() and probe.launched == pack)
	probe.launched = ""
	file = FileAccess.open(pack,FileAccess.WRITE)
	file.store_string("bad")
	file.close()
	assert(not probe._launch_login_offline() and probe.launched.is_empty())
	DirAccess.remove_absolute(ProjectSettings.globalize_path(pack))
	DirAccess.remove_absolute(ProjectSettings.globalize_path(metadata))
	DirAccess.remove_absolute(ProjectSettings.globalize_path(directory))
	DirAccess.remove_absolute(ProjectSettings.globalize_path(probe.install_dir))
	probe.free()
	print("OFFLINE_LOGIN_PASS verified_local_pack missing_manifest corrupt_pack")
	quit()

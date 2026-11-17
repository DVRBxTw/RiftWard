extends SceneTree

func _initialize() -> void:
	var launcher = load("res://main.gd").new()
	var digest = "a".repeat(64)
	var good = {"ok": true, "protocol": 1, "version": "0.3.0-beta.1", "sha256": digest, "size": 3, "download": "/updates/" + digest + ".pck"}
	assert(launcher.validate_manifest(good).is_empty())
	for path in ["https://evil.example/game.pck", "//evil.example/x", "/updates/../game.pck", "/updates/x.pck?redirect=x"]:
		var bad = good.duplicate()
		bad.download = path
		assert(not launcher.validate_manifest(bad).is_empty())
	for size in [0, -1, 536870913, 1.5, "3"]:
		var bad = good.duplicate()
		bad.size = size
		assert(not launcher.validate_manifest(bad).is_empty())
	var bad = good.duplicate()
	bad.version = "../../native.exe"
	assert(not launcher.validate_manifest(bad).is_empty())
	bad = good.duplicate()
	bad.protocol = true
	assert(not launcher.validate_manifest(bad).is_empty())
	var file = FileAccess.open("user://launcher-verification-test.tmp", FileAccess.WRITE)
	file.store_string("abc")
	file.close()
	var path = "user://launcher-verification-test.tmp"
	assert(launcher.verify_pack(path, 3, "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad").is_empty())
	assert(not launcher.verify_pack(path, 2, digest).is_empty())
	assert(not launcher.verify_pack(path, 3, digest).is_empty())
	DirAccess.remove_absolute(ProjectSettings.globalize_path(path))
	launcher.free()
	print("LAUNCHER VERIFICATION PASS: manifest paths, size bounds, protocol, SHA-256 and size mismatch")
	quit()

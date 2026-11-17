extends SceneTree

class SessionGame extends "res://beta_game.gd":
	var reply: Dictionary = {}
	var connections = 0
	func _api(_path: String, _data: Dictionary, _server_only: bool = false) -> Dictionary:
		return reply.duplicate(true)
	func _connect_base():
		connections += 1
		mode = "home"

func _initialize():
	_run.call_deferred()

func _run():
	var game = SessionGame.new()
	root.add_child(game)
	game.set_process(false)
	game.set_physics_process(false)
	game.test_mode = false
	game.session_path = "user://qa-session-" + str(Time.get_ticks_usec()) + ".cfg"
	game._show_account()
	var token = "test-session-token-never-a-password-12345678"
	var user = {"id":321,"username":"SessionTest","unlocked":0,"tutorial_complete":true}
	game.reply = {"ok":true,"profile":user,"game_online":true}
	await game._authenticated(token,user)
	await process_frame
	assert(game.connections == 1)
	assert(FileAccess.file_exists(game.session_path))
	assert(FileAccess.get_file_as_bytes(game.session_path).hex_encode().find(token.to_utf8_buffer().hex_encode()) == -1)
	# Replace an existing session atomically, then restore it without entering a password.
	game._save_session()
	game.auth_token = ""
	game.profile.clear()
	game._show_account()
	await process_frame
	await game._restore_session()
	await process_frame
	assert(game.auth_token == token and game.connections == 2)
	# Network failure preserves the saved session but leaves the player at login.
	game.reply = {"ok":false,"error":"offline"}
	await game._check_session_connection()
	await process_frame
	assert(game.mode == "account" and game.profile.is_empty())
	assert(FileAccess.file_exists(game.session_path))
	game._show_account()
	await process_frame
	await game._restore_session()
	await process_frame
	assert(game.mode == "account" and game.connections == 2)
	# An available account API with an unavailable game server must also block entry.
	game.reply = {"ok":true,"profile":user,"game_online":false}
	await game._authenticated(token,user)
	await process_frame
	assert(game.mode == "account" and game.connections == 2)
	# Expired/revoked credentials are removed, including from encrypted local storage.
	game.reply = {"ok":false,"http_status":401,"error":"expired"}
	await game._authenticated(token,user)
	await process_frame
	assert(not FileAccess.file_exists(game.session_path))
	game.reply = {"ok":true,"profile":user,"game_online":true}
	await game._authenticated(token,user)
	await process_frame
	await game._logout()
	await process_frame
	assert(game.auth_token.is_empty() and game.mode == "account")
	assert(not FileAccess.file_exists(game.session_path))
	game.queue_free()
	await process_frame
	print("LOGIN_SESSION_PASS encrypted_storage restore offline_gate game_service_gate expiry logout")
	quit()

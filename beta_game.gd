extends "res://main.gd"

const VERSION = "0.4.1-beta.1"
const SESSION_PATH = "user://login-session.cfg"
const BASE_WEAPON_DAMAGE = 28.0
const BUFFS = [
	["超載彈芯", "基礎傷害增加 20%（可疊加）", "damage"],
	["急速扳機", "射擊間隔縮短 15%", "fire_interval"],
	["動能靴", "移動速度增加 12%", "speed"],
	["強化護盾", "護盾上限 +30，立即恢復 30", "max_hp"],
	["修復協議", "每波開始恢復 15 點護盾", "regen"],
	["弱點分析", "增加 20% 機率造成雙倍傷害", "critical"],
	["擴充彈匣", "彈匣容量 +10 發，換彈速度加快 15%", "mag"]
]
const BUFF_ICONS = ["✦", "⚡", "➤", "⬢", "✚", "◎", "▤"]
const XP_BASE = 100.0
const XP_GROWTH = 1.35
var xp = 0
var level = 1
var xp_need = 100
var pending_levels = 0
var xp_bar: ProgressBar
var level_label: Label
var xp_label: Label
const BULLET_SPEED = 7.5
const BOSS_BULLET_SPEED = 6.0
const BULLET_RANGE = 24.0
const BULLET_LIFE = 4.5
const BULLET_HIT_RADIUS = 0.55
const MAX_BULLETS = 140
var bullets: Dictionary = {}
var next_bullet = 0
var bullet_visuals: Dictionary = {}
var left_mission = false
const REGIONS = [
	["灰燼前哨", "帝國先遣隊已占領邊境。\n清掃前哨，找出通往城內的路。", Color("263a50"), Color("66f5d0")],
	["熔核工廠", "反應爐正在替入侵艦隊供能。\n穿過熔核工廠，切斷帝國的能源。", Color("493429"), Color("ffad66")],
	["帝國裂隙", "傳送門後是帝國的重型守衛。\n消滅守衛，封鎖通往地球的裂隙。", Color("352c4c"), Color("b492ff")]
]
var test_mode = false
var auth_token = ""
var profile: Dictionary = {}
var verified: Dictionary = {}
var auth_pending: Dictionary = {}
var server_key = ""
var account_page: Control
var tutorial_running = false
var tutorial_step = 0
var tutorial_origin = Vector3.ZERO
var story_index = 0
var buff_pending = false
var buff_wave = -1
var buff_wait = 0.0
var buff_choices: Dictionary = {}
var buff_overlay: Control
var buffs_picked = 0
var region_props: Node3D
var session_notice = ""
var session_generation = 0
var session_polling = false
var logging_out = false
var session_path = SESSION_PATH
var api_port = 24771
const BOSS_HUD_WIDTH = 640.0
const BOSS_HUD_CENTER_OFFSET = 0.0
const BOSS_HUD_TOP = 24.0
const BOSS_BAR_HEIGHT = 16.0
var boss_panel: PanelContainer
var boss_name_label: Label
var boss_health_bar: ProgressBar

func _ready():
	var args = OS.get_cmdline_user_args()
	test_mode = "--smoke-test" in args or "--network-test" in args or "--preview-combat" in args or "--capture" in args
	if test_mode:
		super._ready()
		return
	dedicated = "--server" in args
	_load_server_address()
	_setup_input()
	_setup_theme()
	_build_world()
	audio = AudioStreamPlayer.new()
	add_child(audio)
	multiplayer.peer_connected.connect(_peer_joined)
	multiplayer.peer_disconnected.connect(_peer_left)
	multiplayer.connected_to_server.connect(_connected)
	multiplayer.connection_failed.connect(func(): _disconnect("連線失敗，請先啟動基地伺服器"))
	multiplayer.server_disconnected.connect(func(): _disconnect("與基地中斷連線，請重新連線"))
	if dedicated:
		var data_dir = OS.get_environment("RIFTWARD_SERVER_DATA")
		if data_dir.is_empty(): data_dir = OS.get_executable_path().get_base_dir().path_join("server-data")
		var path = data_dir.path_join("server.key")
		server_key = FileAccess.get_file_as_string(path).strip_edges()
		if server_key.is_empty():
			push_error("請使用 StartServer.cmd 一起啟動帳號與遊戲伺服器")
			get_tree().quit(1)
			return
		_host(true)
	else:
		DisplayServer.window_set_mode(DisplayServer.WINDOW_MODE_FULLSCREEN)
		_show_account()
		_restore_session.call_deferred()
		var session_timer = Timer.new()
		session_timer.wait_time = 5.0
		session_timer.timeout.connect(_check_session_connection)
		add_child(session_timer)
		session_timer.start()

func api_base() -> String:
	if dedicated:
		var address = OS.get_environment("RIFTWARD_API_HOST")
		if address.is_empty(): address = "192.168.213.100"
		return "http://%s:%d" % [address,api_port]
	return "http://%s:%d" % [server_address,api_port]

func _api(path: String, data: Dictionary, server_only: bool = false) -> Dictionary:
	var request = HTTPRequest.new()
	request.timeout = 8
	request.body_size_limit = 65536
	add_child(request)
	var headers = PackedStringArray(["Content-Type: application/json"])
	if server_only: headers.append("X-Server-Key: " + server_key)
	elif not auth_token.is_empty(): headers.append("Authorization: Bearer " + auth_token)
	var err = request.request(api_base()+path,headers,HTTPClient.METHOD_POST,JSON.stringify(data))
	if err != OK:
		request.queue_free()
		return {"ok":false,"error":"無法連接帳號服務"}
	var response = await request.request_completed
	request.queue_free()
	if response[0] != HTTPRequest.RESULT_SUCCESS:
		return {"ok":false,"error":"帳號服務無回應，請確認伺服器已啟動"}
	var parsed = JSON.parse_string(response[3].get_string_from_utf8())
	if not parsed is Dictionary: return {"ok":false,"error":"伺服器回覆格式錯誤"}
	parsed["http_status"] = response[1]
	return parsed

func _session_key() -> String:
	return (OS.get_unique_id() + "RiftWard/session/v1").sha256_text()

func _save_session():
	if auth_token.is_empty() or logging_out: return
	var config = ConfigFile.new()
	config.set_value("session","token",auth_token)
	config.set_value("session","server",server_address)
	var temporary = session_path + ".tmp"
	if config.save_encrypted_pass(temporary,_session_key()) == OK:
		DirAccess.rename_absolute(ProjectSettings.globalize_path(temporary),ProjectSettings.globalize_path(session_path))

func _forget_session():
	if FileAccess.file_exists(session_path):
		DirAccess.remove_absolute(ProjectSettings.globalize_path(session_path))

func _restore_session():
	var config = ConfigFile.new()
	if config.load_encrypted_pass(session_path,_session_key()) != OK: return
	if str(config.get_value("session","server","")) != server_address: return
	var token = str(config.get_value("session","token",""))
	if token.length() < 20 or token.length() > 128: return
	account_page._switch_mode()
	account_page._set_busy(true)
	account_page._status.text = "正在恢復登入，請稍候…"
	await _authenticated(token,{})

func _check_session_connection():
	if dedicated or test_mode or logging_out or session_polling or auth_token.is_empty() or mode == "account" or mode == "connecting": return
	session_polling = true
	var generation = session_generation
	var result = await _api("/api/me",{})
	session_polling = false
	if generation != session_generation: return
	if not result.get("ok",false) or not result.get("game_online",false):
		if result.get("http_status",0) == 401: _forget_session()
		_disconnect("伺服器未連線，請稍後重新登入。")

func _exit_game():
	_save_session()
	get_tree().quit()

func _show_account(message: String = ""):
	mode = "account"
	Input.mouse_mode = Input.MOUSE_MODE_VISIBLE
	_clear_ui()
	account_page = load("res://ui/account_screen.gd").new()
	screen.add_child(account_page)
	account_page.authenticated.connect(_authenticated)
	account_page.setup(self,api_base())
	if not message.is_empty():
		account_page._switch_mode()
		var notice = _label(message,16,ORANGE)
		notice.position = Vector2(32,744)
		screen.add_child(notice)

func _authenticated(token: String, user: Dictionary):
	logging_out = false
	session_generation += 1
	var generation = session_generation
	auth_token = token
	_save_session()
	var result = await _api("/api/me",{})
	if generation != session_generation: return
	if not result.get("ok",false) or not result.get("game_online",false):
		if result.get("http_status",0) == 401: _forget_session()
		auth_token = ""
		profile.clear()
		_show_account(str(result.get("error","伺服器未連線，請稍後重新登入。")))
		return
	profile = result.get("profile",user)
	unlocked = int(profile.get("unlocked",0))
	if not profile.get("tutorial_complete",false):
		story_index = 0
		_show_story()
	else: _connect_base()

func _show_story():
	mode = "story"
	_clear_ui()
	Input.mouse_mode = Input.MOUSE_MODE_VISIBLE
	var stories = [
		["2189 / 最後的槍手", "你是未來地球的裂隙守衛。\n三天前，來自宇宙深處的赫利奧帝國撕開了天空。\n帝國軍團與異化怪物湧進城市，最後的防線已經崩潰。"],
		["從前哨到裂隙", "抵抗軍只剩下一條路：\n奪回灰燼前哨 → 切斷熔核工廠 → 關閉帝國裂隙。\n每個區域都有更危險的敵人；每一波結束，都能取得新的戰術加成。"],
		["先學會活下來", "滑鼠自由轉身與瞄準，WASD 移動，左鍵射擊。\nShift 衝刺能拉開距離，Space 可以跳躍。\n接下來進入安全訓練場，完成瞄準、移動和擊破目標。"]
	]
	_dim(screen)
	var v = _box(screen,Vector2(240,176),800)
	v.add_child(_label("RIFTWARD / BETA " + VERSION,16,CYAN))
	v.add_child(_label(stories[story_index][0],36))
	var text = _label(stories[story_index][1],22)
	text.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	text.custom_minimum_size = Vector2(800,224)
	v.add_child(text)
	_button("下一頁" if story_index < 2 else "進入新手訓練",func():
		if story_index < 2:
			story_index += 1
			_show_story()
		else: _start_training(),v,56)

func _dim(parent: Node):
	var shade = ColorRect.new()
	shade.color = Color(0.025,0.05,0.09,0.94)
	shade.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	parent.add_child(shade)

func _start_training():
	multiplayer.multiplayer_peer = OfflineMultiplayerPeer.new()
	network = "offline"
	local_id = 1
	tutorial_running = true
	tutorial_step = 0
	actors.clear()
	_add_actor(1)
	super._start_mission(0)
	tutorial_origin = actors[1].p
	wave_delay = INF
	enemies[9999] = {"p":tutorial_origin+Vector3(0,0,-9),"hp":56.0,"speed":0.0,"cd":INF,"y":0.0,"boss":false,"damage":0}

func _complete_training():
	tutorial_step = 4
	Input.mouse_mode = Input.MOUSE_MODE_VISIBLE
	var result = await _api("/api/tutorial",{})
	if not result.get("ok",false):
		mode = "tutorial_save"
		_clear_ui()
		_dim(screen)
		var v = _box(screen,Vector2(300,240),680)
		v.add_child(_label("教學完成，但保存失敗",30,ORANGE))
		v.add_child(_label(str(result.get("error","請重試")),18))
		_button("重試儲存",_complete_training,v)
		return
	profile = result.profile
	tutorial_running = false
	_clean_models()
	actors.clear()
	enemies.clear()
	_connect_base()

func _connect_base():
	actors.clear()
	enemies.clear()
	inputs.clear()
	_clean_models()
	_show_account("正在驗證帳號並連接基地…")
	account_page._set_busy(true)
	mode = "connecting"
	_join(server_address)
	var generation = session_generation
	get_tree().create_timer(12).timeout.connect(func():
		if generation == session_generation and mode == "connecting":
			_disconnect("無法連接遊戲伺服器，請稍後重新登入。"))

func _connected():
	if test_mode:
		super._connected()
		return
	network = "client"
	local_id = multiplayer.get_unique_id()
	print("BETA_ACCOUNT_AUTH_REQUEST peer=",local_id)
	_authenticate.rpc_id(1,auth_token,VERSION)

func _peer_joined(id: int):
	if test_mode:
		super._peer_joined(id)
		return
	if multiplayer.is_server():
		print("BETA_ACCOUNT_PENDING peer=",id)
		get_tree().create_timer(12).timeout.connect(func():
			if multiplayer.is_server() and id in multiplayer.get_peers() and not verified.has(id):
				multiplayer.multiplayer_peer.disconnect_peer(id))

@rpc("any_peer", "call_remote", "reliable")
func _authenticate(token: String, version: String):
	if not multiplayer.is_server() or test_mode: return
	var id = multiplayer.get_remote_sender_id()
	if verified.has(id) or auth_pending.has(id) or token.length()>128: return
	auth_pending[id] = true
	print("BETA_ACCOUNT_VALIDATING peer=",id)
	var result = await _api("/api/server/validate",{"token":token,"version":version},true)
	auth_pending.erase(id)
	print("BETA_ACCOUNT_VALIDATED peer=",id," accepted=",result.get("ok",false))
	if not id in multiplayer.get_peers(): return
	if not result.get("ok",false):
		_account_error.rpc_id(id,str(result.get("error","登入已失效")))
		return
	var user: Dictionary = result.profile
	if not user.get("tutorial_complete",false):
		_account_error.rpc_id(id,"請先完成新手教學")
		return
	for other in verified.values():
		if other.id == user.id:
			_account_error.rpc_id(id,"這個帳號已在其他視窗登入遊戲")
			return
	verified[id] = user
	_add_actor(id)
	_set_actor_stats(id)
	if actors.size() == 1:
		leader_id = id
		unlocked = int(user.unlocked)
		selected = mini(selected,unlocked)
	_account_ok.rpc_id(id,user)
	_lobby_state.rpc(selected,unlocked,leader_id)
	if mode == "playing":
		_deploy.rpc_id(id,selected)
		if buff_pending: _offer_to(id)
	elif mode == "result": _finish_remote.rpc_id(id,result_success,elapsed,kills,selected)
	print("ACCOUNT_PEER_VERIFIED id=",id)

@rpc("authority", "call_remote", "reliable")
func _account_ok(user: Dictionary):
	profile = user
	unlocked = int(user.unlocked)
	mode = "home"
	_home()

@rpc("authority", "call_remote", "reliable")
func _account_error(message: String):
	multiplayer.multiplayer_peer = OfflineMultiplayerPeer.new()
	network = "offline"
	auth_token = ""
	profile.clear()
	_show_account(message)

func _peer_left(id: int):
	verified.erase(id)
	buff_choices.erase(id)
	super._peer_left(id)
	if not test_mode and dedicated and verified.has(leader_id):
		unlocked = int(verified[leader_id].unlocked)
	if buff_pending and buff_choices.is_empty(): _resolve_buffs()

func _home():
	if test_mode:
		super._home()
		return
	if dedicated:
		mode = "home"
		return
	if profile.is_empty():
		_show_account()
		return
	mode = "home"
	modal_open = false
	buff_overlay = null
	Input.mouse_mode = Input.MOUSE_MODE_VISIBLE
	showcase.visible = true
	_clear_ui()
	_backdrop(screen,0.95,0.2)
	var region_color: Color = REGIONS[selected][3]
	# Top bar: title on the left, pilot card on the right.
	var title = _box(screen,Vector2(48,28),520)
	title.add_theme_constant_override("separation",0)
	title.add_child(_label("RIFTWARD  ·  " + VERSION,14,CYAN))
	title.add_child(_label("裂隙守衛  抵抗軍基地",36))
	var pilot = _panel(screen,region_color)
	pilot.anchor_left = 1.0
	pilot.anchor_right = 1.0
	pilot.offset_left = -360
	pilot.offset_right = -48
	pilot.offset_top = 32
	var pv = VBoxContainer.new()
	pv.add_theme_constant_override("separation",2)
	pilot.add_child(pv)
	var name_row = HBoxContainer.new()
	pv.add_child(name_row)
	name_row.add_child(_label("槍手  " + str(profile.get("username","")),20))
	var fill = Control.new()
	fill.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	name_row.add_child(fill)
	name_row.add_child(_label("◆ %d G" % int(profile.get("gold",0)),20,GOLD))
	status_label = _label(str(profile.get("save_error",_connection_text())),14,CYAN)
	status_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	pv.add_child(status_label)
	# Left column: region list and actions.
	var left = _box(screen,Vector2(48,128),470)
	left.add_theme_constant_override("separation",10)
	left.add_child(_label("選擇部署區域",16,MUTED))
	for i in range(REGIONS.size()):
		var region = REGIONS[i]
		var locked = i > int(profile.get("unlocked",0))
		var button = _button("%02d   %s%s
%s" % [i+1,region[0],"   🔒 尚未解鎖" if locked else "",MISSIONS[i][3]],func(): _choose_mission(i),left,84)
		button.alignment = HORIZONTAL_ALIGNMENT_LEFT
		button.disabled = locked
		mission_buttons.append(button)
	var gap = Control.new()
	gap.custom_minimum_size.y = 6
	left.add_child(gap)
	var launch = _button("▶   部署到 " + REGIONS[selected][0],_request_start,left,64)
	launch.add_theme_font_size_override("font_size",22)
	launch.add_theme_stylebox_override("normal",_button_style(Color(0.35,0.16,0.08,0.95),ORANGE,true))
	launch.add_theme_stylebox_override("hover",_button_style(Color(0.5,0.22,0.1,0.98),ORANGE,true))
	launch.add_theme_stylebox_override("pressed",_button_style(ORANGE,ORANGE,true))
	launch.disabled = network != "client" or local_id != leader_id
	if launch.disabled and network == "client": launch.text = "等待隊長部署"
	var actions = HBoxContainer.new()
	actions.add_theme_constant_override("separation",8)
	left.add_child(actions)
	var armory_button = _button("軍械庫",_show_armory,actions,46)
	armory_button.add_theme_color_override("font_color",GOLD)
	var story_button = _button("故事與教學",func():
		if network != "offline":
			multiplayer.multiplayer_peer = OfflineMultiplayerPeer.new()
			network = "offline"
		story_index = 0
		_show_story(),actions,46)
	var logout_button = _button("登出帳號",_logout,actions,46)
	var exit_button = _button("離開遊戲",_exit_game,actions,46)
	for b in [armory_button,story_button,logout_button,exit_button]:
		b.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		b.add_theme_font_size_override("font_size",16)
	# Right: mission briefing card.
	var brief = _panel(screen,region_color,Color(0.035,0.07,0.115,0.88))
	brief.anchor_left = 1.0
	brief.anchor_right = 1.0
	brief.anchor_top = 1.0
	brief.anchor_bottom = 1.0
	brief.offset_left = -560
	brief.offset_right = -48
	brief.offset_top = -470
	brief.offset_bottom = -40
	var bv = VBoxContainer.new()
	bv.add_theme_constant_override("separation",10)
	brief.add_child(bv)
	bv.add_child(_label("任務簡報  ·  宇宙帝國入侵 第一章",14,MUTED))
	bv.add_child(_label("%02d  %s" % [selected+1,REGIONS[selected][0]],30,region_color))
	var description = _label(REGIONS[selected][1],19)
	description.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	bv.add_child(description)
	var line = ColorRect.new()
	line.color = Color(1,1,1,0.08)
	line.custom_minimum_size.y = 1
	bv.add_child(line)
	var grid = GridContainer.new()
	grid.columns = 2
	grid.add_theme_constant_override("h_separation",24)
	grid.add_theme_constant_override("v_separation",6)
	bv.add_child(grid)
	for pair in [["WASD","移動"],["左鍵","射擊（爆頭 ×2）"],["右鍵","瞄準 / 開鏡"],["R","換彈"],["1–4","切換武器"],["Q","手榴彈"],["Shift","衝刺"],["E","救援倒地隊友"],["Space","跳躍"],["V","切換視角"]]:
		var row = HBoxContainer.new()
		row.add_theme_constant_override("separation",8)
		var key = _label(pair[0],14,CYAN)
		key.custom_minimum_size.x = 52
		row.add_child(key)
		row.add_child(_label(pair[1],15))
		grid.add_child(row)
	var tip = _label("升級三選一 Buff（普通 / 稀有 / 傳說）；完成任務獲得金幣，到軍械庫升級武器。
小心狙擊兵的紅線預警、自爆蟲與正面無敵的護盾兵。",15,GOLD)
	tip.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	bv.add_child(tip)
	_select(clampi(selected,0,int(profile.get("unlocked",0))))

func _show_armory():
	pass

func _logout():
	if logging_out: return
	logging_out = true
	session_generation += 1
	_forget_session()
	var previous_token = auth_token
	await _api("/api/logout",{})
	if auth_token != previous_token: return
	multiplayer.multiplayer_peer = OfflineMultiplayerPeer.new()
	network = "offline"
	actors.clear()
	enemies.clear()
	_clean_models()
	profile.clear()
	auth_token = ""
	_show_account()

func _disconnect(message: String):
	if test_mode:
		super._disconnect(message)
		return
	multiplayer.multiplayer_peer = OfflineMultiplayerPeer.new()
	network = "offline"
	local_id = 1
	session_generation += 1
	tutorial_running = false
	modal_open = false
	actors.clear()
	enemies.clear()
	inputs.clear()
	_clean_models()
	profile.clear()
	auth_token = ""
	_show_account(message)

func _reset_actors():
	super._reset_actors()
	for id in actors: _set_actor_stats(id)

func _set_actor_stats(id: int):
	actors[id].merge({"max_hp":100.0,"damage":BASE_WEAPON_DAMAGE,"speed":6.5,"fire_interval":0.16,"regen":0,"critical":0.0,"mag":MAG_SIZE,"ammo":MAG_SIZE,"reload":0.0,"reload_time":RELOAD_TIME},true)

static func xp_for_level(lv: int) -> int:
	return int(round(XP_BASE*pow(XP_GROWTH,lv-1)))

func _level_scale() -> float:
	return 1.0+0.08*(level-1)

func _gain_xp(amount: int):
	if tutorial_running: return
	xp += amount
	while xp >= xp_need:
		xp -= xp_need
		level += 1
		xp_need = xp_for_level(level)
		pending_levels += 1

@rpc("authority", "call_remote", "unreliable_ordered")
func _xp_sync(current: int, need: int, lv: int):
	xp = current
	xp_need = need
	level = lv

func _broadcast_state():
	super._broadcast_state()
	if not test_mode:
		_xp_sync.rpc(xp,xp_need,level)
		_bullets_sync.rpc(_bullet_data())

func _start_mission(index: int):
	xp = 0
	level = 1
	xp_need = xp_for_level(1)
	pending_levels = 0
	bullets.clear()
	buff_pending = false
	buff_wave = -1
	buff_choices.clear()
	if not test_mode and dedicated and not verified.is_empty():
		if index > int(verified[leader_id].unlocked): return
		for user in verified.values():
			if index > int(user.unlocked):
				_base_notice.rpc_id(leader_id,"隊友尚未解鎖此區域，請先完成前一區域")
				return
	super._start_mission(index)

@rpc("authority", "call_remote", "reliable")
func _deploy(index: int):
	if is_instance_valid(buff_overlay): buff_overlay.queue_free()
	buff_overlay = null
	buffs_picked = 0
	left_mission = false
	bullets.clear()
	super._deploy(index)
	_apply_region(index)
	if not test_mode and is_instance_valid(heading_label):
		heading_label.text = "R / W    "+REGIONS[index][0]
	if not dedicated and is_instance_valid(hud):
		_build_xp_hud()
		_build_boss_hud()

func _leave_mission():
	if test_mode or network != "client":
		super._leave_mission()
		return
	# Stay connected to the base: the server drops us from this mission, we go to the home page.
	_request_leave.rpc_id(1)
	left_mission = true
	bullets.clear()
	_clean_models()
	_home()

@rpc("any_peer", "call_remote", "reliable")
func _request_leave():
	if not multiplayer.is_server() or mode != "playing": return
	var id = multiplayer.get_remote_sender_id()
	var others = 0
	for pid in actors:
		if pid != id and actors[pid].hp > 0: others += 1
	if others == 0:
		_return_home()
		return
	if actors.has(id): actors[id].hp = 0
	if buff_choices.has(id):
		buff_choices.erase(id)
		if buff_pending and buff_choices.is_empty(): _resolve_buffs()

func _build_xp_hud():
	var panel = _panel(hud,GOLD)
	panel.anchor_left = 0.5
	panel.anchor_right = 0.5
	panel.offset_left = -230
	panel.offset_right = 230
	panel.offset_top = 20
	var v = VBoxContainer.new()
	v.add_theme_constant_override("separation",6)
	panel.add_child(v)
	var row = HBoxContainer.new()
	v.add_child(row)
	level_label = _label("Lv. 1",20,GOLD)
	row.add_child(level_label)
	var spacer = Control.new()
	spacer.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	row.add_child(spacer)
	xp_label = _label("0 / 100 XP",14,MUTED)
	xp_label.size_flags_vertical = Control.SIZE_SHRINK_END
	row.add_child(xp_label)
	xp_bar = _bar(GOLD,8)
	v.add_child(xp_bar)

func _build_boss_hud():
	boss_panel = PanelContainer.new()
	boss_panel.mouse_filter = Control.MOUSE_FILTER_IGNORE
	boss_panel.add_theme_stylebox_override("panel",_style(INK,ORANGE))
	hud.add_child(boss_panel)
	boss_panel.anchor_left = 0.5
	boss_panel.anchor_right = 0.5
	boss_panel.offset_left = BOSS_HUD_CENTER_OFFSET-BOSS_HUD_WIDTH/2.0
	boss_panel.offset_right = BOSS_HUD_CENTER_OFFSET+BOSS_HUD_WIDTH/2.0
	boss_panel.offset_top = BOSS_HUD_TOP+80
	var content = VBoxContainer.new()
	content.mouse_filter = Control.MOUSE_FILTER_IGNORE
	content.add_theme_constant_override("separation",8)
	boss_panel.add_child(content)
	boss_name_label = _label("",18,WHITE)
	boss_name_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	boss_name_label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	content.add_child(boss_name_label)
	boss_health_bar = ProgressBar.new()
	boss_health_bar.custom_minimum_size.y = BOSS_BAR_HEIGHT
	boss_health_bar.show_percentage = false
	boss_health_bar.mouse_filter = Control.MOUSE_FILTER_IGNORE
	var background = _style(INK,CYAN)
	var fill = _style(ORANGE)
	# Bars have their own compact height; the shared panel's text padding is omitted.
	for style in [background,fill]:
		style.content_margin_left = 0
		style.content_margin_right = 0
		style.content_margin_top = 0
		style.content_margin_bottom = 0
	boss_health_bar.add_theme_stylebox_override("background",background)
	boss_health_bar.add_theme_stylebox_override("fill",fill)
	content.add_child(boss_health_bar)
	boss_panel.hide()

func _apply_region(index: int):
	if not is_instance_valid(arena): return
	if is_instance_valid(region_props):
		remove_child(region_props)
		region_props.queue_free()
	region_props = Node3D.new()
	add_child(region_props)
	var colors = REGIONS[index]
	arena.get_child(0).material_override = _mat(colors[2])
	for i in range(5+index*2):
		var angle = float(i)/float(5+index*2)*TAU
		var p = Vector3(sin(angle)*19,0,cos(angle)*19)
		if index == 0:
			_cube(region_props,p+Vector3(0,0.6,0),Vector3(2.4,1.2,1.2),Color("3a4e5c"))
		elif index == 1:
			var cylinder = CylinderMesh.new()
			cylinder.top_radius = 0.7
			cylinder.bottom_radius = 0.9
			cylinder.height = 3.5
			_mesh(region_props,cylinder,p+Vector3(0,1.75,0),_mat(Color("624b36")))
			_cube(region_props,p+Vector3(0,3.6,0),Vector3(1.6,0.2,1.6),colors[3],1)
		else:
			var spike = _cube(region_props,p+Vector3(0,2.5,0),Vector3(0.8,5,0.8),colors[3],0.3)
			spike.rotation_degrees.z = 18 if i%2==0 else -18

func _simulate(dt: float):
	if test_mode:
		super._simulate(dt)
		return
	if tutorial_running:
		wave_delay = INF
		actors[1].hp = 100
		super._simulate(dt)
		if tutorial_step == 0 and absf(yaw)>0.25: tutorial_step = 1
		if tutorial_step == 1 and actors[1].p.distance_to(tutorial_origin)>2: tutorial_step = 2
		if tutorial_step == 2 and kills>0:
			tutorial_step = 3
			_complete_training()
		return
	if buff_pending:
		buff_wait += dt
		if buff_wait > 30:
			for id in buff_choices.keys(): _apply_buff(id,buff_choices[id][0])
			_resolve_buffs()
		return
	if pending_levels > 0:
		pending_levels -= 1
		_begin_buffs()
		return
	super._simulate(dt)
	if mode == "playing": _simulate_bullets(dt)

func _simulate_bullets(dt: float):
	var alive = []
	for id in actors:
		if actors[id].hp > 0: alive.append(id)
	# Enemies fire slow energy bolts that players can sidestep or dash through.
	for e in enemies.values():
		if alive.is_empty() or bullets.size() >= MAX_BULLETS: break
		if not e.get("shoots",true): continue
		e["shot_cd"] = float(e.get("shot_cd",randf_range(1.5,3.5)))-dt
		if e.shot_cd > 0: continue
		var target = -1
		var best = BULLET_RANGE
		for pid in alive:
			var d = e.p.distance_to(actors[pid].p)
			if d < best:
				best = d
				target = pid
		if target == -1:
			e.shot_cd = 0.5
			continue
		var origin: Vector3 = e.p+Vector3(0,1.15*float(e.get("scale",1.5 if e.boss else 1.0)),0)
		var aim: Vector3 = (actors[target].p+Vector3(0,1.0,0)-origin).normalized()
		_enemy_shoot(e,origin,aim)
	for bid in bullets.keys():
		var b = bullets[bid]
		if b.get("grenade",false): continue
		b.p += b.v*dt
		b.life -= dt
		var hit = false
		for pid in alive:
			var a = actors[pid]
			var body: Vector3 = a.p+Vector3(0,0.9,0)
			if absf(b.p.y-body.y) < 1.0 and Vector2(b.p.x-body.x,b.p.z-body.z).length() < BULLET_HIT_RADIUS:
				a.hp = maxf(0,a.hp-b.dmg)
				hit = true
				if pid == local_id: shake = 0.06
				break
		if hit or b.life <= 0 or absf(b.p.x) > 23 or absf(b.p.z) > 23 or b.p.y < 0 or _in_cover(b.p):
			bullets.erase(bid)

func _enemy_shoot(e: Dictionary, origin: Vector3, aim: Vector3):
	var damage = float(e.get("damage",10))*0.6
	if e.boss:
		for spread in [-0.36,-0.18,0.0,0.18,0.36]:
			_spawn_bullet(origin,aim.rotated(Vector3.UP,spread)*BOSS_BULLET_SPEED,damage*0.8)
		e.shot_cd = randf_range(1.8,2.6)
	else:
		_spawn_bullet(origin,aim*BULLET_SPEED,damage)
		e.shot_cd = randf_range(2.4,4.2)

func _spawn_bullet(origin: Vector3, velocity: Vector3, damage: float, kind: int = 0) -> int:
	bullets[next_bullet] = {"p":origin,"v":velocity,"dmg":damage,"life":BULLET_LIFE,"kind":kind}
	next_bullet += 1
	return next_bullet-1

func _bullet_data() -> PackedFloat32Array:
	var data = PackedFloat32Array()
	for bid in bullets:
		var b = bullets[bid]
		data.append_array(PackedFloat32Array([bid,b.p.x,b.p.y,b.p.z,b.v.x,b.v.y,b.v.z,b.get("kind",0)]))
	return data

@rpc("authority", "call_remote", "unreliable_ordered")
func _bullets_sync(data: PackedFloat32Array):
	if data.size()%8 != 0: return
	var seen = {}
	for j in range(0,data.size(),8):
		var bid = int(data[j])
		seen[bid] = true
		var p = Vector3(data[j+1],data[j+2],data[j+3])
		if bullets.has(bid): bullets[bid].p = bullets[bid].p.lerp(p,0.5)
		else: bullets[bid] = {"p":p,"v":Vector3(data[j+4],data[j+5],data[j+6]),"dmg":0.0,"life":BULLET_LIFE,"kind":int(data[j+7]),"grenade":int(data[j+7]) == 2}
		bullets[bid].v = Vector3(data[j+4],data[j+5],data[j+6])
	for bid in bullets.keys():
		if not seen.has(bid): bullets.erase(bid)

func _render_models(dt: float):
	super._render_models(dt)
	if dedicated: return
	if network == "client":
		for b in bullets.values(): b.p += b.v*dt
	for bid in bullets:
		if not bullet_visuals.has(bid):
			var ball = SphereMesh.new()
			ball.radius = 0.16
			ball.height = 0.32
			bullet_visuals[bid] = _mesh(self,ball,bullets[bid].p,_mat(Color("ff5a3c"),1.4))
			bullet_visuals[bid].cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
		bullet_visuals[bid].position = bullets[bid].p
	for bid in bullet_visuals.keys():
		if not bullets.has(bid):
			bullet_visuals[bid].queue_free()
			bullet_visuals.erase(bid)

func _clean_models():
	super._clean_models()
	for v in bullet_visuals.values(): v.queue_free()
	bullet_visuals.clear()

func _spawn_wave():
	super._spawn_wave()
	if test_mode: return
	var elapsed_waves = max(0,wave-1)
	var region = float(selected)
	var party_scale = 1.0+0.6*max(0,actors.size()-1)
	# Each player level makes newly spawned enemies tougher.
	var lv_hp = _level_scale()
	var lv_dmg = 1.0+0.05*(level-1)
	for id in enemies:
		var e = enemies[id]
		if e.boss:
			var base_health = 3000.0 if wave == 10 else 7000.0
			e.hp = base_health*(1.0+0.18*region)*party_scale*lv_hp
			e.damage = (20.0+3.0*region)*(1.0+0.03*elapsed_waves)*lv_dmg
			e.speed = 1.6*(1.0+0.1*region)*(1.0+minf(0.2,0.01*elapsed_waves))
		else:
			e.hp = (55.0+10.0*region)*(1.0+0.08*elapsed_waves)*lv_hp
			e.damage = (10.0+2.0*region)*(1.0+0.04*elapsed_waves)*lv_dmg
			e.speed = (2.1+0.3*region)*(1.0+minf(0.25,0.015*elapsed_waves))
		e.max_hp = e.hp
		e.shot_cd = randf_range(1.5,3.5)
	for id in actors:
		var a = actors[id]
		if a.hp>0: a.hp = minf(a.get("max_hp",100),a.hp+a.get("regen",0))

func _begin_buffs():
	buff_pending = true
	buff_wave = wave
	buff_wait = 0
	buff_choices.clear()
	for id in actors:
		if actors[id].hp > 0: _offer_to(id)
	if buff_choices.is_empty(): _resolve_buffs()

func _offer_to(id: int):
	var pool = range(BUFFS.size())
	pool.shuffle()
	var offer = pool.slice(0,3)
	buff_choices[id] = offer
	if id == 1: _buff_offer(offer,level)
	else: _buff_offer.rpc_id(id,offer,level)

@rpc("authority", "call_remote", "reliable")
func _buff_offer(offer: Array, new_level: int):
	if dedicated: return
	modal_open = true
	Input.mouse_mode = Input.MOUSE_MODE_VISIBLE
	if is_instance_valid(buff_overlay): buff_overlay.queue_free()
	buff_overlay = Control.new()
	buff_overlay.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	screen.add_child(buff_overlay)
	var shade = ColorRect.new()
	shade.color = Color(0.015,0.03,0.06,0.82)
	shade.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	buff_overlay.add_child(shade)
	var center = CenterContainer.new()
	center.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	buff_overlay.add_child(center)
	var v = VBoxContainer.new()
	v.add_theme_constant_override("separation",18)
	v.alignment = BoxContainer.ALIGNMENT_CENTER
	center.add_child(v)
	var kicker = _label("LEVEL UP",16,GOLD)
	kicker.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	v.add_child(kicker)
	var title = _label("等級提升 · Lv. %d" % new_level,40)
	title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	v.add_child(title)
	var sub = _label("選擇一項加成（可疊加）· 敵人也會隨等級變強 · 30 秒後自動選擇",16,MUTED)
	sub.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	v.add_child(sub)
	var cards = HBoxContainer.new()
	cards.add_theme_constant_override("separation",20)
	cards.alignment = BoxContainer.ALIGNMENT_CENTER
	v.add_child(cards)
	var buttons: Array = []
	for index in offer:
		var b = _button("%s\n\n%s\n\n%s" % [BUFF_ICONS[index],BUFFS[index][0],BUFFS[index][1]],func():
			for other in buttons: other.disabled = true
			if network == "client": _choose_buff.rpc_id(1,index)
			else:
				_apply_buff(1,index)
				if buff_choices.is_empty(): _resolve_buffs(),cards,260)
		b.custom_minimum_size.x = 240
		b.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		b.add_theme_font_size_override("font_size",19)
		buttons.append(b)
	var waiting = _label("選擇後等待隊友；所有人選完就繼續作戰。",16,CYAN)
	waiting.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	v.add_child(waiting)

@rpc("any_peer", "call_remote", "reliable")
func _choose_buff(index: int):
	if not multiplayer.is_server() or not buff_pending: return
	var id = multiplayer.get_remote_sender_id()
	if not buff_choices.has(id) or not index in buff_choices[id]: return
	_apply_buff(id,index)
	if buff_choices.is_empty(): _resolve_buffs()

func _apply_buff(id: int, index: int):
	if not actors.has(id):
		buff_choices.erase(id)
		return
	var a = actors[id]
	match index:
		0: a.damage = a.get("damage",BASE_WEAPON_DAMAGE)+BASE_WEAPON_DAMAGE*0.2
		1: a.fire_interval = maxf(0.045,a.get("fire_interval",0.16)*0.85)
		2: a.speed = minf(12.0,a.get("speed",6.5)*1.12)
		3:
			a.max_hp = a.get("max_hp",100)+30
			a.hp = minf(a.max_hp,a.hp+30)
		4: a.regen = a.get("regen",0)+15
		5: a.critical = minf(0.8,a.get("critical",0.0)+0.2)
		6:
			a.mag = int(a.get("mag",MAG_SIZE))+10
			a.reload_time = maxf(0.5,float(a.get("reload_time",RELOAD_TIME))*0.85)
			if float(a.get("reload",0)) <= 0: a.ammo = a.mag
	buff_choices.erase(id)
	if id != 1: _buff_applied.rpc_id(id,index,a.get("max_hp",100))
	else: _buff_applied(index,a.get("max_hp",100))

@rpc("authority", "call_remote", "reliable")
func _buff_applied(_index: int, maximum: float):
	buffs_picked += 1
	if actors.has(local_id): actors[local_id].max_hp = maximum

func _resolve_buffs():
	buff_pending = false
	buff_choices.clear()
	wave_delay = 1.0
	if network == "host": _buff_done.rpc()
	_buff_done()

@rpc("authority", "call_remote", "reliable")
func _buff_done():
	if is_instance_valid(buff_overlay): buff_overlay.queue_free()
	buff_overlay = null
	modal_open = false
	if not dedicated and mode == "playing": Input.mouse_mode = Input.MOUSE_MODE_CAPTURED

func _fire(id: int, target: Vector3):
	if test_mode:
		super._fire(id,target)
		return
	var original_damage = actors[id].get("damage",BASE_WEAPON_DAMAGE)
	if randf()<actors[id].get("critical",0.0): actors[id].damage = original_damage*2
	super._fire(id,target)
	actors[id].damage = original_damage

func _on_enemy_killed(eid: int, e: Dictionary, shooter: int):
	if test_mode: return
	_gain_xp(300 if e.get("boss",false) else int(e.get("xp",20+5*selected)))

func _update_hud():
	super._update_hud()
	_update_boss_hud()
	if test_mode or dedicated or not is_instance_valid(hud): return
	if tutorial_running:
		var messages = ["教學 1/3：移動滑鼠，轉動第一人稱視角", "教學 2/3：按 WASD 移動一段距離", "教學 3/3：找到前方訓練機器人，左鍵射擊", "教學完成，正在儲存…", "正在連接基地…"]
		objective_label.text = messages[tutorial_step]
	elif actors.has(local_id):
		health_label.text = "護盾  %d / %d      加成 ×%d" % [actors[local_id].hp,actors[local_id].get("max_hp",100),buffs_picked]
	if is_instance_valid(xp_bar):
		xp_bar.max_value = maxi(1,xp_need)
		xp_bar.value = xp
		level_label.text = "Lv. %d" % level
		xp_label.text = "%d / %d XP   ·   敵人強度 ×%.2f" % [xp,xp_need,_level_scale()]

func _update_boss_hud():
	if not is_instance_valid(boss_panel): return
	boss_panel.hide()
	if mode != "playing" or modal_open or tutorial_running or buff_pending: return
	for enemy in enemies.values():
		if not enemy.get("boss",false) or float(enemy.hp) <= 0: continue
		var maximum = maxf(1.0,float(enemy.get("max_hp",enemy.hp)))
		boss_health_bar.max_value = maximum
		boss_health_bar.value = clampf(float(enemy.hp),0,maximum)
		boss_name_label.text = "%s · BOSS   %d / %d" % [REGIONS[selected][0],ceili(float(enemy.hp)),ceili(maximum)]
		boss_panel.show()
		return

func _finish(success: bool):
	if test_mode or tutorial_running:
		super._finish(success)
		return
	if mode != "playing": return
	mode = "saving"
	var gold = _mission_gold(success)
	for id in verified.keys():
		if not verified.has(id): continue
		var reward = await _api("/api/server/reward",{"account_id":verified[id].id,"gold":gold},true)
		if not id in multiplayer.get_peers(): continue
		if reward.get("ok",false):
			verified[id] = reward.profile
			_profile_sync.rpc_id(id,reward.profile)
		_gold_reward.rpc_id(id,gold)
	if success:
		for id in verified.keys():
			if not verified.has(id): continue
			var result = await _api("/api/server/progress",{"account_id":verified[id].id,"mission":selected},true)
			if not id in multiplayer.get_peers(): continue
			if result.get("ok",false):
				verified[id] = result.profile
				_profile_sync.rpc_id(id,result.profile)
			else: _progress_error.rpc_id(id,str(result.get("error","進度保存失敗")))
		if verified.has(leader_id): unlocked = int(verified[leader_id].unlocked)
	mode = "playing"
	super._finish(success)

func _mission_gold(success: bool) -> int:
	var gold = kills*3+wave*12+(level-1)*15
	if success: gold += 200+150*selected
	return clampi(gold,0,3000)

var last_gold = -1

@rpc("authority", "call_remote", "reliable")
func _gold_reward(gold: int):
	last_gold = gold

@rpc("authority", "call_remote", "reliable")
func _finish_remote(success: bool, duration: float, defeated: int, index: int):
	if left_mission and not dedicated:
		left_mission = false
		return
	if is_instance_valid(buff_overlay): buff_overlay.queue_free()
	buff_overlay = null
	modal_open = false
	super._finish_remote(success,duration,defeated,index)
	if not test_mode and not dedicated and last_gold >= 0:
		var reward = _label("獲得金幣  +%d G      持有  %d G" % [last_gold,int(profile.get("gold",0))],22,GOLD)
		reward.anchor_left = 0.5
		reward.anchor_right = 0.5
		reward.offset_left = -300
		reward.offset_right = 300
		reward.offset_top = 600
		reward.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		screen.add_child(reward)
		last_gold = -1
	if not test_mode and not dedicated and profile.has("save_error"):
		var warning = _label("進度尚未保存："+str(profile.save_error),18,ORANGE)
		warning.position = Vector2(240,680)
		screen.add_child(warning)

@rpc("authority", "call_remote", "reliable")
func _profile_sync(user: Dictionary):
	profile = user
	unlocked = int(user.unlocked)

@rpc("authority", "call_remote", "reliable")
func _progress_error(message: String):
	push_warning(message)
	profile["save_error"] = message

@rpc("authority", "call_remote", "reliable")
func _base_notice(message: String):
	if is_instance_valid(status_label): status_label.text = message

func _save_progress():
	if test_mode: super._save_progress()

func _return_home():
	if tutorial_running and not test_mode:
		tutorial_running = false
		actors.clear()
		enemies.clear()
		_clean_models()
		story_index = 2
		_show_story()
		return
	super._return_home()

func _load_progress():
	if test_mode: super._load_progress()

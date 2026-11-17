extends "res://main.gd"

const VERSION = "0.3.0-beta.1"
const BUFFS = [
	["超載彈芯", "本次任務的傷害增加 20%", "damage"],
	["急速扳機", "射擊間隔縮短 15%", "fire_interval"],
	["動能靴", "移動速度增加 12%", "speed"],
	["強化護盾", "護盾上限 +30，立即恢復 30", "max_hp"],
	["修復協議", "每波開始恢復 15 點護盾", "regen"],
	["弱點分析", "增加 20% 機率造成雙倍傷害", "critical"]
]
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

func api_base() -> String:
	return "http://%s:24771" % server_address

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
	return parsed

func _show_account(message: String = ""):
	mode = "account"
	Input.mouse_mode = Input.MOUSE_MODE_VISIBLE
	_clear_ui()
	account_page = load("res://ui/account_screen.gd").new()
	screen.add_child(account_page)
	account_page.authenticated.connect(_authenticated)
	account_page.setup(self,api_base())
	if not message.is_empty():
		var notice = _label(message,16,ORANGE)
		notice.position = Vector2(32,744)
		screen.add_child(notice)

func _authenticated(token: String, user: Dictionary):
	auth_token = token
	profile = user
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
	_home()
	mode = "connecting"
	status_label.text = "正在驗證帳號並連接基地…"
	_join(server_address)

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
	_dim(screen)
	var left = _box(screen,Vector2(56,32),496)
	left.add_theme_constant_override("separation",8)
	left.add_child(_label("RIFTWARD  /  " + VERSION,16,CYAN))
	left.add_child(_label("抵抗軍基地",38))
	left.add_child(_label("槍手："+str(profile.get("username","")),18))
	left.add_child(_label("區域依序解鎖 · 每波選擇戰術加成",18,CYAN))
	for i in range(REGIONS.size()):
		var region = REGIONS[i]
		var locked = i > int(profile.get("unlocked",0))
		var button = _button("%02d  %s%s\n%s" % [i+1,region[0]," [尚未解鎖]" if locked else "",MISSIONS[i][3]],func(): _choose_mission(i),left,88)
		button.alignment = HORIZONTAL_ALIGNMENT_LEFT
		button.disabled = locked
		mission_buttons.append(button)
	var launch = _button("部署到選定區域",_request_start,left,56)
	launch.disabled = network != "client" or local_id != leader_id
	_button("重新連接基地",func():
		if network == "offline": _connect_base(),left,48)
	_button("重看故事與教學",func():
		if network != "offline":
			multiplayer.multiplayer_peer = OfflineMultiplayerPeer.new()
			network = "offline"
		story_index = 0
		_show_story(),left,48)
	_button("登出帳號",_logout,left,48)
	status_label = _label(str(profile.get("save_error",_connection_text())),14,CYAN)
	status_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	left.add_child(status_label)
	var right = _box(screen,Vector2(616,112),584)
	right.size.x = 584
	right.add_child(_label("宇宙帝國入侵 / 第一章",24,CYAN))
	var description = _label(REGIONS[selected][1],26)
	description.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	description.custom_minimum_size = Vector2(584,176)
	right.add_child(description)
	var controls = _label("自由 360° 轉身 · 第一人稱射擊\nWASD 移動 / 左鍵射擊 / Shift 衝刺\nSpace 跳躍 / Esc 選單",20)
	controls.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	right.add_child(controls)
	var briefing = _label("基地 192.168.213.100\n每波結束三選一 Buff，任務內可疊加。\n清除區域後回基地，裝備加成會重置。",18,CYAN)
	briefing.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	right.add_child(briefing)
	_select(clampi(selected,0,int(profile.get("unlocked",0))))

func _logout():
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
	actors.clear()
	enemies.clear()
	inputs.clear()
	_clean_models()
	_home()
	if is_instance_valid(status_label): status_label.text = message

func _reset_actors():
	super._reset_actors()
	for id in actors: _set_actor_stats(id)

func _set_actor_stats(id: int):
	actors[id].merge({"max_hp":100.0,"damage":28.0,"speed":6.5,"fire_interval":0.16,"regen":0,"critical":0.0},true)

func _start_mission(index: int):
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
	super._deploy(index)
	_apply_region(index)
	if not test_mode and is_instance_valid(hud):
		var heading = hud.get_child(0).get_child(0)
		heading.text = "R / W    "+REGIONS[index][0]

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
	if enemies.is_empty() and wave > 0 and buff_wave != wave:
		_begin_buffs()
		return
	super._simulate(dt)

func _spawn_wave():
	super._spawn_wave()
	if test_mode: return
	for id in enemies:
		var e = enemies[id]
		e.hp *= 1.0+0.22*(wave-1)+selected*0.1
		e.speed = minf(5.0,e.speed+0.16*(wave-1))
		e.damage = (20 if e.boss else 10)+2*(wave-1)+selected*2
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
	var pool = [0,1,2,3,4,5]
	pool.shuffle()
	var offer = pool.slice(0,3)
	buff_choices[id] = offer
	if id == 1: _buff_offer(offer,wave)
	else: _buff_offer.rpc_id(id,offer,wave)

@rpc("authority", "call_remote", "reliable")
func _buff_offer(offer: Array, completed_wave: int):
	if dedicated: return
	modal_open = true
	Input.mouse_mode = Input.MOUSE_MODE_VISIBLE
	if is_instance_valid(buff_overlay): buff_overlay.queue_free()
	buff_overlay = Control.new()
	buff_overlay.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	screen.add_child(buff_overlay)
	_dim(buff_overlay)
	var v = _box(buff_overlay,Vector2(240,128),800)
	v.add_child(_label("第 %d 波已清除" % completed_wave,34,CYAN))
	v.add_child(_label("選擇一項本次任務加成 · 可疊加 · 30 秒後自動選擇",18))
	for index in offer:
		_button(BUFFS[index][0]+"\n"+BUFFS[index][1],func():
			for child in v.get_children():
				if child is Button: child.disabled = true
			if network == "client": _choose_buff.rpc_id(1,index)
			else:
				_apply_buff(1,index)
				if buff_choices.is_empty(): _resolve_buffs(),v,96)
	var waiting = _label("選擇後等待隊友；所有人選完就繼續作戰。",18,CYAN)
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
		0: a.damage = a.get("damage",28)*1.2
		1: a.fire_interval = maxf(0.045,a.get("fire_interval",0.16)*0.85)
		2: a.speed = minf(12.0,a.get("speed",6.5)*1.12)
		3:
			a.max_hp = a.get("max_hp",100)+30
			a.hp = minf(a.max_hp,a.hp+30)
		4: a.regen = a.get("regen",0)+15
		5: a.critical = minf(0.8,a.get("critical",0.0)+0.2)
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
	var original_damage = actors[id].get("damage",28.0)
	if randf()<actors[id].get("critical",0.0): actors[id].damage = original_damage*2
	super._fire(id,target)
	actors[id].damage = original_damage

func _update_hud():
	super._update_hud()
	if test_mode or dedicated or not is_instance_valid(hud): return
	if tutorial_running:
		var messages = ["教學 1/3：移動滑鼠，轉動第一人稱視角", "教學 2/3：按 WASD 移動一段距離", "教學 3/3：找到前方訓練機器人，左鍵射擊", "教學完成，正在儲存…", "正在連接基地…"]
		objective_label.text = messages[tutorial_step]
	elif actors.has(local_id):
		health_label.text = "護盾 %d / %d   ·   加成 %d" % [actors[local_id].hp,actors[local_id].get("max_hp",100),buffs_picked]

func _finish(success: bool):
	if test_mode or tutorial_running:
		super._finish(success)
		return
	if mode != "playing": return
	mode = "saving"
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

@rpc("authority", "call_remote", "reliable")
func _finish_remote(success: bool, duration: float, defeated: int, index: int):
	if is_instance_valid(buff_overlay): buff_overlay.queue_free()
	buff_overlay = null
	modal_open = false
	super._finish_remote(success,duration,defeated,index)
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

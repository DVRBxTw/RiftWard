extends Node3D

const CYAN = Color("66f5d0")
const ORANGE = Color("ff8855")
const INK = Color("101c2b")
const WHITE = Color("e8f3ff")
const PORT = 24770
const DEFAULT_SERVER_IP = "123.241.56.94"
const EYE_HEIGHT = 1.65
const LOOK_SENSITIVITY = 0.0025
const LOOK_LIMIT = PI * 85.0 / 180.0
const SHOT_RANGE = 65.0
const DASH_DURATION = 0.18
const DASH_DISTANCE = 4.0
const DASH_COOLDOWN = 2.8
const DASH_VISUAL_DURATION = 0.32
const BASE_FOV = 80.0
const DASH_FOV_BOOST = 7.0
const WEAPON_REST_POSITION = Vector3(0.27,-0.23,-0.48)
const MAG_SIZE = 30
const RELOAD_TIME = 1.6
const THIRD_PERSON_OFFSET = Vector3(0.75,0.35,3.2)
const PANEL_BG = Color(0.035,0.07,0.115,0.82)
const PANEL_EDGE = Color("2b4a63")
const MUTED = Color("8fa6bb")
const GOLD = Color("ffd166")
const MISSIONS = [
	["01", "邊境訊號", "清除廢墟中的巡邏機群", "20 波 · 第 10、20 波 Boss", 20, 4],
	["02", "失落反應爐", "深入遺跡，摧毀增援部隊", "20 波 · 第 10、20 波 Boss", 20, 5],
	["03", "裂隙之心", "迎戰重型守衛，關閉裂隙", "20 波 · 第 10、20 波 Boss", 20, 6]
]
var arena: Node3D
var camera: Camera3D
var showcase: Node3D
var weapon: Node3D
var muzzle: Node3D
var actors: Dictionary = {}
var enemies: Dictionary = {}
var inputs: Dictionary = {}
var visuals: Dictionary = {}
var enemy_visuals: Dictionary = {}
var effects: Array = []
var ui: CanvasLayer
var screen: Control
var hud: Control
var health_label: Label
var objective_label: Label
var dash_label: Label
var toast: Label
var heading_label: Label
var ammo_label: Label
var reserve_label: Label
var reload_bar: ProgressBar
var health_bar: ProgressBar
var dash_bar: ProgressBar
var view_label: Label
var third_person = false
var status_label: Label
var ip_input: LineEdit
var mission_buttons: Array = []
var selected = 0
var unlocked = 0
var mode = "home"
var network = "offline"
var server_address = DEFAULT_SERVER_IP
var server_port = PORT
var dedicated = false
var yaw = 0.0
var pitch = 0.0
var release_fire_to_resume = false
var elapsed = 0.0
var clock_time = 0.0
var sync_time = 0.0
var wave = 0
var wave_delay = 2.0
var next_enemy = 0
var kills = 0
var shots = 0
var result_success = false
var leader_id = 1
var modal_open = false
var local_id = 1
var shake = 0.0
var audio: AudioStreamPlayer
var smoke_test = false
var smoke_phase = 0
var captured = false
var net_test_phase = 0
var input_sequence = 0
var pending_movement: Array = []
var local_previous_position = Vector3.ZERO
var local_position_initialized = false
var camera_correction = Vector3.ZERO

func _ready():
	dedicated = "--server" in OS.get_cmdline_user_args()
	smoke_test = "--smoke-test" in OS.get_cmdline_user_args()
	_load_server_address()
	_setup_input()
	_setup_theme()
	_build_world()
	_load_progress()
	multiplayer.peer_connected.connect(_peer_joined)
	multiplayer.peer_disconnected.connect(_peer_left)
	multiplayer.connected_to_server.connect(_connected)
	multiplayer.connection_failed.connect(func(): _disconnect("連線失敗，請確認 IP 與伺服器狀態"))
	multiplayer.server_disconnected.connect(func(): _disconnect("與伺服器中斷連線"))
	audio = AudioStreamPlayer.new()
	add_child(audio)
	if dedicated:
		_host(true)
	else:
		_home()
	if "--test-client" in OS.get_cmdline_user_args():
		_join("127.0.0.1")
	if smoke_test or "--preview-combat" in OS.get_cmdline_user_args():
		_start_mission(0)

func _setup_input():
	for pair in [["forward", KEY_W], ["back", KEY_S], ["left", KEY_A], ["right", KEY_D], ["dash", KEY_SHIFT], ["jump", KEY_SPACE], ["reload", KEY_R], ["toggle_view", KEY_V]]:
		if InputMap.has_action(pair[0]): continue
		InputMap.add_action(pair[0])
		var ev = InputEventKey.new()
		ev.physical_keycode = pair[1]
		InputMap.action_add_event(pair[0], ev)

func _style(color: Color, edge: Color = Color.TRANSPARENT) -> StyleBoxFlat:
	var s = StyleBoxFlat.new()
	s.bg_color = color
	s.border_color = edge
	s.set_border_width_all(1)
	s.set_corner_radius_all(6)
	s.content_margin_left = 24
	s.content_margin_right = 24
	s.content_margin_top = 16
	s.content_margin_bottom = 16
	return s

func _panel_style(bg: Color = PANEL_BG, accent: Color = CYAN, radius: int = 10) -> StyleBoxFlat:
	var s = StyleBoxFlat.new()
	s.bg_color = bg
	s.border_color = PANEL_EDGE
	s.set_border_width_all(1)
	s.border_width_left = 3
	s.border_color = accent.darkened(0.15)
	s.set_corner_radius_all(radius)
	s.shadow_color = Color(0,0,0,0.35)
	s.shadow_size = 10
	s.content_margin_left = 20
	s.content_margin_right = 20
	s.content_margin_top = 14
	s.content_margin_bottom = 14
	return s

func _panel(parent: Node, accent: Color = CYAN, bg: Color = PANEL_BG) -> PanelContainer:
	var p = PanelContainer.new()
	p.mouse_filter = Control.MOUSE_FILTER_IGNORE
	p.add_theme_stylebox_override("panel",_panel_style(bg,accent))
	parent.add_child(p)
	return p

func _bar(color: Color, height: float = 10.0, back: Color = Color(1,1,1,0.08)) -> ProgressBar:
	var b = ProgressBar.new()
	b.show_percentage = false
	b.custom_minimum_size.y = height
	b.mouse_filter = Control.MOUSE_FILTER_IGNORE
	for pair in [["background",back],["fill",color]]:
		var s = StyleBoxFlat.new()
		s.bg_color = pair[1]
		s.set_corner_radius_all(int(height/2))
		b.add_theme_stylebox_override(pair[0],s)
	return b

func _backdrop(parent: Node, left_alpha: float = 0.96, right_alpha: float = 0.25):
	var g = Gradient.new()
	g.set_color(0,Color(0.02,0.04,0.075,left_alpha))
	g.set_color(1,Color(0.02,0.04,0.075,right_alpha))
	var tex = GradientTexture2D.new()
	tex.gradient = g
	tex.fill_from = Vector2(0.3,0)
	tex.fill_to = Vector2(1,0)
	var r = TextureRect.new()
	r.texture = tex
	r.stretch_mode = TextureRect.STRETCH_SCALE
	r.mouse_filter = Control.MOUSE_FILTER_IGNORE
	r.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	parent.add_child(r)
	return r

func _setup_theme():
	ui = CanvasLayer.new()
	add_child(ui)
	var theme = Theme.new()
	var font = FontVariation.new()
	font.base_font = load("res://ui-font.ttf")
	font.variation_opentype = {"wght":500}
	font.variation_embolden = 0.5
	theme.default_font = font
	theme.default_font_size = 18
	theme.set_color("font_color", "Label", WHITE)
	theme.set_color("font_color", "Button", WHITE)
	theme.set_color("font_hover_color", "Button", CYAN)
	theme.set_color("font_disabled_color", "Button", Color("73849a"))
	theme.set_color("font_pressed_color", "Button", Color("0b1622"))
	theme.set_color("font_hover_pressed_color", "Button", Color("0b1622"))
	theme.set_stylebox("normal", "Button", _button_style(Color(0.09,0.16,0.23,0.92), Color("2f4b63")))
	theme.set_stylebox("hover", "Button", _button_style(Color(0.12,0.27,0.33,0.95), CYAN, true))
	theme.set_stylebox("pressed", "Button", _button_style(CYAN, CYAN, true))
	theme.set_stylebox("focus", "Button", _button_style(Color(0,0,0,0), Color(CYAN,0.6)))
	theme.set_stylebox("disabled", "Button", _button_style(Color(0.06,0.09,0.13,0.85), Color("1d2b3a")))
	theme.set_stylebox("normal", "LineEdit", _button_style(Color(0.03,0.06,0.1,0.95), Color("2f4b63")))
	theme.set_stylebox("focus", "LineEdit", _button_style(Color(0.03,0.06,0.1,0.95), CYAN, true))
	theme.set_color("font_color", "LineEdit", WHITE)
	theme.set_color("font_placeholder_color", "LineEdit", Color("5f7489"))
	theme.set_color("caret_color", "LineEdit", CYAN)
	theme.set_stylebox("panel", "PopupPanel", _panel_style(Color(0.04,0.08,0.13,0.98)))
	theme.set_stylebox("panel", "AcceptDialog", _panel_style(Color(0.04,0.08,0.13,0.98)))
	screen = Control.new()
	screen.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	screen.theme = theme
	ui.add_child(screen)

func _button_style(color: Color, edge: Color, accent: bool = false) -> StyleBoxFlat:
	var s = _style(color, edge)
	s.set_corner_radius_all(8)
	s.border_width_left = 4 if accent else 2
	s.content_margin_left = 22
	s.content_margin_right = 22
	s.content_margin_top = 12
	s.content_margin_bottom = 12
	if accent:
		s.shadow_color = Color(edge,0.25)
		s.shadow_size = 8
	return s

func _label(text: String, size: int = 18, color: Color = WHITE) -> Label:
	var l = Label.new()
	l.text = text
	l.add_theme_font_size_override("font_size", size)
	l.add_theme_color_override("font_color", color)
	return l

func _button(text: String, action: Callable, parent: Node, height: int = 48) -> Button:
	var b = Button.new()
	b.text = text
	b.custom_minimum_size.y = height
	b.pressed.connect(action)
	parent.add_child(b)
	return b

func _box(parent: Node, pos: Vector2, width: float) -> VBoxContainer:
	var v = VBoxContainer.new()
	v.position = pos
	v.custom_minimum_size.x = width
	v.add_theme_constant_override("separation", 16)
	parent.add_child(v)
	return v

func _clear_ui():
	for child in screen.get_children():
		screen.remove_child(child)
		child.queue_free()
	mission_buttons.clear()
	hud = null
	toast = null

func _home():
	mode = "home"
	if is_instance_valid(showcase): showcase.visible = true
	Input.mouse_mode = Input.MOUSE_MODE_VISIBLE
	_clear_ui()
	_backdrop(screen)
	var left = _box(screen, Vector2(48, 32), 462)
	left.add_theme_constant_override("separation", 8)
	left.add_child(_label("R / W     OPERATIONS DIVISION", 14, CYAN))
	left.add_child(_label("裂 隙 守 衛", 46))
	left.add_child(_label("RIFTWARD  /  合作戰術行動", 18, Color("a7bccc")))
	left.add_child(_label("選擇部署地點", 20))
	for i in range(MISSIONS.size()):
		var m = MISSIONS[i]
		var suffix = "" if i <= unlocked else "  [尚未解鎖]"
		var b = _button("%s   %s%s\n%s" % [m[0], m[1], suffix, m[3]], func(): _choose_mission(i), left, 78)
		b.alignment = HORIZONTAL_ALIGNMENT_LEFT
		b.disabled = i > unlocked
		mission_buttons.append(b)
	_button("▶  開始任務", _request_start, left, 54)
	var row = HBoxContainer.new()
	row.add_theme_constant_override("separation", 8)
	left.add_child(row)
	_button("建立連線房間", func(): _host(false), row)
	_button("離線單人", func(): _disconnect("已切換為離線單人"), row)
	ip_input = LineEdit.new()
	ip_input.placeholder_text = "伺服器 IP"
	ip_input.text = server_address
	ip_input.custom_minimum_size.y = 44
	left.add_child(ip_input)
	_button("加入伺服器", func(): _join(ip_input.text.strip_edges()), left, 44)
	status_label = _label(_connection_text(), 14, CYAN)
	left.add_child(status_label)
	var right = _box(screen, Vector2(620, 52), 560)
	right.add_child(_label("// SECTOR 07", 16, CYAN))
	right.add_child(_label("訊號仍在。\n守住最後一道防線。", 32))
	var footer = _box(screen, Vector2(620, 638), 550)
	footer.add_child(_label("01 / 部署   →   02 / 清掃   →   03 / 撤離", 18, CYAN))
	footer.add_child(_label("WASD 移動 · 滑鼠瞄準 · 左鍵射擊\nShift 衝刺 · Space 跳躍 · Esc 選單", 16))
	footer.add_child(_label("PRE-ALPHA 0.2   /   第一人稱射擊 · 1–4 人合作原型", 13, Color("90a7bc")))
	_select(mini(selected, unlocked))

func _select(i: int):
	selected = i
	for j in range(mission_buttons.size()):
		var b: Button = mission_buttons[j]
		if j == i:
			b.add_theme_stylebox_override("normal",_button_style(Color(0.08,0.24,0.28,0.95),CYAN,true))
			b.add_theme_color_override("font_color",CYAN)
		else:
			b.remove_theme_stylebox_override("normal")
			b.remove_theme_color_override("font_color")

func _connection_text() -> String:
	if network == "host":
		return "基地已啟動 | %d / 4" % actors.size()
	if network == "client":
		return "● 已連線 · 你是隊長" if local_id == leader_id else "● 已連線 · 等待隊長選關與開始"
	if network == "connecting":
		return "◌ 正在連線…"
	return "尚未連線至基地"


func _load_server_address():
	var cfg = ConfigFile.new()
	var path = OS.get_executable_path().get_base_dir().path_join("server.cfg")
	if cfg.load(path) == OK:
		var address = str(cfg.get_value("server","address",DEFAULT_SERVER_IP)).strip_edges()
		if address.is_valid_ip_address(): server_address = address

func _mat(color: Color, glow: float = 0.0) -> StandardMaterial3D:
	var m = StandardMaterial3D.new()
	m.albedo_color = color
	m.roughness = 0.75
	if glow > 0:
		m.emission_enabled = true
		m.emission = color
		m.emission_energy_multiplier = glow
	return m

func _mesh(parent: Node3D, shape: Mesh, pos: Vector3, material: Material) -> MeshInstance3D:
	var m = MeshInstance3D.new()
	m.mesh = shape
	m.position = pos
	m.material_override = material
	parent.add_child(m)
	return m

func _cube(parent: Node3D, pos: Vector3, size: Vector3, color: Color, glow: float = 0.0) -> MeshInstance3D:
	var b = BoxMesh.new()
	b.size = size
	return _mesh(parent, b, pos, _mat(color, glow))

func _build_world():
	arena = Node3D.new()
	add_child(arena)
	var env = WorldEnvironment.new()
	var e = Environment.new()
	e.background_mode = Environment.BG_COLOR
	e.background_color = Color("101e33")
	e.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	e.ambient_light_color = Color("83b1d1")
	e.ambient_light_energy = 0.4
	e.fog_enabled = true
	e.fog_light_color = Color("152b40")
	e.fog_density = 0.008
	e.tonemap_mode = Environment.TONE_MAPPER_FILMIC
	env.environment = e
	add_child(env)
	var sun = DirectionalLight3D.new()
	sun.rotation_degrees = Vector3(-48, -32, 0)
	sun.light_color = Color("c9e9ff")
	sun.light_energy = 0.95
	sun.shadow_enabled = true
	add_child(sun)
	_cube(arena, Vector3(0,-0.3,0), Vector3(46,0.6,46), Color("203547"))
	for n in range(-11,12):
		_cube(arena, Vector3(n*2,0.012,0), Vector3(0.025,0.015,46), Color("345568"))
		_cube(arena, Vector3(0,0.012,n*2), Vector3(46,0.015,0.025), Color("345568"))
	for side in [-1, 1]:
		_cube(arena, Vector3(side*23,0.5,0), Vector3(0.4,1,46), Color("34485a"))
		_cube(arena, Vector3(0,0.5,side*23), Vector3(46,1,0.4), Color("34485a"))
		_cube(arena, Vector3(side*22.7,1.05,0), Vector3(0.12,0.08,46), CYAN, 2)
		_cube(arena, Vector3(0,1.05,side*22.7), Vector3(46,0.08,0.12), CYAN, 2)
	var rng = RandomNumberGenerator.new()
	rng.seed = 107
	for i in range(42):
		var angle = rng.randf_range(0,TAU)
		var radius = rng.randf_range(30,65)
		var h = rng.randf_range(4,22)
		var p = Vector3(sin(angle)*radius, h/2-2, cos(angle)*radius)
		var tower = _cube(arena,p,Vector3(rng.randf_range(2,6),h,rng.randf_range(2,6)),Color("162b3d"))
		tower.rotation.y = rng.randf_range(-0.2,0.2)
		_cube(arena,p+Vector3(0,h/2,0),Vector3(0.12,0.4,2),CYAN,1.5)
	for x in [-17,17]:
		for z in [-17,0,17]:
			_cube(arena,Vector3(x,2.2,z),Vector3(1.3,4.4,1.3),Color("34485a"))
			_cube(arena,Vector3(x,4.5,z),Vector3(1.6,0.15,1.6),CYAN,2)
	for z in [-13,13]:
		for x in [-8,8]:
			_cube(arena,Vector3(x,0.5,z),Vector3(2,1,2),Color("3a4e5c"))
	# Central floating reactor, decorative; combat plane remains unobstructed.
	var ring = TorusMesh.new()
	ring.inner_radius = 2.6
	ring.outer_radius = 2.8
	var portal = _mesh(arena,ring,Vector3(0,5,-19),_mat(CYAN,3))
	portal.rotation_degrees.x = 90
	_cube(arena,Vector3(0,1.4,-19),Vector3(6,2.8,1.2),Color("314759"))
	for x in [-3,3]:
		_cube(arena,Vector3(x,4,-19),Vector3(0.6,7,0.8),Color("4a6170"))
	camera = Camera3D.new()
	camera.fov = BASE_FOV
	camera.near = 0.05
	camera.far = 180
	add_child(camera)
	camera.current = true
	_build_weapon()
	showcase = _actor_model(CYAN)
	showcase.position = Vector3(10,0,0)
	showcase.rotation_degrees.y = 145
	showcase.scale = Vector3.ONE*2.5
	add_child(showcase)

func _build_weapon():
	weapon = Node3D.new()
	weapon.position = WEAPON_REST_POSITION
	weapon.visible = false
	camera.add_child(weapon)
	_cube(weapon,Vector3.ZERO,Vector3(0.15,0.14,0.42),INK)
	_cube(weapon,Vector3(0,-0.11,0.1),Vector3(0.1,0.22,0.12),Color("334d61"))
	_cube(weapon,Vector3(0,0,-0.28),Vector3(0.09,0.09,0.2),Color("34485a"))
	_cube(weapon,Vector3(0,0.077,-0.1),Vector3(0.05,0.018,0.2),CYAN,0.5)
	_cube(weapon,Vector3(0,0,-0.39),Vector3(0.075,0.065,0.025),CYAN,1)
	for mesh in weapon.get_children():
		if mesh is GeometryInstance3D:
			mesh.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	muzzle = Node3D.new()
	muzzle.position = Vector3(0,0,-0.42)
	weapon.add_child(muzzle)

func _actor_model(color: Color, heavy: bool = false) -> Node3D:
	var root = Node3D.new()
	var body = CapsuleMesh.new()
	body.radius = 0.35 if not heavy else 0.65
	body.height = 1.2 if not heavy else 2.1
	_mesh(root,body,Vector3(0,1,0),_mat(Color("334d61") if color == CYAN else Color("593c38")))
	_cube(root,Vector3(0,1.65,-0.12),Vector3(0.56,0.38,0.5),color,0.3)
	_cube(root,Vector3(0,1.67,-0.39),Vector3(0.42,0.09,0.03),Color("d6ffff"),2)
	_cube(root,Vector3(-0.48,1.15,0),Vector3(0.25,0.6,0.35),color)
	_cube(root,Vector3(0.48,1.15,-0.2),Vector3(0.25,0.38,0.8),Color("12212c"))
	_cube(root,Vector3(0.48,1.15,-0.64),Vector3(0.12,0.1,0.1),color,3)
	for x in [-0.2,0.2]:
		_cube(root,Vector3(x,0.3,0),Vector3(0.25,0.6,0.28),Color("233447"))
	var ring = TorusMesh.new()
	ring.inner_radius = 0.48 if not heavy else 0.9
	ring.outer_radius = 0.53 if not heavy else 1.0
	_mesh(root,ring,Vector3(0,0.04,0),_mat(color,1))
	if heavy:
		root.scale = Vector3.ONE*1.5
	return root

func _add_actor(id: int):
	if actors.has(id): return
	actors[id] = _new_actor(Vector3((actors.size()%4)*2-3,0,10))

func _new_actor(pos: Vector3) -> Dictionary:
	return {"p":pos, "hp":100.0, "cd":0.0, "dash":0.0, "v":0.0, "y":0.0, "ammo":MAG_SIZE, "mag":MAG_SIZE, "reload":0.0}

func _reset_actors():
	for id in actors:
		actors[id] = _new_actor(Vector3((int(id)%4)*2-3,0,10))

func _host(headless: bool):
	if network != "offline": return
	var peer = ENetMultiplayerPeer.new()
	var bind_address = "127.0.0.1" if "--network-test" in OS.get_cmdline_user_args() else "0.0.0.0"
	peer.set_bind_ip(bind_address)
	var err = peer.create_server(server_port, 4 if headless else 3)
	if err != OK:
		var message = "無法監聽 UDP %s:%d（%s）；請確認沒有重複啟動伺服器" % [bind_address,server_port,error_string(err)]
		push_warning(message)
		if is_instance_valid(status_label): status_label.text = message
		if headless: get_tree().quit(1)
		return
	multiplayer.multiplayer_peer = peer
	network = "host"
	if not headless: _add_actor(1)
	print("SERVER_READY address=", bind_address, " port=", server_port)
	if not headless: _home()

func _join(ip: String):
	if network != "offline":
		return
	if ip.is_empty():
		status_label.text = "請輸入伺服器 IP 位址"
		return
	var peer = ENetMultiplayerPeer.new()
	if peer.create_client(ip, server_port) != OK:
		status_label.text = "無法連線，請檢查伺服器位址"
		return
	multiplayer.multiplayer_peer = peer
	network = "connecting"
	if is_instance_valid(status_label): status_label.text = "正在連線至基地..."


func _connected():
	network = "client"
	local_id = multiplayer.get_unique_id()
	print("CLIENT_CONNECTED id=", local_id)
	if mode == "home": _home()

func _disconnect(message: String):
	multiplayer.multiplayer_peer = OfflineMultiplayerPeer.new()
	network = "offline"
	local_id = 1
	actors.clear()
	inputs.clear()
	enemies.clear()
	_clean_models()
	_home()
	status_label.text = message

func _peer_joined(id: int):
	if multiplayer.is_server():
		_add_actor(id)
		print("PEER_JOINED ", id)
		if dedicated and actors.size() == 1: leader_id = id
		_lobby_state.rpc(selected, unlocked, leader_id)
		if mode == "playing": _deploy.rpc_id(id, selected)
		elif mode == "result": _finish_remote.rpc_id(id, result_success, elapsed, kills, selected)
	if is_instance_valid(status_label): status_label.text = _connection_text()

func _peer_left(id: int):
	actors.erase(id)
	inputs.erase(id)
	if multiplayer.is_server() and dedicated and id == leader_id:
		leader_id = actors.keys()[0] if not actors.is_empty() else 1
		_lobby_state.rpc(selected, unlocked, leader_id)
	if is_instance_valid(status_label): status_label.text = _connection_text()

func _request_start():
	if network == "connecting": return
	if network == "client":
		if local_id != leader_id:
			if is_instance_valid(status_label): status_label.text = "由隊長選關並開始任務"
			return
		_request_deploy.rpc_id(1, selected)
	else: _start_mission(selected)

@rpc("any_peer", "call_remote", "reliable")
func _request_deploy(index: int):
	if multiplayer.is_server() and mode != "playing" and multiplayer.get_remote_sender_id() == leader_id:
		_start_mission(selected)

func _start_mission(index: int):
	selected = index
	if actors.is_empty() and not dedicated: _add_actor(1)
	_reset_actors()
	enemies.clear()
	inputs.clear()
	wave = 0
	wave_delay = 1.8
	elapsed = 0
	kills = 0
	shots = 0
	if network == "host": _deploy.rpc(index)
	_deploy(index)

@rpc("authority", "call_remote", "reliable")
func _deploy(index: int):
	pending_movement.clear()
	local_position_initialized = false
	camera_correction = Vector3.ZERO
	selected = index
	mode = "playing"
	if is_instance_valid(showcase): showcase.visible = false
	modal_open = false
	release_fire_to_resume = false
	yaw = 0
	pitch = 0
	_clean_models()
	_clear_ui()
	if dedicated: return
	Input.mouse_mode = Input.MOUSE_MODE_CAPTURED
	hud = Control.new()
	hud.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	hud.mouse_filter = Control.MOUSE_FILTER_IGNORE
	screen.add_child(hud)
	# Top-left: mission and objective.
	var top = _panel(hud)
	top.position = Vector2(24,20)
	top.custom_minimum_size.x = 380
	var tv = VBoxContainer.new()
	tv.add_theme_constant_override("separation",4)
	top.add_child(tv)
	heading_label = _label("R / W    " + MISSIONS[index][1],20,CYAN)
	tv.add_child(heading_label)
	objective_label = _label("部署中…",16)
	tv.add_child(objective_label)
	# Bottom-left: shield + dash.
	var vitals = _panel(hud)
	vitals.anchor_top = 1.0
	vitals.anchor_bottom = 1.0
	vitals.offset_left = 24
	vitals.offset_top = -132
	vitals.offset_right = 404
	vitals.offset_bottom = -24
	var vv = VBoxContainer.new()
	vv.add_theme_constant_override("separation",6)
	vitals.add_child(vv)
	health_label = _label("護盾  100 / 100",20,CYAN)
	vv.add_child(health_label)
	health_bar = _bar(CYAN,12)
	vv.add_child(health_bar)
	dash_label = _label("Shift 衝刺就緒",14,MUTED)
	vv.add_child(dash_label)
	dash_bar = _bar(Color("82aaff"),5)
	dash_bar.max_value = DASH_COOLDOWN
	vv.add_child(dash_bar)
	# Bottom-right: ammo.
	var ammo_panel = _panel(hud,ORANGE)
	ammo_panel.anchor_left = 1.0
	ammo_panel.anchor_right = 1.0
	ammo_panel.anchor_top = 1.0
	ammo_panel.anchor_bottom = 1.0
	ammo_panel.offset_left = -284
	ammo_panel.offset_top = -132
	ammo_panel.offset_right = -24
	ammo_panel.offset_bottom = -24
	var av = VBoxContainer.new()
	av.add_theme_constant_override("separation",4)
	ammo_panel.add_child(av)
	var ammo_row = HBoxContainer.new()
	ammo_row.alignment = BoxContainer.ALIGNMENT_END
	ammo_row.add_theme_constant_override("separation",8)
	av.add_child(ammo_row)
	ammo_label = _label(str(MAG_SIZE),40,WHITE)
	ammo_row.add_child(ammo_label)
	reserve_label = _label("/ %d" % MAG_SIZE,20,MUTED)
	reserve_label.size_flags_vertical = Control.SIZE_SHRINK_END
	ammo_row.add_child(reserve_label)
	reload_bar = _bar(ORANGE,5)
	reload_bar.max_value = 1.0
	av.add_child(reload_bar)
	view_label = _label("R 換彈   ·   V 第一人稱",13,MUTED)
	view_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	av.add_child(view_label)
	# Crosshair.
	var cross = Control.new()
	cross.set_anchors_and_offsets_preset(Control.PRESET_CENTER)
	cross.mouse_filter = Control.MOUSE_FILTER_IGNORE
	for offset in [Vector2(-14,-1),Vector2(6,-1),Vector2(-1,-14),Vector2(-1,6)]:
		var line = ColorRect.new()
		line.color = Color(CYAN,0.9)
		line.mouse_filter = Control.MOUSE_FILTER_IGNORE
		line.position = offset
		line.size = Vector2(8,2) if offset.y == -1 else Vector2(2,8)
		cross.add_child(line)
	var dot = ColorRect.new()
	dot.color = WHITE
	dot.position = Vector2(-1,-1)
	dot.size = Vector2(2,2)
	dot.mouse_filter = Control.MOUSE_FILTER_IGNORE
	cross.add_child(dot)
	hud.add_child(cross)
	toast = _label("",22,ORANGE)
	toast.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	toast.anchor_left = 0.5
	toast.anchor_right = 0.5
	toast.offset_left = -360
	toast.offset_right = 360
	toast.anchor_top = 0.5
	toast.anchor_bottom = 0.5
	toast.offset_top = 60
	toast.offset_bottom = 100
	toast.add_theme_color_override("font_outline_color",Color(0,0,0,0.8))
	toast.add_theme_constant_override("outline_size",6)
	toast.mouse_filter = Control.MOUSE_FILTER_IGNORE
	hud.add_child(toast)

func _clean_models():
	for v in visuals.values(): v.queue_free()
	for v in enemy_visuals.values(): v.queue_free()
	visuals.clear()
	enemy_visuals.clear()
	for fx in effects:
		if is_instance_valid(fx[0]): fx[0].queue_free()
	effects.clear()

func _input(event):
	if dedicated: return
	if event is InputEventMouseButton and event.button_index == MOUSE_BUTTON_LEFT:
		if not event.pressed:
			release_fire_to_resume = false
		elif mode == "playing" and not modal_open and Input.mouse_mode != Input.MOUSE_MODE_CAPTURED:
			Input.mouse_mode = Input.MOUSE_MODE_CAPTURED
			release_fire_to_resume = true
			get_viewport().set_input_as_handled()
			return
	if event is InputEventMouseMotion and mode == "playing" and not modal_open and Input.mouse_mode == Input.MOUSE_MODE_CAPTURED:
		yaw = wrapf(yaw-event.screen_relative.x*LOOK_SENSITIVITY,-PI,PI)
		pitch = clampf(pitch-event.screen_relative.y*LOOK_SENSITIVITY,-LOOK_LIMIT,LOOK_LIMIT)
		get_viewport().set_input_as_handled()
	if event is InputEventKey and event.pressed and not event.echo and event.physical_keycode == KEY_V and mode == "playing" and not modal_open:
		third_person = not third_person
		get_viewport().set_input_as_handled()
	if event is InputEventKey and event.pressed and not event.echo and event.keycode == KEY_ESCAPE:
		if mode == "playing" and not modal_open:
			get_viewport().set_input_as_handled()
			modal_open = true
			Input.mouse_mode = Input.MOUSE_MODE_VISIBLE
			var popup = ConfirmationDialog.new()
			popup.title = "離開任務？"
			popup.dialog_text = "離開將結束本次任務。連線玩家可先取消繼續戰鬥。"
			popup.ok_button_text = "返回主頁"
			popup.cancel_button_text = "繼續戰鬥"
			popup.confirmed.connect(func():
				if network == "client": _disconnect("已離開伺服器")
				else: _return_home()
				popup.queue_free())
			popup.canceled.connect(func(): modal_open = false; Input.mouse_mode = Input.MOUSE_MODE_CAPTURED; popup.queue_free())
			screen.add_child(popup)
			popup.popup_centered()

func _notification(what):
	if what == NOTIFICATION_APPLICATION_FOCUS_OUT and mode == "playing":
		Input.mouse_mode = Input.MOUSE_MODE_VISIBLE

func _look_direction() -> Vector3:
	return Vector3.FORWARD.rotated(Vector3.RIGHT,pitch).rotated(Vector3.UP,yaw)

func _process(dt):
	if mode == "playing": _render_models(dt)
	_update_camera(dt)

func _physics_process(dt):
	clock_time += dt
	if mode == "playing":
		if actors.has(local_id):
			local_previous_position = actors[local_id].p
			local_position_initialized = true
		if not dedicated:
			var active = not modal_open and Input.mouse_mode == Input.MOUSE_MODE_CAPTURED
			var move = Input.get_vector("left","right","forward","back") if active else Vector2.ZERO
			var eye: Vector3 = actors[local_id].p+Vector3.UP*EYE_HEIGHT if actors.has(local_id) else camera.position
			var target = (camera.global_position if third_person else eye)+_look_direction()*SHOT_RANGE
			var packet = {"m":move,"yaw":yaw,"aim":target,"reload":Input.is_action_pressed("reload") and active,"fire":Input.is_mouse_button_pressed(MOUSE_BUTTON_LEFT) and active and not release_fire_to_resume,"dash":Input.is_action_pressed("dash") and active,"jump":Input.is_action_pressed("jump") and active}
			if network == "client":
				input_sequence += 1
				packet["seq"] = input_sequence
				_receive_input.rpc_id(1, packet)
				if actors.has(local_id) and not modal_open:
					pending_movement.append({"packet":packet,"dt":dt})
					if pending_movement.size() > 120: pending_movement.pop_front()
					_move_actor(actors[local_id],packet,dt)
			else: inputs[1] = packet
		if network != "client":
			_simulate(dt)
		_update_hud()
	if network == "host":
		sync_time += dt
		if sync_time >= 0.05:
			sync_time = 0
			_broadcast_state()
	for i in range(effects.size()-1,-1,-1):
		effects[i][1] -= dt
		if effects[i][1] <= 0:
			effects[i][0].queue_free()
			effects.remove_at(i)
	if smoke_test: _smoke_tick()
	if "--capture" in OS.get_cmdline_user_args() and clock_time > 2 and not captured:
		captured = true
		_capture.call_deferred()
	if "--network-test" in OS.get_cmdline_user_args(): _network_tick()

@rpc("any_peer", "call_remote", "unreliable_ordered")
func _receive_input(packet: Dictionary):
	if not multiplayer.is_server(): return
	var id = multiplayer.get_remote_sender_id()
	if not actors.has(id): return
	if not packet.get("m") is Vector2 or not packet.get("aim") is Vector3: return
	if not packet["m"].is_finite() or not packet["aim"].is_finite(): return
	if not packet.get("seq",0) is int: return
	if int(packet.get("seq",0)) < int(inputs.get(id,{}).get("seq",0)): return
	packet["received_at"] = clock_time
	inputs[id] = packet

func _broadcast_state():
	var ids = PackedInt32Array()
	var a_data = PackedFloat32Array()
	var e_data = PackedFloat32Array()
	for id in actors:
		ids.append(id)
		var a = actors[id]
		var dash_direction: Vector3 = a.get("dash_direction",Vector3.ZERO)
		a_data.append_array(PackedFloat32Array([a.p.x,a.p.y,a.p.z,a.hp,a.cd,a.dash,a.y,a.v,a.get("max_hp",100),a.get("speed",6.5),a.get("fire_interval",0.16),a.get("ack",0),a.get("dash_left",0),dash_direction.x,dash_direction.y,dash_direction.z,a.get("ammo",MAG_SIZE),a.get("mag",MAG_SIZE),float(a.get("reload",0))/maxf(0.01,float(a.get("reload_time",RELOAD_TIME)))]))
	for id in enemies:
		var e = enemies[id]
		e_data.append_array(PackedFloat32Array([id,e.p.x,e.p.y,e.p.z,e.hp,e.y,1.0 if e.boss else 0.0,e.get("max_hp",e.hp)]))
	_snapshot.rpc(ids,a_data,e_data,wave,kills,elapsed)

@rpc("authority", "call_remote", "unreliable_ordered")
func _snapshot(ids: PackedInt32Array, a: PackedFloat32Array, e: PackedFloat32Array, w: int, k: int, t: float):
	var stride = 19 if a.size() == ids.size()*19 else 16
	if a.size() != ids.size()*stride or e.size()%8 != 0: return
	var old_position: Vector3 = actors[local_id].p if actors.has(local_id) else Vector3.ZERO
	var had_local = actors.has(local_id)
	actors.clear()
	for i in range(ids.size()):
		var j = i*stride
		actors[ids[i]] = {"p":Vector3(a[j],a[j+1],a[j+2]),"hp":a[j+3],"cd":a[j+4],"dash":a[j+5],"y":a[j+6],"v":a[j+7],"max_hp":a[j+8],"speed":a[j+9],"fire_interval":a[j+10],"ack":int(a[j+11]),"dash_left":a[j+12],"dash_direction":Vector3(a[j+13],a[j+14],a[j+15])}
		if stride == 19:
			actors[ids[i]].merge({"ammo":int(a[j+16]),"mag":int(a[j+17]),"reload_ratio":a[j+18]},true)
	if actors.has(local_id):
		var local_actor = actors[local_id]
		while not pending_movement.is_empty() and int(pending_movement[0].packet.seq) <= local_actor.ack:
			pending_movement.pop_front()
		if mode == "playing" and not modal_open:
			for step in pending_movement: _move_actor(local_actor,step.packet,step.dt)
		if had_local and old_position.distance_to(local_actor.p) < 5.0:
			var correction: Vector3 = old_position-local_actor.p
			camera_correction += correction
			local_previous_position -= correction
		else:
			local_previous_position = local_actor.p
			camera_correction = Vector3.ZERO
		local_position_initialized = true
	enemies.clear()
	for j in range(0,e.size(),8):
		enemies[int(e[j])] = {"p":Vector3(e[j+1],e[j+2],e[j+3]),"hp":e[j+4],"y":e[j+5],"boss":e[j+6]>0,"max_hp":e[j+7]}
	wave = w
	kills = k
	elapsed = t

func _move_actor(a: Dictionary, packet: Dictionary, dt: float):
	a.cd = maxf(0,a.cd-dt)
	a.dash = maxf(0,a.dash-dt)
	if a.hp <= 0: return
	var mv: Vector2 = packet.get("m",Vector2.ZERO).limit_length(1)
	var rotation_y = float(packet.get("yaw",0))
	if not is_finite(rotation_y): rotation_y = 0
	var motion = Vector3(mv.x,0,mv.y).rotated(Vector3.UP,rotation_y)
	if packet.get("dash",false) and a.dash <= 0 and mv.length()>0:
		a.dash = DASH_COOLDOWN
		a["dash_left"] = DASH_DURATION
		a["dash_direction"] = motion.normalized()
	var dash_left = float(a.get("dash_left",0))
	if dash_left > 0:
		var next_left = maxf(0,dash_left-dt)
		var start = 1.0-dash_left/DASH_DURATION
		var finish = 1.0-next_left/DASH_DURATION
		var distance = DASH_DISTANCE*(smoothstep(0.0,1.0,finish)-smoothstep(0.0,1.0,start))
		a.p += a.get("dash_direction",Vector3.ZERO)*distance
		a["dash_left"] = next_left
	if packet.get("jump",false) and a.p.y <= 0.01: a.v = 7.0
	a.v -= 20*dt
	a.p += motion*float(a.get("speed",6.5))*dt
	a.p.y = maxf(0,a.p.y+a.v*dt)
	a.p.x = clampf(a.p.x,-21,21)
	a.p.z = clampf(a.p.z,-21,21)
	a.y = rotation_y

func _simulate(dt: float):
	elapsed += dt
	for id in actors:
		var a = actors[id]
		var p: Dictionary = inputs.get(id,{})
		if network == "host" and id != 1 and clock_time-float(p.get("received_at",clock_time)) > 0.25:
			p = {"seq":p.get("seq",0),"yaw":a.y}
		_move_actor(a,p,dt)
		a["ack"] = int(p.get("seq",0))
		if a.hp <= 0: continue
		_update_ammo(a,p,dt)
		if p.get("fire",false) and a.cd <= 0 and float(a.get("reload",0)) <= 0:
			if int(a.get("ammo",MAG_SIZE)) > 0:
				a.cd = float(a.get("fire_interval",0.16))
				a["ammo"] = int(a.get("ammo",MAG_SIZE))-1
				_fire(id, p.get("aim", a.p+Vector3(0,1,-30)))
				if a.ammo <= 0: _start_reload(a)
			else: _start_reload(a)
	var alive = []
	for id in actors:
		if actors[id].hp > 0: alive.append(id)
	if alive.is_empty() and not actors.is_empty():
		_finish(false)
		return
	for id in enemies.keys():
		var e = enemies[id]
		var closest = -1
		var distance = INF
		for pid in alive:
			var d = e.p.distance_to(actors[pid].p)
			if d < distance:
				distance = d
				closest = pid
		if closest == -1: continue
		var dest: Vector3 = actors[closest].p
		var toward: Vector3 = dest-e.p
		toward.y = 0
		e.y = atan2(-toward.x,-toward.z)
		if distance > 1.4:
			e.p += toward.normalized()*e.speed*dt
		e.cd = maxf(0,e.cd-dt)
		if distance < (2.4 if e.boss else 1.7) and e.cd <= 0:
			e.cd = 0.85
			actors[closest].hp = maxf(0,actors[closest].hp-float(e.get("damage",20 if e.boss else 10)))
	if enemies.is_empty():
		wave_delay -= dt
		if wave_delay <= 0:
			if wave >= MISSIONS[selected][4]:
				_finish(true)
			else:
				wave += 1
				_spawn_wave()
				wave_delay = 2.0

func _start_reload(a: Dictionary):
	if float(a.get("reload",0)) > 0 or int(a.get("ammo",MAG_SIZE)) >= int(a.get("mag",MAG_SIZE)): return
	a["reload"] = float(a.get("reload_time",RELOAD_TIME))

func _update_ammo(a: Dictionary, p: Dictionary, dt: float):
	var r = float(a.get("reload",0))
	if r > 0:
		r = maxf(0,r-dt)
		a["reload"] = r
		if r <= 0: a["ammo"] = int(a.get("mag",MAG_SIZE))
	elif p.get("reload",false): _start_reload(a)

func _spawn_wave():
	var count = mini(24,MISSIONS[selected][5]+ceili(wave/2.0)+maxi(0,actors.size()-1)*2)
	for i in range(count):
		var angle = TAU*i/count+wave*0.7
		var boss = wave in [10,20] and i == 0
		enemies[next_enemy] = {"p":Vector3(sin(angle)*19,0,cos(angle)*19),"hp":500.0 if boss else 55.0+selected*10,"speed":1.5 if boss else 2.1+selected*0.3,"cd":0.6,"y":0.0,"boss":boss}
		enemies[next_enemy]["max_hp"] = enemies[next_enemy].hp
		next_enemy += 1

func _fire(id: int, target: Vector3):
	var origin: Vector3 = actors[id].p+Vector3.UP*EYE_HEIGHT
	var direction = (target-origin).normalized()
	var best = -1
	var nearest = SHOT_RANGE
	for eid in enemies:
		var center: Vector3 = enemies[eid].p+Vector3(0,1.0,0)
		var along = (center-origin).dot(direction)
		var radius = 1.4 if enemies[eid].boss else 0.85
		if along > 0 and along < nearest and center.distance_to(origin+direction*along)<radius:
			best = eid
			nearest = along
	var end = origin+direction*nearest
	if best != -1:
		enemies[best].hp -= float(actors[id].get("damage",28))
		if enemies[best].hp <= 0:
			kills += 1
			enemies.erase(best)
	shots += 1
	if network == "host": _shot_fx.rpc(origin,end,best != -1,id)
	_shot_fx(origin,end,best != -1,id)

@rpc("authority", "call_remote", "unreliable")
func _shot_fx(origin: Vector3, end: Vector3, hit: bool, shooter: int):
	if dedicated: return
	if shooter == local_id and is_instance_valid(muzzle): origin = muzzle.global_position
	var length = origin.distance_to(end)
	var beam = _cube(self,(origin+end)/2,Vector3(0.045,0.045,maxf(length,0.01)),CYAN,3)
	if length > 0.01: beam.look_at(end)
	effects.append([beam,0.075])
	if hit:
		var ball = SphereMesh.new()
		ball.radius = 0.2
		ball.height = 0.4
		var spark = _mesh(self,ball,end,_mat(ORANGE,3))
		effects.append([spark,0.12])
	if shooter == local_id:
		shake = 0.035
		_tone(740 if hit else 360,0.045,0.1)

func _tone(freq: float, duration: float, volume: float):
	if dedicated or DisplayServer.get_name() == "headless": return
	audio.stop()
	var wav = AudioStreamWAV.new()
	wav.format = AudioStreamWAV.FORMAT_16_BITS
	wav.mix_rate = 22050
	var data = PackedByteArray()
	var count = int(22050*duration)
	data.resize(count*2)
	for i in range(count):
		var sample = int(sin(TAU*freq*i/22050.0)*volume*32767*(1.0-float(i)/count))
		data.encode_s16(i*2,sample)
	wav.data = data
	audio.stream = wav
	audio.play()

func _render_models(dt: float):
	if dedicated: return
	for id in actors:
		if not visuals.has(id):
			visuals[id] = _actor_model(CYAN if id == local_id else Color("82aaff"))
			add_child(visuals[id])
			visuals[id].position = actors[id].p
		var v = visuals[id]
		v.position = v.position.lerp(actors[id].p,1.0-exp(-20.0*dt))
		v.rotation.y = actors[id].y
		v.rotation.x = -0.18*_dash_animation_strength(actors[id])
		v.visible = actors[id].hp > 0 and (id != local_id or third_person)
	for id in visuals.keys():
		if not actors.has(id):
			visuals[id].queue_free()
			visuals.erase(id)
	for id in enemies:
		if not enemy_visuals.has(id):
			enemy_visuals[id] = _actor_model(ORANGE,enemies[id].boss)
			add_child(enemy_visuals[id])
			enemy_visuals[id].position = enemies[id].p
		var v = enemy_visuals[id]
		v.position = v.position.lerp(enemies[id].p,1.0-exp(-14.0*dt))
		v.rotation.y = enemies[id].y
	for id in enemy_visuals.keys():
		if not enemies.has(id):
			var v = enemy_visuals[id]
			var ring = SphereMesh.new()
			ring.radius = 0.6
			ring.height = 1.2
			var burst = _mesh(self,ring,v.position+Vector3.UP,_mat(ORANGE,2))
			effects.append([burst,0.12])
			v.queue_free()
			enemy_visuals.erase(id)

func _update_camera(dt: float):
	if dedicated: return
	weapon.visible = mode == "playing" and actors.has(local_id) and actors[local_id].hp > 0 and not third_person
	if mode == "playing" and actors.has(local_id):
		var position: Vector3 = actors[local_id].p
		if not local_position_initialized:
			local_previous_position = position
			local_position_initialized = true
		var blend = clampf(Engine.get_physics_interpolation_fraction(),0.0,1.0)
		camera_correction *= exp(-18.0*dt)
		camera.position = local_previous_position.lerp(position,blend)+camera_correction+Vector3.UP*EYE_HEIGHT
		camera.rotation = Vector3(pitch,yaw,0)
		if third_person:
			camera.position += Basis.from_euler(Vector3(pitch,yaw,0))*THIRD_PERSON_OFFSET
			camera.position.y = maxf(0.4,camera.position.y)
		# Recoil moves only the view model, preserving the centered aim ray.
		shake = maxf(0,shake-dt)
		var animation_offset = blend/float(Engine.physics_ticks_per_second)
		var strength = _dash_animation_strength(actors[local_id],animation_offset) if weapon.visible and not modal_open else 0.0
		camera.fov = BASE_FOV+DASH_FOV_BOOST*strength
		weapon.position = WEAPON_REST_POSITION+Vector3(0.02,-0.045,0.10)*strength+Vector3(0,0,shake)
		weapon.rotation = Vector3(-0.10,0.04,-0.10)*strength
	else:
		camera.fov = BASE_FOV
		weapon.position = WEAPON_REST_POSITION
		weapon.rotation = Vector3.ZERO
		camera.position = Vector3(16+sin(clock_time*0.08)*2,11,19)
		camera.look_at(Vector3(0,2,-6))

func _dash_animation_strength(actor: Dictionary, time_offset: float = 0.0) -> float:
	if float(actor.get("hp",0)) <= 0: return 0.0
	var cooldown = float(actor.get("dash",0))
	if cooldown <= 0: return 0.0
	var elapsed_dash = DASH_COOLDOWN-cooldown+time_offset
	if elapsed_dash < 0 or elapsed_dash >= DASH_VISUAL_DURATION: return 0.0
	return sin(PI*elapsed_dash/DASH_VISUAL_DURATION)

func _update_hud():
	if dedicated or not is_instance_valid(hud): return
	objective_label.text = "波次 %d / %d    ·    剩餘 %d    ·    擊破 %d\n%s" % [wave,MISSIONS[selected][4],enemies.size(),kills,_format_time(elapsed)]
	if actors.has(local_id):
		var a = actors[local_id]
		var max_hp = float(a.get("max_hp",100))
		health_label.text = "護盾  %d / %d" % [a.hp,max_hp]
		health_label.modulate = ORANGE if a.hp <= max_hp*0.3 else Color.WHITE
		if is_instance_valid(health_bar):
			health_bar.max_value = max_hp
			health_bar.value = a.hp
			health_bar.modulate = ORANGE if a.hp <= max_hp*0.3 else Color.WHITE
			dash_bar.value = DASH_COOLDOWN-a.dash
		dash_label.text = "Shift 衝刺就緒" if a.dash <= 0 else "衝刺冷卻  %.1f s" % a.dash
		if is_instance_valid(ammo_label):
			var ammo = int(a.get("ammo",MAG_SIZE))
			var mag = int(a.get("mag",MAG_SIZE))
			var ratio = float(a.get("reload_ratio",float(a.get("reload",0))/float(a.get("reload_time",RELOAD_TIME))))
			ammo_label.text = "換彈中" if ratio > 0 else str(ammo)
			ammo_label.add_theme_font_size_override("font_size",26 if ratio > 0 else 40)
			ammo_label.modulate = ORANGE if ratio > 0 or ammo <= mag/5 else Color.WHITE
			reserve_label.text = "/ %d" % mag
			reload_bar.value = 1.0-ratio if ratio > 0 else float(ammo)/maxf(1,mag)
			view_label.text = "R 換彈   ·   V %s" % ("第三人稱" if third_person else "第一人稱")
		toast.text = "你已倒下 · 隊友仍可完成任務" if a.hp <= 0 else ("點擊畫面恢復滑鼠瞄準" if not modal_open and Input.mouse_mode != Input.MOUSE_MODE_CAPTURED else ("下一波即將抵達" if enemies.is_empty() else ""))

func _finish(success: bool):
	if mode != "playing": return
	result_success = success
	if network == "host": _finish_remote.rpc(success,elapsed,kills,selected)
	_finish_remote(success,elapsed,kills,selected)

@rpc("authority", "call_remote", "reliable")
func _finish_remote(success: bool, duration: float, defeated: int, index: int):
	selected = index
	mode = "result"
	result_success = success
	if network == "client" and "--network-test" in OS.get_cmdline_user_args():
		assert(success)
		print("NETWORK_CLIENT_RESULT_PASS")
		net_test_phase = 2
	print("MISSION_RESULT success=", success, " mission=", selected, " kills=", defeated)
	if success:
		unlocked = maxi(unlocked,mini(2,selected+1))
		_save_progress()
	if dedicated: return
	Input.mouse_mode = Input.MOUSE_MODE_VISIBLE
	_clear_ui()
	var bg = ColorRect.new()
	bg.color = Color(0.02,0.04,0.07,0.88)
	bg.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	screen.add_child(bg)
	var center = CenterContainer.new()
	center.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	screen.add_child(center)
	var card = _panel(center,CYAN if success else ORANGE,Color(0.04,0.08,0.13,0.96))
	card.mouse_filter = Control.MOUSE_FILTER_PASS
	card.custom_minimum_size.x = 560
	var v = VBoxContainer.new()
	v.add_theme_constant_override("separation",14)
	card.add_child(v)
	v.add_child(_label("MISSION COMPLETE" if success else "SIGNAL LOST",18,CYAN if success else ORANGE))
	v.add_child(_label("任務完成" if success else "任務失敗",48))
	v.add_child(_label(MISSIONS[selected][1],24))
	v.add_child(_label("擊破敵人  %d     /     作戰時間  %s" % [defeated,_format_time(duration)],20))
	v.add_child(_label("下一個部署地點已解鎖" if success and selected<2 else ("裂隙已封鎖。感謝你的奮戰。" if success else "調整走位，保持距離，再試一次。"),18,CYAN))
	_button("返回主頁",_request_home,v,54)
	_button("再次挑戰",_request_start,v,48)

func _request_home():
	if network == "client": _home_request.rpc_id(1)
	else: _return_home()

@rpc("any_peer", "call_remote", "reliable")
func _home_request():
	if multiplayer.is_server() and mode == "result": _return_home()

func _return_home():
	enemies.clear()
	inputs.clear()
	if network == "host": _back_home.rpc()
	_back_home()

@rpc("authority", "call_remote", "reliable")
func _back_home():
	_clean_models()
	if dedicated: mode = "home"
	else: _home()
	print("RETURNED_HOME")

func _format_time(t: float) -> String:
	return "%02d:%02d" % [int(t)/60,int(t)%60]

func _save_progress():
	if smoke_test or "--network-test" in OS.get_cmdline_user_args(): return
	var cfg = ConfigFile.new()
	cfg.set_value("progress","unlocked",unlocked)
	cfg.save("user://progress.cfg")

func _load_progress():
	if smoke_test or "--network-test" in OS.get_cmdline_user_args(): return
	var cfg = ConfigFile.new()
	if cfg.load("user://progress.cfg") == OK:
		unlocked = clampi(int(cfg.get_value("progress","unlocked",0)),0,2)

func _smoke_tick():
	# Deterministic integration checks use the real combat/transition methods.
	if smoke_phase == 0 and clock_time > 0.5:
		assert(mode == "playing" and actors.has(1))
		_spawn_wave()
		var eid = enemies.keys()[0]
		var aim: Vector3 = enemies[eid].p+Vector3(0,1,0)
		_fire(1,aim)
		_fire(1,aim)
		_fire(1,aim)
		assert(kills >= 1)
		actors[1].hp = 0
		smoke_phase = 1
	elif smoke_phase == 1 and mode == "result":
		assert(not result_success)
		_return_home()
		assert(mode == "home")
		_start_mission(0)
		enemies.clear()
		wave = MISSIONS[selected][4]
		wave_delay = 0
		smoke_phase = 2
	elif smoke_phase == 2 and mode == "result":
		assert(result_success and unlocked >= 1)
		_return_home()
		assert(mode == "home")
		print("SMOKE_PASS combat_damage defeat victory unlock return_home")
		get_tree().quit()

func _capture():
	await RenderingServer.frame_post_draw
	var output = "res://combat-preview.png" if mode == "playing" else "res://home-preview.png"
	get_viewport().get_texture().get_image().save_png(output)
	print("CAPTURE_SAVED ",output)
	get_tree().quit()

func _network_tick():
	if network == "host" and net_test_phase == 0 and actors.size() >= 2:
		net_test_phase = 1
		_start_mission(0)
	if network == "host" and net_test_phase == 1 and elapsed > 3:
		assert(actors.size() >= 2)
		print("NETWORK_SERVER_PASS players=",actors.size()," inputs=",inputs.size())
		_finish(true)
		net_test_phase = 2
	if network == "client" and mode == "playing" and actors.has(local_id):
		if net_test_phase == 0:
			print("NETWORK_CLIENT_SNAPSHOT_PASS actors=",actors.size()," enemies=",enemies.size())
			net_test_phase = 1
	if network == "client" and mode == "result" and net_test_phase == 2:
		assert(result_success)
		_request_home()
		net_test_phase = 3
	if network == "client" and mode == "home" and net_test_phase >= 2:
		print("NETWORK_CLIENT_RETURN_PASS")
		get_tree().quit()
	if network == "host" and net_test_phase == 2 and mode == "home" and actors.is_empty():
		print("NETWORK_SERVER_RETURN_PASS")
		get_tree().quit()
	if clock_time > 25:
		push_error("NETWORK_TEST_TIMEOUT")
		get_tree().quit(1)


func _choose_mission(index: int):
	if network == "client":
		if local_id != leader_id:
			status_label.text = "由隊長選擇關卡"
			return
		_choose_request.rpc_id(1,index)
	else:
		_select(index)
		if network == "host": _lobby_state.rpc(selected,unlocked,leader_id)

@rpc("any_peer", "call_remote", "reliable")
func _choose_request(index: int):
	if multiplayer.is_server() and multiplayer.get_remote_sender_id() == leader_id and mode == "home":
		selected = clampi(index,0,unlocked)
		_lobby_state.rpc(selected,unlocked,leader_id)

@rpc("authority", "call_remote", "reliable")
func _lobby_state(index: int, progress: int, leader: int):
	selected = index
	unlocked = maxi(unlocked,progress)
	leader_id = leader
	if mode == "home": _home()

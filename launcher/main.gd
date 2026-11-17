extends Control

const CYAN = Color("66f5d0")
const ORANGE = Color("ff8855")
const INK = Color("101c2b")
const WHITE = Color("e8f3ff")
const MAX_PACK_SIZE = 512 * 1024 * 1024
const UPDATE_DIR = "user://updates"
const PART_PATH = UPDATE_DIR + "/download.part"
var origin = "http://192.168.213.100:24771"
var manifest: Dictionary = {}
var request: HTTPRequest
var status: Label
var detail: Label
var progress: ProgressBar
var retry: Button
var busy = false
var downloading = false

func _ready() -> void:
	TranslationServer.set_locale("zh_TW")
	_build_ui()
	_load_address()
	request = HTTPRequest.new()
	request.timeout = 45.0
	request.max_redirects = 0
	request.accept_gzip = false
	add_child(request)
	request.request_completed.connect(_completed)
	_check()

func _build_ui() -> void:
	var theme_new = Theme.new()
	if ResourceLoader.exists("res://ui-font.ttf"):
		theme_new.default_font = load("res://ui-font.ttf")
	theme_new.default_font_size = 20
	theme_new.set_color("font_color", "Label", WHITE)
	theme_new.set_color("font_color", "Button", WHITE)
	for state in ["normal", "hover", "pressed", "focus"]:
		var box = StyleBoxFlat.new()
		box.bg_color = INK.lightened(0.12 if state == "hover" else 0.05)
		box.border_color = CYAN
		box.set_border_width_all(2)
		box.set_corner_radius_all(8)
		theme_new.set_stylebox(state, "Button", box)
	self.theme = theme_new
	var backdrop = ColorRect.new()
	backdrop.color = INK
	backdrop.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	add_child(backdrop)
	var center = CenterContainer.new()
	center.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	add_child(center)
	var column = VBoxContainer.new()
	column.custom_minimum_size = Vector2(640, 0)
	column.add_theme_constant_override("separation", 24)
	center.add_child(column)
	var title = Label.new()
	title.text = "RIFTWARD / BETA"
	title.add_theme_font_size_override("font_size", 44)
	title.add_theme_color_override("font_color", CYAN)
	column.add_child(title)
	status = Label.new()
	status.text = "正在確認最新版本…"
	status.add_theme_font_size_override("font_size", 28)
	column.add_child(status)
	detail = Label.new()
	detail.custom_minimum_size = Vector2(640, 70)
	detail.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	column.add_child(detail)
	progress = ProgressBar.new()
	progress.custom_minimum_size.y = 16
	progress.show_percentage = false
	column.add_child(progress)
	var row = HBoxContainer.new()
	row.add_theme_constant_override("separation", 16)
	column.add_child(row)
	retry = Button.new()
	retry.text = "重試更新"
	retry.custom_minimum_size = Vector2(200, 56)
	retry.pressed.connect(_check)
	row.add_child(retry)
	var close = Button.new()
	close.text = "離開"
	close.custom_minimum_size = Vector2(160, 56)
	close.pressed.connect(func(): get_tree().quit())
	row.add_child(close)

func _load_address() -> void:
	var config = ConfigFile.new()
	var path = OS.get_executable_path().get_base_dir().path_join("server.cfg")
	if not FileAccess.file_exists(path):
		return
	var error = config.load(path)
	if error != OK:
		origin = ""
		return
	var address = str(config.get_value("server", "address", "192.168.213.100")).strip_edges()
	var regex = RegEx.new()
	regex.compile("^[A-Za-z0-9][A-Za-z0-9.-]{0,252}$")
	if regex.search(address) == null:
		origin = ""
		return
	origin = "http://%s:24771" % address

func _check() -> void:
	if busy:
		return
	if origin.is_empty():
		_fail("無法讀取 server.cfg，或伺服器位址格式無效。")
		return
	busy = true
	downloading = false
	retry.disabled = true
	status.add_theme_color_override("font_color", WHITE)
	status.text = "正在確認最新版本…"
	detail.text = "連線至更新伺服器；驗證完成後自動啟動。"
	progress.value = 0
	request.download_file = ""
	request.body_size_limit = 65536
	var error = request.request(origin + "/api/version")
	if error != OK:
		_fail("無法送出版本請求（%s）。" % error_string(error))

func validate_manifest(value: Variant) -> String:
	if not value is Dictionary:
		return "版本資料不是物件。"
	if not value.get("ok") is bool or value.get("ok") != true:
		return "伺服器尚未提供更新。"
	if not (value.get("protocol") is int or value.get("protocol") is float) or value.get("protocol") != 1:
		return "更新協定不受支援。"
	for key in ["version", "sha256", "download"]:
		if not value.get(key) is String:
			return "版本資料缺少必要欄位。"
	var version_regex = RegEx.new()
	version_regex.compile("^[0-9]+\\.[0-9]+\\.[0-9]+(?:-[A-Za-z0-9]+(?:[.-][A-Za-z0-9]+)*)?$")
	if value.version.length() > 80 or version_regex.search(value.version) == null:
		return "版本名稱格式無效。"
	var hash_regex = RegEx.new()
	hash_regex.compile("^[0-9a-f]{64}$")
	if hash_regex.search(value.sha256) == null:
		return "檔案指紋格式無效。"
	if value.download != "/updates/" + value.sha256 + ".pck":
		return "下載路徑無效。"
	var size_value = value.get("size")
	if not (size_value is int or size_value is float):
		return "檔案大小無效。"
	if size_value < 1 or size_value > MAX_PACK_SIZE or float(size_value) != floor(float(size_value)):
		return "檔案大小超出限制。"
	return ""

func verify_pack(path: String, expected_size: int, expected_hash: String) -> String:
	var file = FileAccess.open(path, FileAccess.READ)
	if file == null:
		return "無法開啟更新檔（%s）。" % error_string(FileAccess.get_open_error())
	var size = file.get_length()
	file.close()
	if size != expected_size:
		return "更新檔大小不符。"
	if FileAccess.get_sha256(path) != expected_hash:
		return "更新檔 SHA-256 驗證失敗。"
	return ""

func _completed(result: int, code: int, _headers: PackedStringArray, body: PackedByteArray) -> void:
	if result != HTTPRequest.RESULT_SUCCESS or code != 200:
		_fail("更新連線失敗（HTTP %d / %d）。請確認網路及伺服器。" % [code, result])
		return
	if downloading:
		downloading = false
		status.text = "正在驗證下載檔案…"
		var problem = verify_pack(PART_PATH, int(manifest.size), manifest.sha256)
		if not problem.is_empty():
			_fail(problem)
			return
		var destination = _cache_path()
		var error = DirAccess.rename_absolute(ProjectSettings.globalize_path(PART_PATH), ProjectSettings.globalize_path(destination))
		if error != OK:
			_fail("無法安裝更新（%s）。" % error_string(error))
			return
		_launch(destination)
		return
	var decoded = JSON.parse_string(body.get_string_from_utf8())
	var problem = validate_manifest(decoded)
	if not problem.is_empty():
		_fail(problem)
		return
	manifest = decoded
	var cached = _cache_path()
	if FileAccess.file_exists(cached) and verify_pack(cached, int(manifest.size), manifest.sha256).is_empty():
		_launch(cached)
		return
	var bundled = OS.get_executable_path().get_base_dir().path_join("game/RiftWardGame.pck")
	if FileAccess.file_exists(bundled) and verify_pack(bundled, int(manifest.size), manifest.sha256).is_empty():
		_launch(bundled)
		return
	var error = DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(UPDATE_DIR))
	if error != OK:
		_fail("無法建立更新資料夾（%s）。" % error_string(error))
		return
	if FileAccess.file_exists(cached):
		error = DirAccess.remove_absolute(ProjectSettings.globalize_path(cached))
		if error != OK:
			_fail("無法移除損壞的快取（%s）。" % error_string(error))
			return
	request.download_file = PART_PATH
	request.body_size_limit = int(manifest.size)
	downloading = true
	status.text = "正在下載 %s" % manifest.version
	error = request.request(origin + manifest.download)
	if error != OK:
		_fail("無法開始下載（%s）。" % error_string(error))

func _cache_path() -> String:
	return UPDATE_DIR.path_join("%s-%s.pck" % [manifest.version, manifest.sha256])

func _process(_delta: float) -> void:
	if downloading and request != null:
		var count = request.get_downloaded_bytes()
		progress.max_value = int(manifest.size)
		progress.value = count
		detail.text = "%.1f / %.1f MB · 下載後將驗證 SHA-256" % [count / 1048576.0, int(manifest.size) / 1048576.0]

func _launch(path: String) -> void:
	status.text = "驗證完成，正在啟動…"
	detail.text = "版本 %s" % manifest.version
	var process_id = OS.create_process(OS.get_executable_path(), PackedStringArray(["--main-pack", ProjectSettings.globalize_path(path)]))
	if process_id <= 0:
		_fail("無法啟動遊戲。請確認執行檔仍在原位置。")
		return
	get_tree().quit()

func _fail(message: String) -> void:
	busy = false
	downloading = false
	status.text = "更新未完成"
	status.add_theme_color_override("font_color", ORANGE)
	detail.text = message
	retry.disabled = false
	retry.grab_focus()

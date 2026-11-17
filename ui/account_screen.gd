extends Control

signal authenticated(token: String, profile: Dictionary)

const FORM_WIDTH = 480
const FIELD_HEIGHT = 48
const SURFACE = Color(0.025, 0.05, 0.09, 0.97)

var _host: Node
var _api_base = ""
var _registering = true
var _busy = false
var _finished = false
var _request: HTTPRequest
var _heading: Label
var _username: LineEdit
var _password: LineEdit
var _confirmation: LineEdit
var _confirmation_label: Label
var _submit_button: Button
var _switch_button: Button
var _status: Label

func setup(host: Node, api_base: String):
	_host = host
	_api_base = api_base.trim_suffix("/")
	set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	host._backdrop(self, 0.9, 0.55)
	var center = CenterContainer.new()
	center.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	add_child(center)
	var card: PanelContainer = host._panel(center, host.CYAN, Color(0.04, 0.08, 0.13, 0.95))
	card.mouse_filter = Control.MOUSE_FILTER_PASS
	var form: VBoxContainer = host._box(card, Vector2.ZERO, FORM_WIDTH)
	form.add_theme_constant_override("separation", 8)
	form.add_child(host._label("RIFTWARD  /  BETA", 18, host.CYAN))
	_heading = host._label("建立遊戲帳號", 32)
	form.add_child(_heading)
	var explanation: Label = host._label("請先註冊或登入，再進入遊戲。\n關卡進度將儲存在你的帳號中。", 16)
	explanation.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	form.add_child(explanation)
	form.add_child(host._label("使用者名稱", 16))
	_username = _field(form, "3–24 個英文字母、數字或底線", false)
	_username.max_length = 24
	form.add_child(host._label("密碼", 16))
	_password = _field(form, "8–72 個字元", true)
	_confirmation_label = host._label("確認密碼", 16)
	form.add_child(_confirmation_label)
	_confirmation = _field(form, "再次輸入密碼", true)
	_submit_button = host._button("註冊並進入遊戲", _submit, form, FIELD_HEIGHT)
	_status = host._label("", 16, host.ORANGE)
	_status.custom_minimum_size.y = 48
	_status.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	form.add_child(_status)
	_switch_button = host._button("已有帳號？登入", _switch_mode, form, FIELD_HEIGHT)
	var server: Label = host._label("帳號伺服器：" + _api_base, 14, host.CYAN)
	server.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	form.add_child(server)
	_request = HTTPRequest.new()
	_request.timeout = 8.0
	_request.request_completed.connect(_request_completed)
	add_child(_request)
	_username.text_submitted.connect(func(_text: String): _password.grab_focus())
	_password.text_submitted.connect(func(_text: String):
		if _registering:
			_confirmation.grab_focus()
		else:
			_submit()
	)
	_confirmation.text_submitted.connect(func(_text: String): _submit())
	_username.grab_focus.call_deferred()

func _field(parent: Node, hint: String, secret: bool) -> LineEdit:
	var field = LineEdit.new()
	field.placeholder_text = hint
	field.secret = secret
	field.custom_minimum_size.y = FIELD_HEIGHT
	field.focus_mode = Control.FOCUS_ALL
	parent.add_child(field)
	return field

func _switch_mode():
	if _busy or _finished:
		return
	_registering = not _registering
	_confirmation.visible = _registering
	_confirmation_label.visible = _registering
	_confirmation.clear()
	_password.clear()
	_heading.text = "建立遊戲帳號" if _registering else "登入遊戲帳號"
	_submit_button.text = "註冊並進入遊戲" if _registering else "登入並進入遊戲"
	_switch_button.text = "已有帳號？登入" if _registering else "還沒有帳號？註冊"
	_status.text = ""
	_username.grab_focus()

func _submit():
	if _busy or _finished:
		return
	var username = _username.text.strip_edges()
	var valid_username = username.length() >= 3 and username.length() <= 24
	for character in username:
		if not (character >= "a" and character <= "z" or character >= "A" and character <= "Z" or character >= "0" and character <= "9" or character == "_"):
			valid_username = false
	if not valid_username:
		_status.text = "使用者名稱須為 3–24 個英文字母、數字或底線。"
		_username.grab_focus()
		return
	if _password.text.length() < 8 or _password.text.length() > 72:
		_status.text = "密碼長度須為 8–72 個字元。"
		_password.grab_focus()
		return
	if _registering and _password.text != _confirmation.text:
		_status.text = "兩次輸入的密碼不一致，請重新確認。"
		_confirmation.grab_focus()
		return
	_set_busy(true)
	_status.add_theme_color_override("font_color", _host.CYAN)
	_status.text = "正在建立帳號…" if _registering else "正在登入…"
	var endpoint = "/api/register" if _registering else "/api/login"
	var payload = JSON.stringify({"username": username, "password": _password.text})
	var error = _request.request(_api_base + endpoint, ["Content-Type: application/json"], HTTPClient.METHOD_POST, payload)
	if error != OK:
		_fail("無法連線至帳號伺服器，請稍後重試。")

func _set_busy(value: bool):
	_busy = value
	_submit_button.disabled = value
	_switch_button.disabled = value
	_username.editable = not value
	_password.editable = not value
	_confirmation.editable = not value

func _fail(message: String):
	_set_busy(false)
	_status.add_theme_color_override("font_color", _host.ORANGE)
	_status.text = message
	_submit_button.grab_focus()

func _request_completed(result: int, response_code: int, _headers: PackedStringArray, body: PackedByteArray):
	if _finished:
		return
	if result != HTTPRequest.RESULT_SUCCESS:
		_fail("連線逾時或中斷，請確認帳號伺服器後重試。")
		return
	var response = JSON.parse_string(body.get_string_from_utf8())
	if not response is Dictionary:
		_fail("帳號伺服器回應異常，請稍後重試。")
		return
	if response_code not in [200, 201] or response.get("ok", false) != true:
		var message = response.get("error", "登入或註冊失敗，請稍後重試。")
		_fail(message if message is String else "帳號伺服器回應異常，請稍後重試。")
		return
	var token = response.get("token", "")
	var profile = response.get("profile", null)
	if not token is String or token.is_empty() or not profile is Dictionary:
		_fail("帳號伺服器回應不完整，請稍後重試。")
		return
	_finished = true
	_password.clear()
	_confirmation.clear()
	_status.text = "登入成功，正在進入遊戲…"
	authenticated.emit(token, profile)

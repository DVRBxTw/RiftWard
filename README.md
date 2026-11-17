# RIFTWARD 裂隙守衛 — Beta 0.3.0-beta.3

Windows 原生第一人稱合作 PVE。你是公元 2189 年的抵抗軍槍手，必須奪回被赫利奧帝國占領的地球區域。

## 直接遊玩

下載並完整解壓 **RiftWard-Windows-Beta.zip**，雙擊 `RiftWard.exe`。
主機須先啟動 `StartServer.cmd`；玩家電腦不需要安裝 Godot 或 Python。

啟動流程：檢查更新 → 註冊或登入 → 初次故事與訓練 → 抵抗軍基地 → 部署區域 → 清場及選 Buff → 結算返回基地。

- 遊戲固定全螢幕。滑鼠自由 360° 轉身，上下各 85°。
- WASD 移動、左鍵射擊、Shift 衝刺、Space 跳躍、Esc 返回確認。
- 切換視窗後點一下恢復滑鼠瞄準；恢復的第一下不會射擊。
- 初次帳號要完成滑鼠轉向、移動、擊破目標的新手教學。
- 註冊後自動登入；已有帳號可切到登入頁。客戶端不儲存密碼。
- 主頁可按「離開遊戲」；本機加密保留登入憑證，下次開啟會向伺服器驗證後恢復登入。憑證有效期為登入後 24 小時，主動「登出帳號」會清除。
- 伺服器離線或遊戲中斷線時，停留／返回登入畫面。離線開啟會先驗證隨附遊戲檔案完整性，僅顯示登入畫面，無法離線進入正式關卡。
- 關卡依序解鎖：灰燼前哨 → 熔核工廠 → 帝國裂隙。
- 區域有不同配色和場景配置；第三區域有重型守衛。
- 每波結束都可從三個隨機 Buff 中選一個；傷害、射速、移速、護盾、修復、暴擊能在任務內疊加。
- 所有人選完才繼續；30 秒沒有選擇時伺服器會自動選第一項。
- 敵人的護盾、速度和傷害會隨波次與區域提升。任務結束後 Buff 重置。
- 每個區域固定 20 波，第 10、20 波各有一隻 Boss；畫面上方顯示 Boss 目前／最大血量。
- 移動採本機預測、帶輸入序號的伺服器校正，以及每幀畫面平滑；敵人與隊友也改為每幀平滑。伺服器仍決定傷害、位置與勝負。

## 20 波平衡

普通敵人以第 1 波為基準，每波增加基礎血量 8%、基礎傷害 4%；移速每波增加基礎值 1.5%，最高增加 25%。
第 20 波的普通敵人因此為第 1 波血量的 2.52 倍、傷害的 1.76 倍、移速的 1.25 倍；增幅按基礎值累加。
每波數量隨波次與隊伍人數增加，上限 24，避免大量單位與同步封包造成卡頓。

第 10／20 波 Boss 單人首區基礎血量為 3000／7000；區域每深入一區增加 18%，每多一名玩家再增加首名玩家基準的 60%。Boss 傷害與移速也逐波提升，移速增幅最高 20%。
傷害 Buff 每次增加武器基礎傷害的 20%（28 × 20% = 5.6），避免 20 次選擇後連乘失衡；其餘 Buff 保留原有疊加與上限。
這是 Beta 初始平衡，尚未以大量玩家的通關率校準。

## 本機伺服器

指定主機：**123.241.56.94**。

| 服務 | 位址 | 用途 |
| --- | --- | --- |
| 遊戲 | UDP 24770 | 玩家、敵人、傷害、Buff、波次及勝負 |
| 帳號與更新 | HTTP TCP 24771 | 註冊、登入、教學／通關進度與更新檔 |

在持有這個 IP 的主機上安裝 Python 3.11 或更新版本，雙擊 `StartServer.cmd`。
它會一起啟動 SQLite 帳號服務與獨立遊戲伺服器。關閉該終端視窗會停止服務。
第一位登入基地的玩家是隊長，可選關及部署；隊長離線會交接。
隊伍中所有人都要解鎖目標區域才能進入。

對外玩家需要路由器將 TCP 24771、UDP 24770 轉發至主機的區域網路位址（預設 192.168.213.100）。伺服器監聽本機介面，玩家使用對外 IP。
目前帳號與更新仍使用 HTTP，適合受控測試網路；正式公開服務應先設定 HTTPS。程式不會修改防火牆、路由器或 Windows IP。
若需要這台主機長期維持位址，請在路由器做 DHCP 位址保留。
預設綁定位址也可由執行檔旁的 `server.cfg` 管理。

## 帳號與資料庫

`server-data/players.sqlite3` 保存帳號、教學完成狀態、解鎖區域與登入 Session。
密碼使用個別隨機 salt 與 PBKDF2-HMAC-SHA256（310,000 次）；Session token 僅儲存 SHA-256 摘要。
登入過期時間為 24 小時，同一帳號不能同時連入兩個玩家端。
遊戲伺服器驗證 Session 和客戶端版本，並透過伺服器專用金鑰更新進度。
資料庫與金鑰不放進 Git 或遊戲壓縮包。備份時請先停機，整份複製 `server-data/`。

## 自動更新如何使用

`RiftWard.exe` 先讀取主機 `/api/version`，檢查版本與 SHA-256。
若與隨附遊戲檔相同，直接啟動；有新版本就下載到使用者快取，核對完整大小與雜湊後原子切換。
下載失敗、檔案不完整或雜湊不符時不會啟動該檔案，可以重試；已有的完整版本不會被未完成下載取代。
舊版客戶端也不能繞過更新器連入新版本伺服器。

未來更新：同步變更遊戲 `beta_game.gd` 的 `VERSION` 和 `server/service.py` 的 `VERSION`，
執行建置後重新啟動伺服器。新的 manifest 與遊戲檔會發布在主機；玩家下次開啟便自動更新。
目前自動更新範圍是遊戲 PCK（程式、UI、地圖與素材）；引擎與啟動器本身升級需要重新下載壓縮包。

## 原始碼建置

1. 準備 Godot **4.5.1 stable Windows x64**，將官方 GUI 與 console 執行檔放入 `tools/`。
2. 共享字型已附 Noto Sans TC；授權放在 `licenses/`。
3. 執行 `powershell -NoProfile -ExecutionPolicy Bypass -File tools/build_beta.ps1`。
4. `build/` 會產生更新器、遊戲 PCK、服務程式與 SHA-256 manifest。

主要程式：

- [beta_game.gd](./beta_game.gd)：帳號驗證、基地、故事／教學、區域、Buff 及伺服器進度。
- [main.gd](./main.gd)：第一人稱控制、3D 場景、ENet 同步與戰鬥。
- [ui/account_screen.gd](./ui/account_screen.gd)：註冊及登入介面。
- [launcher/main.gd](./launcher/main.gd)：更新器。
- [server/service.py](./server/service.py)：SQLite 與 HTTP API。
- [server/run_server.py](./server/run_server.py)：帳號／更新／遊戲服務的一起啟動入口。
- [server/schema.sql](./server/schema.sql)：資料表說明。

## 驗證

```powershell
py -3 -m unittest discover -s tests -p test_service.py -v
./tools/Godot_v4.5.1-stable_win64_console.exe --path . --script res://tests/fps_controls.gd -- --smoke-test
./tools/Godot_v4.5.1-stable_win64_console.exe --headless --path . --script res://tests/beta_gameplay.gd -- --smoke-test
./tools/Godot_v4.5.1-stable_win64_console.exe --headless --path launcher --script res://tests/verify.gd
py -3 tests/run_beta_network.py
```

最後一項會在指定區網 IP 上啟動暫時帳號服務及三個 Godot 程序；請先停掉正式伺服器，測試資料放在暫存目錄。
原生滑鼠捕捉檢查必須有顯示視窗，不能加 `--headless`。

這仍是使用簡化模型的 Beta 原型；未包含正式商業美術、帳號找回、跨網配對或公開網際網路部署。

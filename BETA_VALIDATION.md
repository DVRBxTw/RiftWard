# Beta 0.3 驗證紀錄

- SQLite／HTTP 單元整合：註冊、重複帳號、錯誤密碼、Session 持久化與登出、教學保存、伺服器金鑰保護、舊版拒絕、越關拒絕、manifest 與路徑限制，4 組測試通過。
- 原生顯示／第一人稱：viewport 真正派送滑鼠事件，測試 HUD 不攔截、連續轉身、±85°、眼高鏡頭、準星方向、自身隱藏、切換視窗恢復、防誤射、身後射擊及朝天不命中地面，通過。
- 故事／教學／Buff：教學轉頭與移動步驟、波間暫停、傷害疊加、護盾上限、修復、難度提升、任務加成重置與返回基地，通過。
- 真實指定 IP 的 HTTP＋SQLite＋ENet：兩個獨立 Session，驗證後加入、各選兩波 Buff、伺服器保存解鎖進度並一起返回基地，通過。
- 更新器單元檢查：下載路徑、協定、大小限制、錯誤 hash、錯誤大小，通過。
- 更新器 HTTP 整合：下載、SHA-256／大小驗證、原子安裝，再以相同 manifest 命中快取，通過。
- 原生 viewport 截圖涵蓋註冊、故事、基地、Buff、區域戰鬥。獨立檢查發現的教學重播 local_id 問題已修正；基地右側描述加入寬度限制與明確換行。

## UI 驗證邊界

沿用上一階段獨立校準：暫存目錄中的單檔參考，故意把 CYAN 準星改為 ORANGE，獨立檢查者成功辨識。這只證明靜態顏色差異的檢查，不代表滑鼠手感、網路或排版。
本次 UI 調整／實作／驗證技能使帳號頁、更新器、故事與 Buff 選單沿用原本 native Theme 與按鈕，並加入獨立檢查及原生渲染確認。
獨立 UI 檢查者沒有親自操作實體滑鼠或重跑網路測試；滑鼠與網路驗證由自動測試執行。

仍未驗證外網路由、4 人長時間壓力、公開網路安全、所有螢幕比例與完整遊戲平衡。更新只包含遊戲 PCK；啟動器／引擎本身需要重新發包。
測試資料庫皆使用臨時目錄；正式玩家資料不會打包或推送 GitHub。
# Beta 0.3.0-beta.2: remembered sessions and exit

- Home has one exit button beside logout using the same native Button, theme and height.
- `tests/login_session.gd`: PASS encrypted token storage, replacement and restore, offline API gate, offline game service gate, expired credentials and explicit logout. No passwords saved. Existing sessions expire after 24 hours.
- Service unittest suite: 5 PASS, including game availability and expired session rejection.
- `launcher/tests/offline_login.gd`: PASS verified bundled pack permits opening the login UI without a server; missing metadata or corrupted pack prevents fallback. This does not authorize offline gameplay.
- Launcher manifest/hash validation and real HTTP download/cache integration: PASS. Integration probe still reports its pre-existing ObjectDB timer cleanup warning.
- Two-client HTTP/SQLite/ENet integration and gameplay regression: PASS after excluding an old dedicated server occupying UDP 24770. Disconnect teardown still logs pre-existing ENet channel warnings; no gameplay assertions failed.
- Exported game pack opened headlessly without parse errors. Native home capture regenerated at 1280x800.
- Separate UI reviewer checked the home against the previous published version and the capture: equal logout/exit widths and heights, no extra vertical space or clipping. Keyboard activation, hover interaction, other resolutions and connected/locked variants were not exercised interactively.
- Calibration: temporary detached worktree `riftward-session-calibration-253c985e5b2f4fb788c4f2f5f687eccf` under the session temporary directory; exit button minimum height was deliberately changed from 48 to 96. A separate calibration agent reported the difference against the checklist. The copy was removed after review. This proves source review of that height difference, not all runtime layout or session behavior.

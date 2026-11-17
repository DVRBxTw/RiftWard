# RIFTWARD 裂隙守衛

Windows 第一人稱合作 PvE 射擊（Unity 6）。你是地球遠征軍的士兵，墜落在一顆陌生星球，守護這個世界並反攻赫利奧帝國。

## 玩家：直接遊玩

1. 下載並完整解壓 **RiftWard-Windows.zip**。
2. 雙擊 `RiftWard.exe`（啟動器）：自動檢查更新、驗證 SHA-256、安裝並啟動遊戲。
3. 註冊或登入 → 故事與新手訓練 → 基地主頁 → 隊長部署 → 20 波戰鬥 → 結算金幣。

連不上遊戲伺服器時會自動改為離線單人任務（不發放金幣）。

## 操作

| 按鍵 | 動作 | 按鍵 | 動作 |
|---|---|---|---|
| WASD | 移動 | 左鍵 / 右鍵 | 射擊 / 瞄準開鏡 |
| R | 換彈 | 1、2、滾輪 | 切換兩把出擊武器 |
| Q | 手榴彈 | Shift | 衝刺 |
| E | 救援倒地隊友 | Space | 跳躍（管理員飛行時上升） |
| V | 第一 / 第三人稱 | Esc | 戰術選單 |

## 主機：啟動伺服器

在 `build/` 執行 `StartServer.cmd`，會同時啟動：

- 帳號與更新服務：HTTP **TCP 24771**
- Unity 專用遊戲伺服器：**TCP 24770**（無畫面）

外部玩家需要路由器轉發 **TCP 24770 與 TCP 24771**。玩家資料在 `build/server-data/`（帳號、金幣、武器、戰績）；管理員名單在 `build/server-data/admins.txt`，每行一個帳號。

關閉伺服器請在 StartServer 視窗按 Ctrl+C。

## 開發

| 路徑 | 內容 |
|---|---|
| `unity/` | Unity 6 專案。`Assets/Scripts/Sim` 純 C# 戰鬥模擬（伺服器與客戶端共用），`Assets/Scripts/Client` 畫面、UI、連線 |
| `server/` | Python 帳號 / 更新服務與伺服器啟動腳本 |
| `launcher/` | Godot 啟動器（下載與驗證更新） |
| `tools/build_unity.ps1` | 一鍵打包：Unity 編譯 → 遊戲 zip 與更新清單 → 伺服器檔案 → 啟動器 |
| `tools/package_beta.py` | 產生玩家下載用的 RiftWard-Windows.zip |

測試：

- Unity 單元測試：Unity `-runTests -testPlatform EditMode`
- 帳號服務：`cd server && python -m unittest discover -s ../tests -p test_service.py`
- 多人連線整合：`python tests/run_unity_network.py build/unity/RiftWard.exe`

新增或修改連線訊息時，請同時提高 `Defs.Version`，讓舊版客戶端被要求更新。

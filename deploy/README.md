# 測試服 / 正式服

| | 測試服 | 正式服 |
|---|---|---|
| 資料夾 | `build/` | `prod/` |
| 帳號／更新埠 (TCP) | 24771 | 24781 |
| 遊戲埠 (TCP) | 24770 | 24780 |
| 玩家資料 | `build/server-data` | `prod/server-data` |
| 玩家下載 | `RiftWard-Test-Windows.zip`（GitHub 預先發行版） | `RiftWard-Windows.zip`（GitHub 正式版） |

流程：
1. `tools\release.ps1 -Notes 說明.md`：建置並更新 **測試服**，發布測試版。
2. 在測試服確認沒問題後，`tools\promote.ps1 -Notes 說明.md`：把**同一個建置**推上 **正式服**（先備份正式服資料庫），發布正式版。

`server.cfg` 決定玩家的遊戲連到哪一服：`port` / `game_port` 為埠號，`name` 會顯示在遊戲左上角。

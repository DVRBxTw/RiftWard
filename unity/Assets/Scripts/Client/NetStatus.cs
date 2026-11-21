using System;
using UnityEngine;
using UnityEngine.UI;

namespace RiftWard.Client
{
    /// Connection readouts above every other layer: ping and frame rate in the top-right corner, a banner while the
    /// game reconnects on its own, and a dialog once every attempt has failed.
    public sealed class NetStatus
    {
        readonly Canvas canvas;
        readonly RectTransform statsPlate, banner, dialog;
        readonly Text stats, bannerTitle, bannerDetail, dialogBody;
        float fpsTime, shownFps;
        int fpsFrames;
        Action retry, dismiss;

        public bool DialogOpen => dialog.gameObject.activeSelf;

        public NetStatus()
        {
            canvas = Ui.Canvas("NetStatus", 40);
            var root = canvas.transform;

            // ---- corner readout: small, out of the way of the radar (which starts 28 px down)
            statsPlate = Ui.Box(root, new Color(0.04f, 0.045f, 0.05f, 0.55f), "Stats").rectTransform;
            statsPlate.Place(new Vector2(1, 1), new Vector2(-6, -4), new Vector2(176, 20), new Vector2(1, 1));
            statsPlate.GetComponent<Image>().raycastTarget = false;
            stats = Ui.Label(statsPlate, "", 12, Palette.Bone, TextAnchor.MiddleCenter);
            stats.rectTransform.Fill();
            stats.raycastTarget = false;
            stats.supportRichText = true;

            // ---- reconnecting banner (top centre, below the XP bar and above the wave banner)
            banner = Ui.Box(root, new Color(0.04f, 0.045f, 0.05f, 0.86f), "Reconnect").rectTransform;
            banner.Place(new Vector2(0.5f, 1), new Vector2(0, -84), new Vector2(460, 62), new Vector2(0.5f, 1));
            banner.GetComponent<Image>().raycastTarget = false;
            Ui.Frame(banner, new Color(Palette.Amber.r, Palette.Amber.g, Palette.Amber.b, 0.55f), 1f, 3f);
            bannerTitle = Ui.Label(banner, "", 17, Palette.Amber, TextAnchor.UpperLeft, true);
            bannerTitle.rectTransform.Place(new Vector2(0, 1), new Vector2(20, -9), new Vector2(420, 24), new Vector2(0, 1));
            bannerDetail = Ui.Label(banner, "", 13, Palette.Hex("d6d1c6"), TextAnchor.UpperLeft);
            bannerDetail.rectTransform.Place(new Vector2(0, 1), new Vector2(20, -35), new Vector2(420, 20), new Vector2(0, 1));
            banner.gameObject.SetActive(false);

            // ---- dialog after the last attempt
            dialog = Ui.Box(root, new Color(0, 0, 0, 0.62f), "Disconnected").rectTransform.Fill();
            var sheet = Ui.Box(dialog, Palette.Sheet, "Sheet").rectTransform;
            sheet.Place(new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(480, 270));
            var rule = Ui.Box(sheet, Palette.Danger, "Rule").rectTransform;
            rule.anchorMin = new Vector2(0, 1); rule.anchorMax = new Vector2(1, 1); rule.pivot = new Vector2(0.5f, 1);
            rule.offsetMin = new Vector2(0, -2); rule.offsetMax = Vector2.zero;
            var st = Ui.VStack(sheet, 6);
            st.Fill();
            st.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(28, 28, 22, 22);
            Ui.Label(st, "連線狀況", 13, Palette.Danger).Sized(18);
            Ui.Label(st, "與遊戲伺服器的連線已中斷", 22, Palette.Bone, TextAnchor.UpperLeft, true).Sized(32);
            dialogBody = Ui.Label(st, "", 14, Palette.Hex("d6d1c6"));
            dialogBody.Sized(104);
            var row = Ui.HStack(st, 10).Sized(40);
            Ui.Button(row, "重新連線", () => retry?.Invoke(), 15, Palette.Amber);
            Ui.Button(row, "離線遊玩", () => dismiss?.Invoke(), 15, Palette.Muted);
            dialog.gameObject.SetActive(false);
        }

        /// Every frame: frame rate over half-second windows, ping from the live connection (or offline).
        public void Update(NetClient net, bool inMission)
        {
            // In a mission the strip sits above the radar; in the menus the header owns the top, so it moves to the
            // bottom-right corner, which every page leaves empty.
            statsPlate.Place(new Vector2(1, inMission ? 1 : 0), new Vector2(-6, inMission ? -4 : 4), new Vector2(176, 20), new Vector2(1, inMission ? 1 : 0));
            fpsFrames++;
            fpsTime += Time.unscaledDeltaTime;
            if (fpsTime >= 0.5f) { shownFps = fpsFrames / fpsTime; fpsFrames = 0; fpsTime = 0; }
            bool showPing = GameSettings.ShowPing, showFps = GameSettings.ShowFps;
            statsPlate.gameObject.SetActive(showPing || showFps);
            if (!showPing && !showFps) return;
            // One item alone gets a narrower plate so it hugs the corner.
            statsPlate.sizeDelta = new Vector2(showPing && showFps ? 176 : 110, 20);
            string fpsColor = shownFps >= 55 ? "9fe39a" : shownFps >= 30 ? "f2b84b" : "ff6a5c";
            string ping;
            if (net == null || !net.Connected) ping = "<color=#8a8780>離線</color>";
            else if (net.Silence > 2.5f) ping = $"<color=#ff6a5c>Ping 無回應 {net.Silence:0}s</color>";
            else if (net.PingMs < 0) ping = "<color=#8a8780>Ping --</color>";
            else
            {
                string c = net.PingMs < 80 ? "9fe39a" : net.PingMs < 160 ? "f2b84b" : "ff6a5c";
                ping = $"<color=#{c}>Ping {net.PingMs} ms</color>";
            }
            string fps = $"<color=#{fpsColor}>{shownFps:0} FPS</color>";
            stats.text = showPing && showFps ? $"{ping}  ·  {fps}" : showPing ? ping : fps;
        }

        public void ShowReconnecting(int attempt, int max, string reason, float wait)
        {
            HideDialog();
            banner.gameObject.SetActive(true);
            bannerTitle.text = wait > 0.05f ? $"連線中斷 · {wait:0} 秒後重試（第 {attempt} / {max} 次）" : $"正在重新連線…（第 {attempt} / {max} 次）";
            bannerDetail.text = string.IsNullOrEmpty(reason) ? "任務座位會保留 60 秒，連回後直接回到戰場" : $"原因：{reason} · 任務座位保留 60 秒";
        }

        public void HideReconnecting() => banner.gameObject.SetActive(false);

        public void ShowFailed(int attempts, string reason, Action onRetry, Action onDismiss)
        {
            HideReconnecting();
            retry = onRetry;
            dismiss = onDismiss;
            dialogBody.text = $"已自動重試 {attempts} 次仍無法連上伺服器。\n原因：{(string.IsNullOrEmpty(reason) ? "未知" : reason)}\n請檢查網路後按「重新連線」；在 60 秒內連回仍可回到原任務。\n也可以先離線遊玩單人任務。";
            dialog.gameObject.SetActive(true);
            dialog.SetAsLastSibling();
        }

        public void HideDialog() => dialog.gameObject.SetActive(false);
    }
}

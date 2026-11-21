using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using RiftWard.Sim;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace RiftWard.Client
{
    /// 自動更新: while the game runs it asks the account server for the published version every few minutes. When a
    /// newer one is out it offers to update; updating starts the launcher (which downloads only the changed files)
    /// and closes the game. During a mission the offer waits until the player is back in the menus.
    public sealed class UpdateWatcher
    {
        public const float Every = 180f;
        readonly AccountApi api;
        readonly MonoBehaviour runner;
        readonly Func<bool> busy;
        readonly RectTransform dialog;
        readonly Text body;
        readonly Button now;
        float next = 20f;
        bool checking, dismissed;
        string found = "";

        public UpdateWatcher(MonoBehaviour runner, AccountApi api, Func<bool> busy)
        {
            this.runner = runner; this.api = api; this.busy = busy;
            var canvas = Ui.Canvas("UpdateWatcher", 45);
            dialog = Ui.Box(canvas.transform, new Color(0, 0, 0, 0.62f), "Update").rectTransform.Fill();
            var sheet = Ui.Box(dialog, Palette.Sheet, "Sheet").rectTransform;
            sheet.Place(new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(480, 250));
            var rule = Ui.Box(sheet, Palette.Amber, "Rule").rectTransform;
            rule.anchorMin = new Vector2(0, 1); rule.anchorMax = new Vector2(1, 1); rule.pivot = new Vector2(0.5f, 1);
            rule.offsetMin = new Vector2(0, -2); rule.offsetMax = Vector2.zero;
            var st = Ui.VStack(sheet, 6);
            st.Fill();
            st.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(28, 28, 22, 22);
            Ui.Label(st, "遊戲更新", 13, Palette.Amber).Sized(18);
            Ui.Label(st, "有新版本可以更新", 22, Palette.Bone, TextAnchor.UpperLeft, true).Sized(32);
            body = Ui.Label(st, "", 14, Palette.Hex("d6d1c6"));
            body.Sized(84);
            var row = Ui.HStack(st, 10).Sized(40);
            now = Ui.Button(row, "立即更新", Apply, 15, Palette.Amber);
            Ui.Button(row, "稍後再說", () => { dismissed = true; dialog.gameObject.SetActive(false); }, 15, Palette.Muted);
            dialog.gameObject.SetActive(false);
        }

        public bool Open => dialog.gameObject.activeSelf;

        public void Update()
        {
            if (found != "" && !dismissed && !Open && !busy()) Show();
            next -= Time.unscaledDeltaTime;
            if (next > 0 || checking) return;
            next = Every;
            runner.StartCoroutine(Check());
        }

        IEnumerator Check()
        {
            checking = true;
            using (var req = UnityWebRequest.Get(api.BaseUrl + "/api/version"))
            {
                req.timeout = 8;
                yield return req.SendWebRequest();
                checking = false;
                if (req.result != UnityWebRequest.Result.Success) yield break;
                System.Collections.Generic.Dictionary<string, object> data;
                try { data = Json.Parse(req.downloadHandler.text) as System.Collections.Generic.Dictionary<string, object>; }
                catch { yield break; }
                string version = data != null && data.TryGetValue("version", out var v) ? v as string ?? "" : "";
                if (version == "" || version == Defs.Version || version == found) yield break;
                found = version;
                dismissed = false;   // a newer build than the one dismissed is offered again
            }
        }

        void Show()
        {
            bool canRun = LauncherPath() != null;
            body.text = canRun
                ? $"新版本 {found} 已推出（目前 {Defs.Version}）。\n按「立即更新」會關閉遊戲並自動下載更新，完成後重新開啟。"
                : $"新版本 {found} 已推出（目前 {Defs.Version}）。\n請關閉遊戲，再從 RiftWard 啟動器開啟，就會自動更新。";
            now.gameObject.SetActive(canRun);
            dialog.gameObject.SetActive(true);
        }

        void Apply()
        {
            string launcher = LauncherPath();
            if (launcher == null) return;
            try
            {
                Process.Start(new ProcessStartInfo(launcher) { WorkingDirectory = Path.GetDirectoryName(launcher), UseShellExecute = false });
                Application.Quit();
            }
            catch (Exception) { body.text = "無法啟動更新程式。請關閉遊戲，再從 RiftWard 啟動器開啟。"; now.gameObject.SetActive(false); }
        }

        /// The launcher that started this copy: passed as "--launcher <path>", or recorded by it in launcher.txt in
        /// the update cache (the folder above this game's versioned folder).
        static string LauncherPath()
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "--launcher" && File.Exists(args[i + 1])) return args[i + 1];
            try
            {
                var game = Directory.GetParent(Application.dataPath);
                string note = game?.Parent == null ? null : Path.Combine(game.Parent.FullName, "launcher.txt");
                if (note != null && File.Exists(note))
                {
                    string path = File.ReadAllText(note).Trim();
                    if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(path)) return path;
                }
            }
            catch (Exception) { }
            return null;
        }
    }
}

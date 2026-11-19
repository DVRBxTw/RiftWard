// RiftWard launcher: checks the update server, verifies SHA-256, safely extracts and starts the Unity client.
// Built with the .NET Framework 4 compiler that ships with Windows (see tools/build_launcher.ps1).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("RiftWard")]
[assembly: System.Reflection.AssemblyProduct("RiftWard")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]

namespace RiftWard.Launcher
{
    public class Manifest
    {
        public string Version, Sha256, Download, Package;
        public long Size;
    }

    public static class Updater
    {
        public const long MaxPackSize = 512L * 1024 * 1024;
        static readonly Regex VersionRx = new Regex(@"^[0-9]+\.[0-9]+\.[0-9]+(?:-[A-Za-z0-9]+(?:[.-][A-Za-z0-9]+)*)?$");
        static readonly Regex HashRx = new Regex("^[0-9a-f]{64}$");
        static readonly Regex HostRx = new Regex(@"^[A-Za-z0-9][A-Za-z0-9.-]{0,252}$");

        public static string ReadOrigin(string installDir)
        {
            string address = "123.241.56.94";
            int port = 24771;   // 測試服; the 正式服 package's server.cfg says port=24781
            string path = Path.Combine(installDir, "server.cfg");
            if (File.Exists(path))
            {
                foreach (string raw in File.ReadAllLines(path))
                {
                    string line = raw.Trim();
                    int eq = line.IndexOf('=');
                    if (line.StartsWith("address"))
                    {
                        if (eq < 0) return null;
                        address = line.Substring(eq + 1).Trim().Trim('"').Trim();
                    }
                    else if (line.StartsWith("port") && eq > 0)
                    {
                        int p;
                        if (!int.TryParse(line.Substring(eq + 1).Trim().Trim('"'), out p) || p <= 0 || p >= 65536) return null;
                        port = p;
                    }
                }
            }
            return HostRx.IsMatch(address) ? "http://" + address + ":" + port : null;
        }

        /// <summary>Parses and validates a manifest; returns null and sets problem on failure.</summary>
        public static Manifest Parse(string json, bool requireServerFields, out string problem)
        {
            problem = null;
            Dictionary<string, object> d;
            try { d = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>; }
            catch { d = null; }
            if (d == null) { problem = "版本資料不是物件。"; return null; }
            var m = new Manifest();
            m.Package = (d.ContainsKey("package") && (d["package"] as string) == "zip") ? "zip" : "pck";
            if (requireServerFields)
            {
                if (!d.ContainsKey("ok") || !(d["ok"] is bool) || !(bool)d["ok"]) { problem = "伺服器尚未提供更新。"; return null; }
                if (!d.ContainsKey("protocol") || Convert.ToDouble(d["protocol"]) != 1) { problem = "更新協定不受支援。"; return null; }
            }
            m.Version = d.ContainsKey("version") ? d["version"] as string : null;
            m.Sha256 = d.ContainsKey("sha256") ? d["sha256"] as string : null;
            m.Download = requireServerFields ? (d.ContainsKey("download") ? d["download"] as string : null) : "/updates/" + m.Sha256 + "." + m.Package;
            if (m.Version == null || m.Sha256 == null || m.Download == null) { problem = "版本資料缺少必要欄位。"; return null; }
            if (m.Version.Length > 80 || !VersionRx.IsMatch(m.Version)) { problem = "版本名稱格式無效。"; return null; }
            if (!HashRx.IsMatch(m.Sha256)) { problem = "檔案指紋格式無效。"; return null; }
            if (m.Package != "zip") { problem = "此啟動器只支援 Unity 遊戲包，請重新下載安裝包。"; return null; }
            if (m.Download != "/updates/" + m.Sha256 + ".zip") { problem = "下載路徑無效。"; return null; }
            object size;
            if (!d.TryGetValue("size", out size) || !(size is int || size is long || size is decimal || size is double)) { problem = "檔案大小無效。"; return null; }
            double s = Convert.ToDouble(size);
            if (s < 1 || s > MaxPackSize || s != Math.Floor(s)) { problem = "檔案大小超出限制。"; return null; }
            m.Size = (long)s;
            return m;
        }

        public static string Verify(string path, long size, string sha256)
        {
            if (!File.Exists(path)) return "找不到更新檔。";
            if (new FileInfo(path).Length != size) return "更新檔大小不符。";
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create())
            {
                string hex = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                return hex == sha256 ? null : "更新檔 SHA-256 驗證失敗。";
            }
        }

        /// <summary>Extracts into a versioned folder, refusing any entry that could escape it.</summary>
        public static string Extract(string zipPath, string target, string sha256)
        {
            string marker = Path.Combine(target, ".verified");
            if (File.Exists(marker) && File.ReadAllText(marker) == sha256) return target;
            if (Directory.Exists(target)) Directory.Delete(target, true);
            string root = Path.GetFullPath(target) + Path.DirectorySeparatorChar;
            using (var zip = ZipFile.OpenRead(zipPath))
            {
                foreach (var entry in zip.Entries)
                {
                    string name = entry.FullName.Replace('\\', '/');
                    if (name.StartsWith("/") || name.Contains("..") || name.Contains(":"))
                        throw new InvalidDataException("unsafe entry " + name);
                    string destination = Path.GetFullPath(Path.Combine(target, name));
                    if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("unsafe entry " + name);
                    if (name.EndsWith("/")) { Directory.CreateDirectory(destination); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    entry.ExtractToFile(destination, true);
                }
            }
            File.WriteAllText(marker, sha256);
            return target;
        }
    }

    public class LauncherForm : Form
    {
        static readonly Color Ink = ColorTranslator.FromHtml("#101113");
        static readonly Color Bone = ColorTranslator.FromHtml("#ebe6db");
        static readonly Color Amber = ColorTranslator.FromHtml("#f0b24a");
        static readonly Color Danger = ColorTranslator.FromHtml("#e5534b");
        static readonly Color Muted = ColorTranslator.FromHtml("#8a877f");

        readonly string installDir = AppDomain.CurrentDomain.BaseDirectory;
        readonly string updateDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RiftWard", "updates");
        readonly Label status, detail;
        readonly Panel bar, barFill;
        readonly Button retry;
        float fraction;
        bool busy;

        public LauncherForm()
        {
            Text = "RiftWard";
            ClientSize = new Size(720, 360);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Ink;
            ForeColor = Bone;
            Font = new Font("Microsoft JhengHei UI", 11f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var rule = new Panel { BackColor = Amber, Location = new Point(48, 44), Size = new Size(48, 3) };
            var title = new Label { Text = "RIFTWARD", Font = new Font("Segoe UI", 30f, FontStyle.Bold), ForeColor = Bone, AutoSize = true, Location = new Point(42, 54) };
            var sub = new Label { Text = "裂隙守衛 · 戰地更新", ForeColor = Muted, AutoSize = true, Location = new Point(50, 112) };
            status = new Label { Text = "正在確認最新版本…", Font = new Font("Microsoft JhengHei UI", 16f, FontStyle.Bold), AutoSize = true, Location = new Point(48, 160) };
            detail = new Label { ForeColor = Muted, Location = new Point(50, 200), Size = new Size(620, 44) };
            bar = new Panel { BackColor = ColorTranslator.FromHtml("#26272a"), Location = new Point(50, 252), Size = new Size(620, 6) };
            barFill = new Panel { BackColor = Amber, Location = new Point(0, 0), Size = new Size(0, 6) };
            bar.Controls.Add(barFill);
            retry = MakeButton("重試更新", new Point(50, 284), true);
            retry.Click += delegate { Check(); };
            var close = MakeButton("離開", new Point(210, 284), false);
            close.Click += delegate { Close(); };
            Controls.AddRange(new Control[] { rule, title, sub, status, detail, bar, retry, close });
            Shown += delegate { Check(); };
        }

        Button MakeButton(string text, Point at, bool primary)
        {
            var b = new Button { Text = text, Location = at, Size = new Size(144, 44), FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
            b.FlatAppearance.BorderColor = primary ? Amber : Muted;
            b.FlatAppearance.BorderSize = 1;
            b.FlatAppearance.MouseOverBackColor = ColorTranslator.FromHtml("#1d1e21");
            b.ForeColor = primary ? Amber : Bone;
            return b;
        }

        void Ui(Action a) { if (IsHandleCreated && !IsDisposed) BeginInvoke(a); }

        void SetStatus(string s, string d)
        {
            Ui(delegate { status.ForeColor = Bone; status.Text = s; if (d != null) detail.Text = d; });
        }

        void SetProgress(float f)
        {
            fraction = Math.Max(0, Math.Min(1, f));
            Ui(delegate { barFill.Width = (int)(bar.Width * fraction); });
        }

        void Fail(string message)
        {
            Ui(delegate
            {
                busy = false;
                status.ForeColor = Danger;
                status.Text = "更新未完成";
                detail.Text = message;
                retry.Enabled = true;
                retry.Focus();
            });
        }

        void Check()
        {
            if (busy) return;
            busy = true;
            retry.Enabled = false;
            SetProgress(0);
            SetStatus("正在確認最新版本…", "連線至更新伺服器；驗證完成後自動啟動。");
            new Thread(Run) { IsBackground = true }.Start();
        }

        void Run()
        {
            try { RunInner(); }
            catch (Exception e) { Fail("發生錯誤：" + e.Message); }
        }

        void RunInner()
        {
            string origin = Updater.ReadOrigin(installDir);
            if (origin == null) { Fail("無法讀取 server.cfg，或伺服器位址格式無效。"); return; }
            string problem;
            Manifest m;
            string json = null;
            for (int attempt = 0; attempt < 3 && json == null; attempt++)
            {
                try { json = Get(origin + "/api/version", 8000); }
                catch (WebException) { }
                catch (IOException) { }
            }
            if (json == null)
            {
                if (LaunchOffline()) return;
                Fail("無法連線更新伺服器。請確認網路及伺服器狀態。");
                return;
            }
            m = Updater.Parse(json, true, out problem);
            if (m == null) { Fail(problem); return; }

            Directory.CreateDirectory(updateDir);
            string cached = Path.Combine(updateDir, m.Version + "-" + m.Sha256 + ".zip");
            if (Updater.Verify(cached, m.Size, m.Sha256) == null) { Launch(m, cached); return; }
            string bundled = Path.Combine(installDir, "game", "RiftWard.zip");
            if (Updater.Verify(bundled, m.Size, m.Sha256) == null) { Launch(m, bundled); return; }

            string part = Path.Combine(updateDir, "download.part");
            SetStatus("正在下載 " + m.Version, null);
            if (!Download(origin + m.Download, part, m.Size)) return;
            SetStatus("正在驗證下載檔案…", null);
            problem = Updater.Verify(part, m.Size, m.Sha256);
            if (problem != null) { Fail(problem); return; }
            if (File.Exists(cached)) File.Delete(cached);
            File.Move(part, cached);
            Launch(m, cached);
        }

        bool LaunchOffline()
        {
            string metadata = Path.Combine(installDir, "game", "manifest.json");
            if (!File.Exists(metadata)) return false;
            string problem;
            var m = Updater.Parse(File.ReadAllText(metadata), false, out problem);
            if (m == null) return false;
            string bundled = Path.Combine(installDir, "game", "RiftWard.zip");
            if (Updater.Verify(bundled, m.Size, m.Sha256) != null) return false;
            Launch(m, bundled);
            return true;
        }

        static string Get(string url, int timeout)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Timeout = timeout;
            req.AllowAutoRedirect = false;
            using (var res = (HttpWebResponse)req.GetResponse())
            {
                if (res.StatusCode != HttpStatusCode.OK || res.ContentLength > 65536) return null;
                using (var reader = new StreamReader(res.GetResponseStream(), System.Text.Encoding.UTF8))
                    return reader.ReadToEnd();
            }
        }

        /// <summary>Downloads with up to 5 automatic retries; each retry resumes from the bytes already saved
        /// (HTTP Range), so a dropped connection costs seconds instead of the whole transfer.</summary>
        bool Download(string url, string path, long size)
        {
            if (File.Exists(path)) File.Delete(path);
            string last = null;
            for (int attempt = 0; attempt < 6; attempt++)
            {
                if (attempt > 0)
                {
                    int a = attempt;
                    Ui(delegate { detail.Text = "連線中斷，正在第 " + a + " 次重新連線並續傳…"; });
                    Thread.Sleep(1500 * attempt);
                }
                try
                {
                    if (DownloadOnce(url, path, size)) return true;
                    return false;   // a definite failure (already reported)
                }
                catch (WebException e) { last = e.Message; }
                catch (IOException e) { last = e.Message; }
            }
            Fail("下載失敗（已自動重試 5 次）：" + last);
            return false;
        }

        bool DownloadOnce(string url, string path, long size)
        {
            {
                long have = File.Exists(path) ? new FileInfo(path).Length : 0;
                if (have >= size) { SetProgress(1); return true; }
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Timeout = 45000;
                req.ReadWriteTimeout = 45000;
                req.AllowAutoRedirect = false;
                if (have > 0) req.AddRange(have);
                using (var res = (HttpWebResponse)req.GetResponse())
                using (var input = res.GetResponseStream())
                {
                    bool resumed = res.StatusCode == HttpStatusCode.PartialContent;
                    if (res.StatusCode != HttpStatusCode.OK && !resumed) { Fail("更新連線失敗（HTTP " + (int)res.StatusCode + "）。"); return false; }
                    if (!resumed) have = 0;   // server ignored the range: start over
                    using (var output = new FileStream(path, resumed ? FileMode.Append : FileMode.Create, FileAccess.Write))
                    {
                    var buffer = new byte[81920];
                    long total = have;
                    int read, lastTick = 0;
                    while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        total += read;
                        if (total > size) { Fail("下載檔案超出預期大小。"); return false; }
                        output.Write(buffer, 0, read);
                        if (Environment.TickCount - lastTick > 100)
                        {
                            lastTick = Environment.TickCount;
                            SetProgress((float)total / size);
                            long t = total;
                            Ui(delegate { detail.Text = string.Format("{0:0.0} / {1:0.0} MB · 下載後將驗證 SHA-256", t / 1048576.0, size / 1048576.0); });
                        }
                    }
                    // The stream ended early (connection closed cleanly mid-transfer): retry and resume.
                    if (total < size) throw new IOException("連線提前結束（" + total + " / " + size + " bytes）");
                    }
                }
                SetProgress(1);
                return true;
            }
        }

        /// <summary>Removes earlier versions from the update cache (old zips, old extracted game folders and any
        /// stale partial download), keeping only the version about to start. Anything still in use, such as an
        /// older game left running, is skipped and removed on a later launch.</summary>
        void CleanOldUpdates(string keepFolder, string keepZip)
        {
            try
            {
                if (!Directory.Exists(updateDir)) return;
                string keepF = Path.GetFullPath(keepFolder).TrimEnd(Path.DirectorySeparatorChar);
                string keepZ = Path.GetFullPath(keepZip);
                foreach (string dir in Directory.GetDirectories(updateDir, "game-*"))
                {
                    if (string.Equals(Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar), keepF, StringComparison.OrdinalIgnoreCase)) continue;
                    try { Directory.Delete(dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                }
                foreach (string file in Directory.GetFiles(updateDir))
                {
                    string name = Path.GetFileName(file);
                    bool cache = name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || name == "download.part";
                    if (!cache || string.Equals(Path.GetFullPath(file), keepZ, StringComparison.OrdinalIgnoreCase)) continue;
                    try { File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                }
            }
            catch (Exception) { }   // cleanup must never stop the game from starting
        }

        void Launch(Manifest m, string zip)
        {
            SetStatus("正在安裝遊戲檔案…", "版本 " + m.Version);
            string folder;
            try { folder = Updater.Extract(zip, Path.Combine(updateDir, "game-" + m.Version + "-" + m.Sha256.Substring(0, 16)), m.Sha256); }
            catch (Exception) { Fail("無法解壓遊戲檔案。請確認磁碟空間後重試。"); return; }
            string cfg = Path.Combine(installDir, "server.cfg");
            if (File.Exists(cfg)) File.Copy(cfg, Path.Combine(folder, "server.cfg"), true);
            string exe = Path.Combine(folder, "RiftWard.exe");
            if (!File.Exists(exe)) { Fail("遊戲執行檔遺失，請重試。"); return; }
            SetStatus("驗證完成，正在啟動…", "版本 " + m.Version);
            CleanOldUpdates(folder, zip);
            Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = folder, UseShellExecute = false });
            Ui(delegate { Close(); });
        }

        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "--self-test") return SelfTest(args[1]);
            Application.EnableVisualStyles();
            Application.Run(new LauncherForm());
            return 0;
        }

        /// <summary>Headless check used by the build: validates the bundled manifest and zip.</summary>
        static int SelfTest(string installDir)
        {
            string problem;
            var m = Updater.Parse(File.ReadAllText(Path.Combine(installDir, "game", "manifest.json")), false, out problem);
            if (m == null) { Console.WriteLine("FAIL " + problem); return 1; }
            problem = Updater.Verify(Path.Combine(installDir, "game", "RiftWard.zip"), m.Size, m.Sha256);
            if (problem != null) { Console.WriteLine("FAIL " + problem); return 1; }
            if (Updater.Parse("{\"ok\":true,\"protocol\":1,\"version\":\"1.0.0\",\"sha256\":\"" + m.Sha256 + "\",\"download\":\"/updates/../x.zip\",\"size\":5,\"package\":\"zip\"}", true, out problem) != null)
            { Console.WriteLine("FAIL bad path accepted"); return 1; }
            string temp = Path.Combine(Path.GetTempPath(), "riftward-selftest-" + Guid.NewGuid().ToString("N"));
            try
            {
                Updater.Extract(Path.Combine(installDir, "game", "RiftWard.zip"), temp, m.Sha256);
                if (!File.Exists(Path.Combine(temp, "RiftWard.exe"))) { Console.WriteLine("FAIL no exe"); return 1; }
            }
            finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
            Console.WriteLine("SELFTEST_OK " + m.Version);
            return 0;
        }
    }
}

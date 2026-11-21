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
        /// <summary>Delta updates: URL of the version's file list (null when the server offers none).</summary>
        public string Files;
    }

    /// <summary>One file of a version, as the server's file list names it.</summary>
    public class FileEntry
    {
        public string Path, Sha256;
        public long Size;
    }

    public static class Updater
    {
        public const long MaxPackSize = 512L * 1024 * 1024;
        static readonly Regex VersionRx = new Regex(@"^[0-9]+\.[0-9]+\.[0-9]+(?:-[A-Za-z0-9]+(?:[.-][A-Za-z0-9]+)*)?$");
        static readonly Regex HashRx = new Regex("^[0-9a-f]{64}$");
        static readonly Regex HostRx = new Regex(@"^[A-Za-z0-9][A-Za-z0-9.-]{0,252}$");

        /// Which server this launcher belongs to is compiled in (/define:PROD builds RiftWard.exe for the 正式服,
        /// the default build is RiftWard-Test.exe for the 測試服), so the exe alone is a complete download. A
        /// server.cfg next to it still overrides the address and ports.
#if PROD
        public const string ServerName = "正式服";
        public const int ApiPort = 24781, GamePort = 24780;
        public const string CacheFolder = "updates-prod";
#else
        public const string ServerName = "測試服";
        public const int ApiPort = 24771, GamePort = 24770;
        public const string CacheFolder = "updates";
#endif
        public const string DefaultAddress = "123.241.56.94";

        /// The server.cfg the game reads, for installs that came as the bare exe.
        public static string EmbeddedConfig()
        {
            return "[server]\r\naddress=\"" + DefaultAddress + "\"\r\nname=\"" + ServerName + "\"\r\nport=" + ApiPort + "\r\ngame_port=" + GamePort + "\r\n";
        }

        public static string ReadOrigin(string installDir)
        {
            string address = DefaultAddress;
            int port = ApiPort;
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
            // Delta updates: optional, and only ever the list that belongs to this exact zip.
            if (requireServerFields && d.ContainsKey("files") && d["files"] as string == "/updates/files-" + m.Sha256 + ".json")
                m.Files = (string)d["files"];
            return m;
        }

        /// <summary>A relative path from a file list or zip that stays inside its folder.</summary>
        public static bool SafeRelative(string name)
        {
            return !string.IsNullOrEmpty(name) && name.Length < 400 && !name.StartsWith("/") && !name.Contains("..") && !name.Contains(":") && !name.Contains("\\");
        }

        /// <summary>Parses the delta file list; returns null and sets problem on anything unexpected.</summary>
        public static List<FileEntry> ParseFileList(string json, out string problem)
        {
            problem = null;
            Dictionary<string, object> d;
            try { d = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 }.DeserializeObject(json) as Dictionary<string, object>; }
            catch { d = null; }
            object raw;
            if (d == null || !d.TryGetValue("files", out raw) || !(raw is object[])) { problem = "檔案清單格式無效。"; return null; }
            var list = new List<FileEntry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long total = 0;
            foreach (object o in (object[])raw)
            {
                var e = o as Dictionary<string, object>;
                if (e == null || !(e.ContainsKey("path") && e.ContainsKey("sha256") && e.ContainsKey("size"))) { problem = "檔案清單項目無效。"; return null; }
                var f = new FileEntry { Path = e["path"] as string, Sha256 = e["sha256"] as string };
                double size;
                try { size = Convert.ToDouble(e["size"]); } catch { problem = "檔案大小無效。"; return null; }
                if (!SafeRelative(f.Path) || f.Sha256 == null || !HashRx.IsMatch(f.Sha256) || size < 0 || size != Math.Floor(size) || !seen.Add(f.Path)) { problem = "檔案清單項目無效。"; return null; }
                f.Size = (long)size;
                total += f.Size;
                list.Add(f);
            }
            if (list.Count == 0 || list.Count > 20000 || total > MaxPackSize * 2) { problem = "檔案清單超出限制。"; return null; }
            return list;
        }

        public static string Sha256Of(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
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
        // RIFTWARD_UPDATE_DIR: tests point the cache elsewhere so they never touch a real install.
        readonly string updateDir = Environment.GetEnvironmentVariable("RIFTWARD_UPDATE_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RiftWard", Updater.CacheFolder);
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

            // Delta update: copy unchanged files from the version already installed, download only the rest.
            // Anything unexpected falls through to the full download below.
            if (m.Files != null)
            {
                try { if (DeltaUpdate(origin, m)) return; }
                catch (Exception) { }
                SetProgress(0);
            }

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

        static string Get(string url, int timeout, long limit = 65536)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Timeout = timeout;
            req.AllowAutoRedirect = false;
            using (var res = (HttpWebResponse)req.GetResponse())
            {
                if (res.StatusCode != HttpStatusCode.OK || res.ContentLength > limit) return null;
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

        string VersionFolder(Manifest m) { return Path.Combine(updateDir, "game-" + m.Version + "-" + m.Sha256.Substring(0, 16)); }

        /// <summary>Delta update. Builds the new version's folder from the server's file list: a file whose hash
        /// matches one in an installed version (or the bundled package) is copied locally, the rest are
        /// downloaded one by one and SHA-256 checked. Returns false (nothing launched) when there is no
        /// installed version to build from or the list cannot be read; exceptions also mean "use the zip".</summary>
        bool DeltaUpdate(string origin, Manifest m)
        {
            string target = VersionFolder(m);
            string marker = Path.Combine(target, ".verified");
            if (File.Exists(marker) && File.ReadAllText(marker) == m.Sha256) { StartGame(m, target, null); return true; }

            SetStatus("正在比對遊戲檔案…", "只下載這次有變動的檔案");
            string json = Get(origin + m.Files, 15000, 16 * 1024 * 1024);
            string problem;
            var files = json == null ? null : Updater.ParseFileList(json, out problem);
            if (files == null) return false;

            // Installed versions to copy from, newest first; a fresh install builds one from its bundled package.
            var bases = new List<string>();
            foreach (string dir in Directory.GetDirectories(updateDir, "game-*"))
            {
                if (dir.EndsWith(".partial")) continue;
                if (string.Equals(Path.GetFullPath(dir).TrimEnd('\\'), Path.GetFullPath(target).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) continue;
                if (File.Exists(Path.Combine(dir, ".verified"))) bases.Add(dir);
            }
            bases.Sort((a, b) => Directory.GetLastWriteTimeUtc(b).CompareTo(Directory.GetLastWriteTimeUtc(a)));
            if (bases.Count == 0)
            {
                string metadata = Path.Combine(installDir, "game", "manifest.json");
                string bundled = Path.Combine(installDir, "game", "RiftWard.zip");
                Manifest own = File.Exists(metadata) ? Updater.Parse(File.ReadAllText(metadata), false, out problem) : null;
                if (own == null || Updater.Verify(bundled, own.Size, own.Sha256) != null) return false;
                SetStatus("正在準備遊戲檔案…", "版本 " + own.Version);
                bases.Add(Updater.Extract(bundled, VersionFolder(own), own.Sha256));
            }

            // Work in a side folder (kept across retries, so finished files are not fetched twice), then move it in.
            string partial = target + ".partial";
            Directory.CreateDirectory(partial);
            string root = Path.GetFullPath(partial) + Path.DirectorySeparatorChar;
            var missing = new List<FileEntry>();
            long reused = 0;
            foreach (var f in files)
            {
                string dest = Path.GetFullPath(Path.Combine(partial, f.Path));
                if (!dest.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return false;
                if (File.Exists(dest) && new FileInfo(dest).Length == f.Size && Updater.Sha256Of(dest) == f.Sha256) { reused += f.Size; continue; }
                string from = null;
                foreach (string b in bases)
                {
                    string candidate = Path.Combine(b, f.Path);
                    if (File.Exists(candidate) && new FileInfo(candidate).Length == f.Size && Updater.Sha256Of(candidate) == f.Sha256) { from = candidate; break; }
                }
                if (from == null) { missing.Add(f); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                File.Copy(from, dest, true);
                reused += f.Size;
            }

            long need = 0;
            foreach (var f in missing) need += f.Size;
            if (need > m.Size) return false;   // most files changed: the compressed zip is the smaller download
            long done = 0;
            SetStatus("正在下載 " + m.Version + " 的更新檔案", string.Format("{0} 個檔案 · {1:0.0} MB（其餘 {2:0.0} MB 沿用本機檔案）", missing.Count, need / 1048576.0, reused / 1048576.0));
            foreach (var f in missing)
            {
                string dest = Path.GetFullPath(Path.Combine(partial, f.Path));
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                string temp = dest + ".part";
                long before = done;
                for (int attempt = 0; ; attempt++)
                {
                    try { FetchBlob(origin + "/updates/blobs/" + f.Sha256, temp, f.Size, before, need); break; }
                    catch (Exception) { if (attempt >= 4) throw; Thread.Sleep(1500 * (attempt + 1)); }
                }
                if (Updater.Sha256Of(temp) != f.Sha256) { File.Delete(temp); throw new InvalidDataException("hash mismatch " + f.Path); }
                if (File.Exists(dest)) File.Delete(dest);
                File.Move(temp, dest);
                done += f.Size;
            }

            File.WriteAllText(Path.Combine(partial, ".verified"), m.Sha256);
            if (Directory.Exists(target)) Directory.Delete(target, true);
            Directory.Move(partial, target);
            SetProgress(1);
            StartGame(m, target, null);
            return true;
        }

        void FetchBlob(string url, string path, long size, long before, long need)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Timeout = 45000;
            req.ReadWriteTimeout = 45000;
            req.AllowAutoRedirect = false;
            using (var res = (HttpWebResponse)req.GetResponse())
            using (var input = res.GetResponseStream())
            using (var output = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                if (res.StatusCode != HttpStatusCode.OK) throw new IOException("HTTP " + (int)res.StatusCode);
                var buffer = new byte[81920];
                long total = 0;
                int read, lastTick = 0;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    total += read;
                    if (total > size) throw new IOException("larger than listed");
                    output.Write(buffer, 0, read);
                    if (need > 0 && Environment.TickCount - lastTick > 100)
                    {
                        lastTick = Environment.TickCount;
                        SetProgress((float)(before + total) / need);
                    }
                }
                if (total != size) throw new IOException("connection ended early");
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
                string keepZ = keepZip == null ? "" : Path.GetFullPath(keepZip);
                foreach (string dir in Directory.GetDirectories(updateDir, "game-*"))
                {
                    if (dir.EndsWith(".partial") && !string.Equals(Path.GetFullPath(dir), keepF + ".partial", StringComparison.OrdinalIgnoreCase)) { try { Directory.Delete(dir, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } continue; }
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
            try { folder = Updater.Extract(zip, VersionFolder(m), m.Sha256); }
            catch (Exception) { Fail("無法解壓遊戲檔案。請確認磁碟空間後重試。"); return; }
            StartGame(m, folder, zip);
        }

        void StartGame(Manifest m, string folder, string zip)
        {
            string cfg = Path.Combine(installDir, "server.cfg");
            if (File.Exists(cfg)) File.Copy(cfg, Path.Combine(folder, "server.cfg"), true);
            else File.WriteAllText(Path.Combine(folder, "server.cfg"), Updater.EmbeddedConfig());   // bare-exe install
            string exe = Path.Combine(folder, "RiftWard.exe");
            if (!File.Exists(exe)) { Fail("遊戲執行檔遺失，請重試。"); return; }
            SetStatus("驗證完成，正在啟動…", "版本 " + m.Version);
            CleanOldUpdates(folder, zip);
            // The game finds its way back here to update itself: the path as an argument, and in a note in the
            // update cache for copies started some other way.
            string self = Application.ExecutablePath;
            try { File.WriteAllText(Path.Combine(updateDir, "launcher.txt"), self); } catch (Exception) { }
            Process.Start(new ProcessStartInfo(exe, "--launcher \"" + self + "\"") { WorkingDirectory = folder, UseShellExecute = false });
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
            string listProblem;
            if (Updater.ParseFileList("{\"files\":[{\"path\":\"../x.exe\",\"sha256\":\"" + m.Sha256 + "\",\"size\":1}]}", out listProblem) != null)
            { Console.WriteLine("FAIL unsafe list path accepted"); return 1; }
            if (Updater.ParseFileList("{\"files\":[{\"path\":\"a/b.dll\",\"sha256\":\"" + m.Sha256 + "\",\"size\":3}]}", out listProblem) == null)
            { Console.WriteLine("FAIL list rejected: " + listProblem); return 1; }
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

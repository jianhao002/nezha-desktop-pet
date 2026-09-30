using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace NezhaDesktop
{
    public sealed class UpdateSettings
    {
        public string Host { get; set; }
        public string ProjectPath { get; set; }
        public string PackageName { get; set; }
        public string Channel { get; set; }

        public static UpdateSettings Load(string root)
        {
            string path = Path.Combine(root, "update-settings.json");
            UpdateSafety.AssertNoReparsePath(path);
            if (!File.Exists(path)) throw new InvalidOperationException("当前发布包没有更新设置，请下载完整新版发布包。");
            if (new FileInfo(path).Length > 16384) throw new InvalidDataException("更新设置文件过大。");
            UpdateSettings settings = UpdateSafety.ReadJson<UpdateSettings>(File.ReadAllText(path, Encoding.UTF8));
            settings.Validate();
            return settings;
        }
        public void Validate()
        {
            if (!String.Equals(Host, "https://jihulab.com", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("更新源必须是 https://jihulab.com。");
            if (String.IsNullOrWhiteSpace(ProjectPath) || ProjectPath.Length > 200 ||
                !Regex.IsMatch(ProjectPath, @"^[A-Za-z0-9_-][A-Za-z0-9_.-]*(/[A-Za-z0-9_-][A-Za-z0-9_.-]*)+$"))
                throw new InvalidDataException("更新项目路径无效。");
            if (ProjectPath.Split('/').Any(x => x == "." || x == ".." || x.EndsWith(".")))
                throw new InvalidDataException("更新项目路径无效。");
            if (PackageName != "nezha-desktop-pet" || Channel != "latest")
                throw new InvalidDataException("更新发布通道无效。");
        }
        public string GenericBaseUrl
        {
            get { Validate(); return Host.ToLowerInvariant() + "/api/v4/projects/" + Uri.EscapeDataString(ProjectPath) + "/packages/generic/" + PackageName + "/"; }
        }
        public Uri ManifestUri { get { return new Uri(GenericBaseUrl + Channel + "/update.json"); } }
        public Uri PackageUri(string version)
        {
            UpdateSafety.ParseVersion(version);
            return new Uri(GenericBaseUrl + version + "/nezha-desktop-pet-" + version + ".zip");
        }
    }

    public sealed class UpdateManifest
    {
        public string Version { get; set; }
        public string PackageUrl { get; set; }
        public string Sha256 { get; set; }
        public long Size { get; set; }
    }
    public sealed class UpdateCheck
    {
        public string CurrentVersion { get; internal set; }
        public string LatestVersion { get; internal set; }
        public bool Available { get; internal set; }
        public UpdateManifest Manifest { get; internal set; }
    }
    public sealed class PreparedUpdate
    {
        public string StageDirectory { get; internal set; }
        public string TargetVersion { get; internal set; }
    }
    public sealed class UpdatePlan
    {
        public string InstallRoot { get; set; }
        public string StageDirectory { get; set; }
        public string TargetVersion { get; set; }
        public int ParentProcessId { get; set; }
        public long ParentStartTimeUtcTicks { get; set; }
    }

    public sealed class UpdateClient
    {
        private readonly string root;
        private readonly UpdateSettings settings;
        public UpdateClient(string installRoot)
        {
            root = UpdateSafety.NormalizeRoot(installRoot);
            UpdateSafety.AssertNoReparsePath(root);
            settings = UpdateSettings.Load(root);
        }
        public UpdateCheck Check(string currentVersion)
        {
            Version current = UpdateSafety.ParseVersion(currentVersion);
            byte[] json = DownloadBytes(settings.ManifestUri, 65536);
            UpdateManifest manifest = UpdateSafety.ReadJson<UpdateManifest>(Encoding.UTF8.GetString(json));
            ValidateManifest(settings, manifest);
            Version latest = UpdateSafety.ParseVersion(manifest.Version);
            return new UpdateCheck { CurrentVersion = currentVersion, LatestVersion = manifest.Version,
                Available = latest.CompareTo(current) > 0, Manifest = manifest };
        }
        public PreparedUpdate Download(UpdateCheck update, Action<int, string> progress)
        {
            ValidateCheck(update);
            // Each operation gets its own directory. Existing failed updates and their backups are untouched.
            string stage = Path.Combine(root, ".update", Guid.NewGuid().ToString("N"));
            UpdateSafety.AssertNoReparsePath(stage);
            Directory.CreateDirectory(stage);
            try
            {
                string archive = Path.Combine(stage, "package.zip");
                Report(progress, 0, "正在下载新版发布包…");
                DownloadFile(settings.PackageUri(update.Manifest.Version), archive, update.Manifest.Size, progress);
                return PrepareArchive(stage, archive, update, progress);
            }
            catch
            {
                // Only remove the unique, prevalidated staging directory owned by this attempt.
                try { UpdateSafety.DeleteStage(stage, root); } catch { }
                throw;
            }
        }
        public PreparedUpdate PrepareFile(string archivePath, UpdateCheck update, Action<int, string> progress)
        {
            ValidateCheck(update);
            UpdateSafety.AssertNoReparsePath(archivePath);
            if (new FileInfo(archivePath).Length != update.Manifest.Size)
                throw new InvalidDataException("本地发布包大小与发布清单不一致。");
            string stage = Path.Combine(root, ".update", Guid.NewGuid().ToString("N"));
            UpdateSafety.AssertNoReparsePath(stage); Directory.CreateDirectory(stage);
            try
            {
                string archive = Path.Combine(stage, "package.zip");
                File.Copy(archivePath, archive, false);
                return PrepareArchive(stage, archive, update, progress);
            }
            catch { try { UpdateSafety.DeleteStage(stage, root); } catch { } throw; }
        }
        private void ValidateCheck(UpdateCheck update)
        {
            if (update == null || !update.Available || update.Manifest == null)
                throw new InvalidOperationException("没有可安装的新版本。");
            ValidateManifest(settings, update.Manifest);
            if (UpdateSafety.ParseVersion(update.Manifest.Version).CompareTo(UpdateSafety.ParseVersion(update.CurrentVersion)) <= 0)
                throw new InvalidDataException("拒绝安装相同或更旧的版本。");
        }
        private PreparedUpdate PrepareArchive(string stage, string archive, UpdateCheck update, Action<int, string> progress)
        {
            Report(progress, 85, "正在校验文件和 SHA-256…");
            if (new FileInfo(archive).Length != update.Manifest.Size ||
                !String.Equals(UpdateSafety.FileHash(archive), update.Manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("发布包校验失败，未改动当前程序。");
            string payload = Path.Combine(stage, "payload");
            UpdateSafety.ExtractPackage(archive, payload, update.Manifest.Version);
            UpdateSettings nextSettings = UpdateSettings.Load(payload);
            if (nextSettings.Host != settings.Host || nextSettings.ProjectPath != settings.ProjectPath ||
                nextSettings.PackageName != settings.PackageName || nextSettings.Channel != settings.Channel)
                throw new InvalidDataException("发布包试图改变更新仓库，已拒绝安装。");
            Report(progress, 100, "新版已校验，将保存进度并重启。");
            return new PreparedUpdate { StageDirectory = stage, TargetVersion = update.Manifest.Version };
        }
        public void Launch(PreparedUpdate update, int parentProcessId)
        {
            if (update == null) throw new ArgumentNullException("update");
            UpdateSafety.ValidateStage(update.StageDirectory, root);
            UpdateSafety.ValidatePayload(Path.Combine(update.StageDirectory, "payload"), update.TargetVersion);
            string updater = Path.Combine(root, "NezhaUpdater.exe");
            UpdateSafety.AssertNoReparsePath(updater);
            if (!File.Exists(updater)) throw new FileNotFoundException("缺少 NezhaUpdater.exe，请完整解压发布包。", updater);
            string runner = Path.Combine(update.StageDirectory, "runner.exe");
            File.Copy(updater, runner, false);
            long parentStartTime;
            using (Process parent = Process.GetProcessById(parentProcessId)) parentStartTime = parent.StartTime.ToUniversalTime().Ticks;
            UpdatePlan plan = new UpdatePlan { InstallRoot = root, StageDirectory = update.StageDirectory,
                TargetVersion = update.TargetVersion, ParentProcessId = parentProcessId, ParentStartTimeUtcTicks = parentStartTime };
            string planPath = Path.Combine(update.StageDirectory, "plan.json");
            File.WriteAllText(planPath, UpdateSafety.WriteJson(plan), new UTF8Encoding(false));
            ProcessStartInfo start = new ProcessStartInfo(runner, "--apply " + UpdateSafety.QuoteArgument(planPath)) {
                WorkingDirectory = update.StageDirectory, UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            using (Process process = Process.Start(start))
            {
                if (process == null) throw new InvalidOperationException("无法启动更新助手。");
            }
        }
        public static void ValidateManifest(UpdateSettings settings, UpdateManifest manifest)
        {
            if (manifest == null) throw new InvalidDataException("更新清单为空。");
            UpdateSafety.ParseVersion(manifest.Version);
            if (manifest.Size <= 0 || manifest.Size > UpdateSafety.MaxArchiveBytes)
                throw new InvalidDataException("发布包大小无效。");
            if (String.IsNullOrEmpty(manifest.Sha256) || !Regex.IsMatch(manifest.Sha256, @"^[A-Fa-f0-9]{64}$"))
                throw new InvalidDataException("发布包 SHA-256 无效。");
            Uri source;
            if (!Uri.TryCreate(manifest.PackageUrl, UriKind.Absolute, out source) || source.UserInfo.Length != 0 ||
                source.Query.Length != 0 || source.Fragment.Length != 0 ||
                !String.Equals(source.AbsoluteUri, settings.PackageUri(manifest.Version).AbsoluteUri, StringComparison.Ordinal))
                throw new InvalidDataException("发布包地址不属于当前仓库的正式版本目录。");
        }
        private static byte[] DownloadBytes(Uri uri, int maximum)
        {
            HttpWebRequest request = CreateRequest(uri);
            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            {
                CheckResponse(response, maximum);
                using (Stream input = response.GetResponseStream())
                using (MemoryStream output = new MemoryStream())
                {
                    byte[] buffer = new byte[8192]; int read;
                    while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        if (output.Length + read > maximum) throw new InvalidDataException("更新清单超过大小限制。");
                        output.Write(buffer, 0, read);
                    }
                    return output.ToArray();
                }
            }
        }
        private static void DownloadFile(Uri uri, string destination, long size, Action<int, string> progress)
        {
            HttpWebRequest request = CreateRequest(uri);
            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            {
                CheckResponse(response, size);
                if (response.ContentLength >= 0 && response.ContentLength != size)
                    throw new InvalidDataException("下载文件大小与发布清单不一致。");
                using (Stream input = response.GetResponseStream())
                using (FileStream output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    byte[] buffer = new byte[65536]; int read; long total = 0; int last = -1;
                    Stopwatch timer = Stopwatch.StartNew();
                    while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        if (timer.Elapsed > TimeSpan.FromMinutes(5)) throw new IOException("下载超时，请稍后重试。");
                        total += read;
                        if (total > size) throw new InvalidDataException("下载文件超出发布清单大小。");
                        output.Write(buffer, 0, read);
                        int percent = (int)(total * 80 / size);
                        if (percent != last) { last = percent; Report(progress, percent, "正在下载新版发布包… " + percent + "%"); }
                    }
                    if (total != size) throw new InvalidDataException("下载文件不完整，请稍后重试。");
                    output.Flush(true);
                }
            }
        }
        private static HttpWebRequest CreateRequest(Uri uri)
        {
            if (uri.Scheme != "https" || uri.Host != "jihulab.com" || !uri.IsDefaultPort)
                throw new InvalidDataException("更新仅允许使用极狐 GitLab 的 HTTPS 地址。");
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(uri);
            request.Method = "GET"; request.AllowAutoRedirect = false; request.Timeout = 20000;
            request.ReadWriteTimeout = 20000; request.UserAgent = "NezhaDesktopPet-Updater/1.0";
            request.AutomaticDecompression = DecompressionMethods.None;
            return request;
        }
        private static void CheckResponse(HttpWebResponse response, long maximum)
        {
            if (response.StatusCode != HttpStatusCode.OK) throw new IOException("更新源暂时不可用，HTTP " + (int)response.StatusCode + "。");
            if (response.ContentLength > maximum) throw new InvalidDataException("更新文件超过大小限制。");
        }
        private static void Report(Action<int, string> progress, int percent, string message)
        { if (progress != null) progress(percent, message); }
    }

    public static class UpdateSafety
    {
        public const long MaxArchiveBytes = 160L * 1024 * 1024;
        public const long MaxExpandedBytes = 250L * 1024 * 1024;
        private static readonly string[] RequiredFiles = { "NezhaPet.exe", "NezhaUpdater.exe", "config.json", "README.md", "update-settings.json" };
        private static readonly string[] ImageExtensions = { ".png", ".ico", ".webp", ".gif", ".jpg", ".jpeg" };
        public static T ReadJson<T>(string json)
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = 262144, RecursionLimit = 32 };
            T value = serializer.Deserialize<T>(json);
            if (value == null) throw new InvalidDataException("JSON 文件为空。");
            return value;
        }
        public static string WriteJson(object value) { return new JavaScriptSerializer().Serialize(value); }
        public static Version ParseVersion(string value)
        {
            if (String.IsNullOrEmpty(value) || !Regex.IsMatch(value, @"^(0|[1-9][0-9]{0,5})\.(0|[1-9][0-9]{0,5})\.(0|[1-9][0-9]{0,5})$"))
                throw new InvalidDataException("版本号必须为三个数字，例如 0.3.0。");
            return Version.Parse(value);
        }
        public static string NormalizeRoot(string root)
        {
            string full = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (full.Length < 4 || String.Equals(full + Path.DirectorySeparatorChar, Path.GetPathRoot(full), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("禁止使用磁盘根目录作为安装目录。");
            return full;
        }
        public static void AssertNoReparsePath(string path)
        {
            string current = Path.GetFullPath(path);
            while (!String.IsNullOrEmpty(current))
            {
                if ((File.Exists(current) || Directory.Exists(current)) &&
                    (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("更新目录包含链接或重解析点，请将桌宠放在普通文件夹中。");
                DirectoryInfo parent = Directory.GetParent(current);
                current = parent == null ? null : parent.FullName;
            }
        }
        public static void ValidateStage(string stage, string root)
        {
            root = NormalizeRoot(root);
            stage = NormalizeRoot(stage);
            string parent = Path.GetDirectoryName(stage);
            if (!String.Equals(parent, Path.Combine(root, ".update"), StringComparison.OrdinalIgnoreCase) ||
                !Regex.IsMatch(Path.GetFileName(stage), @"^[a-f0-9]{32}$"))
                throw new InvalidDataException("更新暂存目录不属于当前安装目录。");
            AssertNoReparsePath(stage);
        }
        public static void DeleteStage(string stage, string root)
        {
            ValidateStage(stage, root);
            if (Directory.Exists(stage)) { AssertTreeNoReparse(stage); Directory.Delete(stage, true); }
        }
        public static string FileHash(string path)
        {
            using (SHA256 hash = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
                return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }
        public static string InstallMutexName(string root)
        {
            using (SHA256 hash = SHA256.Create())
                return "Local\\NezhaPet_" + BitConverter.ToString(hash.ComputeHash(
                    Encoding.UTF8.GetBytes(NormalizeRoot(root).ToUpperInvariant()))).Replace("-", "");
        }
        public static string QuoteArgument(string value)
        {
            if (value == null || value.IndexOf('"') >= 0 || value.IndexOf('\r') >= 0 || value.IndexOf('\n') >= 0)
                throw new ArgumentException("无效的进程参数。");
            // These arguments are absolute file paths, never trailing directory separators.
            return "\"" + value + "\"";
        }
        public static void ExtractPackage(string archivePath, string payload, string expectedVersion)
        {
            AssertNoReparsePath(archivePath); AssertNoReparsePath(payload);
            if (Directory.Exists(payload)) throw new IOException("更新解压目录已存在。");
            using (ZipArchive archive = ZipFile.OpenRead(archivePath))
            {
                if (archive.Entries.Count > 1000) throw new InvalidDataException("发布包文件过多。");
                HashSet<string> entries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                long expanded = 0;
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    bool directory = entry.FullName.EndsWith("/", StringComparison.Ordinal);
                    string relative = ValidateRelativePath(entry.FullName, directory);
                    if (!entries.Add(relative)) throw new InvalidDataException("发布包包含重复路径。");
                    int mode = (entry.ExternalAttributes >> 16) & 0xF000;
                    if (mode == 0xA000 || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                        throw new InvalidDataException("发布包禁止包含文件链接。");
                    if (entry.Length > 64L * 1024 * 1024 || (directory && entry.Length != 0))
                        throw new InvalidDataException("发布包文件大小无效。");
                    expanded += entry.Length;
                    if (expanded > MaxExpandedBytes) throw new InvalidDataException("发布包解压后超过大小限制。");
                }
                Directory.CreateDirectory(payload);
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    bool directory = entry.FullName.EndsWith("/", StringComparison.Ordinal);
                    string relative = ValidateRelativePath(entry.FullName, directory);
                    string destination = Path.Combine(payload, relative.Replace('/', Path.DirectorySeparatorChar));
                    if (directory) { Directory.CreateDirectory(destination); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    using (Stream input = entry.Open())
                    using (FileStream output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        byte[] buffer = new byte[65536]; int read; long count = 0;
                        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            count += read;
                            if (count > entry.Length) throw new InvalidDataException("发布包文件解压大小不一致。");
                            output.Write(buffer, 0, read);
                        }
                        if (count != entry.Length) throw new InvalidDataException("发布包文件解压不完整。");
                        output.Flush(true);
                    }
                }
            }
            ValidatePayload(payload, expectedVersion);
        }
        public static string ValidateRelativePath(string name, bool directory)
        {
            if (String.IsNullOrWhiteSpace(name) || name.Length > 180 || name.Contains("\\") || name.StartsWith("/") || name.Contains(":"))
                throw new InvalidDataException("发布包包含非法路径。");
            string trimmed = directory ? name.TrimEnd('/') : name;
            string[] parts = trimmed.Split('/');
            foreach (string part in parts)
            {
                if (String.IsNullOrEmpty(part) || part == "." || part == ".." || part.EndsWith(".") || part.EndsWith(" ") ||
                    !Regex.IsMatch(part, @"^[A-Za-z0-9_.-]+$") ||
                    Regex.IsMatch(part.Split('.')[0], @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$", RegexOptions.IgnoreCase))
                    throw new InvalidDataException("发布包包含 Windows 不支持的路径。");
            }
            if (directory)
            {
                if (!String.Equals(parts[0], "assets", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("发布包包含非素材目录。");
            }
            else if (parts.Length == 1)
            {
                if (!RequiredFiles.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
                    throw new InvalidDataException("发布包包含未授权根目录文件：" + trimmed);
            }
            else if (!String.Equals(parts[0], "assets", StringComparison.OrdinalIgnoreCase) ||
                !ImageExtensions.Contains(Path.GetExtension(trimmed), StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException("发布包仅允许程序文件和图片素材。");
            return trimmed;
        }
        public static void ValidatePayload(string payload, string expectedVersion)
        {
            ParseVersion(expectedVersion); AssertTreeNoReparse(payload);
            foreach (string name in RequiredFiles)
            {
                string path = Path.Combine(payload, name);
                if (!File.Exists(path) || new FileInfo(path).Length == 0)
                    throw new InvalidDataException("发布包缺少必要文件：" + name);
            }
            if (!File.Exists(Path.Combine(payload, "assets", "nezha.png")) || !File.Exists(Path.Combine(payload, "assets", "pet.ico")))
                throw new InvalidDataException("发布包缺少必要宠物素材。");
            foreach (string file in Directory.GetFiles(payload, "*", SearchOption.AllDirectories))
                ValidateRelativePath(file.Substring(payload.TrimEnd(Path.DirectorySeparatorChar).Length + 1).Replace('\\', '/'), false);
            foreach (string folder in Directory.GetDirectories(payload, "*", SearchOption.AllDirectories))
                ValidateRelativePath(folder.Substring(payload.TrimEnd(Path.DirectorySeparatorChar).Length + 1).Replace('\\', '/') + "/", true);
            string configPath = Path.Combine(payload, "config.json");
            if (new FileInfo(configPath).Length > 262144) throw new InvalidDataException("宠物配置过大。");
            Dictionary<string, object> config = ReadJson<Dictionary<string, object>>(File.ReadAllText(configPath, Encoding.UTF8));
            object version;
            if (!config.TryGetValue("version", out version) || !String.Equals(Convert.ToString(version, CultureInfo.InvariantCulture), expectedVersion, StringComparison.Ordinal))
                throw new InvalidDataException("发布包版本和发布清单不一致。");
            UpdateSettings.Load(payload);
        }
        public static void AssertTreeNoReparse(string path)
        {
            AssertNoReparsePath(path);
            if (!Directory.Exists(path)) throw new DirectoryNotFoundException("目录不存在：" + path);
            // Inspect each directory before traversing it; SearchOption.AllDirectories can follow links.
            Stack<string> pending = new Stack<string>(); pending.Push(path);
            while (pending.Count > 0)
            {
                string current = pending.Pop();
                foreach (string child in Directory.GetFileSystemEntries(current))
                {
                    FileAttributes attributes = File.GetAttributes(child);
                    if ((attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("更新目录中禁止存在文件链接。");
                    if ((attributes & FileAttributes.Directory) != 0) pending.Push(child);
                }
            }
        }
    }
}

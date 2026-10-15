using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace NezhaDesktop
{
    // Resolve the embedded SDK before any WebView2 type is loaded. Old update ZIP rules stay valid.
    internal static class WebRuntime
    {
        private static bool registered;
        private static IntPtr loader;
        private static readonly string[] Managed = { "Microsoft.Web.WebView2.Core", "Microsoft.Web.WebView2.Wpf" };
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryEx(string file, IntPtr reserved, uint flags);
        public static void Register()
        {
            if (registered) return; registered = true;
            AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs args) {
                string name = new AssemblyName(args.Name).Name;
                return Managed.Contains(name) ? Assembly.Load(Read("Nezha.WebSDK." + name + ".dll")) : null;
            };
        }
        internal static byte[] Read(string name)
        {
            using (Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)) {
                if (source == null) throw new InvalidDataException("缺少网页游戏组件，请完整安装这一版程序。");
                using (MemoryStream output = new MemoryStream()) { source.CopyTo(output); return output.ToArray(); }
            }
        }
        internal static string Digest(byte[] bytes)
        { using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
        internal static string Prepare(string data)
        {
            string cache = Path.Combine(data, "web-runtime", "1.0.3405.78"); UpdateSafety.AssertNoReparsePath(cache); Directory.CreateDirectory(cache);
            foreach (string name in Managed) WriteVerified(Path.Combine(cache, name + ".dll"), Read("Nezha.WebSDK." + name + ".dll"));
            if (loader == IntPtr.Zero) {
                string native = Path.Combine(cache, IntPtr.Size == 8 ? "x64" : "x86", "WebView2Loader.dll");
                WriteVerified(native, Read("Nezha.WebSDK." + (IntPtr.Size == 8 ? "x64" : "x86") + ".dll"));
                loader = LoadLibraryEx(native, IntPtr.Zero, 0x00000100);
                if (loader == IntPtr.Zero) throw new IOException("网页组件无法加载，错误 " + Marshal.GetLastWin32Error());
            }
            return cache;
        }
        private static void WriteVerified(string file, byte[] bytes)
        {
            UpdateSafety.AssertNoReparsePath(file); Directory.CreateDirectory(Path.GetDirectoryName(file));
            if (File.Exists(file) && UpdateSafety.FileHash(file) == Digest(bytes)) return;
            string temporary = file + ".tmp-" + Guid.NewGuid().ToString("N");
            try { File.WriteAllBytes(temporary, bytes); if (File.Exists(file)) File.Replace(temporary, file, null); else File.Move(temporary, file); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        internal static string Assets(string data)
        {
            byte[] bytes = Read("Nezha.WebGames.gridland.zip"); string hash = Digest(bytes);
            string folder = Path.Combine(data, "web-assets", "gridland", hash); UpdateSafety.AssertNoReparsePath(folder);
            string marker = Path.Combine(folder, ".complete");
            UpdateSafety.AssertNoReparsePath(marker); UpdateSafety.AssertNoReparsePath(Path.Combine(folder, "www", "index.html"));
            if (File.Exists(marker) && File.Exists(Path.Combine(folder, "www", "index.html"))) return Path.Combine(folder, "www");
            Directory.CreateDirectory(folder);
            using (MemoryStream input = new MemoryStream(bytes))
            using (ZipArchive zip = new ZipArchive(input, ZipArchiveMode.Read)) {
                if (zip.Entries.Count > 1000) throw new InvalidDataException("网页资源数量异常。");
                long total = 0;
                foreach (ZipArchiveEntry entry in zip.Entries) {
                    if (entry.FullName.EndsWith("/")) continue;
                    if (entry.FullName.Contains("\\") || entry.FullName.Contains(":") || entry.FullName.Split('/').Any(p => p == ".." || p == "." || p.Length == 0)) throw new InvalidDataException("网页资源路径异常。");
                    total += entry.Length; if (entry.Length > 20000000 || total > 60000000) throw new InvalidDataException("网页资源过大。");
                    string file = Path.GetFullPath(Path.Combine(folder, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
                    if (!file.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("网页资源越界。");
                    UpdateSafety.AssertNoReparsePath(file); Directory.CreateDirectory(Path.GetDirectoryName(file));
                    using (Stream source = entry.Open()) using (FileStream output = File.Create(file)) source.CopyTo(output);
                }
            }
            File.WriteAllText(marker, hash); return Path.Combine(folder, "www");
        }
        internal static string[] CompilerReferences(string data)
        { string folder = Prepare(data); return Managed.Select(name => Path.Combine(folder, name + ".dll")).ToArray(); }
    }
}

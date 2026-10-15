using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using Microsoft.CSharp;

namespace NezhaDesktop.MiniGames
{
    public sealed class MiniGameManifest
    {
        public int ApiVersion { get; set; }
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Author { get; set; }
        public string EntryType { get; set; }
        public string[] Sources { get; set; }
    }
    public sealed class MiniGameEntry
    {
        public MiniGameManifest Manifest { get; internal set; }
        public string Directory { get; internal set; }
    }
    public sealed class MiniGameCatalog
    {
        private readonly string root, data;
        public readonly List<string> Errors = new List<string>();
        public string GamesDirectory { get { return Path.Combine(root, "games"); } }
        public MiniGameCatalog(string root, string data)
        { this.root = Path.GetFullPath(root); this.data = Path.GetFullPath(data); }

        public List<MiniGameEntry> Scan()
        {
            Errors.Clear(); InstallBundled();
            List<MiniGameEntry> entries = new List<MiniGameEntry>();
            UpdateSafety.AssertNoReparsePath(GamesDirectory);
            if (!Directory.Exists(GamesDirectory)) return entries;
            foreach (string folder in Directory.GetDirectories(GamesDirectory).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                try {
                    UpdateSafety.AssertNoReparsePath(folder);
                    string manifestPath = Path.Combine(folder, "game.json"); UpdateSafety.AssertNoReparsePath(manifestPath);
                    if (!File.Exists(manifestPath) || new FileInfo(manifestPath).Length > 65536) throw new InvalidDataException("缺少或无效的 game.json。");
                    MiniGameManifest spec = new JavaScriptSerializer { MaxJsonLength = 65536 }.Deserialize<MiniGameManifest>(File.ReadAllText(manifestPath, Encoding.UTF8));
                    if (spec == null || spec.ApiVersion != 1) throw new InvalidDataException("需要 ApiVersion 1。");
                    if (spec.Id == null || !Regex.IsMatch(spec.Id, "^[a-z0-9][a-z0-9-]{0,47}$") || spec.Id != Path.GetFileName(folder))
                        throw new InvalidDataException("Id 需要与文件夹名称一致。");
                    if (String.IsNullOrWhiteSpace(spec.Name) || spec.Name.Length > 48 || (spec.Description ?? "").Length > 240 || (spec.Author ?? "").Length > 80)
                        throw new InvalidDataException("游戏描述过长或没有名称。");
                    if (spec.EntryType == null || !Regex.IsMatch(spec.EntryType, "^[A-Za-z_][A-Za-z0-9_.]{0,159}$"))
                        throw new InvalidDataException("游戏入口类无效。");
                    if (spec.Sources == null || spec.Sources.Length == 0 || spec.Sources.Length > 8 || spec.Sources.Distinct(StringComparer.OrdinalIgnoreCase).Count() != spec.Sources.Length)
                        throw new InvalidDataException("需要 1～8 个不同的 C# 源文件。");
                    foreach (string source in spec.Sources)
                    {
                        if (source == null || !Regex.IsMatch(source, "^[A-Za-z0-9_-][A-Za-z0-9_.-]{0,79}\\.cs$") || source.Contains(".."))
                            throw new InvalidDataException("源文件必须位于本游戏文件夹。");
                        string path = Path.Combine(folder, source); UpdateSafety.AssertNoReparsePath(path);
                        if (!File.Exists(path) || new FileInfo(path).Length > 524288) throw new InvalidDataException("源文件不存在或过大。");
                    }
                    entries.Add(new MiniGameEntry { Directory = folder, Manifest = spec });
                } catch (Exception ex) { Errors.Add(Path.GetFileName(folder) + "：" + ex.Message); }
            }
            return entries;
        }
        // Compilation and constructors happen only after the player chooses a game.
        public IMiniGame Create(MiniGameEntry entry, Func<PetSnapshot> snapshot, Action<string> notice)
        {
            return Create(entry, Compile(entry), snapshot, notice);
        }
        public IMiniGame Create(MiniGameEntry entry, Type type, Func<PetSnapshot> snapshot, Action<string> notice)
        {
            IMiniGame game = (IMiniGame)Activator.CreateInstance(type);
            try {
                game.Initialize(new MiniGameContext(entry.Directory, Path.Combine(data, "scores", entry.Manifest.Id), snapshot, notice));
                if (game.View == null) throw new InvalidDataException("游戏未提供界面。");
                return game;
            } catch { game.Dispose(); throw; }
        }
        public Type Compile(MiniGameEntry entry)
        {
            string temporary = Path.Combine(data, "compiler", Guid.NewGuid().ToString("N"));
            UpdateSafety.AssertNoReparsePath(temporary); Directory.CreateDirectory(temporary);
            CompilerParameters options = new CompilerParameters { GenerateExecutable = false, GenerateInMemory = true,
                CompilerOptions = "/optimize+ /utf8output /codepage:65001", TempFiles = new TempFileCollection(temporary, false) };
            string framework = RuntimeEnvironment.GetRuntimeDirectory();
            foreach (string reference in new[] { "System.dll", "System.Core.dll", "System.Xaml.dll", "System.Web.Extensions.dll",
                "WPF/WindowsBase.dll", "WPF/PresentationCore.dll", "WPF/PresentationFramework.dll" })
                options.ReferencedAssemblies.Add(Path.Combine(framework, reference.Replace('/', Path.DirectorySeparatorChar)));
            options.ReferencedAssemblies.Add(Assembly.GetExecutingAssembly().Location);
            if (entry.Manifest.Id == "gridland") foreach (string reference in WebRuntime.CompilerReferences(data)) options.ReferencedAssemblies.Add(reference);
            try {
                using (CSharpCodeProvider compiler = new CSharpCodeProvider(new Dictionary<string, string> { { "CompilerVersion", "v4.0" } }))
                {
                    CompilerResults compiled = compiler.CompileAssemblyFromFile(options, entry.Manifest.Sources.Select(s => Path.Combine(entry.Directory, s)).ToArray());
                    if (compiled.Errors.HasErrors)
                        throw new InvalidDataException("游戏编译失败：" + String.Join("；", compiled.Errors.Cast<CompilerError>().Where(e => !e.IsWarning).Take(3).Select(e => e.ErrorText)));
                    Type type = compiled.CompiledAssembly.GetType(entry.Manifest.EntryType, true);
                    if (!typeof(IMiniGame).IsAssignableFrom(type) || type.IsAbstract || type.GetConstructor(Type.EmptyTypes) == null)
                        throw new InvalidDataException("入口需要实现 IMiniGame 并有无参数构造函数。");
                    return type;
                }
            } finally { options.TempFiles.Delete(); if (Directory.Exists(temporary) && Directory.GetFileSystemEntries(temporary).Length == 0) Directory.Delete(temporary); }
        }
        private void InstallBundled()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            const string prefix = "Nezha.MiniGames.";
            Dictionary<string, string> previous = MiniGameFiles.ReadJson<Dictionary<string, string>>(Path.Combine(data, "bundled-index.json")) ?? new Dictionary<string, string>();
            Dictionary<string, string> next = new Dictionary<string, string>(previous);
            foreach (IGrouping<string, string> group in assembly.GetManifestResourceNames().Where(n => n.StartsWith(prefix, StringComparison.Ordinal))
                .GroupBy(n => n.Substring(prefix.Length).Split('.')[0]))
            {
                string id = group.Key, folder = Path.Combine(GamesDirectory, id);
                if (!Regex.IsMatch(id, "^[a-z0-9][a-z0-9-]{0,47}$")) throw new InvalidDataException("内置游戏目录无效。");
                UpdateSafety.AssertNoReparsePath(folder);
                Dictionary<string, byte[]> files = new Dictionary<string, byte[]>();
                bool customized = false;
                foreach (string resource in group)
                {
                    string name = resource.Substring(prefix.Length + id.Length + 1);
                    if (Path.GetFileName(name) != name || !(name.EndsWith(".cs", StringComparison.Ordinal) || name == "game.json"))
                        throw new InvalidDataException("内置游戏资源名称无效。");
                    byte[] bytes;
                    using (Stream stream = assembly.GetManifestResourceStream(resource))
                    using (MemoryStream output = new MemoryStream()) { stream.CopyTo(output); bytes = output.ToArray(); }
                    files[name] = bytes;
                    string target = Path.Combine(folder, name), key = id + "/" + name; UpdateSafety.AssertNoReparsePath(target);
                    if (File.Exists(target)) {
                        string current = UpdateSafety.FileHash(target), known;
                        if (current != Hash(bytes) && (!previous.TryGetValue(key, out known) || current != known)) customized = true;
                    }
                }
                // Respect edited or independently installed folders, including their source code.
                if (customized) { Errors.Add(id + "：保留本机修改的内置游戏。"); continue; }
                Directory.CreateDirectory(folder);
                foreach (KeyValuePair<string, byte[]> file in files)
                {
                    string target = Path.Combine(folder, file.Key), hash = Hash(file.Value);
                    if (!File.Exists(target) || UpdateSafety.FileHash(target) != hash) File.WriteAllBytes(target, file.Value);
                    next[id + "/" + file.Key] = hash;
                }
            }
            if (!previous.OrderBy(p => p.Key).SequenceEqual(next.OrderBy(p => p.Key)))
                MiniGameFiles.WriteJson(Path.Combine(data, "bundled-index.json"), next);
        }
        private static string Hash(byte[] bytes)
        { using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
    }
}

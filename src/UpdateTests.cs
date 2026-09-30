using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace NezhaDesktop
{
    // Local fixtures only. These tests never call the real GitLab update source.
    public static class UpdateTests
    {
        public static int Run(string root, string output)
        {
            output = Path.GetFullPath(output);
            if (Directory.Exists(output) && Directory.GetFileSystemEntries(output).Length != 0)
                throw new IOException("更新测试需要空的输出目录，避免覆盖之前的报告。");
            Directory.CreateDirectory(output);
            List<string> results = new List<string>(); int failures = 0;
            Action<string, Action> test = delegate(string name, Action body) {
                try { body(); results.Add("PASS " + name); }
                catch (Exception ex) { failures++; results.Add("FAIL " + name + ": " + ex.Message); }
            };
            UpdateSettings settings = new UpdateSettings { Host = "https://jihulab.com", ProjectId = 375689, ProjectPath = "personal-fixture/nezha-desktop-pet", PackageName = "nezha-desktop-pet", Channel = "latest" };

            test("strict-version-and-numeric-order", delegate {
                Check(UpdateSafety.ParseVersion("0.10.0") > UpdateSafety.ParseVersion("0.9.9"), "版本必须按数字比较");
                foreach (string invalid in new[] { "v0.3.0", "0.3", "0.3.0.1", "01.3.0", "0.3.0-beta", "-1.3.0", "1000000.3.0", "0.3.0 " })
                    Reject(delegate { UpdateSafety.ParseVersion(invalid); }, "接受了非法版本 " + invalid);
            });
            test("manifest-version-bound-download-url", delegate {
                UpdateManifest manifest = Manifest(settings, "0.4.0", new string('a', 64), 32);
                UpdateClient.ValidateManifest(settings, manifest);
                foreach (string badUrl in new[] { "http://jihulab.com/x.zip", "https://evil.example/x.zip", settings.PackageUri("0.4.0").AbsoluteUri + "?token=fixture", settings.PackageUri("0.4.0").AbsoluteUri + "#fragment", settings.PackageUri("0.3.0").AbsoluteUri })
                {
                    manifest.PackageUrl = badUrl;
                    Reject(delegate { UpdateClient.ValidateManifest(settings, manifest); }, "接受了错误来源 " + badUrl);
                }
            });
            test("manifest-hash-and-size-limits", delegate {
                UpdateManifest manifest = Manifest(settings, "0.4.0", new string('a', 64), 32);
                foreach (string invalid in new[] { "", new string('a', 63), new string('g', 64) })
                {
                    manifest.Sha256 = invalid;
                    Reject(delegate { UpdateClient.ValidateManifest(settings, manifest); }, "接受了非法哈希");
                }
                manifest.Sha256 = new string('a', 64);
                foreach (long invalid in new[] { 0L, -1L, UpdateSafety.MaxArchiveBytes + 1 })
                {
                    manifest.Size = invalid;
                    Reject(delegate { UpdateClient.ValidateManifest(settings, manifest); }, "接受了非法大小");
                }
            });
            test("settings-reject-another-host-or-project-traversal", delegate {
                new UpdateSettings { Host = settings.Host, ProjectId = settings.ProjectId, ProjectPath = settings.ProjectPath, PackageName = settings.PackageName, Channel = settings.Channel }.Validate();
                Reject(delegate { new UpdateSettings { Host = "https://company.example", ProjectId = settings.ProjectId, ProjectPath = settings.ProjectPath, PackageName = settings.PackageName, Channel = settings.Channel }.Validate(); }, "接受了其他主机");
                foreach (string path in new[] { "../pet", "personal/../pet", "personal/pet.", "personal//pet", "personal/pet?token=x" })
                    Reject(delegate { new UpdateSettings { Host = settings.Host, ProjectId = settings.ProjectId, ProjectPath = path, PackageName = settings.PackageName, Channel = settings.Channel }.Validate(); }, "接受了非法项目路径");
            });
            test("update-uri-uses-positive-numeric-project-id", delegate {
                Check(settings.GenericBaseUrl == "https://jihulab.com/api/v4/projects/375689/packages/generic/nezha-desktop-pet/", "更新地址未使用项目编号");
                Check(settings.ManifestUri.AbsoluteUri.Contains("/projects/375689/"), ".NET Uri 改变了项目编号路径");
                Check(!settings.ManifestUri.AbsoluteUri.Contains("%2F") && !settings.ManifestUri.AbsoluteUri.Contains(settings.ProjectPath), "更新地址仍依赖转义斜杠项目路径");
                Check(settings.PackageUri("0.4.0").AbsoluteUri == "https://jihulab.com/api/v4/projects/375689/packages/generic/nezha-desktop-pet/0.4.0/nezha-desktop-pet-0.4.0.zip", "正式版本包地址错误");
                foreach (int invalid in new[] { 0, -1 })
                    Reject(delegate { new UpdateSettings { Host = settings.Host, ProjectId = invalid, ProjectPath = settings.ProjectPath, PackageName = settings.PackageName, Channel = settings.Channel }.Validate(); }, "接受了无效项目编号");
            });
            test("zip-valid-package-extracts-in-chinese-space-directory", delegate {
                string folder = Folder(output, "正常发布 中文目录");
                string archive = MakeArchive(folder, "valid.zip", PackageEntries(settings, "0.4.0"));
                string payload = Path.Combine(folder, "新版 payload");
                UpdateSafety.ExtractPackage(archive, payload, "0.4.0");
                Check(File.Exists(Path.Combine(payload, "NezhaPet.exe")) && File.Exists(Path.Combine(payload, "assets", "nezha.png")), "必要文件未解压");
                Check(UpdateSafety.ReadJson<Dictionary<string, object>>(File.ReadAllText(Path.Combine(payload, "config.json")))["version"].ToString() == "0.4.0", "版本被改变");
            });
            test("zip-traversal-and-absolute-paths-rejected-before-extraction", delegate {
                int index = 0;
                foreach (string entry in new[] { "../outside.txt", "/NezhaPet.exe", "C:/NezhaPet.exe", "assets/../../config.json", "assets\\nezha.png" })
                    RejectArchive(output, settings, "path-" + index++, entry, false);
            });
            test("zip-protects-save-credentials-and-development-files", delegate {
                int index = 0;
                foreach (string entry in new[] { "data/save.json", "data/update-auth.bin", ".git/config", ".update/runner.exe", "src/Core.cs", "update.local.json" })
                    RejectArchive(output, settings, "protected-" + index++, entry, false);
            });
            test("zip-rejects-windows-special-paths-and-executable-assets", delegate {
                int index = 0;
                foreach (string entry in new[] { "assets/image.png:payload", "assets/NUL.png", "assets/COM1.png", "assets/image.png.", "assets/image.png ", "assets/evil.exe", "assets/sub//image.png" })
                    RejectArchive(output, settings, "windows-" + index++, entry, false);
            });
            test("zip-case-insensitive-duplicate-paths-rejected", delegate {
                RejectArchive(output, settings, "duplicate-case", "ASSETS/NEZHA.PNG", false);
                RejectArchive(output, settings, "duplicate-exact", "config.json", false);
            });
            test("zip-version-mismatch-preserves-existing-installation-and-save", delegate {
                string folder = Folder(output, "version-mismatch");
                string data = SeedProtectedFiles(folder);
                string oldHash = UpdateSafety.FileHash(data);
                string archive = MakeArchive(folder, "wrong-version.zip", PackageEntries(settings, "0.3.0"));
                Reject(delegate { UpdateSafety.ExtractPackage(archive, Path.Combine(folder, "payload"), "0.4.0"); }, "接受了包内错误版本");
                Check(UpdateSafety.FileHash(data) == oldHash, "失败更新改变了存档");
            });
            test("zip-missing-required-file-rejected", delegate {
                string folder = Folder(output, "missing-required");
                List<KeyValuePair<string, byte[]>> entries = PackageEntries(settings, "0.4.0");
                entries.RemoveAll(x => x.Key == "NezhaUpdater.exe");
                string archive = MakeArchive(folder, "missing.zip", entries);
                Reject(delegate { UpdateSafety.ExtractPackage(archive, Path.Combine(folder, "payload"), "0.4.0"); }, "接受了缺少更新助手的包");
            });
            test("zip-entry-count-limit-rejected-before-extraction", delegate {
                string folder = Folder(output, "entry-limit");
                List<KeyValuePair<string, byte[]>> entries = PackageEntries(settings, "0.4.0");
                for (int index = 0; index < 1001; index++) entries.Add(new KeyValuePair<string, byte[]>("assets/" + index + ".png", new byte[] { 1 }));
                string payload = Path.Combine(folder, "payload");
                string archive = MakeArchive(folder, "too-many.zip", entries);
                Reject(delegate { UpdateSafety.ExtractPackage(archive, payload, "0.4.0"); }, "接受了文件过多的包");
                Check(!Directory.Exists(payload), "拒绝前创建了解压目录");
            });
            test("staging-directory-cannot-delete-another-directory", delegate {
                string install = Folder(output, "stage-safety");
                string outside = Folder(output, "keep-this-directory");
                string marker = Path.Combine(outside, "keep.txt"); File.WriteAllText(marker, "keep");
                Reject(delegate { UpdateSafety.DeleteStage(outside, install); }, "允许删除外部目录");
                Check(File.Exists(marker), "删除了不属于此次更新的文件");
                string validStage = Path.Combine(install, ".update", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(validStage); UpdateSafety.ValidateStage(validStage, install);
                UpdateSafety.DeleteStage(validStage, install); Check(!Directory.Exists(validStage), "合法暂存目录未清理");
            });
            test("same-or-older-download-aborts-without-network-or-stage", delegate {
                string install = Folder(output, "downgrade");
                File.WriteAllText(Path.Combine(install, "update-settings.json"), UpdateSafety.WriteJson(settings));
                UpdateClient client = new UpdateClient(install);
                foreach (string version in new[] { "0.3.0", "0.2.9" })
                {
                    UpdateCheck update = new UpdateCheck { CurrentVersion = "0.3.0", LatestVersion = version, Available = true, Manifest = Manifest(settings, version, new string('a', 64), 1) };
                    Reject(delegate { client.Download(update, null); }, "接受了相同或更旧版本");
                    Check(!Directory.Exists(Path.Combine(install, ".update")), "拒绝降级前创建了暂存目录");
                }
            });
            test("local-preparation-verifies-bytes-hash-and-preserves-data", delegate {
                string install = Folder(output, "local-verified-package");
                File.WriteAllText(Path.Combine(install, "update-settings.json"), UpdateSafety.WriteJson(settings));
                string save = SeedProtectedFiles(install), before = UpdateSafety.FileHash(save);
                string archive = MakeArchive(install, "verified.zip", PackageEntries(settings, "0.4.0"));
                UpdateClient client = new UpdateClient(install);
                UpdateCheck check = new UpdateCheck { CurrentVersion = "0.3.0", LatestVersion = "0.4.0", Available = true,
                    Manifest = Manifest(settings, "0.4.0", UpdateSafety.FileHash(archive), new FileInfo(archive).Length) };
                PreparedUpdate prepared = client.PrepareFile(archive, check, null);
                Check(prepared.TargetVersion == "0.4.0" && File.Exists(Path.Combine(prepared.StageDirectory, "payload", "NezhaPet.exe")), "未生成可安装的已校验文件");
                Check(UpdateSafety.FileHash(save) == before, "准备更新时改变了存档");
                int stages = Directory.GetDirectories(Path.Combine(install, ".update")).Length;
                check.Manifest.Sha256 = new string('0', 64);
                Reject(delegate { client.PrepareFile(archive, check, null); }, "接受了哈希不匹配的包");
                Check(Directory.GetDirectories(Path.Combine(install, ".update")).Length == stages, "失败时未清理自己的暂存目录或删除了成功暂存目录");
                check.Manifest.Sha256 = UpdateSafety.FileHash(archive); check.Manifest.Size++;
                Reject(delegate { client.PrepareFile(archive, check, null); }, "接受了文件大小不匹配的包");
                Check(UpdateSafety.FileHash(save) == before, "损坏包改变了存档");
            });
            test("local-preparation-rejects-changing-the-update-repository", delegate {
                string install = Folder(output, "changed-repository");
                File.WriteAllText(Path.Combine(install, "update-settings.json"), UpdateSafety.WriteJson(settings));
                UpdateSettings changed = new UpdateSettings { Host = settings.Host, ProjectId = 376000, ProjectPath = "other-person/another-project", PackageName = settings.PackageName, Channel = settings.Channel };
                string archive = MakeArchive(install, "redirect-source.zip", PackageEntries(changed, "0.4.0"));
                UpdateCheck check = new UpdateCheck { CurrentVersion = "0.3.0", LatestVersion = "0.4.0", Available = true,
                    Manifest = Manifest(settings, "0.4.0", UpdateSafety.FileHash(archive), new FileInfo(archive).Length) };
                Reject(delegate { new UpdateClient(install).PrepareFile(archive, check, null); }, "接受了改变更新仓库的包");
            });
            test("sha256-known-value-and-command-argument-protection", delegate {
                string path = Path.Combine(output, "abc.txt"); File.WriteAllText(path, "abc", new UTF8Encoding(false));
                Check(UpdateSafety.FileHash(path) == "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", "SHA256 结果错误");
                Check(UpdateSafety.QuoteArgument("C:\\中文 空格\\plan.json") == "\"C:\\中文 空格\\plan.json\"", "进程参数未正确引用");
                Reject(delegate { UpdateSafety.QuoteArgument("x\" --apply other"); }, "接受了参数注入");
            });
            File.WriteAllLines(Path.Combine(output, "update-test-results.txt"), results.Concat(new[] { "TOTAL=" + results.Count, "FAILED=" + failures, "NETWORK=NOT_USED" }), new UTF8Encoding(false));
            return failures == 0 ? 0 : 1;
        }

        private static UpdateManifest Manifest(UpdateSettings settings, string version, string hash, long length)
        { return new UpdateManifest { Version = version, Sha256 = hash, Size = length, PackageUrl = settings.PackageUri(version).AbsoluteUri }; }
        private static string Folder(string output, string name)
        { string path = Path.Combine(output, name); Directory.CreateDirectory(path); return path; }
        private static string SeedProtectedFiles(string install)
        {
            string data = Path.Combine(install, "data"); Directory.CreateDirectory(data);
            string path = Path.Combine(data, "save.json"); File.WriteAllText(path, "{\"coins\":123,\"experience\":99,\"sentinel\":\"不得修改\"}", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(data, "update-auth.bin"), "fixture-local-secret", new UTF8Encoding(false));
            return path;
        }
        private static List<KeyValuePair<string, byte[]>> PackageEntries(UpdateSettings settings, string version)
        {
            Dictionary<string, string> files = new Dictionary<string, string> {
                { "NezhaPet.exe", "fixture-executable-not-run" }, { "NezhaUpdater.exe", "fixture-helper-not-run" },
                { "config.json", "{\"version\":\"" + version + "\"}" }, { "README.md", "fixture-readme" },
                { "update-settings.json", UpdateSafety.WriteJson(settings) }, { "assets/nezha.png", "fixture-image-not-rendered" }, { "assets/pet.ico", "fixture-icon-not-rendered" }
            };
            return files.Select(x => new KeyValuePair<string, byte[]>(x.Key, Encoding.UTF8.GetBytes(x.Value))).ToList();
        }
        private static string MakeArchive(string folder, string name, IEnumerable<KeyValuePair<string, byte[]>> entries)
        {
            string path = Path.Combine(folder, name);
            using (ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create))
                foreach (KeyValuePair<string, byte[]> file in entries)
                    using (Stream stream = archive.CreateEntry(file.Key, CompressionLevel.Optimal).Open())
                        stream.Write(file.Value, 0, file.Value.Length);
            return path;
        }
        private static void RejectArchive(string output, UpdateSettings settings, string name, string hostilePath, bool unused)
        {
            string folder = Folder(output, name); string save = SeedProtectedFiles(folder);
            string savedHash = UpdateSafety.FileHash(save), authPath = Path.Combine(folder, "data", "update-auth.bin"), authHash = UpdateSafety.FileHash(authPath);
            List<KeyValuePair<string, byte[]>> entries = PackageEntries(settings, "0.4.0");
            entries.Add(new KeyValuePair<string, byte[]>(hostilePath, Encoding.UTF8.GetBytes("must-not-extract")));
            string archive = MakeArchive(folder, "hostile.zip", entries), payload = Path.Combine(folder, "payload");
            Reject(delegate { UpdateSafety.ExtractPackage(archive, payload, "0.4.0"); }, "接受了恶意路径 " + hostilePath);
            Check(!Directory.Exists(payload), "扫描所有路径前已写入文件");
            Check(UpdateSafety.FileHash(save) == savedHash && UpdateSafety.FileHash(authPath) == authHash, "拒绝更新却改变了存档或设备凭证");
        }
        private static void Reject(Action action, string message)
        {
            bool rejected = false;
            try { action(); } catch (InvalidDataException) { rejected = true; } catch (ArgumentException) { rejected = true; } catch (InvalidOperationException) { rejected = true; }
            if (!rejected) throw new Exception(message);
        }
        private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    }
}

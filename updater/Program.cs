using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace NezhaDesktop.Updater
{
    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            string stage = null;
            bool quiet = args.Length == 3 && args[2] == "--quiet";
            try
            {
                if ((args.Length != 2 && !quiet) || args[0] != "--apply") throw new ArgumentException("此更新助手由桌宠自动调用，请直接运行 NezhaPet.exe。");
                string planPath = Path.GetFullPath(args[1]);
                UpdateSafety.AssertNoReparsePath(planPath);
                if (new FileInfo(planPath).Length > 16384) throw new InvalidDataException("更新计划文件过大。");
                UpdatePlan plan = UpdateSafety.ReadJson<UpdatePlan>(File.ReadAllText(planPath, Encoding.UTF8));
                plan.InstallRoot = UpdateSafety.NormalizeRoot(plan.InstallRoot);
                plan.StageDirectory = UpdateSafety.NormalizeRoot(plan.StageDirectory);
                UpdateSafety.ValidateStage(plan.StageDirectory, plan.InstallRoot);
                if (!String.Equals(planPath, Path.Combine(plan.StageDirectory, "plan.json"), StringComparison.OrdinalIgnoreCase) ||
                    !String.Equals(UpdateSafety.NormalizeRoot(AppDomain.CurrentDomain.BaseDirectory), plan.StageDirectory, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("更新助手和计划文件不在同一暂存目录。");
                stage = plan.StageDirectory;
                return UpdateInstaller.Apply(plan, false);
            }
            catch (Exception ex)
            {
                if (stage != null) { try { File.WriteAllText(Path.Combine(stage, "error.txt"), ex.ToString(), Encoding.UTF8); } catch { } }
                if (!quiet) MessageBox.Show("更新没有完成：" + ex.Message + "\n存档保留在 data 目录。请重新打开桌宠后重试。", "焰轮哪吒更新", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }
    }

    public static class UpdateInstaller
    {
        private static readonly string[] RuntimeFiles = { "NezhaPet.exe", "NezhaUpdater.exe", "config.json", "README.md", "update-settings.json" };

        public static int Apply(UpdatePlan plan, bool diagnostic)
        {
            string root = UpdateSafety.NormalizeRoot(plan.InstallRoot);
            UpdateSafety.ValidateStage(plan.StageDirectory, root);
            if (!diagnostic) WaitForParent(plan);
            Exception failure = null; int result = 1;
            using (Mutex mutex = new Mutex(false, UpdateSafety.InstallMutexName(root)))
            {
                bool owns;
                try { owns = mutex.WaitOne(0); }
                catch (AbandonedMutexException) { owns = true; }
                if (!owns) throw new IOException("这个目录下的桌宠已经重新打开。请关闭后再次更新，当前文件未改动。");
                try { result = ApplyLocked(plan, diagnostic); }
                catch (Exception ex) { failure = ex; }
                finally { mutex.ReleaseMutex(); }
            }
            if (failure != null)
            {
                UpdateInstallException installFailure = failure as UpdateInstallException;
                if (!diagnostic && (installFailure == null || installFailure.RolledBack))
                { try { StartPet(root); } catch { } }
                throw failure;
            }
            if (!diagnostic)
            {
                try { StartPet(root); }
                catch (Exception startError) { throw RollbackAfterStartFailure(root, plan.StageDirectory, startError); }
            }
            WriteStatusBestEffort(plan.StageDirectory, "更新成功：" + plan.TargetVersion);
            return result;
        }
        private static int ApplyLocked(UpdatePlan plan, bool diagnostic)
        {
            string root = UpdateSafety.NormalizeRoot(plan.InstallRoot);
            string stage = UpdateSafety.NormalizeRoot(plan.StageDirectory);
            UpdateSafety.ValidateStage(stage, root);
            UpdateSafety.AssertNoReparsePath(root);
            string payload = Path.Combine(stage, "payload");
            string backup = Path.Combine(stage, "backup");
            UpdateSafety.ValidatePayload(payload, plan.TargetVersion);
            foreach (string name in RuntimeFiles) UpdateSafety.AssertNoReparsePath(Path.Combine(root, name));
            string assets = Path.Combine(root, "assets");
            if (Directory.Exists(assets)) UpdateSafety.AssertTreeNoReparse(assets);
            if (Directory.Exists(backup)) throw new InvalidDataException("更新备份目录已存在，拒绝重复执行计划。");
            HashSet<string> oldFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Directory.CreateDirectory(backup);
            // Prepare complete backups before touching any live file. data is deliberately outside this list.
            foreach (string name in RuntimeFiles)
            {
                string source = Path.Combine(root, name);
                if (File.Exists(source)) { File.Copy(source, Path.Combine(backup, name), false); oldFiles.Add(name); }
            }
            if (!oldFiles.Contains("NezhaPet.exe")) throw new InvalidDataException("原安装目录缺少 NezhaPet.exe。");
            bool hadAssets = Directory.Exists(assets);
            bool replacementStarted = false;
            bool oldAssetsMoved = false;
            bool newAssetsMoved = false;
            try
            {
                File.WriteAllText(Path.Combine(stage, "status.txt"), "正在替换程序文件", Encoding.UTF8);
                replacementStarted = true;
                if (hadAssets) { Directory.Move(assets, Path.Combine(backup, "assets")); oldAssetsMoved = true; }
                Directory.Move(Path.Combine(payload, "assets"), assets); newAssetsMoved = true;
                foreach (string name in RuntimeFiles) CopyAtomically(Path.Combine(payload, name), Path.Combine(root, name));
                if (!diagnostic) ProbeHealth(root, stage, plan.TargetVersion);
                File.WriteAllText(Path.Combine(stage, "status.txt"), "启动验证通过，正在重新打开桌宠。", Encoding.UTF8);
                return 0;
            }
            catch (Exception updateError)
            {
                Exception rollbackError = null;
                if (replacementStarted)
                {
                    try
                    {
                        // Restore only application-owned files. Save files were never moved, copied or removed.
                        foreach (string name in RuntimeFiles)
                        {
                            string destination = Path.Combine(root, name);
                            if (oldFiles.Contains(name)) CopyAtomically(Path.Combine(backup, name), destination);
                            else if (File.Exists(destination)) File.Delete(destination);
                        }
                        if (newAssetsMoved && Directory.Exists(assets)) { UpdateSafety.AssertTreeNoReparse(assets); Directory.Delete(assets, true); }
                        if (oldAssetsMoved) Directory.Move(Path.Combine(backup, "assets"), assets);
                    }
                    catch (Exception restoreError) { rollbackError = restoreError; }
                }
                string detail = "更新失败：" + updateError.Message + "\n" +
                    (rollbackError == null ? "已恢复旧版程序，存档未改动。" : "自动恢复失败：" + rollbackError.Message + "\n旧版文件在 " + backup);
                File.WriteAllText(Path.Combine(stage, "status.txt"), detail, Encoding.UTF8);
                File.WriteAllText(Path.Combine(stage, "error.txt"), updateError.ToString() +
                    (rollbackError == null ? "" : "\nROLLBACK\n" + rollbackError), Encoding.UTF8);
                throw new UpdateInstallException(detail, updateError, rollbackError == null);
            }
        }
        private static void WaitForParent(UpdatePlan plan)
        {
            if (plan.ParentProcessId <= 0 || plan.ParentProcessId == Process.GetCurrentProcess().Id)
                throw new InvalidDataException("更新计划的父进程无效。");
            Process parent;
            try { parent = Process.GetProcessById(plan.ParentProcessId); }
            catch (ArgumentException) { return; }
            using (parent)
            {
                // A recycled PID must never make the helper wait for or terminate an unrelated process.
                try
                {
                    if (parent.StartTime.ToUniversalTime().Ticks != plan.ParentStartTimeUtcTicks) return;
                    if (!parent.WaitForExit(180000)) throw new IOException("桌宠尚未退出，当前程序没有被替换。请关闭后重试。");
                    if (parent.ExitCode != 0) throw new IOException("旧版桌宠未正常退出，当前文件未改动。请重新打开并保存进度后重试。");
                }
                catch (InvalidOperationException) { return; }
            }
        }
        private static void CopyAtomically(string source, string destination)
        {
            UpdateSafety.AssertNoReparsePath(source); UpdateSafety.AssertNoReparsePath(destination);
            string temporary = destination + ".update-tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                using (FileStream input = File.OpenRead(source))
                using (FileStream output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { input.CopyTo(output); output.Flush(true); }
                if (File.Exists(destination)) File.Replace(temporary, destination, null);
                else File.Move(temporary, destination);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        private static void ProbeHealth(string root, string stage, string targetVersion)
        {
            string marker = Path.Combine(stage, "health.ok");
            if (File.Exists(marker)) File.Delete(marker);
            ProcessStartInfo start = new ProcessStartInfo(Path.Combine(root, "NezhaPet.exe"), "--update-health " + UpdateSafety.QuoteArgument(marker)) {
                WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            using (Process process = Process.Start(start))
            {
                if (process == null) throw new IOException("无法验证新版本启动。");
                if (!process.WaitForExit(45000))
                {
                    try { process.Kill(); process.WaitForExit(5000); } catch { }
                    throw new IOException("新版本启动验证超时。");
                }
                if (process.ExitCode != 0 || !File.Exists(marker) ||
                    !String.Equals(File.ReadAllText(marker, Encoding.UTF8).Trim(), targetVersion, StringComparison.Ordinal))
                    throw new IOException("新版本未通过启动和存档兼容性检查。");
            }
        }
        private static void StartPet(string root)
        {
            using (Process pet = Process.Start(new ProcessStartInfo(Path.Combine(root, "NezhaPet.exe")) {
                WorkingDirectory = root, UseShellExecute = false }))
            { if (pet == null) throw new IOException("无法重新打开桌宠。"); }
        }
        private static UpdateInstallException RollbackAfterStartFailure(string root, string stage, Exception startError)
        {
            bool restored = false;
            Exception restoreError = null;
            try
            {
                UpdateSafety.ValidateStage(stage, root);
                using (Mutex mutex = new Mutex(false, UpdateSafety.InstallMutexName(root)))
                {
                    bool owns;
                    try { owns = mutex.WaitOne(0); }
                    catch (AbandonedMutexException) { owns = true; }
                    if (!owns) throw new IOException("另一个桌宠进程正在使用此目录，未强制关闭或替换它。");
                    try
                    {
                        string backup = Path.Combine(stage, "backup");
                        UpdateSafety.AssertTreeNoReparse(backup);
                        if (!File.Exists(Path.Combine(backup, "NezhaPet.exe"))) throw new IOException("旧版程序备份不完整。");
                        foreach (string name in RuntimeFiles)
                        {
                            string source = Path.Combine(backup, name);
                            string destination = Path.Combine(root, name);
                            UpdateSafety.AssertNoReparsePath(destination);
                            if (File.Exists(source)) CopyAtomically(source, destination);
                            else if (File.Exists(destination)) File.Delete(destination);
                        }
                        string assets = Path.Combine(root, "assets");
                        if (Directory.Exists(assets)) { UpdateSafety.AssertTreeNoReparse(assets); Directory.Delete(assets, true); }
                        string oldAssets = Path.Combine(backup, "assets");
                        if (Directory.Exists(oldAssets)) Directory.Move(oldAssets, assets);
                        restored = true;
                    }
                    finally { mutex.ReleaseMutex(); }
                }
            }
            catch (Exception ex) { restoreError = ex; }
            string detail = "新版通过验证，但自动重启失败：" + startError.Message + "\n" +
                (restored ? "已恢复旧版程序，存档未改动。" : "自动恢复未完成：" + restoreError.Message + "\n旧版文件在 " + Path.Combine(stage, "backup"));
            if (restored)
            {
                try { StartPet(root); }
                catch (Exception oldStartError) { detail += "\n旧版未能自动打开，请手动运行 NezhaPet.exe：" + oldStartError.Message; }
            }
            WriteStatusBestEffort(stage, detail);
            try { File.WriteAllText(Path.Combine(stage, "error.txt"), startError.ToString() +
                (restoreError == null ? "" : "\nROLLBACK\n" + restoreError), Encoding.UTF8); } catch { }
            return new UpdateInstallException(detail, startError, restored);
        }
        private static void WriteStatusBestEffort(string stage, string status)
        {
            // A diagnostic log failure after a successful launch must not roll back a running application.
            try { File.WriteAllText(Path.Combine(stage, "status.txt"), status, Encoding.UTF8); } catch { }
        }
    }
    public sealed class UpdateInstallException : IOException
    {
        public bool RolledBack { get; private set; }
        public UpdateInstallException(string message, Exception cause, bool rolledBack) : base(message, cause)
        { RolledBack = rolledBack; }
    }
}

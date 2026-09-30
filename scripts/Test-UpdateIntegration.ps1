param([string]$RuntimeDirectory, [string]$ReportDirectory)
$ErrorActionPreference = 'Stop'
$petProjectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ([string]::IsNullOrWhiteSpace($RuntimeDirectory)) {
    $petSourceVersion = (Get-Content -LiteralPath (Join-Path $petProjectRoot 'config.json') -Raw -Encoding UTF8 | ConvertFrom-Json).version
    $RuntimeDirectory = Join-Path $petProjectRoot ('dist\nezha-desktop-v' + $petSourceVersion)
}
$petRuntime = (Resolve-Path -LiteralPath $RuntimeDirectory).Path
if ([string]::IsNullOrWhiteSpace($ReportDirectory)) { $ReportDirectory = Join-Path $petProjectRoot ('qa\update-integration-' + [Guid]::NewGuid().ToString('N')) }
$petReportRoot = [IO.Path]::GetFullPath($ReportDirectory)
if (Test-Path -LiteralPath $petReportRoot) { throw '集成测试必须使用新的报告目录。' }
New-Item -ItemType Directory -Path $petReportRoot -Force | Out-Null
$petRequiredFiles = @('NezhaPet.exe', 'NezhaUpdater.exe', 'config.json', 'README.md', 'update-settings.json')
foreach ($petName in $petRequiredFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $petRuntime $petName))) { throw ('发布目录缺少 ' + $petName) }
}
$petCurrentVersion = [Version](Get-Content -LiteralPath (Join-Path $petRuntime 'config.json') -Raw -Encoding UTF8 | ConvertFrom-Json).version
$petNextVersion = '{0}.{1}.{2}' -f $petCurrentVersion.Major, $petCurrentVersion.Minor, ($petCurrentVersion.Build + 1)
$petFramework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$petCompiler = Join-Path $petFramework 'csc.exe'
$petHarnessSource = Join-Path $petReportRoot 'fixture-harness.cs'
$petHarness = Join-Path $petReportRoot 'fixture-harness.exe'
$petStub = Join-Path $petReportRoot 'old-pet-stub.exe'

# Only test fixture processes are launched or stopped. No real pet installation is changed.
$petHarnessCode = @'
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using NezhaDesktop;
public static class FixtureHarness
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 2 && args[0] == "seed")
            {
                PetConfig config = PetConfig.Load(args[1]); DateTime now = DateTime.UtcNow;
                SaveData save = SaveData.Fresh(config, now); save.Coins = 123; save.Experience = 60;
                save.RewardedLevels = new System.Collections.Generic.List<int> { 1, 2, 3 };
                foreach (Food food in config.Foods) save.Inventory[food.Id] = 7;
                save.LastUpdatedUtc = now.AddDays(2); // Stable bytes while a restarted fixture checks its save.
                save.Work = new ActiveWork { JobId = config.Jobs[0].Id, Name = "fixture-job", Animation = config.Jobs[0].Animation,
                    EndsUtc = now.AddHours(1), Reward = 88, Experience = 9 };
                string data = Path.Combine(args[1], "data"); Directory.CreateDirectory(data);
                string path = Path.Combine(data, "save.json"); new SaveStore(path).Write(save);
                File.Copy(path, path + ".bak"); File.WriteAllText(Path.Combine(data, "update-auth.bin"), "fixture-device-data");
                return 0;
            }
            if (args.Length == 4 && (args[0] == "parent" || args[0] == "lock-assets" || args[0] == "lock-mutex"))
            {
                IDisposable held = null;
                try
                {
                    if (args[0] == "lock-assets")
                    {
                        SafeFileHandle handle = CreateFile(args[1], 0x80000000, 1, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
                        if (handle.IsInvalid) throw new IOException("Cannot lock fixture assets directory, Win32=" + Marshal.GetLastWin32Error());
                        held = handle;
                    }
                    else if (args[0] == "lock-mutex")
                    {
                        bool created; Mutex mutex = new Mutex(true, UpdateSafety.InstallMutexName(args[1]), out created);
                        if (!created) { mutex.Dispose(); throw new IOException("Fixture mutex already exists"); }
                        held = mutex;
                    }
                    File.WriteAllText(args[2], "ready"); Stopwatch timeout = Stopwatch.StartNew();
                    while (!File.Exists(args[3])) { if (timeout.ElapsedMilliseconds > 120000) throw new IOException("Fixture holder timeout"); Thread.Sleep(30); }
                    return 0;
                }
                finally { if (held != null) held.Dispose(); }
            }
            throw new ArgumentException("Invalid fixture arguments");
        }
        catch (Exception ex) { Console.Error.WriteLine(ex.ToString()); return 1; }
    }
}
public static class OldPetStub
{
    public static int Main(string[] args)
    {
        File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "stub-restarted.txt"), "old-pet-restarted");
        return 0;
    }
}
'@
[IO.File]::WriteAllText($petHarnessSource, $petHarnessCode, [Text.UTF8Encoding]::new($false))
Copy-Item -LiteralPath (Join-Path $petRuntime 'NezhaPet.exe') -Destination (Join-Path $petReportRoot 'NezhaPet.exe')
$petCompilerArgs = @('/nologo', '/target:exe', '/platform:anycpu', '/utf8output', '/codepage:65001', '/main:FixtureHarness', ('/out:' + $petHarness), ('/reference:' + (Join-Path $petRuntime 'NezhaPet.exe')), ('/reference:' + (Join-Path $petFramework 'System.dll')), ('/reference:' + (Join-Path $petFramework 'System.Core.dll')), ('/reference:' + (Join-Path $petFramework 'System.Web.Extensions.dll')), $petHarnessSource)
& $petCompiler @petCompilerArgs
if ($LASTEXITCODE -ne 0) { throw '无法编译本地测试辅助程序。' }
$petStubArgs = @('/nologo', '/target:winexe', '/platform:anycpu', '/utf8output', '/codepage:65001', '/main:OldPetStub', ('/out:' + $petStub), ('/reference:' + (Join-Path $petRuntime 'NezhaPet.exe')), ('/reference:' + (Join-Path $petFramework 'System.dll')), ('/reference:' + (Join-Path $petFramework 'System.Core.dll')), ('/reference:' + (Join-Path $petFramework 'System.Web.Extensions.dll')), $petHarnessSource)
& $petCompiler @petStubArgs
if ($LASTEXITCODE -ne 0) { throw '无法编译回退验证程序。' }

function Assert-Pet([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Get-PetSnapshot([string]$Directory, [bool]$OnlyData) {
    $petSnapshot = [ordered]@{}
    if ($OnlyData) { $petPaths = @(Get-ChildItem -LiteralPath (Join-Path $Directory 'data') -File -Recurse | Sort-Object FullName) }
    else {
        $petPaths = @($petRequiredFiles | ForEach-Object { Get-Item -LiteralPath (Join-Path $Directory $_) }) + @(Get-ChildItem -LiteralPath (Join-Path $Directory 'assets') -File -Recurse | Sort-Object FullName)
    }
    foreach ($petPath in $petPaths) {
        $petKey = $petPath.FullName.Substring($Directory.TrimEnd('\').Length + 1)
        $petSnapshot[$petKey] = (Get-FileHash -LiteralPath $petPath.FullName -Algorithm SHA256).Hash
    }
    return ($petSnapshot | ConvertTo-Json -Compress)
}
function Stop-PetFixture([string]$Directory) {
    $petExpected = [IO.Path]::GetFullPath((Join-Path $Directory 'NezhaPet.exe'))
    foreach ($petProcess in @(Get-Process -Name NezhaPet -ErrorAction SilentlyContinue)) {
        try {
            if ([string]::Equals($petProcess.Path, $petExpected, [StringComparison]::OrdinalIgnoreCase)) {
                $petProcess.Kill(); [void]$petProcess.WaitForExit(5000)
            }
        } catch { if (-not $petProcess.HasExited) { throw } }
    }
}
function Wait-PetFile([string]$Path, [int]$TimeoutMs = 10000) {
    $petDeadline = [Diagnostics.Stopwatch]::StartNew()
    while (-not (Test-Path -LiteralPath $Path)) {
        if ($petDeadline.ElapsedMilliseconds -ge $TimeoutMs) { throw ('等待测试文件超时：' + $Path) }
        Start-Sleep -Milliseconds 50
    }
}
function New-PetFixture([string]$Name) {
    $petInstall = Join-Path $petReportRoot ('测试 安装目录 ' + $Name)
    New-Item -ItemType Directory -Path $petInstall | Out-Null
    foreach ($petFile in $petRequiredFiles) { Copy-Item -LiteralPath (Join-Path $petRuntime $petFile) -Destination $petInstall }
    Copy-Item -LiteralPath (Join-Path $petRuntime 'assets') -Destination $petInstall -Recurse
    $petSeedProcess = Start-Process -FilePath $petHarness -ArgumentList ('seed "' + $petInstall + '"') -WorkingDirectory $petReportRoot -WindowStyle Hidden -PassThru
    if (-not $petSeedProcess.WaitForExit(15000) -or $petSeedProcess.ExitCode -ne 0) { throw '测试存档初始化失败。' }
    # A harmless stub confirms rollback relaunch without opening the old pet UI.
    Copy-Item -LiteralPath $petStub -Destination (Join-Path $petInstall 'NezhaPet.exe') -Force
    $petStage = Join-Path $petInstall ('.update\' + [Guid]::NewGuid().ToString('N'))
    $petPayload = Join-Path $petStage 'payload'
    New-Item -ItemType Directory -Path $petPayload -Force | Out-Null
    foreach ($petFile in $petRequiredFiles) { Copy-Item -LiteralPath (Join-Path $petRuntime $petFile) -Destination $petPayload }
    Copy-Item -LiteralPath (Join-Path $petRuntime 'assets') -Destination $petPayload -Recurse
    $petNextConfig = Get-Content -LiteralPath (Join-Path $petPayload 'config.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $petNextConfig.version = $petNextVersion
    [IO.File]::WriteAllText((Join-Path $petPayload 'config.json'), ($petNextConfig | ConvertTo-Json -Depth 30), [Text.UTF8Encoding]::new($false))
    Copy-Item -LiteralPath (Join-Path $petRuntime 'NezhaUpdater.exe') -Destination (Join-Path $petStage 'runner.exe')
    $petPlanPath = Join-Path $petStage 'plan.json'
    $petPlan = [ordered]@{ InstallRoot = $petInstall; StageDirectory = $petStage; TargetVersion = $petNextVersion; ParentProcessId = [int]::MaxValue; ParentStartTimeUtcTicks = 0L }
    [IO.File]::WriteAllText($petPlanPath, ($petPlan | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
    return [PSCustomObject]@{ Root = $petInstall; Stage = $petStage; Payload = $petPayload; PlanPath = $petPlanPath; Plan = $petPlan; FilesBefore = (Get-PetSnapshot $petInstall $false); DataBefore = (Get-PetSnapshot $petInstall $true) }
}
function Start-PetHelper($Fixture) {
    return Start-Process -FilePath (Join-Path $Fixture.Stage 'runner.exe') -ArgumentList ('--apply "' + $Fixture.PlanPath + '" --quiet') -WorkingDirectory $Fixture.Stage -WindowStyle Hidden -PassThru
}
function Wait-PetHelper($Process, [int]$ExpectedExit) {
    if (-not $Process.WaitForExit(65000)) { $Process.Kill(); [void]$Process.WaitForExit(5000); throw '更新助手集成测试超时。' }
    $Process.Refresh(); Assert-Pet ($Process.ExitCode -eq $ExpectedExit) ('更新助手退出码错误：' + $Process.ExitCode + '，预期：' + $ExpectedExit)
}
function Hold-PetFixture([string]$Mode, [string]$Path, $Fixture) {
    $petReady = Join-Path $Fixture.Stage ($Mode + '-ready.txt')
    $petRelease = Join-Path $Fixture.Stage ($Mode + '-release.txt')
    $petHolder = Start-Process -FilePath $petHarness -ArgumentList ($Mode + ' "' + $Path + '" "' + $petReady + '" "' + $petRelease + '"') -WorkingDirectory $petReportRoot -WindowStyle Hidden -PassThru
    Wait-PetFile $petReady
    return [PSCustomObject]@{ Process = $petHolder; Release = $petRelease }
}
function Release-PetFixture($Holder) {
    if ($null -eq $Holder) { return }
    [IO.File]::WriteAllText($Holder.Release, 'release')
    if (-not $Holder.Process.WaitForExit(5000)) { $Holder.Process.Kill(); [void]$Holder.Process.WaitForExit(5000) }
}

$petResults = [Collections.Generic.List[string]]::new()
$petFailures = 0
try {
    $petFixture = New-PetFixture '成功更新'; $petHolder = $null
    try {
        $petHolder = Hold-PetFixture 'parent' $petFixture.Root $petFixture
        $petFixture.Plan.ParentProcessId = $petHolder.Process.Id
        $petFixture.Plan.ParentStartTimeUtcTicks = $petHolder.Process.StartTime.ToUniversalTime().Ticks
        [IO.File]::WriteAllText($petFixture.PlanPath, ($petFixture.Plan | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
        $petHelper = Start-PetHelper $petFixture
        Start-Sleep -Milliseconds 700
        Assert-Pet (-not $petHelper.HasExited) '旧进程尚未退出，助手已提前退出。'
        Assert-Pet ((Get-PetSnapshot $petFixture.Root $false) -eq $petFixture.FilesBefore) '等待旧进程时提前改动了程序。'
        Release-PetFixture $petHolder; $petHolder = $null
        Wait-PetHelper $petHelper 0
        Start-Sleep -Milliseconds 800
        Stop-PetFixture $petFixture.Root
        Assert-Pet ((Get-Content -LiteralPath (Join-Path $petFixture.Stage 'health.ok') -Raw).Trim() -eq $petNextVersion) '真实新版未通过启动及存档检查。'
        Assert-Pet ((Get-Content -LiteralPath (Join-Path $petFixture.Stage 'status.txt') -Raw) -match '更新成功') '更新没有成功状态。'
        Assert-Pet ((Get-FileHash -LiteralPath (Join-Path $petFixture.Root 'NezhaPet.exe')).Hash -eq (Get-FileHash -LiteralPath (Join-Path $petRuntime 'NezhaPet.exe')).Hash) '运行程序没有被替换为新发布包。'
        Assert-Pet ((Get-PetSnapshot $petFixture.Root $true) -eq $petFixture.DataBefore) '更新改变了原始存档、备份或设备数据。'
        $petResults.Add('PASS real-helper-parent-wait-install-health-restart-data-byte-preservation')
    } finally { Release-PetFixture $petHolder; Stop-PetFixture $petFixture.Root }
} catch { $petFailures++; $petResults.Add('FAIL real-helper-parent-wait-install-health-restart-data-byte-preservation: ' + $_.Exception.Message) }

try {
    $petFixture = New-PetFixture '启动失败回退'
    try {
        $petMissingFrame = Join-Path $petFixture.Payload 'assets\eat-candy\00.png'
        Assert-Pet (Test-Path -LiteralPath $petMissingFrame) '缺少用于验证启动失败的食物帧。'
        Remove-Item -LiteralPath $petMissingFrame
        $petHelper = Start-PetHelper $petFixture; Wait-PetHelper $petHelper 1
        Wait-PetFile (Join-Path $petFixture.Root 'stub-restarted.txt')
        Assert-Pet ((Get-PetSnapshot $petFixture.Root $false) -eq $petFixture.FilesBefore) '新版本启动失败没有逐字节恢复旧程序和素材。'
        Assert-Pet ((Get-PetSnapshot $petFixture.Root $true) -eq $petFixture.DataBefore) '启动失败回退改变了存档。'
        Assert-Pet ((Get-Content -LiteralPath (Join-Path $petFixture.Stage 'status.txt') -Raw) -match '已恢复旧版程序') '缺少旧版恢复状态。'
        $petResults.Add('PASS real-helper-failed-health-rolls-back-original-runtime-assets-and-data')
    } finally { Stop-PetFixture $petFixture.Root }
} catch { $petFailures++; $petResults.Add('FAIL real-helper-failed-health-rolls-back-original-runtime-assets-and-data: ' + $_.Exception.Message) }

try {
    $petFixture = New-PetFixture '素材被占用'; $petHolder = $null
    try {
        $petHolder = Hold-PetFixture 'lock-assets' (Join-Path $petFixture.Root 'assets') $petFixture
        $petHelper = Start-PetHelper $petFixture; Wait-PetHelper $petHelper 1
        Assert-Pet ((Get-PetSnapshot $petFixture.Root $false) -eq $petFixture.FilesBefore) '旧素材目录移动失败，却删除或改动了原文件。'
        Assert-Pet ((Get-PetSnapshot $petFixture.Root $true) -eq $petFixture.DataBefore) '素材被占用导致存档变化。'
        Assert-Pet ((Get-Content -LiteralPath (Join-Path $petFixture.Stage 'status.txt') -Raw) -match '已恢复旧版程序') '素材被占用时未安全退出。'
        $petResults.Add('PASS old-assets-move-failure-does-not-delete-original-assets')
    } finally { Release-PetFixture $petHolder; Stop-PetFixture $petFixture.Root }
} catch { $petFailures++; $petResults.Add('FAIL old-assets-move-failure-does-not-delete-original-assets: ' + $_.Exception.Message) }

try {
    $petFixture = New-PetFixture '并发安装保护'; $petHolder = $null
    try {
        $petHolder = Hold-PetFixture 'lock-mutex' $petFixture.Root $petFixture
        $petHelper = Start-PetHelper $petFixture; Wait-PetHelper $petHelper 1
        Assert-Pet ((Get-PetSnapshot $petFixture.Root $false) -eq $petFixture.FilesBefore) '正在运行的安装目录仍被第二个更新助手改动。'
        Assert-Pet ((Get-PetSnapshot $petFixture.Root $true) -eq $petFixture.DataBefore) '并发拒绝改变了存档。'
        $petResults.Add('PASS shared-install-mutex-refuses-concurrent-update')
    } finally { Release-PetFixture $petHolder; Stop-PetFixture $petFixture.Root }
} catch { $petFailures++; $petResults.Add('FAIL shared-install-mutex-refuses-concurrent-update: ' + $_.Exception.Message) }

$petReport = Join-Path $petReportRoot 'update-integration-results.txt'
[IO.File]::WriteAllLines($petReport, @($petResults) + @('TOTAL=' + $petResults.Count, 'FAILED=' + $petFailures, 'NETWORK=NOT_USED'), [Text.UTF8Encoding]::new($false))
foreach ($petResult in $petResults) { Write-Output $petResult }
if ($petFailures -gt 0) { throw ('更新集成测试失败，请查看：' + $petReport) }
Write-Output ('更新集成测试全部通过：' + $petReport)

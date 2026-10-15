param([string]$RuntimeDirectory, [string]$ReportDirectory)
$ErrorActionPreference = 'Stop'
$petProjectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ([string]::IsNullOrWhiteSpace($RuntimeDirectory)) {
    $petVersion = (Get-Content -LiteralPath (Join-Path $petProjectRoot 'config.json') -Raw -Encoding UTF8 | ConvertFrom-Json).version
    $RuntimeDirectory = Join-Path $petProjectRoot ('dist\nezha-desktop-v' + $petVersion)
}
$petRuntime = [IO.Path]::GetFullPath($RuntimeDirectory)
$petExecutable = Join-Path $petRuntime 'NezhaPet.exe'
if (-not (Test-Path -LiteralPath $petExecutable)) { throw '找不到桌宠程序，请先运行 build.ps1。' }
if ([string]::IsNullOrWhiteSpace($ReportDirectory)) { $ReportDirectory = Join-Path $petProjectRoot ('qa\run-' + [DateTime]::Now.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)) }
$petReportRoot = [IO.Path]::GetFullPath($ReportDirectory)
if (Test-Path -LiteralPath $petReportRoot) { throw '测试报告目录必须是新目录，防止旧存档干扰结果。' }
New-Item -ItemType Directory -Path $petReportRoot -Force | Out-Null
& (Join-Path $PSScriptRoot 'Test-ADarkRoomTranslations.ps1') -ReportFile (Join-Path $petReportRoot 'adarkroom-translations.txt')
$petChecks = @(
    @{ Name = 'rules'; Argument = '--self-test'; Report = 'test-results.txt' },
    @{ Name = 'preview'; Argument = '--preview'; Report = 'ui-smoke.txt' },
    @{ Name = 'native'; Argument = '--native-smoke'; Report = 'native-smoke.txt' },
    @{ Name = 'food'; Argument = '--food-preview'; Report = 'food-playback.txt' },
    @{ Name = 'actions'; Argument = '--actions-preview'; Report = 'actions-playback.txt' },
    @{ Name = 'update'; Argument = '--update-test'; Report = 'update-test-results.txt' },
    @{ Name = 'mini-games'; Argument = '--minigame-test'; Report = 'mini-game-results.txt' },
    @{ Name = 'web-game'; Argument = '--webgame-test'; Report = 'web-game-results.txt' },
    @{ Name = 'idle-web-games'; Argument = '--idle-webgame-test'; Report = 'idle-web-game-results.txt' }
)
foreach ($petCheck in $petChecks) {
    $petOutput = Join-Path $petReportRoot $petCheck.Name
    # These controlled paths cannot contain quotes; the quotes preserve spaces.
    if ($petOutput.Contains('"')) { throw '测试路径不能含双引号。' }
    $petArgs = $petCheck.Argument + ' "' + $petOutput + '"'
    if ($petCheck.Name -eq 'mini-games') {
        $petExample = Join-Path $petProjectRoot 'examples\my-game'
        if ($petExample.Contains('"')) { throw '示例路径不能含双引号。' }
        $petArgs += ' "' + $petExample + '"'
    }
    $petProcess = Start-Process -FilePath $petExecutable -ArgumentList $petArgs -WorkingDirectory $petRuntime -WindowStyle Hidden -PassThru
    if (-not $petProcess.WaitForExit(60000)) { $petProcess.Kill(); throw ('测试超时：' + $petCheck.Name) }
    $petProcess.Refresh()
    $petReport = Join-Path $petOutput $petCheck.Report
    if ($petProcess.ExitCode -ne 0) { throw ('测试失败：' + $petCheck.Name + '；报告目录：' + $petOutput) }
    if (-not (Test-Path -LiteralPath $petReport)) { throw ('测试未生成报告：' + $petCheck.Name) }
    $petText = Get-Content -LiteralPath $petReport -Raw -Encoding UTF8
    if ($petText -match '(?m)^FAIL\b|result=FAIL') { throw ('报告中存在失败项：' + $petReport) }
    Write-Output ('PASS ' + $petCheck.Name + ' → ' + $petReport)
}
$petWebRestart = Join-Path $petReportRoot 'web-game-restart'
$petWebSavedData = Join-Path $petReportRoot 'web-game\data'
if ($petWebRestart.Contains('"') -or $petWebSavedData.Contains('"')) { throw '网页验证路径不能包含引号。' }
$petWebProcess = Start-Process -FilePath $petExecutable -ArgumentList ('--webgame-resume "' + $petWebRestart + '" "' + $petWebSavedData + '"') -WorkingDirectory $petRuntime -WindowStyle Hidden -PassThru
if (-not $petWebProcess.WaitForExit(60000)) { $petWebProcess.Kill(); throw '网页游戏跨进程恢复超时。' }
$petWebProcess.Refresh()
$petWebReport = Join-Path $petWebRestart 'web-game-results.txt'
if ($petWebProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $petWebReport) -or (Get-Content -LiteralPath $petWebReport -Raw) -match '(?m)^FAIL\b') { throw ('网页游戏跨进程恢复失败：' + $petWebRestart) }
Write-Output ('PASS web-game-restart → ' + $petWebReport)
$petIdleRestart = Join-Path $petReportRoot 'idle-web-games-restart'
$petIdleData = Join-Path $petReportRoot 'idle-web-games\data'
$petIdleProcess = Start-Process -FilePath $petExecutable -ArgumentList ('--idle-webgame-resume "' + $petIdleRestart + '" "' + $petIdleData + '"') -WorkingDirectory $petRuntime -WindowStyle Hidden -PassThru
if (-not $petIdleProcess.WaitForExit(60000)) { $petIdleProcess.Kill(); throw '放置网页游戏跨进程恢复超时。' }
$petIdleProcess.Refresh()
$petIdleReport = Join-Path $petIdleRestart 'idle-web-game-results.txt'
if ($petIdleProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $petIdleReport) -or (Get-Content -LiteralPath $petIdleReport -Raw) -match '(?m)^FAIL\b') { throw ('放置网页游戏跨进程恢复失败：' + $petIdleRestart) }
Write-Output ('PASS idle-web-games-restart → ' + $petIdleReport)
& (Join-Path $PSScriptRoot 'Test-UpdateIntegration.ps1') -RuntimeDirectory $petRuntime -ReportDirectory (Join-Path $petReportRoot 'update-integration')
Write-Output ('全部测试通过。报告：' + $petReportRoot)

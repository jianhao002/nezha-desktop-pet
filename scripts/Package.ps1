param([switch]$SkipBuild, [switch]$SkipTests)
$ErrorActionPreference = 'Stop'
$petProjectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$petConfig = Get-Content -LiteralPath (Join-Path $petProjectRoot 'config.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$petSettings = Get-Content -LiteralPath (Join-Path $petProjectRoot 'update-settings.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$petVersion = [string]$petConfig.version
if ($petVersion -notmatch '^(0|[1-9][0-9]{0,5})\.(0|[1-9][0-9]{0,5})\.(0|[1-9][0-9]{0,5})$') { throw '版本必须使用三段数字，不允许前导零。' }
$petHost = [Uri]([string]$petSettings.host)
if ($petHost.Scheme -ne 'https' -or $petHost.AbsolutePath -ne '/' -or -not [string]::IsNullOrEmpty($petHost.UserInfo) -or -not [string]::IsNullOrEmpty($petHost.Query) -or -not [string]::IsNullOrEmpty($petHost.Fragment)) { throw '更新主机必须是没有凭证、路径和参数的 HTTPS 域名。' }
if ([string]$petSettings.projectPath -notmatch '^[A-Za-z0-9_.-]+(/[A-Za-z0-9_.-]+)+$' -or [string]$petSettings.packageName -notmatch '^[A-Za-z0-9_.-]+$' -or [string]$petSettings.channel -notmatch '^[A-Za-z0-9_.-]+$') { throw '更新仓库路径、包名或频道格式错误。' }
$petProjectId = 0L
if ([string]$petSettings.host -notin @('https://jihulab.com', 'https://github.com') -or [string]$petSettings.packageName -ne 'nezha-desktop-pet' -or [string]$petSettings.channel -ne 'latest') { throw '更新程序要求 GitHub / 极狐 GitLab 的 nezha-desktop-pet / latest 发布通道。' }
if ([string]$petSettings.host -eq 'https://jihulab.com' -and (-not [long]::TryParse([string]$petSettings.projectId, [ref]$petProjectId) -or $petProjectId -le 0)) { throw '极狐更新设置需要正整数 projectId。' }
if ([string]$petSettings.host -eq 'https://github.com' -and [string]$petSettings.projectPath -notmatch '^[A-Za-z0-9_-][A-Za-z0-9_.-]*/[A-Za-z0-9_-][A-Za-z0-9_.-]*$') { throw 'GitHub 更新项目必须使用 用户名/仓库名。' }
$petRuntime = Join-Path $petProjectRoot ('dist\nezha-desktop-v' + $petVersion)
if (-not $SkipBuild) { & (Join-Path $petProjectRoot 'build.ps1') -OutputDirectory $petRuntime }
foreach ($petRequired in @('NezhaPet.exe', 'NezhaUpdater.exe', 'config.json', 'update-settings.json', 'README.md', 'assets\nezha.png', 'assets\pet.ico')) {
    if (-not (Test-Path -LiteralPath (Join-Path $petRuntime $petRequired) -PathType Leaf)) { throw ('发布目录缺少：' + $petRequired) }
}
$petRuntimeFiles = @(Get-ChildItem -LiteralPath $petRuntime -Recurse -Force)
foreach ($petItem in $petRuntimeFiles) {
    if ($petItem.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw '发布目录含目录链接。' }
    $petRelative = $petItem.FullName.Substring($petRuntime.Length + 1).Replace('\', '/')
    if ($petRelative -notmatch '^(assets(/.*)?|NezhaPet\.exe|NezhaUpdater\.exe|config\.json|update-settings\.json|README\.md)$') { throw ('发布目录有非发布内容，停止打包：' + $petRelative) }
}
if (-not $SkipTests) { & (Join-Path $PSScriptRoot 'Test.ps1') -RuntimeDirectory $petRuntime }
$petRelease = Join-Path $petProjectRoot ('dist\release-' + $petVersion)
New-Item -ItemType Directory -Path $petRelease -Force | Out-Null
$petFileName = [string]$petSettings.packageName + '-' + $petVersion + '.zip'
$petZip = Join-Path $petRelease $petFileName
if (Test-Path -LiteralPath $petZip) { Remove-Item -LiteralPath $petZip -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.IO.Compression
# Framework versions differ in their default entry separators. ZIP paths are
# always forward slashes so existing desktop updaters accept the exact artifact.
$petArchive = [IO.Compression.ZipFile]::Open($petZip, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($petFile in ($petRuntimeFiles | Where-Object { -not $_.PSIsContainer } | Sort-Object FullName)) {
        $petEntryName = $petFile.FullName.Substring($petRuntime.Length + 1).Replace('\', '/')
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($petArchive, $petFile.FullName, $petEntryName, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
}
finally { $petArchive.Dispose() }

# Validate the real output ZIP through the same code used by the update button.
$petArchiveReport = Join-Path $petProjectRoot ('qa\release-archive-' + $petVersion + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8))
if ($petZip.Contains('"') -or $petArchiveReport.Contains('"')) { throw '发布验收路径不能含双引号。' }
$petArchiveArgs = '--package-check "' + $petZip + '" "' + $petArchiveReport + '"'
$petArchiveProcess = Start-Process -FilePath (Join-Path $petRuntime 'NezhaPet.exe') -ArgumentList $petArchiveArgs -WorkingDirectory $petRuntime -WindowStyle Hidden -PassThru
if (-not $petArchiveProcess.WaitForExit(60000)) { $petArchiveProcess.Kill(); throw '正式压缩包校验超时，停止发布。' }
$petArchiveProcess.Refresh()
if ($petArchiveProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath (Join-Path $petArchiveReport 'package-check.txt'))) { throw ('正式压缩包未通过桌宠更新校验；报告 ' + $petArchiveReport) }
Write-Output ('PASS actual-release-archive → ' + $petArchiveReport)
$petPackageBase = $petHost.AbsoluteUri.TrimEnd('/') + '/api/v4/projects/' + $petProjectId.ToString([Globalization.CultureInfo]::InvariantCulture) + '/packages/generic/' + [Uri]::EscapeDataString([string]$petSettings.packageName)
if ([string]$petSettings.host -eq 'https://github.com') { $petPackageBase = 'https://github.com/' + [string]$petSettings.projectPath + '/releases/download' }
$petDownloadUrl = $petPackageBase + '/' + $petVersion + '/' + $petFileName
if ([string]$petSettings.host -eq 'https://github.com') { $petDownloadUrl = $petPackageBase + '/v' + $petVersion + '/' + $petFileName }
$petManifest = [ordered]@{
    version = $petVersion
    packageUrl = $petDownloadUrl
    sha256 = (Get-FileHash -LiteralPath $petZip -Algorithm SHA256).Hash.ToLowerInvariant()
    size = (Get-Item -LiteralPath $petZip).Length
}
$petUtf8 = New-Object Text.UTF8Encoding($false)
$petManifestPath = Join-Path $petRelease 'update.json'
[IO.File]::WriteAllText($petManifestPath, (($petManifest | ConvertTo-Json) + [Environment]::NewLine), $petUtf8)
Write-Output ('发布包：' + $petZip)
Write-Output ('更新清单：' + $petManifestPath)
Write-Output ('SHA256：' + $petManifest.sha256)
if ($SkipTests) { Write-Warning '此次跳过测试。不要直接发布未验证的包。' }

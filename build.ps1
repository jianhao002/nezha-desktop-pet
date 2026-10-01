﻿param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$petProjectRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$petVersion = (Get-Content -LiteralPath (Join-Path $petProjectRoot 'config.json') -Raw -Encoding UTF8 | ConvertFrom-Json).version
if ($petVersion -notmatch '^(0|[1-9][0-9]{0,5})\.(0|[1-9][0-9]{0,5})\.(0|[1-9][0-9]{0,5})$') { throw 'config.json 的 version 必须使用三段数字，例如 0.3.0，不允许前导零。' }
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Join-Path $petProjectRoot ('dist\nezha-desktop-v' + $petVersion) }
$petBuildOutput = [IO.Path]::GetFullPath($OutputDirectory)
$petDistRoot = [IO.Path]::GetFullPath((Join-Path $petProjectRoot 'dist')).TrimEnd('\') + '\'
if ($petBuildOutput.TrimEnd('\') -eq $petProjectRoot.TrimEnd('\')) { throw '不能把开发目录用作编译输出。' }
if (Test-Path -LiteralPath (Join-Path $petBuildOutput 'data')) { throw '输出目录已有存档。编译不会覆盖运行目录，请选择新的输出目录。' }
if (Test-Path -LiteralPath $petBuildOutput) {
    $petExisting = @(Get-ChildItem -LiteralPath $petBuildOutput -Force)
    if ($petExisting.Count -gt 0) {
        if (-not $petBuildOutput.StartsWith($petDistRoot, [StringComparison]::OrdinalIgnoreCase)) { throw '已有内容的输出目录不在项目 dist 内，请选择一个新的目录。' }
        if ((Get-Item -LiteralPath $petBuildOutput).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw '编译输出不能是目录链接。' }
        $petLinks = @(Get-ChildItem -LiteralPath $petBuildOutput -Recurse -Force | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint })
        if ($petLinks.Count -gt 0) { throw '编译输出含目录链接，停止清理。' }
        # Only this verified generated output under this repository's dist is cleared.
        Remove-Item -LiteralPath $petBuildOutput -Recurse -Force
    }
}
$petFramework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$petCompiler = Join-Path $petFramework 'csc.exe'
if (-not (Test-Path -LiteralPath $petCompiler)) { throw '找不到 Windows .NET Framework C# 编译器。' }
$petUpdateCore = Join-Path $petProjectRoot 'src\UpdateCore.cs'
$petHelperSources = @(Get-ChildItem -LiteralPath (Join-Path $petProjectRoot 'updater') -Filter '*.cs' -File | Sort-Object Name | ForEach-Object { $_.FullName })
if (-not (Test-Path -LiteralPath $petUpdateCore) -or $petHelperSources.Count -eq 0) { throw '缺少更新核心或更新助手源码。' }
if (-not (Test-Path -LiteralPath (Join-Path $petProjectRoot 'update-settings.json'))) { throw '缺少 update-settings.json。' }
New-Item -ItemType Directory -Path $petBuildOutput -Force | Out-Null
$petReferences = @('System.dll', 'System.Core.dll', 'System.Web.Extensions.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll', 'System.Xaml.dll', 'Microsoft.CSharp.dll', 'System.Security.dll', 'System.IO.Compression.dll', 'System.IO.Compression.FileSystem.dll', 'WPF\WindowsBase.dll', 'WPF\PresentationCore.dll', 'WPF\PresentationFramework.dll')
function Invoke-PetCompiler([string]$Name, [string[]]$Sources) {
    $petCompileArgs = @('/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/utf8output', '/codepage:65001', ('/out:' + (Join-Path $petBuildOutput $Name)), ('/win32manifest:' + (Join-Path $petProjectRoot 'app.manifest')), ('/win32icon:' + (Join-Path $petProjectRoot 'assets\pet.ico')))
    foreach ($petReference in $petReferences) { $petCompileArgs += ('/reference:' + (Join-Path $petFramework $petReference)) }
    if ($Name -eq 'NezhaPet.exe') {
        foreach ($petGameFolder in (Get-ChildItem -LiteralPath (Join-Path $petProjectRoot 'games') -Directory | Sort-Object Name)) {
            foreach ($petGameFile in (Get-ChildItem -LiteralPath $petGameFolder.FullName -File | Where-Object { $_.Name -eq 'game.json' -or $_.Extension -eq '.cs' } | Sort-Object Name)) {
                $petCompileArgs += ('/resource:' + $petGameFile.FullName + ',Nezha.MiniGames.' + $petGameFolder.Name + '.' + $petGameFile.Name)
            }
        }
    }
    $petCompileArgs += $Sources
    & $petCompiler @petCompileArgs
    if ($LASTEXITCODE -ne 0) { throw ('编译失败：' + $Name) }
}
$petMainSources = @(Get-ChildItem -LiteralPath (Join-Path $petProjectRoot 'src') -Filter '*.cs' -File | Sort-Object Name | ForEach-Object { $_.FullName })
Invoke-PetCompiler -Name 'NezhaPet.exe' -Sources $petMainSources
Invoke-PetCompiler -Name 'NezhaUpdater.exe' -Sources ($petHelperSources + $petUpdateCore)
foreach ($petFile in @('config.json', 'update-settings.json', 'README.md')) { Copy-Item -LiteralPath (Join-Path $petProjectRoot $petFile) -Destination $petBuildOutput -Force }
Copy-Item -LiteralPath (Join-Path $petProjectRoot 'assets') -Destination $petBuildOutput -Recurse -Force
Write-Output ('已生成 v' + $petVersion + '：' + (Join-Path $petBuildOutput 'NezhaPet.exe'))

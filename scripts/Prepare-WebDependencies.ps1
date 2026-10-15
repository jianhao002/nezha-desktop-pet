$ErrorActionPreference = 'Stop'
$petRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$petSdkVersion = '1.0.3405.78'
$petSdkHash = 'd035807b2aaba871e8c014759626f566e96934e6ce6f0587056ee81d5228c373'
$petCache = Join-Path $petRoot ('work\webview2-' + $petSdkVersion)
$petArchive = Join-Path $petCache 'sdk.zip'
$petSdk = Join-Path $petCache 'sdk'
New-Item -ItemType Directory -Path $petCache -Force | Out-Null
if (-not (Test-Path -LiteralPath $petArchive)) {
    Invoke-WebRequest -Uri ('https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/' + $petSdkVersion + '/microsoft.web.webview2.' + $petSdkVersion + '.nupkg') -OutFile $petArchive -TimeoutSec 90
}
if ((Get-FileHash -LiteralPath $petArchive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $petSdkHash) { throw 'WebView2 SDK 校验失败，请保留下载文件供检查。' }
Expand-Archive -LiteralPath $petArchive -DestinationPath $petSdk -Force
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.IO.Compression
$petWebArchives = @{}
foreach ($petGameId in @('gridland', 'adarkroom', 'candybox2')) {
    $petWebRoot = Join-Path $petRoot ('third_party\' + $petGameId)
    $petWebArchive = Join-Path $petCache ($petGameId + '.zip')
    if (Test-Path -LiteralPath $petWebArchive) { Remove-Item -LiteralPath $petWebArchive }
    $petZip = [IO.Compression.ZipFile]::Open($petWebArchive, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($petFile in (Get-ChildItem -LiteralPath $petWebRoot -Recurse -File | Sort-Object FullName)) {
            if ($petFile.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw '网页资源不能含文件链接。' }
            $petRelative = $petFile.FullName.Substring($petWebRoot.Length + 1).Replace('\', '/')
            if ($petRelative -match '(^|/)(nodeploy|src)/|\.pdn$') { continue }
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($petZip, $petFile.FullName, $petRelative, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
        foreach ($petLicenseName in @('LICENSE.txt', 'NOTICE.txt', 'VERSION.md')) {
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($petZip, (Join-Path $petRoot ('third_party\webview2\' + $petLicenseName)), ('licenses/WebView2-' + $petLicenseName), [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
        if ($petGameId -ne 'gridland') {
            foreach ($petSharedFile in (Get-ChildItem -LiteralPath (Join-Path $petRoot 'third_party\desktop-web') -File)) {
                [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($petZip, $petSharedFile.FullName, ('www/nezha/' + $petSharedFile.Name), [IO.Compression.CompressionLevel]::Optimal) | Out-Null
            }
        }
    } finally { $petZip.Dispose() }
    $petWebArchives[$petGameId] = $petWebArchive
}
[pscustomobject]@{ Sdk = $petSdk; Assets = $petWebArchives; Version = $petSdkVersion }

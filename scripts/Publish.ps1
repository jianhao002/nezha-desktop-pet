param([string]$ReleaseDirectory)
$ErrorActionPreference = 'Stop'
$petProjectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$petConfig = Get-Content -LiteralPath (Join-Path $petProjectRoot 'config.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$petSettings = Get-Content -LiteralPath (Join-Path $petProjectRoot 'update-settings.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$petVersion = [string]$petConfig.version
if ($petVersion -notmatch '^(0|[1-9][0-9]{0,5})\.(0|[1-9][0-9]{0,5})\.(0|[1-9][0-9]{0,5})$') { throw '版本必须使用三段数字，不允许前导零。' }
if ([string]$petSettings.host -ne 'https://jihulab.com') { throw '本发布脚本只向已配置的个人极狐 GitLab 主机发送凭证。' }
if ([string]$petSettings.projectPath -notmatch '^[A-Za-z0-9_.-]+(/[A-Za-z0-9_.-]+)+$' -or [string]$petSettings.packageName -notmatch '^[A-Za-z0-9_.-]+$' -or [string]$petSettings.channel -notmatch '^[A-Za-z0-9_.-]+$') { throw '更新设置格式错误。' }
if ([string]$petSettings.packageName -ne 'nezha-desktop-pet' -or [string]$petSettings.channel -ne 'latest') { throw '更新程序只接受 nezha-desktop-pet / latest 发布通道。' }
if ([string]::IsNullOrWhiteSpace($ReleaseDirectory)) { $ReleaseDirectory = Join-Path $petProjectRoot ('dist\release-' + $petVersion) }
$petRelease = [IO.Path]::GetFullPath($ReleaseDirectory)
$petManifestPath = Join-Path $petRelease 'update.json'
$petManifest = Get-Content -LiteralPath $petManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$petPackageName = [string]$petSettings.packageName + '-' + $petVersion + '.zip'
$petPackagePath = Join-Path $petRelease $petPackageName
$petBase = 'https://jihulab.com/api/v4/projects/' + [Uri]::EscapeDataString([string]$petSettings.projectPath) + '/packages/generic/' + [Uri]::EscapeDataString([string]$petSettings.packageName)
$petPackageUrl = $petBase + '/' + $petVersion + '/' + $petPackageName
$petLatestUrl = $petBase + '/' + [Uri]::EscapeDataString([string]$petSettings.channel) + '/update.json'
if ($petManifest.version -ne $petVersion -or $petManifest.packageUrl -ne $petPackageUrl) { throw '发布包与当前版本、仓库设置不一致，请重新运行 Package.ps1。' }
if (-not (Test-Path -LiteralPath $petPackagePath -PathType Leaf)) { throw '缺少发布压缩包。' }
if ((Get-Item -LiteralPath $petPackagePath).Length -ne [long]$petManifest.size -or (Get-FileHash -LiteralPath $petPackagePath -Algorithm SHA256).Hash -ne [string]$petManifest.sha256) { throw '本地发布包与清单的大小或 SHA256 不一致。' }
$petToken = [Environment]::GetEnvironmentVariable('NEZHA_GITLAB_TOKEN', 'Process')
if ([string]::IsNullOrWhiteSpace($petToken)) { throw '缺少临时发布凭证。请运行 Publish-Release.ps1，它会安全提示输入，不会保存凭证。' }
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

function Receive-PetPublicFile([string]$Url, [string]$Destination, [long]$Limit) {
    $petResponse = $null
    $petStream = $null
    $petOutput = $null
    try {
        $petRequest = [Net.HttpWebRequest]::Create($Url)
        $petRequest.Method = 'GET'
        $petRequest.AllowAutoRedirect = $true
        $petRequest.MaximumAutomaticRedirections = 4
        $petRequest.Timeout = 30000
        $petRequest.ReadWriteTimeout = 60000
        $petRequest.UserAgent = 'NezhaPet-Release/0.3'
        $petRequest.Headers['Cache-Control'] = 'no-cache'
        # Anonymous download verifies that pet users do not need a token.
        $petResponse = $petRequest.GetResponse()
        if ($petResponse.ResponseUri.Scheme -ne 'https') { throw '下载跳转到了非 HTTPS 地址。' }
        if ($petResponse.ContentLength -gt $Limit) { throw '远端文件超出允许大小。' }
        $petStream = $petResponse.GetResponseStream()
        $petOutput = [IO.File]::Open($Destination, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
        $petBuffer = New-Object byte[] 65536
        $petTotal = 0L
        while (($petCount = $petStream.Read($petBuffer, 0, $petBuffer.Length)) -gt 0) {
            $petTotal += $petCount
            if ($petTotal -gt $Limit) { throw '远端文件超出允许大小。' }
            $petOutput.Write($petBuffer, 0, $petCount)
        }
        return $true
    }
    catch [Net.WebException] {
        $petFailureResponse = $_.Exception.Response
        if ($null -ne $petFailureResponse) {
            $petStatus = [int]$petFailureResponse.StatusCode
            $petFailureResponse.Close()
            if ($petStatus -eq 404) { return $false }
            throw ('匿名下载失败，HTTP ' + $petStatus + '。请检查仓库和 Package Registry 是否允许公开下载。')
        }
        throw '匿名下载失败。请检查网络连接；没有发布新的 latest 清单。'
    }
    finally {
        if ($null -ne $petOutput) { $petOutput.Dispose() }
        if ($null -ne $petStream) { $petStream.Dispose() }
        if ($null -ne $petResponse) { $petResponse.Close() }
    }
}

function Send-PetPackageFile([string]$Url, [string]$Path) {
    $petResponse = $null
    $petInput = $null
    $petOutput = $null
    try {
        $petRequest = [Net.HttpWebRequest]::Create($Url)
        $petRequest.Method = 'PUT'
        $petRequest.AllowAutoRedirect = $false
        $petRequest.Timeout = 30000
        $petRequest.ReadWriteTimeout = 120000
        $petRequest.ContentType = 'application/octet-stream'
        $petRequest.ContentLength = (Get-Item -LiteralPath $Path).Length
        $petRequest.Headers['PRIVATE-TOKEN'] = $petToken
        $petInput = [IO.File]::OpenRead($Path)
        $petOutput = $petRequest.GetRequestStream()
        $petInput.CopyTo($petOutput)
        $petOutput.Dispose(); $petOutput = $null
        $petResponse = $petRequest.GetResponse()
        if ([int]$petResponse.StatusCode -notin @(200, 201)) { throw ('上传失败，HTTP ' + [int]$petResponse.StatusCode) }
    }
    catch [Net.WebException] {
        $petFailureResponse = $_.Exception.Response
        if ($null -ne $petFailureResponse) {
            $petStatus = [int]$petFailureResponse.StatusCode
            $petFailureResponse.Close()
            throw ('上传失败，HTTP ' + $petStatus + '。请检查个人凭证的 api 权限、有效期和项目权限。')
        }
        throw '上传失败。请检查网络；重新执行会先验证已经上传的版本包。'
    }
    finally {
        if ($null -ne $petOutput) { $petOutput.Dispose() }
        if ($null -ne $petInput) { $petInput.Dispose() }
        if ($null -ne $petResponse) { $petResponse.Close() }
    }
}

$petCheckFile = Join-Path $petRelease ('.publish-check-' + [Guid]::NewGuid().ToString('N') + '.tmp')
try {
    $petHasLatest = Receive-PetPublicFile -Url $petLatestUrl -Destination $petCheckFile -Limit 65536
    if ($petHasLatest) {
        $petExistingLatest = Get-Content -LiteralPath $petCheckFile -Raw -Encoding UTF8 | ConvertFrom-Json
        if ([string]$petExistingLatest.version -notmatch '^(0|[1-9][0-9]{0,5})\.(0|[1-9][0-9]{0,5})\.(0|[1-9][0-9]{0,5})$') { throw '远端更新清单格式异常，停止发布。' }
        if ([version]$petExistingLatest.version -gt [version]$petVersion) { throw '远端版本更新，拒绝把 latest 降级。' }
        if ([version]$petExistingLatest.version -eq [version]$petVersion -and ($petExistingLatest.sha256 -ne $petManifest.sha256 -or $petExistingLatest.packageUrl -ne $petManifest.packageUrl -or [long]$petExistingLatest.size -ne [long]$petManifest.size)) { throw '相同版本已有不同发布内容。请提升 config.json 的版本号。' }
    }
    $petHasPackage = Receive-PetPublicFile -Url $petPackageUrl -Destination $petCheckFile -Limit 104857600
    if ($petHasPackage) {
        if ((Get-Item -LiteralPath $petCheckFile).Length -ne [long]$petManifest.size -or (Get-FileHash -LiteralPath $petCheckFile -Algorithm SHA256).Hash -ne [string]$petManifest.sha256) { throw '该版本的远端压缩包已有不同内容。版本包不可覆盖，请提升版本号。' }
        Write-Output ('版本包已经存在且校验一致：' + $petVersion)
    }
    else {
        Send-PetPackageFile -Url $petPackageUrl -Path $petPackagePath
        Write-Output ('版本包上传完成：' + $petVersion)
    }
    if (-not (Receive-PetPublicFile -Url $petPackageUrl -Destination $petCheckFile -Limit 104857600)) { throw '上传后无法匿名下载版本包；没有发布 latest。' }
    if ((Get-Item -LiteralPath $petCheckFile).Length -ne [long]$petManifest.size -or (Get-FileHash -LiteralPath $petCheckFile -Algorithm SHA256).Hash -ne [string]$petManifest.sha256) { throw '上传后的远端版本包校验失败；没有发布 latest。' }
    # Publish latest only after the exact version package is downloadable and verified.
    if (-not $petHasLatest -or [version]$petExistingLatest.version -ne [version]$petVersion) { Send-PetPackageFile -Url $petLatestUrl -Path $petManifestPath }
    if (-not (Receive-PetPublicFile -Url $petLatestUrl -Destination $petCheckFile -Limit 65536)) { throw '发布后无法匿名下载 latest 清单。' }
    $petPublished = Get-Content -LiteralPath $petCheckFile -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($petPublished.version -ne $petManifest.version -or $petPublished.packageUrl -ne $petManifest.packageUrl -or $petPublished.sha256 -ne $petManifest.sha256 -or [long]$petPublished.size -ne [long]$petManifest.size) { throw 'latest 清单校验失败，请检查 GitLab 是否允许 generic package 的 latest/update.json 重复上传。' }
    Write-Output ('发布并匿名下载校验通过：v' + $petVersion)
    Write-Output ('更新地址：' + $petLatestUrl)
}
finally {
    if (Test-Path -LiteralPath $petCheckFile) { Remove-Item -LiteralPath $petCheckFile -Force }
    $petToken = $null
}

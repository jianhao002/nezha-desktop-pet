param([string]$ReleaseDirectory, [ValidateRange(15, 1800)][int]$UploadTimeoutSeconds = 180)
$ErrorActionPreference = 'Stop'
Write-Host 'GitHub release: checking source and package'
$petProjectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$petSettings = Get-Content -LiteralPath (Join-Path $petProjectRoot 'update-settings.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if ($petSettings.host -ne 'https://github.com' -or $petSettings.projectPath -ne 'jianhao002/nezha-desktop-pet') { throw '发布目标不是已确认的个人 GitHub 仓库。' }
if ((git -C $petProjectRoot remote get-url origin).Trim() -ne 'https://github.com/jianhao002/nezha-desktop-pet.git' -or (git -C $petProjectRoot branch --show-current).Trim() -ne 'main') { throw '请在个人 GitHub 仓库的 main 发布。' }
if (@(git -C $petProjectRoot status --porcelain).Count -gt 0) { throw '请先提交源码。' }
$petHead = (git -C $petProjectRoot rev-parse HEAD).Trim()
$petRemoteHead = @(git -C $petProjectRoot ls-remote origin refs/heads/main)
if ($LASTEXITCODE -ne 0 -or $petRemoteHead.Count -ne 1 -or ($petRemoteHead[0] -split '\s+')[0] -ne $petHead) { throw 'main 尚未完整推送到 GitHub。' }
$petVersion = [string](Get-Content -LiteralPath (Join-Path $petProjectRoot 'config.json') -Raw -Encoding UTF8 | ConvertFrom-Json).version
if ($petVersion -notmatch '^(0|[1-9][0-9]{0,5})\.(0|[1-9][0-9]{0,5})\.(0|[1-9][0-9]{0,5})$') { throw '版本号格式错误。' }
if ([string]::IsNullOrWhiteSpace($ReleaseDirectory)) { $ReleaseDirectory = Join-Path $petProjectRoot ('dist\release-' + $petVersion) }
$petReleaseRoot = [IO.Path]::GetFullPath($ReleaseDirectory)
$petManifestPath = Join-Path $petReleaseRoot 'update.json'
$petZipName = 'nezha-desktop-pet-' + $petVersion + '.zip'
$petZipPath = Join-Path $petReleaseRoot $petZipName
$petManifest = Get-Content -LiteralPath $petManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$petExpectedUrl = 'https://github.com/jianhao002/nezha-desktop-pet/releases/download/v' + $petVersion + '/' + $petZipName
if ($petManifest.version -ne $petVersion -or $petManifest.packageUrl -ne $petExpectedUrl -or (Get-Item -LiteralPath $petZipPath).Length -ne [long]$petManifest.size -or (Get-FileHash -LiteralPath $petZipPath -Algorithm SHA256).Hash -ne $petManifest.sha256) { throw '发布包与版本、仓库或校验清单不一致。' }
$petApiBase = 'https://api.github.com/repos/jianhao002/nezha-desktop-pet'
$petToken = $null
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

function Get-PetCachedGitHubCredential {
    $petStart = New-Object Diagnostics.ProcessStartInfo
    $petStart.FileName = (Get-Command git -ErrorAction Stop).Source
    $petStart.Arguments = '-c credential.interactive=never credential fill'
    $petStart.WorkingDirectory = $petProjectRoot
    $petStart.UseShellExecute = $false
    $petStart.CreateNoWindow = $true
    $petStart.RedirectStandardInput = $true
    $petStart.RedirectStandardOutput = $true
    $petStart.RedirectStandardError = $true
    $petStart.EnvironmentVariables['GIT_TERMINAL_PROMPT'] = '0'
    $petStart.EnvironmentVariables['GCM_INTERACTIVE'] = 'never'
    $petStart.EnvironmentVariables['GIT_TRACE'] = '0'
    $petProcess = New-Object Diagnostics.Process
    $petProcess.StartInfo = $petStart
    $petCredentialText = $null
    try {
        if (-not $petProcess.Start()) { return $null }
        $petOutputTask = $petProcess.StandardOutput.ReadToEndAsync()
        $petErrorTask = $petProcess.StandardError.ReadToEndAsync()
        $petProcess.StandardInput.Write("protocol=https`nhost=github.com`npath=jianhao002/nezha-desktop-pet.git`nusername=jianhao002`n`n")
        $petProcess.StandardInput.Close()
        if (-not $petProcess.WaitForExit(15000)) { $petProcess.Kill(); return $null }
        $petCredentialText = $petOutputTask.Result
        $null = $petErrorTask.Result
        if ($petProcess.ExitCode -eq 0) {
            foreach ($petLine in ($petCredentialText -split "`r?`n")) {
                if ($petLine.StartsWith('password=')) { return $petLine.Substring(9) }
            }
        }
        return $null
    }
    finally { $petCredentialText = $null; $petProcess.Dispose() }
}

function Send-PetGitHubFile([Uri]$Uri, [string]$File) {
    if ($File.Contains('"') -or $Uri.AbsoluteUri.Contains('"')) { throw '上传路径不能含双引号。' }
    if ([IO.Path]::GetFullPath($File) -notin @([IO.Path]::GetFullPath($petZipPath), [IO.Path]::GetFullPath($petManifestPath)) -or
        $Uri.Host -ne 'uploads.github.com' -or $Uri.AbsolutePath -notmatch '^/repos/jianhao002/nezha-desktop-pet/releases/[0-9]+/assets$' -or
        $Uri.Query -ne ('?name=' + [Uri]::EscapeDataString([IO.Path]::GetFileName($File)))) { throw '只允许上传本次已校验的安装包和更新清单。' }
    $petCurlStart = New-Object Diagnostics.ProcessStartInfo
    $petCurlStart.FileName = (Get-Command curl.exe -ErrorAction Stop).Source
    $petCurlStart.Arguments = '--http1.1 --silent --show-error --fail --connect-timeout 20 --max-time ' + $UploadTimeoutSeconds + ' --request POST --header @- --upload-file "' + $File + '" --write-out "\nNEZHA_UPLOAD http=%{http_code} bytes=%{size_upload} speed=%{speed_upload} seconds=%{time_total}\n" "' + $Uri.AbsoluteUri + '"'
    $petCurlStart.UseShellExecute = $false
    $petCurlStart.CreateNoWindow = $true
    $petCurlStart.RedirectStandardInput = $true
    $petCurlStart.RedirectStandardOutput = $true
    $petCurlStart.RedirectStandardError = $true
    $petCurlProcess = New-Object Diagnostics.Process
    $petCurlProcess.StartInfo = $petCurlStart
    $petCurlText = $null
    try {
        if (-not $petCurlProcess.Start()) { throw '无法启动附件上传。' }
        $petCurlOutput = $petCurlProcess.StandardOutput.ReadToEndAsync()
        $petCurlError = $petCurlProcess.StandardError.ReadToEndAsync()
        # Send credentials through stdin only. Never put headers in arguments or files.
        $petCurlProcess.StandardInput.Write("Authorization: Bearer " + $petToken + "`nUser-Agent: NezhaDesktopPet-Release`nAccept: application/vnd.github+json`nX-GitHub-Api-Version: 2022-11-28`nContent-Type: application/octet-stream`nExpect:`n")
        $petCurlProcess.StandardInput.Close()
        if (-not $petCurlProcess.WaitForExit(($UploadTimeoutSeconds + 15) * 1000)) { $petCurlProcess.Kill(); throw '附件上传超时；草稿保留。' }
        $petCurlText = $petCurlOutput.Result
        $null = $petCurlError.Result
        $petMetricPattern = '(?m)^NEZHA_UPLOAD http=[0-9]+ bytes=[0-9]+ speed=[0-9.]+ seconds=[0-9.]+\r?$'
        $petMetric = [regex]::Match($petCurlText, $petMetricPattern)
        if ($petMetric.Success) { Write-Host $petMetric.Value }
        if ($petCurlProcess.ExitCode -ne 0) { throw ('附件上传失败，curl 退出码 ' + $petCurlProcess.ExitCode + '；草稿和本地包保留。') }
        if (-not $petMetric.Success) { throw '未收到附件上传结果指标；保留草稿供核对。' }
        return ([regex]::Replace($petCurlText, $petMetricPattern, '') | ConvertFrom-Json)
    }
    finally { $petCurlText = $null; $petCurlProcess.Dispose() }
}

function Invoke-PetGitHubApi([string]$Method, [string]$Url, [object]$Body, [string]$File, [switch]$AllowMissing) {
    $petUri = [Uri]$Url
    if ($petUri.Scheme -ne 'https' -or -not $petUri.IsDefaultPort -or $petUri.UserInfo -or $petUri.Fragment -or
        -not (($petUri.Host -eq 'api.github.com' -and ($petUri.AbsolutePath -eq '/user' -or $petUri.AbsolutePath.StartsWith('/repos/jianhao002/nezha-desktop-pet/'))) -or
              ($petUri.Host -eq 'uploads.github.com' -and $petUri.AbsolutePath -match '^/repos/jianhao002/nezha-desktop-pet/releases/[0-9]+/assets$'))) { throw '拒绝向未确认的地址发送发布凭据。' }
    $petResponse = $null; $petStream = $null; $petReader = $null; $petInput = $null
    Write-Host ('GitHub release: ' + $Method + ' ' + $petUri.AbsolutePath)
    try {
        if ($File) {
            if ($Method -ne 'POST' -or $petUri.Host -ne 'uploads.github.com') { throw '附件只能上传到已校验的 GitHub 地址。' }
            return (Send-PetGitHubFile -Uri $petUri -File $File)
        }
        $petRequest = [Net.HttpWebRequest]::Create($petUri)
        $petRequest.Method = $Method
        $petRequest.AllowAutoRedirect = $false
        $petRequest.ServicePoint.Expect100Continue = $false
        $petRequest.Timeout = 180000
        $petRequest.ReadWriteTimeout = 180000
        $petRequest.UserAgent = 'NezhaDesktopPet-Release'
        $petRequest.Accept = 'application/vnd.github+json'
        $petRequest.Headers['X-GitHub-Api-Version'] = '2022-11-28'
        $petRequest.Headers['Authorization'] = 'Bearer ' + $petToken
        if ($null -ne $Body) {
            $petBytes = [Text.Encoding]::UTF8.GetBytes(($Body | ConvertTo-Json -Depth 10 -Compress))
            $petRequest.ContentType = 'application/json; charset=utf-8'
            $petRequest.ContentLength = $petBytes.Length
            $petStream = $petRequest.GetRequestStream()
            $petStream.Write($petBytes, 0, $petBytes.Length)
            $petStream.Dispose(); $petStream = $null
        }
        $petResponse = $petRequest.GetResponse()
        $petReader = New-Object IO.StreamReader($petResponse.GetResponseStream(), [Text.Encoding]::UTF8)
        return ($petReader.ReadToEnd() | ConvertFrom-Json)
    }
    catch [Net.WebException] {
        if ($null -ne $_.Exception.Response) {
            $petStatus = [int]$_.Exception.Response.StatusCode
            $_.Exception.Response.Close()
            if ($AllowMissing -and $petStatus -eq 404) { return $null }
            throw ('GitHub 发布请求失败，HTTP ' + $petStatus + '。请检查此仓库的 Contents 读写权限及网络。')
        }
        throw '无法连接 GitHub 发布服务；保留本地包，恢复网络后重试。'
    }
    finally {
        if ($null -ne $petStream) { try { $petStream.Dispose() } catch {} }
        if ($null -ne $petInput) { $petInput.Dispose() }
        if ($null -ne $petReader) { $petReader.Dispose() }
        if ($null -ne $petResponse) { $petResponse.Close() }
    }
}

try {
    Write-Host 'GitHub release: reading existing Git login'
    $petToken = Get-PetCachedGitHubCredential
    if ([string]::IsNullOrWhiteSpace($petToken)) {
        if ([Console]::IsInputRedirected) { throw '本次发布未能读取标准 Git 登录；请在本机完成 GitHub 登录后再运行，非交互发布不会等待令牌输入。' }
        $petSecureToken = Read-Host '请输入仅此仓库 Contents 读写权限的 GitHub 令牌（隐藏输入）' -AsSecureString
        $petPointer = [IntPtr]::Zero
        try {
            $petPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($petSecureToken)
            $petToken = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($petPointer)
        }
        finally {
            if ($petPointer -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($petPointer) }
            $petSecureToken.Dispose()
        }
    }
    Write-Host 'GitHub release: existing login ready'
    if ([string]::IsNullOrWhiteSpace($petToken) -or $petToken -match '[\r\n]') { throw '发布凭据为空或格式错误。' }
    $petUser = Invoke-PetGitHubApi -Method GET -Url 'https://api.github.com/user'
    if ($petUser.login -ne 'jianhao002') { throw '发布登录不是已确认的个人账号 jianhao002。' }
    $petLatest = Invoke-PetGitHubApi -Method GET -Url ($petApiBase + '/releases/latest') -AllowMissing
    if ($null -ne $petLatest) {
        if ($petLatest.tag_name -notmatch '^v[0-9]+\.[0-9]+\.[0-9]+$') { throw '远端最新版本标签异常。' }
        if ([version]$petLatest.tag_name.Substring(1) -gt [version]$petVersion) { throw '拒绝把远端最新版本降级。' }
    }
    $petRelease = Invoke-PetGitHubApi -Method GET -Url ($petApiBase + '/releases/tags/v' + $petVersion) -AllowMissing
    if ($null -eq $petRelease) {
        # A draft may not have created its Git tag yet, so lookup by tag can return 404.
        $petDrafts = @(Invoke-PetGitHubApi -Method GET -Url ($petApiBase + '/releases?per_page=100') | Where-Object { $_.draft -and $_.tag_name -eq ('v' + $petVersion) })
        if ($petDrafts.Count -gt 1) { throw '同版本存在多个发布草稿，请先核对失败草稿，停止重复创建。' }
        if ($petDrafts.Count -eq 1) { $petRelease = $petDrafts[0] }
    }
    if ($null -eq $petRelease) {
        $petReleaseNotesPath = Join-Path $petProjectRoot ('docs\RELEASE-v' + $petVersion + '.md')
        $petReleaseNotes = 'Windows 离线桌宠。完整解压后双击 NezhaPet.exe；个人进度保存在 data。旧极狐版首次迁移请按仓库说明手动安装并保留 data、games、.update。'
        if (Test-Path -LiteralPath $petReleaseNotesPath -PathType Leaf) {
            $petReleaseNotes = Get-Content -LiteralPath $petReleaseNotesPath -Raw -Encoding UTF8
            if ($petReleaseNotes.Length -gt 20000) { throw '发布说明过长，停止发布。' }
        }
        $petRelease = Invoke-PetGitHubApi -Method POST -Url ($petApiBase + '/releases') -Body @{
            tag_name = 'v' + $petVersion; target_commitish = $petHead; name = '焰轮哪吒 v' + $petVersion
            body = $petReleaseNotes
            draft = $true; prerelease = $false
        }
    }
    if ($petRelease.draft -and $petRelease.target_commitish -ne $petHead) {
        $petRelease = Invoke-PetGitHubApi -Method PATCH -Url ($petApiBase + '/releases/' + $petRelease.id) -Body @{ target_commitish = $petHead }
    }
    foreach ($petUpload in @(@{ Name = $petZipName; Path = $petZipPath }, @{ Name = 'update.json'; Path = $petManifestPath })) {
        $petLocalHash = (Get-FileHash -LiteralPath $petUpload.Path -Algorithm SHA256).Hash.ToLowerInvariant()
        $petLocalSize = (Get-Item -LiteralPath $petUpload.Path).Length
        $petExisting = @($petRelease.assets | Where-Object { $_.name -eq $petUpload.Name })
        if ($petExisting.Count -gt 1) { throw '同名发布附件重复，停止发布。' }
        if ($petExisting.Count -eq 1) { $petAsset = $petExisting[0] }
        else {
            if (-not $petRelease.draft) { throw '已发布版本缺少附件；不能改写正式版本，请提升版本号。' }
            $petUploadUrl = 'https://uploads.github.com/repos/jianhao002/nezha-desktop-pet/releases/' + $petRelease.id + '/assets?name=' + [Uri]::EscapeDataString($petUpload.Name)
            $petAsset = Invoke-PetGitHubApi -Method POST -Url $petUploadUrl -File $petUpload.Path
        }
        if ($petAsset.state -ne 'uploaded' -or [long]$petAsset.size -ne $petLocalSize -or $petAsset.digest -ne ('sha256:' + $petLocalHash)) { throw '附件内容与本地校验不一致；保留草稿，不覆盖同版本文件。' }
        Write-Output ('PASS uploaded-asset ' + $petUpload.Name)
    }
    if ($petRelease.draft) {
        $null = Invoke-PetGitHubApi -Method PATCH -Url ($petApiBase + '/releases/' + $petRelease.id) -Body @{ draft = $false; make_latest = 'true' }
    }
    $petRuntime = Join-Path $petProjectRoot ('dist\nezha-desktop-v' + $petVersion)
    $petReport = Join-Path $petProjectRoot ('qa\github-online-' + [Guid]::NewGuid().ToString('N'))
    $petCheckProcess = Start-Process -FilePath (Join-Path $petRuntime 'NezhaPet.exe') -ArgumentList ('--update-online-test "' + $petReport + '"') -WorkingDirectory $petRuntime -WindowStyle Hidden -PassThru
    if (-not $petCheckProcess.WaitForExit(300000)) { $petCheckProcess.Kill(); throw ('版本已发布，但匿名客户端验收超时；报告 ' + $petReport) }
    $petCheckProcess.Refresh()
    if ($petCheckProcess.ExitCode -ne 0 -or -not (Test-Path -LiteralPath (Join-Path $petReport 'online-update.txt'))) { throw ('版本已发布，但匿名客户端验收未通过；报告 ' + $petReport) }
    Write-Output ('发布并通过真实客户端匿名下载校验：v' + $petVersion)
    Write-Output ('验证报告：' + $petReport)
    Write-Output ('发布页：https://github.com/jianhao002/nezha-desktop-pet/releases/tag/v' + $petVersion)
}
finally { $petToken = $null }

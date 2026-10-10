param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$petProjectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$petSettings = Get-Content -LiteralPath (Join-Path $petProjectRoot 'update-settings.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if ($petSettings.host -eq 'https://github.com') {
    if ($petSettings.projectPath -ne 'jianhao002/nezha-desktop-pet') { throw '更新设置与已确认的个人 GitHub 仓库不一致。' }
    $petStatus = @(git -C $petProjectRoot status --porcelain)
    if ($LASTEXITCODE -ne 0 -or $petStatus.Count -gt 0) { throw '请先提交源码，再发布。' }
    if ((git -C $petProjectRoot branch --show-current).Trim() -ne 'main') { throw '正式发布需要在 main 分支。' }
    if ((git -C $petProjectRoot remote get-url origin).Trim() -ne 'https://github.com/jianhao002/nezha-desktop-pet.git') { throw 'origin 不是已确认的个人 GitHub 仓库。' }
    & (Join-Path $PSScriptRoot 'Package.ps1') -SkipBuild:$SkipBuild
    git -C $petProjectRoot push origin main
    if ($LASTEXITCODE -ne 0) { throw '源码推送失败，尚未发布程序。请先完成 GitHub 登录。' }
    & (Join-Path $PSScriptRoot 'Publish-GitHub.ps1')
    Write-Output '源码与 GitHub 发布包均已上传。'
    return
}
if ([string]$petSettings.host -ne 'https://jihulab.com' -or [string]$petSettings.projectPath -ne 'hddhhyhy/nezha-desktop-pet' -or [string]$petSettings.projectId -ne '375689') { throw '仓库设置与已确认的个人仓库不一致。' }
$petGit = (Get-Command git -ErrorAction Stop).Source
$petStatus = @(& $petGit -C $petProjectRoot status --porcelain)
if ($LASTEXITCODE -ne 0) { throw '无法读取本地 Git 仓库。' }
if ($petStatus.Count -gt 0) { throw '源码还有未提交内容。请先 git add 和 git commit，再发布。' }
$petBranch = (& $petGit -C $petProjectRoot branch --show-current).Trim()
if ($LASTEXITCODE -ne 0 -or $petBranch -ne 'main') { throw '请先切换到 main 分支。' }
$petRemote = (& $petGit -C $petProjectRoot remote get-url origin).Trim()
if ($LASTEXITCODE -ne 0 -or $petRemote -ne 'https://jihulab.com/hddhhyhy/nezha-desktop-pet.git') { throw 'origin 与已确认的个人仓库不一致，停止发送凭证。' }
# Build and test before requesting credentials. Generated output is ignored by Git.
& (Join-Path $PSScriptRoot 'Package.ps1') -SkipBuild:$SkipBuild
$petSecureToken = Read-Host '请输入个人极狐 GitLab 短期 api 凭证（输入隐藏，发布后不保存）' -AsSecureString
$petPointer = [IntPtr]::Zero
$petOldEnvironmentToken = [Environment]::GetEnvironmentVariable('NEZHA_GITLAB_TOKEN', 'Process')
$petPlainToken = $null
$petAuthorization = $null
$petGitProcess = $null
try {
    $petPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($petSecureToken)
    $petPlainToken = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($petPointer)
    if ([string]::IsNullOrWhiteSpace($petPlainToken) -or $petPlainToken -match '[\r\n]') { throw '凭证为空或格式错误。' }
    $petAuthorization = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes('hddhhyhy:' + $petPlainToken))
    $petStart = New-Object Diagnostics.ProcessStartInfo
    $petStart.FileName = $petGit
    $petStart.Arguments = 'push -u origin main'
    $petStart.WorkingDirectory = $petProjectRoot
    $petStart.UseShellExecute = $false
    $petStart.CreateNoWindow = $true
    $petStart.RedirectStandardOutput = $true
    $petStart.RedirectStandardError = $true
    # Credentials live only in this child environment, never in arguments or git config.
    $petStart.EnvironmentVariables['GIT_CONFIG_COUNT'] = '3'
    $petStart.EnvironmentVariables['GIT_CONFIG_KEY_0'] = 'http.https://jihulab.com/.extraHeader'
    $petStart.EnvironmentVariables['GIT_CONFIG_VALUE_0'] = 'Authorization: Basic ' + $petAuthorization
    $petStart.EnvironmentVariables['GIT_CONFIG_KEY_1'] = 'http.followRedirects'
    $petStart.EnvironmentVariables['GIT_CONFIG_VALUE_1'] = 'false'
    $petStart.EnvironmentVariables['GIT_CONFIG_KEY_2'] = 'http.sslBackend'
    $petStart.EnvironmentVariables['GIT_CONFIG_VALUE_2'] = 'openssl'
    $petStart.EnvironmentVariables['GIT_TERMINAL_PROMPT'] = '0'
    $petStart.EnvironmentVariables['GCM_INTERACTIVE'] = 'never'
    $petStart.EnvironmentVariables['GIT_TRACE'] = '0'
    $petStart.EnvironmentVariables['GIT_CURL_VERBOSE'] = '0'
    $petStart.EnvironmentVariables['GIT_TRACE_CURL'] = '0'
    $petGitProcess = New-Object Diagnostics.Process
    $petGitProcess.StartInfo = $petStart
    if (-not $petGitProcess.Start()) { throw '无法启动 Git。' }
    $petOutputTask = $petGitProcess.StandardOutput.ReadToEndAsync()
    $petErrorTask = $petGitProcess.StandardError.ReadToEndAsync()
    if (-not $petGitProcess.WaitForExit(180000)) { $petGitProcess.Kill(); throw '源码推送超时；没有发布 latest 清单。' }
    # Collect output to drain pipes; do not print credential-bearing child diagnostics.
    $null = $petOutputTask.Result
    $null = $petErrorTask.Result
    if ($petGitProcess.ExitCode -ne 0) { throw ('源码推送失败，Git 退出码 ' + $petGitProcess.ExitCode + '。请检查凭证权限、有效期和网络；没有发布 latest 清单。') }
    Write-Output '源码 main 分支推送完成。现在上传已经通过测试的发布包。'
    [Environment]::SetEnvironmentVariable('NEZHA_GITLAB_TOKEN', $petPlainToken, 'Process')
    & (Join-Path $PSScriptRoot 'Publish.ps1')
    Write-Output '源码与发布包均已上传。其他电脑可从桌宠的更新菜单获取新版。'
}
finally {
    [Environment]::SetEnvironmentVariable('NEZHA_GITLAB_TOKEN', $petOldEnvironmentToken, 'Process')
    if ($petPointer -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($petPointer) }
    if ($null -ne $petGitProcess) { $petGitProcess.Dispose() }
    if ($null -ne $petStart) {
        $petStart.EnvironmentVariables.Remove('GIT_CONFIG_VALUE_0')
        $petStart.EnvironmentVariables.Clear()
    }
    if ($null -ne $petSecureToken) { $petSecureToken.Dispose() }
    $petPlainToken = $null
    $petAuthorization = $null
    $petOldEnvironmentToken = $null
}

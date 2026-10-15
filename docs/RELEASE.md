# 上传源码与发布桌宠

当前个人公开仓库：[jianhao002/nezha-desktop-pet](https://github.com/jianhao002/nezha-desktop-pet)。此账号下的 `renxingshiyan` 是独立项目，本仓库的脚本只操作哪吒仓库。

源码像菜谱，发布包是做好的菜。`git push` 上传源码；编译、验证、打包、发布 GitHub Release 后，桌宠更新按钮才能发现新版。使用者电脑无需 Git、编译器或下载凭证。

## 开发者发布新版

1. 在功能分支开发并验证；公司端审查后合并到 `main`。
2. 提升 `config.json` 的三段版本号，提交源码和交接记录，确认工作区干净。
3. 在本仓库运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Publish-Release.ps1
```

脚本检查当前分支和个人仓库地址 → 构建 → 完整验证 → 校验最终 ZIP → 用本机 Git 登录推送 main → 创建 GitHub Release 草稿 → 上传 ZIP 与 update.json → 对照 GitHub 附件大小和 SHA256 → 公开发布 → 用真实桌宠客户端匿名下载并检查安装暂存包。

脚本优先使用本机 Git 已有的 GitHub 登录，凭据只在发布进程的内存中使用，不输出或写入源码、Git 配置、远程 URL、报告或压缩包。没有可用登录时会提示隐藏输入令牌；只为 `nezha-desktop-pet` 授予 Contents 读写权限即可，不要选择所有仓库。浏览器登录和 Git 登录各自独立。

若本轮已经构建并通过测试，可使用 `-SkipBuild` 复用程序；默认仍执行完整测试。同版本附件不覆盖，内容变化必须提升版本。网络中断后保留 `dist/release-版本`，可以直接运行 `scripts/Publish.ps1` 复用候选包；它不会重新打包。

附件上传使用 Windows 的 `curl.exe`，令牌仅经子进程标准输入传递，不放入命令行或文件。仍限制为同一个 GitHub 仓库的官方 HTTPS 上传地址、不跟随上传重定向，并核对服务器返回的大小与 SHA256。重试时会查找该版本已有草稿，避免因为草稿尚未创建 Git 标签而重复新建；多个同版本草稿会停止，先核对失败记录再处理。

慢速上传默认允许十五分钟，并输出不含凭据的 HTTP 状态、已传字节与速度。需要调整时直接运行 `Publish-GitHub.ps1 -UploadTimeoutSeconds 900`；已明确核对草稿 ID 时，可加 `-DraftReleaseId ID` 精确续传发布流程。重新上传从文件开头开始，已经完整上传且校验一致的附件直接复用。

## 发布与更新地址

- 发布标签：`v0.6.1` 等固定版本。
- 最新清单：`https://github.com/jianhao002/nezha-desktop-pet/releases/latest/download/update.json`。
- 固定包：`https://github.com/jianhao002/nezha-desktop-pet/releases/download/v版本/nezha-desktop-pet-版本.zip`。
- 清单字段：version、packageUrl、sha256、size。

GitHub 把附件下载跳转到官方 CDN（文件分发服务器）。客户端逐次检查 HTTPS、当前仓库路径和允许的官方域名，最多跟随五次跳转；不发送登录凭证。版本包仍严格固定到同一仓库、同一版本，不能由清单随意换成其他项目。

发布包不包含 data、games 本机扩展、.update、源码或凭证；内置游戏嵌入 exe，沿用旧版 ZIP 文件白名单。更新保存进度后等待旧进程退出，备份替换，检查启动，失败时尝试回滚。

## 从旧极狐版本迁移

旧 0.4.1 / 0.6.0 程序只认极狐地址，而且会拒绝更新包修改仓库，第一次不能直接点旧版更新按钮迁到 GitHub。请按 [迁移说明](GITHUB_MIGRATION.md) 手动安装完整 GitHub 版并保留 data、games 和 .update。以后可以从新版菜单一键更新。

极狐历史发布说明保留在 [RELEASE-GITLAB-HISTORY.md](RELEASE-GITLAB-HISTORY.md)。迁移没有删除原极狐仓库或旧发布包，也不把旧清单指向一个旧程序无法安装的新来源。

## 验证结果怎么判断

- 本地构建、测试和 ZIP 校验成功，只说明候选程序通过检查。
- Release 出现且真实客户端匿名检查、下载、解压校验通过，才说明公开更新源可以使用。
- 自动化启动与回滚检查不等于家庭电脑的缩放、多显示器和人工试玩；这些仍应在目标机器验收。

GitHub Release 支持和固定下载链接见 [官方说明](https://docs.github.com/en/repositories/releasing-projects-on-github/linking-to-releases)；发布 API 见 [官方文档](https://docs.github.com/en/rest/releases/releases)。

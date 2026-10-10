# 上传源码与发布桌宠

## 为什么不是点更新直接 git pull

源码就像菜谱，发布包是做好的菜。`git push` 负责把菜谱上传；编译、测试、打包负责制作可运行的程序。更新按钮下载正式发布包，用户电脑不需要装 Git 或 C# 开发工具。

个人公开仓库：[hddhhyhy/nezha-desktop-pet](https://jihulab.com/hddhhyhy/nezha-desktop-pet)。请使用个人极狐 GitLab 账号 `hddhhyhy`，不要用公司账号。

公开的是源码和成品程序。各电脑的金币、背包、等级、打工和位置存在自己的 `data`，不会上传到仓库。更新包会替换程序的 `config.json`；个人手工改过养成参数时应另行保存，更新后重新应用。

## 第一次上传

本机源码需要先提交到 `main` 分支。第一次源码上传和版本发布都由下面的本机脚本执行；浏览器已登录不会自动让 Git 命令获得推送权限。

1. 登录个人极狐 GitLab，打开个人设置中的“访问令牌 / Access tokens”。创建短期个人访问令牌，选择 `api` 权限。建议一周内到期，发布完可以撤销。
2. `api` 是发布到 GitLab Package Registry 所需的权限，权限覆盖该账号可访问的 API；它像一把账号钥匙，只在自己电脑的隐藏输入框使用，不要发到聊天里或写入 Git。GitLab 的支持方式见 [官方 generic package 文档](https://docs.gitlab.com/user/packages/generic_packages/)。
3. 在开发目录运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Publish-Release.ps1
```

脚本先编译、测试、打包，再提示隐藏输入个人令牌。随后推送 `main` 源码，推送成功才上传压缩包，校验匿名下载成功后最后上传 `latest/update.json`。凭证只传给本次子进程和请求；不写入磁盘、Git 配置、远程 URL 或发布包，也不会打印。

完成后应该看到：

```text
源码 main 分支推送完成。
发布并匿名下载校验通过：v0.3.1
源码与发布包均已上传。
```

实际输出会包含发布路径和版本。出现编译、测试、推送或校验错误时，脚本停止；不要把“本地包生成成功”当成“远端发布成功”。

## 后续发新版

1. 在开发目录修改源码/素材与 `config.json`，把 `version` 提升为 `0.3.2` 等新版本。
2. 提交到本机 Git：

```powershell
git status
git add .
git commit -m "Release v0.3.2"
```

3. 运行同一个 `Publish-Release.ps1`。脚本要求源码已提交、当前分支为 `main`、origin 为个人仓库，防止把凭证发到错误仓库。
4. 其他电脑从桌宠的更新菜单检查新版。每台电脑无需配置下载令牌。

发布脚本默认重新编译和测试。只有已经生成并验证过本地程序时，才可用 `-SkipBuild` 跳过编译；测试仍会执行。如果压缩包已上传、发布 latest 时网络中断，应保留 `dist` 并用下面的命令重试，复用同一份程序；重新编译产生不同内容时不能覆盖同一个已发布版本。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Publish-Release.ps1 -SkipBuild
```

## 发布包和清单

`Package.ps1` 生成：

```text
dist/
  nezha-desktop-v0.3.1/       可运行文件
  release-0.3.1/
    nezha-desktop-pet-0.3.1.zip
    update.json
```

zip 根目录直接包含 exe、配置、README 和 `assets`，没有外层版本文件夹。发布包不含源码、测试存档、真实存档、凭证或构建工具。

清单字段的含义：

`update-settings.json` 同时保留个人仓库路径与数字项目 ID `375689`。浏览器和 Git 使用仓库路径；更新 API 使用数字 ID，避免 Windows .NET Framework 将路径中的编码斜杠重新解释。这个 ID 是公开仓库编号，不是凭证。

| 字段 | 含义 |
| --- | --- |
| `version` | 新程序版本，必须大于本机版本才安装 |
| `packageUrl` | 本仓库的固定版本下载地址 |
| `sha256` | zip 内容的校验指纹 |
| `size` | zip 字节数，用于拒绝截断或异常下载 |

发布顺序是“版本压缩包 → 匿名下载并校验 → latest 清单”。因此清单出现时，对应包应该已经可以下载。相同版本允许重复执行，但脚本只接受内容一致的 zip；内容改变就必须提升版本号。

GitLab generic package 默认允许重复上传同名文件，下载最近上传的一份，因此 `latest/update.json` 可以指向新版。不要关闭 latest 文件的重复上传；相关行为见 [GitLab 官方文档源码](https://gitlab.com/gitlab-org/gitlab/-/blob/5244c0f78042ee24d1fe5e0352d88274c22fc884/doc/user/packages/generic_packages/index.md)。发布脚本会在上传后再次下载清单确认。

当前采用本机 Windows 编译、验证与发布，没有假定 GitLab 已配置 Windows Runner。Runner 是替你自动执行构建命令的机器；以后有需要再接自动流水线。

## 更新与失败排查

- 找不到清单 / HTTP 404：维护者尚未发布程序包与 latest 清单；仅上传源码不够。
- HTTP 401 / 403：发布凭证权限或有效期不足，或仓库 Package Registry 未允许公开下载。
- 同版本内容不同：提升版本，重新提交源码与打包，不能覆盖已经发布的版本。
- 更新下载失败：程序和存档仍保留，恢复网络后重试。首次从 0.1 / 0.2 升级需手工解压 0.3.1 并复制 `data`；试用过 0.3.0 的电脑也先手工换到 0.3.1。
- 安装失败：更新助手尝试恢复旧程序，诊断和备份保留在运行目录的 `.update`。恢复失败时保留整个目录再排查；不要先删除 `data` 或备份。
- 新程序测试通过但实际重启失败：查看暂存目录的状态与错误文件，保留完整目录。目标电脑的权限、杀毒软件与系统环境仍需实际验收。

更新校验不是联网账号同步，也不是防作弊系统。目标电脑的实际运行情况应单独验收。

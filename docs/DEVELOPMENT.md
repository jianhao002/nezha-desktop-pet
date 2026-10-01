# 本机开发指南

## 先分清三个地方

开发目录保存“怎么制作哪吒”，GitLab 保存开发目录的历史，运行目录保存“哪吒程序与这台电脑的游戏进度”。这就像 Java 项目：源码是 `src`，编译产物是 `target` 里的 jar，用户使用的是部署后的 jar。

当前家庭电脑开发目录：`F:\nezha-desktop-pet-dev`。它有自己的 Git 仓库，提交范围只覆盖本项目。用户要求开发路径不放在 C 盘；其他开发电脑选择自己的非系统盘目录。

在 PowerShell 中进入开发目录：

```powershell
Set-Location -LiteralPath 'F:\nezha-desktop-pet-dev'
```

## 源码链路

1. `Program.cs` 加载相对程序目录的 `config.json`、素材、存档，创建桌宠窗口。
2. `Desktop.cs` 把透明图片逐帧画出来，把点击坐标映射成头、手、身体等互动。
3. `Core.cs` 接收互动、喂食、购物、打工动作，检查金币与库存，更新数值并写存档。界面不直接改金币。
4. 更新界面调用 `UpdateCore.cs` 查询和下载公开发布包；校验通过后交给单独的 `NezhaUpdater.exe`。更新助手从暂存目录运行，因此可以替换原目录中的 exe。
5. 小游戏从 `games/<id>` 自动发现，点击后编译独立 C# 模块并显示在 `MiniGameWindow`。添加游戏、暂停与保存接口见 [小游戏使用与扩展](MINI_GAMES.md)。

源码主要使用 C# 与 Windows WPF。WPF 是 Windows 的窗口与绘图框架；编译使用系统的 .NET Framework 编译器。运行用户不需要 Python、Node.js、Git 或数据库。

## 改动、编译、验证

1. 修改源码或动作素材。新增食物、工作先看 `config.json`；已有物品 ID 不要随意改名，否则存档中的库存找不到对应物品。
2. 修改 `config.json` 的 `version`，例如由 `0.3.1` 改为 `0.3.2`。版本用三段数字，更新只安装更高版本。
3. 编译：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

生成 `dist\nezha-desktop-v当前版本\NezhaPet.exe` 和 `NezhaUpdater.exe`。编译脚本只清理本仓库 `dist` 下经过路径检查的生成目录；如果发现 `data` 存档就拒绝覆盖。

4. 验证：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test.ps1
```

测试用新的 `qa/run-*` 目录，检查养成规则、窗口、动作帧、更新规则、小游戏和扩展示例，不会使用正式存档。窗口检查通过隐藏进程运行。

5. 需要手动试玩时，把发布产物复制到另一个可写目录再运行。关闭试玩程序以后再编译。

## Git 日常操作

```powershell
git status
git add .
git commit -m "Add a new pet action"
```

`.gitignore` 已忽略 `dist`、`qa`、`data`、更新暂存、环境凭证文件。提交前的 `git status` 应只看到本次改过的源码、素材、说明；不要把个人存档或凭证加入 Git。

仅共享源码时，提交到功能分支后使用 `git push`；正式发布程序使用 `scripts/Publish-Release.ps1`，见 [发布指南](RELEASE.md)。不要在远程 URL 或 `.git/config` 中加入个人令牌。

## 换开发电脑

```powershell
git clone https://jihulab.com/hddhhyhy/nezha-desktop-pet.git
Set-Location -LiteralPath '.\nezha-desktop-pet'
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

换运行电脑不需要克隆源码：关闭桌宠，复制完整运行目录，包括自己的 `data`，再双击 exe。当前目标平台为 Windows 10 / 11；不同缩放比例和多显示器需要在目标电脑实际检查。

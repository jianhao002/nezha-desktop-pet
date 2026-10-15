# 本机开发指南

## 先分清三个地方

开发目录保存“怎么制作哪吒”，GitHub 保存开发目录的历史，运行目录保存“哪吒程序与这台电脑的游戏进度”。这就像 Java 项目：源码是 `src`，编译产物是 `target` 里的 jar，用户使用的是部署后的 jar。

当前家庭电脑使用固定父目录 `F:\nezha-desktop-pet`。源码目录为 `F:\nezha-desktop-pet\source`，有自己的 Git 仓库，提交范围只覆盖本项目。用户要求开发路径不放在 C 盘；其他开发电脑选择自己的非系统盘目录。

公司电脑的固定源码目录为 `E:\pyprojects\nezha`。两台电脑各自检出同一个个人仓库，路径可以不同；共享的是 Git 提交和项目交接记录，本机存档仍分别保留。

| 电脑 | 源码目录 |
| --- | --- |
| 公司 | `E:\pyprojects\nezha` |
| 家庭 | `F:\nezha-desktop-pet\source` |

公司电脑进入项目：`Set-Location -LiteralPath 'E:\pyprojects\nezha'`。换电脑继续同一分支时先检查 `git status`；工作区干净后执行 `git pull --ff-only`，再读 `AGENTS.md` 和 `docs/PROJECT_CONTEXT.md`。结束前提交、推送代码及交接记录，另一台电脑才能取得这些成果。

公司电脑日常试玩固定在 `E:\pyprojects\nezha-preview`，桌面快捷方式名为“焰轮哪吒”。此目录独立于源码和 dist，后续部署保留其中的 `data`、`games`、`.update`。2026-10-10 首次安装 0.6.1 时使用独立新存档，没有迁移或改动旧版个人进度。

```text
F:\nezha-desktop-pet\
  source\                 源码、Git；构建在 dist，验证报告在 qa
  preview\                固定的日常试玩目录，包含本机设置与存档
  archive\preview-0.5.0\  已保留的旧试玩版，需要回退时使用
```

以后继续在 `source` 开发、在 `preview` 试玩，不为每次小改动增加一个运行文件夹。`dist` 中带版本号的发布产物和 `qa` 中独立测试目录仍保留原有结构。

在 PowerShell 中进入开发目录：

```powershell
Set-Location -LiteralPath 'F:\nezha-desktop-pet\source'
```

## 源码链路

1. `Program.cs` 加载相对程序目录的 `config.json`、素材、存档，创建桌宠窗口。
2. `Desktop.cs` 把透明图片逐帧画出来，把点击坐标映射成头、手、身体等互动。
3. `Core.cs` 接收互动、喂食、购物、打工动作，检查金币与库存，更新数值并写存档。界面不直接改金币。
4. 更新界面调用 `UpdateCore.cs` 查询和下载公开发布包；校验通过后交给单独的 `NezhaUpdater.exe`。更新助手从暂存目录运行，因此可以替换原目录中的 exe。
5. 小游戏从 `games/<id>` 自动发现，点击后编译独立 C# 模块并显示在 `MiniGameWindow`。添加游戏、暂停与保存接口见 [小游戏使用与扩展](MINI_GAMES.md)。
6. 格子王国、小黑屋和 Candy Box 2 的独立 C# 包装模块创建 `WebMiniGameControl`，用 WebView2 显示内置的本地网页；按固定游戏 ID 分配独立资源、域名、浏览器配置和本局文件。网页通过消息把普通进度数据交给现有本局存储，再由小窗统一保存 / 暂停 / 退出。源码在各自的 `third_party/<游戏 ID>`，资源与 SDK 由 `WebRuntime` 从 exe 释放到 `data/mini-games`。小黑屋与 Candy Box 2 复用 MIT 公共桌面脚本，但各自适配器保留原游戏许可。

源码主要使用 C# 与 Windows WPF。WPF 是 Windows 的窗口与绘图框架；编译使用系统的 .NET Framework 编译器。运行用户不需要 Python、Node.js、Git 或数据库。

0.8.0 构建第一次需要联网从官方 NuGet 下载固定版本 WebView2 SDK `1.0.3405.78`。`scripts/Prepare-WebDependencies.ps1` 校验固定 SHA256，把包缓存到忽略的 `work/webview2-1.0.3405.78`；后续构建可使用已校验的缓存。JavaScript 与原游戏资源已经包含在 Git，不另行从在线游戏站点下载。SDK 是编译用的接口库；运行时的浏览器引擎是另外安装的 Microsoft Edge WebView2 Runtime。原十三款 C# 游戏运行不要求该引擎。

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

5. 需要手动试玩时，先退出 `preview` 中正在运行的程序，再把验证后的程序、配置和素材更新到这个固定目录。保留其中的 `data`、`games` 和 `.update`，不把整个运行目录清空，也不从旧测试目录覆盖个人存档。内置小游戏会通过现有机制更新未改过的文件；自定义游戏保留。需要回退时才在 `archive` 备份旧版。构建本身仍在源码的 `dist` 内执行，与试玩存档分开。

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
git clone https://github.com/jianhao002/nezha-desktop-pet.git
Set-Location -LiteralPath '.\nezha-desktop-pet'
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

换运行电脑不需要克隆源码：关闭桌宠，复制完整运行目录，包括自己的 `data`，再双击 exe。当前目标平台为 Windows 10 / 11；不同缩放比例和多显示器需要在目标电脑实际检查。

已有家庭源码检出迁到 GitHub 时，不重新创建开发目录，也不覆盖未提交工作。按 [GitHub 迁移说明](GITHUB_MIGRATION.md) 修改 origin、获取远端，再接续相同分支。

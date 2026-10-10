# 从极狐 GitLab 迁到 GitHub

新个人仓库：[jianhao002/nezha-desktop-pet](https://github.com/jianhao002/nezha-desktop-pet)。`renxingshiyan` 是其他项目，请不要对它执行本文操作。

## 家庭电脑：切换源码仓库

继续使用固定目录 `F:\nezha-desktop-pet\source`。提交历史和 `codex/mini-games` 分支均已迁入 GitHub；不需要重新克隆或搬动试玩存档。

先查看并保存自己的工作：

```powershell
Set-Location -LiteralPath 'F:\nezha-desktop-pet\source'
git status --short --branch
git remote -v
```

如果有未提交内容，先提交到自己的功能分支，或由家庭电脑上的 Codex 核对并保存。随后切换远端：

```powershell
git remote set-url origin https://github.com/jianhao002/nezha-desktop-pet.git
git fetch origin
git switch codex/mini-games
git merge --ff-only origin/codex/mini-games
```

这条 merge 只允许沿现有历史向前接续（fast-forward），不会丢弃自己的提交。若提示分叉，停止这条自动接续，请 Codex 阅读本仓库 AGENTS.md、docs/PROJECT_CONTEXT.md，检查两边提交并正常合并；不要硬重置。

公司完成合并后，后续独立功能从最新 main 开始，继续使用同一个源码目录。首次推送按 Git 提示登录个人 GitHub `jianhao002`；不要把密码或令牌写进命令、远程 URL 或聊天里。

可以把下面这段发给家庭电脑的 Codex：

> 哪吒项目已迁到 https://github.com/jianhao002/nezha-desktop-pet。请只操作 F:\nezha-desktop-pet\source，保留未提交内容和本机存档，修改 origin 后获取更新，按 AGENTS.md 和 docs/PROJECT_CONTEXT.md 接续 codex/mini-games。不要修改 GitHub 的 renxingshiyan 项目，不要硬重置；后续新功能从共同 main 新建功能分支。

## 已安装桌宠：首次切换更新来源

旧极狐版不认识 GitHub 下载地址，而且更新程序会拒绝安装偷偷更换仓库的包。所以首次要手动安装完整 GitHub 版；后续才用新版里的“检查并更新”。

1. 从 [GitHub 最新发布](https://github.com/jianhao002/nezha-desktop-pet/releases/latest) 下载 `nezha-desktop-pet-版本.zip`，完整解压到临时目录。
2. 退出正在运行的旧桌宠，保留一份旧运行目录备份。
3. 把解压出的程序、README、config.json、update-settings.json 和 assets 更新到原运行目录。保留原来的 `data`、`games`、`.update`；不要清空整个目录或用测试存档覆盖个人进度。
4. 双击原目录中的新版 `NezhaPet.exe`。金币、背包、等级和游戏最佳成绩继续使用原 data；已有本机游戏修改由内置释放保护机制保留。
5. 新版更新菜单从 GitHub Releases 检查和下载，无需普通使用者登录 GitHub 或配置令牌。

修改源码的 origin 不会自动修改已安装桌宠的更新来源，这两步请分别完成。原极狐仓库和 0.4.1 发布包保留作历史与回退依据。

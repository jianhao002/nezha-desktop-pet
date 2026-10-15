# Candy Box 2 离线接入说明

- 原作：David “aniwey” L. 及上游贡献者，游戏 v1.2.3。
- 源码：https://github.com/candybox2/candybox2.github.io
- 固定源码提交：`35301d438aa2c1e58e8cb3ea215b0af323bddf9b`。原始 TypeScript、ASCII、文本、构建脚本与版权声明均保存在 `www`。
- 上游仓库没有提交生成的 JavaScript。本项目另外保存官方提供的未压缩完整 JavaScript 为 `www/candybox2.js`，来源 https://candybox2.github.io/candybox2_uncompressed.js ，SHA256：`442f3707e5f169568119b69a132432c43613f7bc01e92404ec44a2382c1f4cec`。未声称本机从上述 TypeScript 提交重新生成了此文件；官方未压缩 JS 本身随本包完整提供。
- 原游戏代码为 GPL-3.0-or-later；保留 `candybox2_sourceCodeLicense.txt`，GPL v3 全文在 `www/COPYING.txt`（来自 https://www.gnu.org/licenses/gpl-3.0.txt）。首次打开后的资源目录和个人仓库都提供完整代码与许可。
- 修改日期：2026-10-15。`index.html` 去除过时 AppCache 并加入离线桌面脚本；`desktop.js` / `desktop.css` 为 GPL-3.0-or-later：默认中文提示、保留字符画、横向滚动、暂停和原生保存字段桥接。官方 `candybox2.js` 保持上述原始哈希。
- 公共 `nezha` 时钟 / 窗口脚本来自 `third_party/desktop-web`，使用 MIT 许可；C# 容器只通过本地网页消息通信。
- 资源、装备、解锁进度使用原生 Saving 字段保存。原作 `postLoad` 返回糖果页面；战斗中的位置、敌人、计时和临时药水不逐帧续接。收起 / 失焦暂停期间和程序退出期间不补算产量。原作 ASCII 英文与中文悬浮提示并存，未改写剧情或数值。
- 完整来源与许可可在首次打开后的 `data/mini-games/web-assets/candybox2/<hash>` 查看。该适配未获得原作者背书。

# A Dark Room 离线接入说明

- 原作：doublespeak games / Michael Townsend 及上游贡献者。
- 源码：https://github.com/doublespeakgames/adarkroom
- 固定提交：`1fada4620b6c66bd07bf15a3f1eb8223df8bc1d7`。
- 上游源码、素材与 MPL-2.0 全文保存在 `www`，编译时一起嵌入程序。
- 修改日期：2026-10-15。`index.html` 使用本地 jQuery、移除在线统计、加入桌面入口；`script/engine.js` 的原生导入 / 重启改为先通知桌宠保存，再重载页面。
- `desktop.js` / `desktop.css` 在 MPL-2.0 下提供：中文默认、纵向排版、静音、原生状态与桌宠存档桥接。公共 `nezha` 时钟与窗口脚本来自 `third_party/desktop-web`，使用 MIT 许可。
- 0.9.1：事件框移到页面顶层，标题放回框内，宽度 / 最大高度随窗口变化；替换原作的大型遮罩伪元素，长事件仅纵向滚动。离线版不把联网推广加入随机池。`lang/zh_cn/desktop.js` 为 MPL-2.0 的补充翻译，保留原语言表，并补齐事件脚本中缺失的 197 条字面文案；在事件数据注册前载入。
- 游戏原有状态 `State` 是保存的依据，不保存 DOM、计时器对象或当前事件现场。资源、建设和探索成长沿用原作加载规则；暂停时间不补算收益。首次打开或重开后需点击继续。
- 完整来源与许可可在首次打开后的 `data/mini-games/web-assets/adarkroom/<hash>` 查看，也随个人仓库提供。该适配未获得原作者背书。

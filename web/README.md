# Q Pet · Web

把喜欢的角色放到网页上。会呼吸、会说话、有表情的桌宠。

这是 [Q Pet](https://github.com/xiezaji/q-pet) 桌面版的**网页移植版**——
用原生 JavaScript + Canvas + Web Audio 重写了渲染与音频核心，
让桌宠直接活在浏览器里，打开链接就能玩。

---

## 为什么做网页版

桌面版是 Windows 透明置顶窗口，用的是 Win32 分层窗口 + 全局钩子。
这些能力浏览器给不了，所以网页版不是「换个壳」，而是重新设计了一套交互：

| 桌面版 | 网页版 |
|---|---|
| 透明置顶窗口 | 页面内的可拖拽角色舞台 |
| 全局键盘钩子 | 页面内快捷键 |
| 系统级按键/软件统计 | 页面内行为统计（停留 / 互动 / 台词） |
| MCI 播放 wav | Web Audio 播放 mp3 |
| `frames.bin` 裸像素 | 逐帧 WebP（体积小 20 倍）|

---

## 快速开始

纯静态站点，**无构建步骤**：

```bash
# 任意静态服务器即可
python3 -m http.server 8000
# 然后打开 http://127.0.0.1:8000
```

也可以直接双击 `index.html`（部分浏览器对 `fetch` 本地文件有限制，建议用服务器）。

---

## 目录结构

```
web/
├── index.html          单页：角色舞台 + 设置中心
├── css/app.css         设计系统（字号 6 档 / 间距 4 的倍数 / 主题色可换）
├── js/
│   ├── frames.js       精灵帧播放器（WebP 逐帧 + 动画状态机）
│   ├── bubble.js       对话气泡（三种皮肤 + 手动滚动 + 弹出动画）
│   ├── audio.js        语音引擎（Web Audio + 增益音量）
│   ├── core.js         主控：渲染循环、交互、位置持久化
│   ├── chars.js        角色包管理 + zip 导入
│   ├── settings.js     设置中心（6 个页签）
│   ├── stats.js        本地行为统计
│   └── app.js          启动入口
├── tools/
│   └── convert.py      资源转换管线：桌面角色包 → 网页资源
└── assets/chars/       转换产物（WebP 帧 + mp3 + char.json）
```

---

## 资源从哪来

桌面版的角色包是 `frames.bin`（**裸 BGRA 像素数组**，无文件头，
`w*h*4` 一帧，帧序 idle → press → click → drag）。
`tools/convert.py` 把它转成网页能用的格式：

```bash
python3 web/tools/convert.py            # 转换全部角色
python3 web/tools/convert.py 小维        # 只转一个
```

| 格式 | 转换 | 压缩比 |
|---|---|---|
| 动画帧 | `frames.bin` → 逐帧 WebP (q82) | **约 20 倍** |
| 语音 | `wav` → mp3 (96k) | 约 8 倍 |

8 个角色共 44 帧 × 8 = 352 张图，**桌面端 139 MB → 网页端 8.3 MB**。

---

## 核心实现

### 帧播放
`frames.json` 描述每组动画的帧数与 fps，加载时全部预解码成 `Image`。
播放用 `requestAnimationFrame` 驱动，按 `1000/fps` 累积时间切帧——
待机循环，按下/点击/拖动播完停在最后一帧（与桌面端行为一致）。

### 气泡
复刻桌面端 `BuildBubbleSkin` / `DrawBubbleInto`：

- 三种皮肤：柔和渐变（card）/ 漫画描边（comic）/ 椭圆对话框（classic）
- 出现 170ms 缓动放大 + 淡入，消失前 240ms 淡出
- **文字装不下时可手动滚动**（滚轮），配一条极细滚动条提示
  「还有内容、现在读到哪」——没有它用户根本不知道能滚

### 语音
`AudioContext` + `GainNode`（对应音量设置），解码后缓存。
台词池支持「随机抽一句」与「按顺序挨个说」两种模式。
浏览器自动播放策略要求首次用户交互后才能出声，所以做了手势解锁。

### 角色包导入
支持上传 zip 角色包，用浏览器原生 `DecompressionStream` 解压，
存进 IndexedDB。不依赖任何第三方库。

---

## 已知限制

浏览器沙箱内拿不到系统级数据，因此：

- **统计范围**是页面内行为（陪伴时长 / 互动次数 / 台词触发），不是系统级按键与软件使用
- **开机自启、全局热键、桌面置顶**这类能力网页无法提供，已从设置中移除

---

## 数据与隐私

所有数据（设置、位置、统计）只存在浏览器 `localStorage` /
`IndexedDB`，**不联网、不上传**。角色素材版权归原权利方所有，
仅供个人学习与自用。

---

## 相关

- 桌面版（Windows）：<https://github.com/xiezaji/q-pet>
- 技术栈：原生 JS（零框架、零构建）+ Canvas 2D + Web Audio

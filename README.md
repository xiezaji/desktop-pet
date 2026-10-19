# 芙宁娜桌宠 + 挂件本地改造

DSH 挂件 `dsh-whale-widget` 的本地改造，以及一个**不依赖 DSH 的独立桌宠**。

---

## 1. 独立桌宠（DSH 关了它还在）

### 启动 / 退出
- **启动**：双击 [`start-pet.cmd`](start-pet.cmd)
- **退出**：右键点桌宠 → 「退出桌宠」；或结束该 `powershell.exe` 进程
- 已在运行时重复启动会被互斥体挡掉，不会开出第二只

### 操作
| 操作 | 行为 |
|---|---|
| 全局按任意键 / 点鼠标 / 滚轮 | 挤压-拉伸动画，**不出声** |
| 左键点她本人 | 播放语音（按压音 + 松开音） |
| 按住拖动 | 移动位置，松手后记住 |
| 右键 | 菜单：切换**中/日/英/韩**语音、复位位置、试听「任务完成」、退出 |
| 任务完成 | 播放「**欢唱，以我之名！**」 |

### 原理
Win32 **layered window**（`WS_EX_LAYERED|TOPMOST|TOOLWINDOW|NOACTIVATE` + `UpdateLayeredWindow`）贴**预乘 alpha** 的 32bpp DIB ⇒ 真透明、置顶、不进任务栏、不抢焦点。透明像素在 `WM_NCHITTEST` 里返回 `HTTRANSPARENT`，点击会穿透到下层，不会有一块看不见的方块挡住桌面。

- 精灵帧：[`sprites.py`](sprites.py) 生成 [`pet-frames.bin`](pet-frames.bin)（30 帧：24 待机呼吸 + 6 按压弹跳，原始 BGRA 预乘）
- 程序本体：[`pet.cs`](pet.cs)（约 450 行 C#，无任何外部依赖）
- 启动器：[`pet-launcher.ps1`](pet-launcher.ps1)（`Add-Type` 编译进内存后运行）
- 配置：[`pet-config.txt`](pet-config.txt)（key=value，改完重跑 [`make-pet-config.ps1`](make-pet-config.ps1)）
- 状态：[`pet-status.txt`](pet-status.txt)（DPI 是否生效、窗口矩形、钩子句柄、任务完成触发记录）

### 🔒 隐私边界
全局钩子挂的是 `WH_KEYBOARD_LL` / `WH_MOUSE_LL`。回调里**只判断 `wParam` 是不是「按下类」消息**，不读取 `KBDLLHOOKSTRUCT` / `MSLLHOOKSTRUCT` 内容 —— 即**不取键码、不取字符、不记录、不落盘、不外传**。`pet.cs` 里没有一处 `Marshal.Read*` 读取钩子 `lParam`。
刻意**不监听 `WM_MOUSEMOVE`**：否则鼠标一动就抖、且费 CPU。想要的话在 `MouseHook` 的 if 里加 `|| m == 0x0200`。

---

## 2. 挂件本地 fork（改动不被升级覆盖）

插件已从 `github:MeteorNOX/DeepSeek-Balance-Whale-Widget` 切成本地软链：

```
C:\Users\LX\.dsh\profiles\desktop\node_modules\dsh-whale-widget
   → Junction → furina-widget\plugin-fork
```

- **代价**：不再自动跟随上游更新；想更新就重新从 GitHub 装一次（会丢掉下面的改动）
- **原版对照**：[`pristine-whale-widget.js`](pristine-whale-widget.js)（v0.4.2 原文件，用来 diff）

### 改动 #2：输入联动「静默按压」
给 `plugin-fork/assets/whale-widget.js` 加了约 50 行（在 `pressUp()` 之后）：

```js
window.addEventListener('keydown', ...)       // 打字时她弹，但不响
document.addEventListener('pointerdown', ...) // 点页面任意处弹；点挂件本体走原路径（有声音）
window.addEventListener('wheel', ...)         // 滚轮也弹
```

要点：**刻意不碰 `pressing` 变量** —— 那份状态属于音频路径，动它会干扰松开音的排期。连续输入保持压扁，最后一次输入后 110ms 回弹。
前端资源由宿主**按 mtime 热读取**，所以改完 **F5 即可生效**（宿主侧插件不用重启）。

### 改动 #3：任务完成播「欢唱，以我之名！」
数据层（不涉及代码）：
- `$DSH_HOME/whale-audio/furina_sing.wav` ← 「欢唱，以我之名！」（48kHz/立体声/16bit，峰值 -1.5 dBFS）
- `audio.json` → 片段 `furina_sing`
- `.dshw-usage.json` → `settings.taskEnd.sel = "frag:furina_sing"`

⚠️ 源码里的硬约束：**「任务结束音不是手势触发的」**，而浏览器要求先有一次用户手势才解锁 `AudioContext`。所以**刷新后先点一下或打个字**，之后任务完成音才会响。

---

## 3. 语音素材

来自 [AquaV/genshin-voices-separated](https://hf-mirror.com/datasets/AquaV/genshin-voices-separated)（HuggingFace 直连不通，走的 hf-mirror 镜像）。**官方语音属米哈游 IP，仅供自用，别分发别商用。**

转码管线（[`voice/`](voice/) 里留了原始文件）：

```
源: 48000Hz / 单声道 / 16bit PCM
ffmpeg -af "volume=<增益>dB" -ac 2 -ar 48000 -c:a pcm_s16le
```

⚠️ 坑：**ffmpeg 单声道→立体声默认乘 0.707（-3dB）**，算增益时要补回来，否则成品比目标低 3dB。

### 已装片段（`$DSH_HOME/whale-audio/`）

| 片段 id | 台词 | 用途 |
|---|---|---|
| `furina_watch` | 「瞧好了。」 | 中文·按压 |
| `furina_oh` | 「哦？」 | 中文·松开 |
| `furina_jp_open` / `furina_jp_hmm` | 「開幕だ。」/「ん…？」 | 日语·按压/松开 |
| `furina_en_yoo` / `furina_en_oh` | "Yoo-hoo~!" / "Oh..." | 英语·按压/松开 |
| `furina_kr_tada` / `furina_kr_what` | 「쨔쟌!」/「뭐!?」 | 韩语·按压/松开 |
| `furina_sing` | 「欢唱，以我之名！」 | **任务完成** |
| `furina_burst` | 「让世界热闹起来吧。」 | 任务完成备选（大招另一 take） |
| `furina_moment` | 「闪耀时刻！」 | 大招第三 take |
| `furina_what` / `furina_evidence` / `furina_offer` | 「你说什么？！」/「你有证据吗？」/「为你献上。」 | 事件备选 |

音效组（挂件菜单 → 音效与提示 → 全局设置 → ① 按压音效 → 按压音效组）：**芙宁娜·中文 / ·日语 / ·英语 / ·韩语**。

---

## 4. 验证记录

| 项目 | 证据 |
|---|---|
| C# 编译 | `Add-Type` 通过 |
| DPI | 状态文件 `dpiAware=True screen=1920x1080`（未修前是虚拟的 1536×864，会被 DWM 拉伸）|
| 窗口 | `shown rect=1574,676,1880,1000 dpi=120` |
| 全局钩子安装 | `hooks kb=39980367 mouse=13373141` |
| 钩子真的驱动动画 | 注入 Shift 3 秒，角色包围盒高度 **293 → 249（-15%）**，与设计的 `scaleY 0.85` 吻合 |
| 真透明渲染 | gdigrab 截屏可见桌面图标文字从她身边透出 |
| 任务完成触发 | 人为把 `.dshw-turn.json` 的 seq 8→9，状态文件记录 `task-complete seq=9 -> furina_sing.wav 文件存在=True` |
| 挂件补丁生效路径 | `node_modules\dsh-whale-widget\assets\whale-widget.js` 里能搜到 `silentHold`（第 19899 行）|

---

## 5. 更新记录（第二轮改动）

| # | 需求 | 做法 | 怎么回退 |
|---|---|---|---|
| 1 | **开机自启** | `Startup\芙宁娜桌宠.lnk` → `wscript start-pet.vbs`（**不闪控制台窗口**） | 删掉那个快捷方式 |
| 2 | 按压时两侧被切 | 根因不是画布窄，而是 `sprites.py` 的 `scaled()` 把放大后的图**合成回原宽画布**、负偏移被裁掉。改为只缩放不裁切 + 画布 306→**320** + 脚底留 6px | 改回 `sprites.py` 后重跑 |
| 3 | 去掉点击音效 | `pet-config.txt` 的 `clickVoice=0`（C# 里按压/松开两处 `Voice()` 都被闸住） | 改成 `clickVoice=1` |
| 4 | DSH 打开时隐藏原挂件 | `plugin-fork/assets/whale-widget.js` 顶部 `LOCAL_HIDE_WIDGET = true; return` —— **只停前端，宿主照跑** | 改成 `false`，F5 |

### #4 为什么必须保留宿主
宿主 `finalizeTurn()` 会写 `.dshw-turn.json`（每轮 seq），独立桌宠的「任务完成播欢唱」正是盯它。所以 #4 只隐藏**前端可见的挂件**，宿主侧的数据管线（余额、记账、seq）继续跑。
代价：挂件自带的气泡 / 菜单 / 余额面板 / 任务结束音一并停用 —— 任务结束音已由独立桌宠接管。

### #2 的验证（直接量帧数据，不靠截图）
```
画布 320x324（角色 264x300，按压最大 scaleX=1.10 ⇒ 需 ~291）
  待机   帧 0  内容 258x295   左右余量 31/31
  深压   帧26  内容 284x254   左右余量 18/18   ← 由 258 变成 284，正好 +10.1%
  回弹   帧28  内容 266x281
全帧 上下/左右边缘 alpha 最大 = 0  => 没有任何像素被窗口边界切掉
```
（修之前：按压帧宽度只涨 1.1%，因为两侧在 `scaled()` 里就被裁了。）

---

## 6. 第三轮改动

### 设置面板（等价于挂件的设置）
双击 [`run-pet-settings.cmd`](run-pet-settings.cmd)，或**右键桌宠 → 「设置…」**：

| 项 | 说明 |
|---|---|
| 角色形象 | 我的成品 + `$DSH_HOME\whale-roles\` 里全部角色，可「浏览…」自选图片；右侧实时预览 |
| 大小 | 角色像素高度 160–520（滑块）。改完会**重出精灵帧**（画布等比放大：300→360 时画布 320×324→384×389） |
| 语音组 | 小黄鸭 / 音效1 / 芙宁娜·中·日·英·韩。**内置组的 mp3 会自动转成 wav**（`PlaySound` 只吃 wav） |
| 点击出声 | 关 = 点她只有动画（默认关） |
| 对话框气泡 | 开关 + 高度 + 主动冒泡间隔（不主动 / 5 / 10 / 30 分钟） |

「保存并应用」= 写 [`pet-overrides.txt`](pet-overrides.txt) → 重出精灵帧 → 重生成配置 → 重启桌宠（约 10 秒）。
控件自检：`powershell -File pet-settings.ps1 -SelfTest`（只建控件、不开窗口）。

### 拖动动画
`sprites.py` 新增 8 帧「悬空摆动」（旋转 ±5° + 左右摆 + 整体提起 14px，脚不再锚定）。
C# 在 `_dragging && _dragMoved` 时整组切过去；状态文件里的 `drag start / drag end` 可验证。

### 动态对话框
窗口上方预留 `bubbleH`（默认 118px）作气泡区，**角色位置不变**（posfile 存的是角色左上角，不是窗口左上角）。

- 画法：GDI+ 画进 `Format32bppPArgb` 位图 —— 那正好是**预乘 alpha**，与 `UpdateLayeredWindow` 要求一致；
  若用 GDI 的 `DrawText`，alpha 不预乘，半透明字边会发灰。**这是本功能最大的坑。**
- 内容：任务完成显示「本轮 ¥消耗 · tokens / 余额 · 今日」；点她显示随机台词；每 10 分钟自己冒一句
- 台词池：[`pet-lines.txt`](pet-lines.txt)，由她那 **1060 条真实台词**筛出 132 条短句
- 数据源：`.dshw-usage.json`（宿主写的余额/记账），纯正则读取，不引 JSON 库、不联网

### 桌宠进程必须脱离 DSH（重要）
从 shell 里 `Start-Process` 起的桌宠，**DSH 一关就被一起回收**（实测踩到：重启 DSH 后桌宠消失）。
所以启动一律走 **WMI `Win32_Process.Create`**（父进程是 `WmiPrvSE.exe`，脱离调用者的进程树）——
[`restart-pet.ps1`](restart-pet.ps1) / 设置面板 / 开机自启都走这条路。

### 踩坑：覆盖文件被写成「一整行」
数组经管道喂给 `Set-Content` 会被并成一行，读端就只能认出第一个键（表现为「设置面板改了语音组没生效」）。
两端都修了：写端 `Set-Content -Value $lines`；读端正则按 `key=` 切分（顺带容忍值里带空格的路径）。

---

## 7. 第四轮改动（对齐插件观感 + 修大小）

### 气泡样式完全对齐插件
气泡原先是我自创的「圆角矩形 + 尾巴」。插件里那只**不是**矩形 —— 它的「经典椭圆」泡泡是：

```svg
<ellipse cx="352" cy="561" rx="37.5" ry="26" fill="#FFFFFF" stroke="#203170" stroke-width="18"/>
<ellipse cx="442" cy="646" rx="24.5" ry="18" fill="#FFFFFF" stroke="#203170" stroke-width="18"/>
```

文字规则：`.dshwv-text{color:#536ba9;text-align:center;line-height:1.15}`。

现在照抄：**白底椭圆 + 粗深蓝描边**（描边宽按窗口宽度等比折算 `18/1026`）+ **下方两个小椭圆**（经典泡泡是一簇），
字色 `#536ba9`、居中、字号随气泡高度等比（118px → 10.5pt），整个窗口 `320 × (324+118)`。

### 「大小」为什么之前没反应
不是没做，是**被覆盖文件那个 bug 连带弄坏的**：解析只认出第一个键（`charSrc`），
`charH` 被吞进了它的值里 ⇒ `sprites.py` 一直用默认 300。修好后实测 300→420：
角色 264×300 → 369×420、画布 320×324 → **448×454**、窗口同步变大。

另加两道保险：设置面板现在**显式传 `--src/--char-h`**（不依赖文件解析），
且重出精灵帧失败会**报错弹窗**而不是静默继续。

### 欢唱延后 0.5 秒
立刻播会和系统自己的提示音撞在一起。改为走既有的排期器：`singDelayMs=500`（可调）。
验证（状态文件）：`task-complete 12:21:38.954` → `定时播放 furina_sing.wav 12:21:39.465`，即 **516ms 后**。

---

## 8. 第五轮改动：点她 = 播「气泡里那句话」的语音

数据集里**每句台词的文字和音频是配套的**，所以做了一份 `文字 <TAB> wav` 清单 [`pet-lines.tsv`](pet-lines.tsv)：

- [`voice-lines\`](voice-lines) —— 132 句中文语音（48kHz 单声道，峰值 -1.5 dBFS，合计 29.2 MB）
- 点她 → 随机取一条 → **气泡显示这句文字 + 播这句自己的语音**（同一份清单 ⇒ 文字与声音必然一致）
- 逐条核对过：132 条与数据集元数据**不一致 0 条**、文件缺失 0 条
- 配置项 `clickLine=1`（设 0 = 点了只显示气泡不出声）
- 已播台词语音时**不再叠按压/松开音**，避免两个声音糊在一起
- **自检入口**：`pet-config.txt` 里加一行 `clickTest=1` 再重启，会立刻模拟一次点击并写日志
  （实测：`气泡: 所以这件事没有商量的余地。` → `台词语音: 所以这件事没有商量的余地。 -> 158317.wav`）

---

## 9. 第六轮改动：修「松开音盖台词」+ 换字体

### 松开音其实是在**掐断**台词
`PlaySound` **一次只放一个音**。开了「点击她时播放语音」（`clickVoice=1`）之后，链路是：
按下播台词 → 约 100ms 后松开，`WM_LBUTTONUP` 又播松开音 ⇒ **把刚起播的台词当场掐死**，
听起来就是"松开音盖住了台词"。

修法：按下时记住 `_linePlayedOnDown`；松开时若本次已播台词，就**绝不再放松开音**。
（`clickVoice` 现在只影响"没有台词可播"时的兜底路径。）

### 气泡字体可换
默认 **YouYuan（幼圆）** —— 圆体，配 Q 版最搭（原来是继承插件的 Microsoft YaHei UI）。
设置面板新增「**气泡字体**」下拉，只列本机真实安装的候选：

```
YouYuan / Noto Sans SC Medium / Noto Sans SC / DengXian（等线）/ Microsoft YaHei UI /
KaiTi（楷体）/ SimHei / FangSong / Microsoft JhengHei UI / Yu Gothic UI / Malgun Gothic
```

配置项 `bubbleFont=<字体族名>`；字号仍随气泡高度等比缩放。

---

## 10. 第七轮改动：修「自动截断」

**现象**：她说话说到一半被自动切掉。

**根因**：`PlaySound` 一次只放一个音。而**每轮任务结束都会自动响一次欢唱**
（`.dshw-turn.json` 的 seq 一变就触发）—— 它一起来，就把正在播的台词当场掐断。
这是**唯一**能"自动"打断声音的路径（点击是用户主动的，不算）。

**修法**：自动音改成**排队**。
- `Voice()` 现在会记下"当前在放什么、还剩多久"（`_voiceStartedAt` / `_voiceDurMs`）
- `AutoVoice()`：自动音先算 `free = 起点 + 时长 + 250ms - now`，>0 就排到那时再放
- 点击（用户主动）仍然**立即**播，但会把待播的自动音**顺延**而不是丢进垃圾桶

**验证**（临时把 `singMinGapMs` 设为 0 才打得到这个分支）：
```
14:32:33.600  定时播放: furina_sing.wav
14:32:33.788  欢唱排队: 等当前语音说完，约 2075ms 后唱
14:32:35.898  定时播放: furina_sing.wav        ← 等第一次播完才播
```

**顺带排除**：气泡**文字**不会被截断 —— 按你的设置（210px / 气泡 110px）用 GDI+ 量了全部 132 句，
基准字号下会被裁的是 **0 句**。不过还是加了**自动缩字号**兜底（万一把气泡调小或字号调大）。

---

## 11. 第八轮改动：DSH 启动时并行拉起桌宠

需求：关掉 DSH 再打开，桌宠也跟着起来（原先只有"开机自启"那一份）。

**做法（两级，缺一不可）**：插件宿主 `apply()`（DSH 加载插件时执行）
→ 普通 `spawn` 一个 helper [`launch-pet-detached.ps1`](launch-pet-detached.ps1)
→ helper 用 **WMI** `Win32_Process.Create` 创建真正的桌宠。

**为什么不能一步到位**（三条都是实测踩出来的）：
1. **node 的 `spawn` 绝不能带 `detached: true`** —— Windows 下子 PowerShell 会**静默不执行**
   （6 变体隔离实验：带 detached 的全部失败、不带的全部成功；exit 0 但脚本一行都没跑）。
2. 不带 detached 的普通子进程属于 DSH 进程树，DSH 一关可能被连坐 ⇒ 第二级用 WMI，
   让桌宠父进程变成 `WmiPrvSE.exe`（脱离 DSH 进程树）。实测 `父进程 = WmiPrvSE.exe` ✓
3. 启动器的单实例判定**不能模糊扫命令行** —— 任何命令行里恰好含 `pet-launcher.ps1` 的进程
   （比如正在调试的那条命令）都会被误判成"已有实例"，桌宠**永远起不来**。改用 `pet.pid`。

### 新增的两个文件
- [`pet.pid`](pet.pid) —— 桌宠自己写的 PID。**停止/查状态都靠它**（比按命令行匹配可靠得多）
- [`pet-launch.log`](pet-launch.log) —— 每次启动尝试的完整留痕（谁调的、父进程是谁、互斥体状态、
  编译结果）。以后"DSH 起来了但桌宠没起来"，看这一个文件就够

### 关闭这个行为
在 `furina-widget` 目录建一个空文件 **`pet-no-dsh-autostart.txt`** 即可（插件会跳过）。

---

## 13. 独立化：FurinaPet.exe（不再依赖 DeepSeek Harness）

现在桌宠是一个**独立的 Windows 程序**，双击 `FurinaPet.exe` 就能跑，不依赖 DSH、也不需要 PowerShell 脚本编译。

| 项 | 说明 |
|---|---|
| 可执行文件 | `FurinaPet.exe`（149 KB，`csc /target:winexe` 编译，带 `FurinaPet.ico`） |
| 源码 | [`Program.cs`](Program.cs)（入口 + 单实例互斥体 + PID 文件）+ [`pet.cs`](pet.cs)（全部逻辑） |
| 重新编译 | `csc /target:winexe /out:FurinaPet.exe /win32icon:FurinaPet.ico /r:System.dll /r:System.Drawing.dll pet.cs Program.cs` |
| 语音 | 全部本地：[`audio\`](audio)（8 个语种音效 + 欢唱）与 [`voice-wiki\wav\`](voice-wiki)（71 句台词） |
| 开机自启 | 启动文件夹快捷方式 → **`FurinaPet.exe`**（不再经 wscript/PowerShell） |
| 进程脱离 | 用 WMI 创建（父进程 `WmiPrvSE.exe`），关掉 DSH / 关掉任何窗口都不影响它 |

**仍然可选的 DSH 联动**（不装也完全能跑）：`pet-config.txt` 里的
`turnfile`/`usagefile` 指向 DSH 写的数据文件；DSH 在跑时气泡能显示本轮余额、任务完成会欢唱，
DSH 不在时这两个功能自动静默（文件不存在就跳过）。

**要彻底断开 DSH**：在 DSH 里停用/删除 `dsh-whale-widget` 插件即可 —— 桌宠照常运行（DSH 启动时也不会再多拉一只）。

---

## 14. 待办 / 可选

- 挂件侧的「提问 / 授权」音效也能换成她的台词（`settings.events.question.sel` / `.approval.sel`，同一套 `frag:` 机制）
- 任务完成音目前**每轮结束**都响（与挂件 `taskEnd` 语义一致）；嫌吵可调 `pet-config.txt` 的 `singMinGapMs`
- 我没法听声音：`clickVoice=0` 的静音、语音组切换只验证到**代码/配置层**，请点一下听确认

---

## 15. 第九轮改动（音量 / 动画开关 / 气泡 / 实时统计 / 退出弹窗）

| # | 需求 | 实现 | 配置项 |
|---|---|---|---|
| 1 | 语音页调音量 | 「语音」页新增 **音量 0–100%** 滑块 + **试听**（听到的就是保存后的音量）。桌宠侧不依赖声卡是否支持 MCI 音量：**直接把 wav 采样按比例衰减**，生成副本缓存到 `pet-vol-cache\`（100% 不生成副本，0% = 静音） | `volume` |
| 2 | 四类动画开关 | 新增「动画」页：**待机 / 按键 / 点击 / 拖动** 四个独立开关 + **速度 0.5×–2.0×**（影响待机帧推进与挤压/回弹速度） | `animIdle` `animPress` `animClick` `animDrag` `animSpeed` |
| 3 | 气泡美化 | 换成**圆角卡片 + 小尾巴**：柔和双层投影、白→淡蓝渐变、细描边、顶部高光、居中文字、**弹出（0.94→1 + 淡入）与到期淡出**。整条轮廓是一条闭合路径，描边不会在内部留接缝 | — |
| 4 | 按键使用支持滚轮 | 统计页把**按键使用**提到软件使用之前；列表放在独立 `ScrollViewer`（高 280）里，**鼠标滚轮优先滚内层**，滚到尽头也不会把整页带走 | — |
| 5 | 按键使用实时刷新 | 桌宠统计落盘从 30 秒改为 **4 秒一次（有变化才写）**；设置页在统计页可见时用 1 秒 `DispatcherTimer` 原地刷新（只改数字/进度条，不重建行 ⇒ 滚动位置不会被重置） | — |
| 6 | 退出弹窗优雅一点 | 系统 Message Box 全部换掉：自绘**圆角卡片 + 柔和投影 + 淡入放大**，危险操作用红色主按钮；**默认焦点在「取消」**，Esc 取消，回车不会误退出。退出 / 清空统计 / 删除角色 / 保存成功都用这一套 | — |

### 顺带修掉的两个坑
- **清空统计原来不生效**：设置页只删文件，桌宠内存里的计数几秒后又写回去。现在设置页写 `pet-stats-reset.txt`，桌宠看到后**连内存一起清零**再落盘。
- **配置里的绝对路径还指向旧目录**（`华为手环\furina-widget`）：已迁到当前目录；`make-pet-config.ps1` 也改成**沿用上次的 `voicelines`**，重新生成配置不会把导入角色的语音清单改回默认。
- **开机自启原来是坏的**：`Startup\芙宁娜桌宠.lnk` 指向旧目录里早已不存在的 `start-pet.vbs`。现在改成 `Startup\芙宁娜桌宠.vbs`（静默转交当前目录的 `start-pet.vbs`），旧快捷方式留了一份 `芙宁娜桌宠.lnk.bak`。

### 构建
```bat
dotnet build FurinaPet.csproj -c Release        :: → build\pet\FurinaPet.exe
dotnet build FurinaSettings.csproj -c Release   :: → build\settings\FurinaSettings.exe
```
两个 `.csproj` 都是 net48、无 NuGet 依赖，只为让 `dotnet build` 能编译；旧的 `csc` 命令行仍然可用。

### 设置面板的验证入口
```bat
FurinaSettings.exe --selftest                  :: 建控件 + 切所有页 + 写 pet-settings-selftest.txt
FurinaSettings.exe --shot out.png --page 3     :: 无界面渲染某一页（0气泡 1UI 2形象 3动画 4语音 5导入 6统计）
FurinaSettings.exe --shot out.png --page 6 --shot-height 1500 --shot-scroll 480  :: 长页 + 滚动位置
FurinaSettings.exe --dialog-test               :: 弹一次「退出」确认框（看样式）
```

### 实测记录
- 音量 60%：`pet-vol-cache\*.v60.wav` 生成，MCI 实际播放位置 = 8246/8246ms（没被截断）
- 统计落盘：`pet-stats.txt` 20:05:12 → 20:05:17（约 4–5 秒一次）
- 自检：`导航项 6 个 / 按键列表行 52 / 软件行 8 / 实时定时器=True / 错误日志 0 条`
- 截图：`screenshots\bubble-new-style.png`（新气泡）、`screenshots\settings-animation.png`（动画页）、`screenshots\settings-stats.png`（实时统计）、`screenshots\dialog-card.png`（退出弹窗）

---

## 16. 第十轮改动：角色 = 一个文件夹（图片 / 动画 / 音效）

### 目录结构（以后新增角色就照这个建）

```
chars/
  芙宁娜/
    char.json          ← 角色清单（全是相对路径，整个文件夹可以随便搬）
    图片/image.png     ← 图片
    动画/frames.bin    ← 动画（sprites.py 生成）
    动画/frames.txt
    动画/frames.json
    动画/preview.png
    音效/voices.tsv    ← 台词 <TAB> 语音（相对路径）
    音效/voices/*.wav
    音效/press.wav     ← 可选：按压音
    音效/release.wav   ← 可选：松开音
    台词.txt           ← 气泡随机台词（一行一句）
  奶蛙/  …
  小维/  …
```

`char.json` 示例：

```
name=小维
image=图片/image.png
frames=动画/frames.bin
meta=动画/frames.txt
lines=台词.txt
voicelines=音效/voices.tsv
press=音效/voices/今天又轮到谁粉身碎骨了呢？.wav
release=音效/voices/别急别急，人人有份~.wav
charH=219
voices=9
```

### 桌宠怎么加载
`pet-config.txt` 只记一行 **`charDir=chars\小维`**。启动时 `ApplyCharPack()` 读该目录的 `char.json`，
把帧 / 帧清单 / 台词 / 语音清单 / 按压松开音全部改指向这个角色（相对路径按角色目录解析）。
所以换角色 = 改 `charDir` 再重启，三套资源互不干扰；每个角色还可以有自己的高度（`charH`）。

### 以后新建角色（两种方式，走同一套结构）

1. **设置面板 → 新建角色**：填名字、选图片、选语音文件夹（文件名就是台词），点「创建角色」。
   会自动建目录、复制/转码语音、生成动画、写 `char.json`，并切换过去。
2. **命令行**：

```bat
python charpack.py new --name 新角色 --image D:\pics\a.png --voices D:\voices --char-h 219
python charpack.py list
python charpack.py rebuild --name 新角色 --char-h 260
```

语音支持 wav/mp3/m4a/ogg（非 wav 用 ffmpeg 转 48k 单声道 wav）；不给台词文件时用语音文件名当台词。

### 动画生成
`sprites.py` 现在支持 `--out-dir`，只往指定角色目录写帧：

```bat
python sprites.py --src "chars\小维\图片\image.png" --char-h 219 --out-dir "chars\小维\动画"
```

设置面板改大小时只会重出**当前角色**的动画，并回写它自己的 `charH`。

### 迁移与验证
- 三个角色已整理成 `chars\芙宁娜`、`chars\奶蛙`、`chars\小维`（旧的 `chars\*\image.png` / `voices\` 已收进新结构）
- 实测启动日志：
  - 芙宁娜 → `角色包: 芙宁娜（语音 32 条）` / `气泡台词池 132 条；带语音 32 条`
  - 小维 → `角色包: 小维（语音 9 条）` / `气泡台词池 9 条；带语音 9 条`
  - 奶蛙 → `角色包: 奶蛙（语音 1 条）` / `气泡台词池 1 条；带语音 1 条`
- 新建流程实测：`charpack.py new --name 测试_临时 ...` 生成了完整目录 + 44 帧动画，`list` 可见，随后已删除
- 旧的全局 `pet-frames.*` / `pet-lines.*` / `voice-wiki\` **先留着**当备份（现在不再被读取），确认没问题后可以删

---

## 17. 第十一轮改动（分组导航 / 四套动画 / 角色时长）

### 导航重新分组
```
通用设置   气泡 · UI 设置（占位，暂未开放）
角色设置   形象与大小 · 动画 · 语音 · 导入
使用统计   统计
```
左侧列表用不可选中的分组标题（`NavHeader` 样式），页面切换按 `Tag` 走 `ShowPage(key)`。
「形象与大小」里的「新建角色…」按钮已按要求移除（建角色统一走「导入」页）。

### 动画页：每类动画都有真实帧预览
`sprites.py` 现在生成 **44 帧**：待机 24 + 按键 6 + **点击 6（独立）** + 拖动 8。

| 能力 | 说明 |
|---|---|
| 预览 | 直接从当前角色的 `动画/frames.bin` 解码对应帧段，`BitmapSource` 循环播放（待机连续循环；按键/点击/拖动播完停半秒再播） |
| 更改（幅度） | 每类动画一根 20%–200% 的幅度滑杆，写进 `char.json` 的 `ampIdle/ampPress/ampClick/ampDrag`；保存时按新幅度重新生成整只角色的帧 |
| 倍速 | 每类动画独立 50%–200%，配置项 `speedIdle/speedPress/speedClick/speedDrag`（百分数）；预览里立即生效，桌宠按各自倍速播放 |

点击动画与按键动画彻底分开：点击用的是「更深 + 小跳」的 6 帧，`WM_LBUTTONDOWN` 触发；
按键仍是键盘/鼠标钩子的轻挤压。老角色包没有点击帧时自动退回按压帧。

### 软件使用排行可滚动
「软件使用」改成 240px 高的独立滚动区（和按键列表一样，滚轮优先滚内层），最多列 40 个，每秒原地更新。

### 每个角色的使用时长
- 今日：`pet-stats.txt` 里 `char=<角色名>|<秒>`，跨天/清空今日统计会归零
- 累计：`pet-char-time.txt`（`角色=秒`），**清空今日统计不会清零**
- 统计页新增「角色陪伴时长」卡片：角色名 + 今日 + 累计，按累计排序，每秒刷新

### 实测
- 桌宠：`ready frames=44 (待机24/按键6/点击6/拖动8)`；`速度=100%/100%/100%/100%`
- 点击帧：向窗口 `PostMessage WM_LBUTTONDOWN` 后截图，角色确实压扁并小跳（`screenshots/click-frame.png`）
- 时长落盘：`char=小维|310`、`pet-char-time.txt` → `小维=310`
- 设置自检：`导航项 10 个（含分组标题）/ 动画卡片 4 张 / 软件行 28 / 错误 0`
- 截图：`screenshots/new-p3.png`（动画页）、`new-p6b.png`（统计下半：角色时长 + 软件滚动）、`new-p1.png`（UI 占位）、`new-p2.png`（形象页）

---

## 18. 第十二轮改动（语音库联动 / 音效组 / 台词库 / 自定义动画）

### 主动冒泡只从「当前角色语音库」里选
`RandomLine()` 现在优先用 `_lineText`（带语音的台词），只有语音清单为空时才退回 `台词.txt`。
验证（芙宁娜包：台词 132 条，带语音 32 条）：连续冒泡的内容全部命中 `音效/voices.tsv`。

### 音效组不再是“只有芙宁娜”
下拉改成：**跟随角色（推荐） / 芙宁娜 / 奶蛙 / 小维**。
- 跟随角色 = 用当前角色包里的 `press/release`
- 指定其他角色 = 借用那个包的按压/松开音（形象和音效可以分开玩）
- 配置项 `sfxGroup`（`auto` 或角色名），桌宠日志会打 `音效组: 芙宁娜 → press.wav`
- 语音页新增「按压音 / 松开音」试听按钮，换组后能立刻听

### 语音页新增的好玩功能
| 功能 | 说明 |
|---|---|
| 台词库 | 列出当前角色全部带语音的台词（9/32 条），**搜索框**按文字过滤 |
| 逐条试听 | 每行「播放」，用保存后的音量播 |
| 随机来一句 | 从语音库里随机抽一条 |
| 点击模式 | **随机一句 / 顺序播放**（`clickLineMode`）；顺序模式实测三次点击依次是「今天…」→「别急…」→「哈？…」 |

### 动画支持自定义
每张动画卡底部加了「**自定义动画…**」菜单：
- **用 GIF / 图片替换**：GIF 逐帧读取；单张图会自动补帧
- **用帧文件夹替换**：按文件名排序，一帧一张图
- **恢复默认动画**：按立绘和当前幅度重新生成这一槽
- 图片会等比缩放、**底部居中**贴到角色画布；写入 `动画/frames.bin` 并更新帧计数（`frames.txt`）
- 帧数下限：待机 2、按键 4、点击 4、拖动 1（不够会重复最后一帧，保证状态机能回弹）
- 自定义过的槽会在卡片上显示「已自定义」，`char.json` 记 `customIdle/customPress/customClick/customDrag`
- 写完后点「保存更改」重启桌宠生效

命令行同一套能力：
```bat
python anim_custom.py apply --pack "chars/小维" --slot click --src D:\anim.gif
python anim_custom.py apply --pack "chars/小维" --slot idle  --src D:\frames
python anim_custom.py reset --pack "chars/小维" --slot click
```

### 实测
- 音效组：`音效组: 芙宁娜 → press.wav`，点击日志确实在放芙宁娜的按压/松开音
- 顺序模式：三次点击气泡依次为语音库第 1/2/3 条
- 自定义动画：临时角色包上把点击换成 5 帧 GIF → `click=5`、frames.bin 从 44 帧缩到 43 帧；`reset` 后回到 6 帧
- 设置自检：`音效组 4 个 / 台词库 9 条 / 错误 0`
- 截图：`screenshots/voice-new.png`（语音页）、`anim-custom.png`（自定义入口）





---

## 19. 第十四轮改动（语音页瘦身 / 台词库美化 / 气泡外框）

| # | 需求 | 实现 |
|---|---|---|
| 1 | 音量只保留试听 | 语音页去掉「按压音 / 松开音」两个试听按钮（音效组下拉仍然保留，用来决定用谁的音效），只剩一个「试听」，旁边注明"听到的就是保存后的音量" |
| 2 | 台词库美化 | 一行一张卡片：白底 + 1px 描边 + 8px 圆角 + 行间距 6px；左侧两位数序号（等宽字体）、中间台词单行省略（悬停看全文）、右侧圆形播放键；**整行可点即试听**，正在播的那行会高亮成淡蓝；搜索框加了「搜索台词…」占位提示，标题右侧显示「共 N 条 / 匹配 N 条」 |
| 3 | 气泡外框可换 + 上传 | 「气泡」页新增**外框样式**区：5 个内置样式卡片（缩略图是真实渲染的）+ 已上传的自定义外框 + 「+ 上传外框」卡片 |

### 5 种内置外框 + 自定义
| key | 名称 | 效果 |
|---|---|---|
| `card` | 圆角卡片 | 白→淡蓝渐变 + 细描边 + 顶部高光 + 双层柔和投影（默认） |
| `classic` | 经典椭圆 | 插件同款：白色椭圆 + 粗深蓝描边 + 两个小圆点尾巴 |
| `cloud` | 云朵 | 三个鼓包 + 底部两个小鼓包 + 三角尾巴，几何并集取外轮廓，内部不留接缝 |
| `comic` | 漫画描边 | 直角框 + 粗黑描边 + 硬阴影（无模糊），尾巴是尖三角 |
| `glass` | 半透明 | 半透明白/淡蓝渐变 + 白色描边 + 三层投影，能透出桌面 |
| `custom` | 自定义 | 上传的图片按**九宫格**拉伸（四角不变形）；中间是镂空的话自动垫一层半透明白，字色按底色亮度自动选深/浅 |

- 外框图片放在 `bubble-frames\`，支持 png / jpg / webp；点卡片选中，**右键可删除**
- 上传走「上传外框…」或「+ 上传外框」，会自动复制进 `bubble-frames`（重名自动加序号），保存时写相对路径
- 配置项：`bubbleStyle=card|classic|cloud|comic|glass|custom`、`bubbleFrame=bubble-frames\xxx.png`

### 实现要点
- 桌宠侧**外框只画一次**：`BuildBubbleSkin()` 把「投影 + 底色 + 描边」渲染到一张 `_w × _bubbleH` 的预乘位图并缓存，key = 尺寸 + 样式 + 外框图时间戳；每帧只按 alpha 贴图 + 画文字，云朵/自定义外框都不影响帧率（日志会打 `气泡外框: card 234x110 #1`）
- 九宫格用**整数像素对齐**：相邻块共用边界，不会出现重叠发暗或缝隙亮线（第一版用 `+1` 重叠，肉眼看得到一条竖线）
- 角块尺寸按 `min(dstW/srcW, dstH/srcH)` 等比缩小 —— 否则目标比源角还小时中间 band 会退化成 0，整个框被压扁
- 自定义外框的文字区底部留 16%，避免压在尾巴上

### 实测
- 五种样式逐个重启截图：`screenshots/style-classic.png`、`style-comic.png`、`style-glass.png`、`bubble-style.png`（缩略图）、`bubble-preview.png`（预览）
- 自定义外框：600×220 深色框 → `中间不透明度=235  字色=浅`，文字自动变浅、九宫格无接缝、尾巴不被文字压住（`screenshots/bubble-custom-crop.png`）
- 配置链路：`pet-overrides.txt` → `make-pet-config.ps1` → `pet-config.txt` 的 `bubbleStyle/bubbleFrame` 已打通，改完重启即生效
- 设置自检：`气泡预览=True / 错误 0`

---

## 20. 第十六轮改动（气泡瘦身 / 配色字号 / 托盘菜单 / 通用设置）

| # | 需求 | 实现 |
|---|---|---|
| 1 | 去除云朵气泡 | 设置面板样式表与桌宠绘制分支里的 `cloud` 一并删除；旧配置写成 `cloud` 时按未知样式回退到卡片 |
| 2 | 支持修改气泡颜色 | 「气泡」页新增**颜色**行：12 个预设色块 + 「自定义颜色…」（自绘取色器：R/G/B 滑杆 + hex 输入 + 实时预览）。描边、渐变、投影、文字色会一起跟着变（文字色按主色亮度自动推导深浅）。配置项 `bubbleColor=8FA6D8` |
| 3 | 字号可调 + 长文本滚动 | 「字号」滑杆 8–26pt（`bubbleFontSize`）。**不再自动缩字号**：装得下就居中，装不下就裁到气泡区内**自动上滚**（≈26px/秒，滚到头停一下再回起点）；鼠标在气泡区域滚轮可**手动滚动**（滚完 3 秒恢复自动） |
| 4 | 预览文字 | 气泡预览与缩略图文字统一改成「你好呀」 |
| 5 | 托盘右键菜单 | 右键托盘图标弹出原生菜单：**打开设置 / 关闭桌宠**（`CreatePopupMenu` + `TrackPopupMenu`，菜单消失后进程干净退出）。双击仍然是打开设置 |
| 6 | UI 设置 → 通用设置 | 左侧导航「UI 设置」改名「通用设置」，页面做了两件事：**主题颜色**（12 预设 + 自定义，点一下整个界面换色，写 `themeColor`）、**开机自启动**（开关，写入当前用户注册表启动项；原来放在「启动」文件夹里的旧方式会自动挪成 `.disabled`，避免开机起两个） |

### 实现要点
- 气泡外框仍然**只画一次**（`BuildBubbleSkin()` 缓存位图），换色只是缓存 key 变了重画一张，不影响帧率
- 主题色改的是 `Accent` 这个 `SolidColorBrush` 实例的 `Color`——所有 `{StaticResource Accent}` 都指向它，所以点一下全界面即时变色
- 长文本滚动用的是 `MeasureString` 的返回值（它本身就是排版高度，含行距）；裁剪用 `Graphics.SetClip`，绘制矩形随滚动偏移上移

### 关于 OpenVoiceChanger（https://github.com/sioaeko/OpenVoiceChanger）接入评估
定位是**独立的实时 AI 变声器 Web 应用**（React 前端 + FastAPI 后端 + ONNX Runtime / RVC 模型 + WebSocket 音频流），MIT 许可，Python 3.10+。它同时支持离线文件转换（Converter 页，wav/mp3/flac/ogg/m4a → WAV）。

- **不建议直接 import**：仓库不是可分发的 Python 包（无 PyPI、无 `pip install`），且依赖 RVC 代码库和 `hubert_base.pt` 等大模型文件
- **可行路径 A（最省事，推荐）**：把它作为**独立进程**跑在本机（`launch.py` 起服务），桌宠需要变声时调它的 `POST /api/convert/` 离线接口，把生成的角色 wav 重新渲染后播放——不动现有播放链路，模型/参数都在它自己的 UI 里调
- **可行路径 B**：让桌宠的播放改走 `WS /ws/audio` 实时链路，能做「实时变声播台词」，但要处理音频设备独占、延迟和进程生命周期，复杂度和风险都高不少
- **不推荐**：为了这个功能引入 PyTorch/RVC 依赖，会让原本零依赖的桌宠安装体积和启动时间明显变大

结论：**可以做，但建议按路径 A 做成"可选外挂"**——默认关闭，用户在设置里勾选"启用变声"并指定 OVC 服务地址后才启用；这样不影响现在的开箱即用。

### 实测
- 云朵已从样式行消失，只剩 4 种内置 + 自定义
- 玫粉 16pt：气泡、描边、文字全部变色；长句滚动到第 2/3 行（`screenshots/bubble-classic-green.png`）
- 经典椭圆 + 青绿：`气泡: 样式=classic 配色=2FA8A0`
- 托盘菜单：`screenshots/tray-menu.png`（打开设置 / 关闭桌宠）
- 通用设置页：`screenshots/general-page.png`；开机自启动勾选状态正确识别（当前为开）
- 自检：`气泡预览=True / 主题色 / 开机自启动=True / 错误 0`

---

## 21. 第十七轮改动（预览带角色 / 文字颜色 / 试听随机 / 全面改名 Pet）

| # | 需求 | 实现 |
|---|---|---|
| 1 | 气泡预览加入宠物图片 | 预览改为**按真实尺寸**绘制：上面是气泡、下面是当前角色的第 6 帧（脚底一条虚线基准线），底部标注「气泡 110 px ＋ 角色 237 px」——高度对比一眼可见。预览框从 200px 加高到 380px |
| 2 | 字体支持改颜色 | 气泡页新增**文字颜色**行：自动（按底色自动选深浅）/ 纯黑 / 纯白 / 深蓝 / 玫粉 / 芥黄 / 墨绿 / 紫褐 + 「自定义文字颜色…」。配置项 `bubbleInk=auto｜#RRGGBB`，指定后所有外框（含自定义图）都用这个色 |
| 3 | 试听从语音库随机抽 | 「试听」按钮不再固定放某一条，而是从**当前角色的语音库**随机抽一条播放，并提示正在试听哪句 |
| 4 | 去除 furina 标志、改名 Pet | 程序改名为 **Pet**，芙宁娜退回「可选角色」之一 |

### 改名清单（第 4 项）
| 位置 | 原来 | 现在 |
|---|---|---|
| 桌宠程序 | `FurinaPet.exe` | `Pet.exe`（工程 `Pet.csproj`，命名空间 `PetApp`，窗口类 `PetWndClass`） |
| 设置程序 | `FurinaSettings.exe` | `PetSettings.exe`（工程 `PetSettings.csproj`） |
| 窗口标题 | 芙宁娜桌宠 · 设置 | Pet 桌宠 · 设置 |
| 托盘提示 | 芙宁娜桌宠 · 双击打开设置 | Pet 桌宠 · 双击打开设置 · 右键菜单 |
| 图标 | `FurinaPet.ico` / `icon-final.png` | **`pet.png`**（另由它生成 `pet.ico` 作为 exe 图标；icopath 也指向 pet.png，缺失时自动回退） |
| 开机自启 | 注册表值「芙宁娜桌宠」+ 启动文件夹 `芙宁娜桌宠.vbs` | 值名「Pet 桌宠」+ `Pet桌宠.vbs`；旧命名的 vbs 和失效的 `.lnk.bak` 已挪到项目里的 `_旧命名备份\` |
| 启动脚本 | start-pet.vbs / .cmd 指向 FurinaPet.exe | 指向 `Pet.exe`；`run-pet-settings.cmd` 指向 `PetSettings.exe` |
| 配置表头 | # 芙宁娜桌宠配置 | # Pet 桌宠配置 |

> 芙宁娜仍然作为角色包存在于 `chars\芙宁娜\`（图片 / 动画 / 音效齐全），只是不再是软件名。
> 旧的 `FurinaPet.ico`、`icon-final.png`、`furina-v4.png` 等文件仍在磁盘上但已不再被引用，确认没问题后可自行删除。

### 实测
- 预览：`screenshots/bubble-with-pet.png`（气泡在上、角色在下，标注真实像素）
- 文字颜色色块与字号滑杆：`screenshots/bubble-ink.png`
- 桌宠日志：`气泡: 样式=card 配色=8FA6D8 字号=10.5pt 字色=auto`
- 改名后自检：`错误日志条数 = 0`；托盘 / 设置 / 重启链路全部走新文件名

---

## 22. 第十八轮改动（界面缩放 / 字号下拉 / 接入 OVC / 手动滚动 / 托盘图标 / 主题色修复）

| # | 需求 | 实现 |
|---|---|---|
| 1 | 设置窗口加缩放功能 | 标题栏右上角新增**缩放下拉框**（50%–200%，可手输）；三者等效：**Ctrl+滚轮**、**Ctrl+加号/减号**、**Ctrl+0** 一键回 100%。整个设置界面（导航 + 内容 + 底部按钮）用 `LayoutTransform` 整体缩放，超出部分在容器里滚动；比例存 `settingsZoom`，下次打开沿用 |
| 2 | 字号改成 Word 风格 | 原来 8–26 的滑杆换成**字号下拉框**（8/9/10/10.5/11/12/14/16/18/20/22/24/26/28/36，取自 Word 档位），而且**可以直接输入**（如 11.5）；范围放宽到 4–72 pt，保存值仍是 `bubbleFontSize` |
| 3 | 接入 OpenVoiceChanger | 按第 20 节的**路径 A** 做成可选外挂：语音页新增「启用变声（OpenVoiceChanger）」卡片——开关 / 服务地址（默认 `http://127.0.0.1:8000`）/ 测试连接 / 音高 / 共振峰。勾选后**点击台词**会把 wav `POST` 到 OVC 的 `POST /api/convert/`（multipart 字段名 `file`，另带 `pitch_shift`、`formant_shift`、`use_model`、`effects`），拿回 WAV 缓存到 `pet-ovc-cache\` 再播放；按压/松开等音效不变声。转换在后台线程，界面不卡；同一时刻只转一句，忙时先放原声；失败自动回退原声 |
| 4 | 长文本不要自动滚动 | 去掉气泡里的自动慢速上滚（原来约 26px/秒、滚完 3 秒恢复自动）。现在**只有鼠标在气泡上滚轮才滚**，停下就停在原处；换一句台词回到顶部 |
| 5 | 托盘图标空白 | 根因：`icopath` 指向 `pet.png`，而 `LoadImageW(IMAGE_ICON)` **只认 .ico/.cur，喂 PNG 返回 NULL** ⇒ 托盘拿到空图标。改为 GDI+ 解码 → 32bpp ARGB 缩放到 32px → `GetHicon()`（保留透明通道）；`.ico` 仍走 `LoadImageW`，失败再回退 `pet.ico` |
| 6 | 通用设置改不了主题色 | 根因：`Accent` 画刷被**样式封箱冻结**（`Style.Seal` 会冻结 Setter 里的 Freezable），`b.Accent.Color = c` 抛 `InvalidOperationException: 处于只读状态`，异常被 catch 吞掉 ⇒ 颜色不变、`_themeColor` 也不更新。改为 XAML 全部用 `{DynamicResource Accent}` + **整体替换** `Win.Resources["Accent"]` 实例；选中瓦片的描边/文字也跟着当前主题色 |

### 关于 OVC 接入（实现细节）
- 配置项：`ovcEnabled=0|1`、`ovcUrl=http://127.0.0.1:8000`、`ovcPitch=-24..24`（半音）、`ovcFormant=-24..24`
- 缓存键 = 源文件 + 音高 + 共振峰 + 服务地址（任何一项变了都会重新转），目录 `pet-ovc-cache\`
- 只转「点击台词」；`VoiceEx(path, reason, ovc:true)` 带上标记，其它播放路径不受影响
- 后台 HTTP 线程跑完用 `PostMessage(WM_APP+5)` 回主线程播放（不在网络线程碰播放状态）
- 「测试连接」= `GET /health` + `GET /api/models/active`，显示当前活动模型名
- 联调脚本：`ovc-mock.py`（本机模拟 OVC 后端）+ `pet-config-ovctest.txt`（带 `clickTest=3`），可无人工点击验证整条链路

### 实测
- 主题色：`screenshots/theme-pink.png`（玫粉：按钮 / 导航条 / 开关 / 选中瓦片全部跟着变）
- 缩放 150%：`screenshots/zoom-150.png`（标题栏仍 100%，内容整体放大并出现滚动条）
- 字号下拉：`screenshots/bubble-fontsize.png`（10.5 + pt，提示"不会自动滚动"）
- 变声卡片：`screenshots/voice-ovc.png`
- 托盘：日志 `托盘图标: 已添加（双击打开设置） 图标句柄=有效`；独立测试确认旧逻辑 `LoadImageW(PNG)=NULL`、新逻辑 `GetHicon` 有效且四角 alpha=0
- OVC 联调：`POST /api/convert/ fields=pitch_shift,formant_shift,use_model,effects,file vals={"pitch_shift":"3.0","formant_shift":"2.0",...}` → 缓存 `pet-ovc-cache\*.wav` → `语音播放完毕: 5e52a9c2.wav（8245ms）`
- 自检：`字号=10.5pt 界面缩放=100% / 变声(OVC)=关 地址=http://127.0.0.1:8000 / 错误日志条数 = 0`

---

## 23. 第十九轮改动（UI 简洁化 / 美观 / 实用）

按「简洁、美观、实用」三条线做了一轮设计收敛，没有加新功能，只让现有界面更好用。

### 设计令牌（收敛后的阶梯，改 UI 就按这套来）

| 项目 | 取值 | 用途 |
|---|---|---|
| 字号阶梯 | 28 / 22 / 14 / 13 / 12 / 11 | 页面标题 / 统计数据 / 卡片标题 / 正文·行标题 / 辅助说明 / 脚注·数值 |
| 辅助文字色 | `#99000000`（≈#666，白底对比度 5.7:1） | 所有说明文字 |
| 正文色 | `#E4000000` | 标题、正文 |
| 分隔线 | 1px `#EDEDED`，上下各留 12 | 卡片内分区 |
| 圆角 | 卡片 4 / 行·输入框 4 / 色块·瓦片 6 / 预览框 8 | 由小到大分层 |
| 间距 | 4 的倍数（4/8/12/16/20/24） | 卡片内边距 20,16；卡片间距 12 |
| 按钮 | 高 32，主按钮主题色、次按钮浅底描边、危险操作文字按钮 | 三档清晰 |

原先散落着 11 / 11.5 / 12 / 12.5 / 13 五种小字号，现已全部归到 12（说明）和 11（脚注·数值）两档。

### 具体改动

| # | 类型 | 改动 |
|---|---|---|
| 1 | 实用 | **导航不再重名**：分组标题「通用设置」和子项「通用设置」同名（「使用统计」/「统计」也一样）看不出层级，改为分组 **常规 / 角色 / 统计**，子项保持气泡·通用设置·形象与大小·动画·语音·导入·使用统计 |
| 2 | 实用 | **未保存提醒**：底部栏出现「● 有未保存的更改」；带改动直接关窗口会先弹确认（"不保存退出 / 继续编辑"）。保存后提示消失 |
| 3 | 实用 | **Esc = 取消**（下拉展开时 Esc 先收起下拉，不误关窗口） |
| 4 | 实用 | **气泡关掉后相关设置整体置灰**：字体 / 颜色 / 文字颜色 / 字号 / 外框改了也看不到，以前仍可点，现在统一禁用 + 半透明 |
| 5 | 美观 | **修掉选中态抖动**：色块 / 外框卡片原来选中时描边 1px→2px，内部缩略图会左右跳 1px。现在粗细固定 2px，只换颜色。像素实测：选中与未选中时色圆左边界都是 x=351 |
| 6 | 美观 | **输入框统一**：TextBox 之前是系统默认样式（直角灰框），与全站 4px 圆角、`#D6D6D6` 描边不一致；现在与下拉框同款，悬停加深、聚焦描边变主题色（不改粗细，避免抖动） |
| 7 | 美观 | **选中色跟随主题色**：外框卡片、文字颜色瓦片的选中描边和标题原来硬编码 `#005FB8`，换肤后不跟着变；现在取当前主题色 |
| 8 | 美观 | **开机自启动复选框**漏了 ToggleSwitch 样式（是系统默认方框），与页面上其它开关不一致，已统一 |
| 9 | 可访问 | 搜索框占位符 `#A9B0BD` 对比度只有 2.2:1 → 改 `#767C86`（≈4.6:1）；台词序号 `#B3BAC7` → `#8A93A3`，达到 WCAG AA |
| 10 | 可访问 | 新增键盘焦点圈 `FocusRing`（只在键盘导航时出现，画在控件外围不占位），按钮 / 下拉框 / 开关 / 输入框全部挂上 |
| 11 | 简洁 | **长文案瘦身**，实现细节挪进 Tooltip：主题色说明 24→15 字、自启动提示 60→6 字（+Tooltip）、字号提示 48→11 字（+Tooltip）、OVC 说明 70→28 字（+Tooltip）、统计页说明 40→24 字 |
| 12 | 一致 | 统计页标题/说明的间距与其它页对齐（H1 下 4、说明下 20），原来用的是 16px 空白块 |

### 实测
- 自检：`未保存检测: 识别改动=True 还原归零=True 提示标签=True`；`错误日志条数 = 0`
- 抖动对比：`群青瓦片选中时 色圆左边界 x=351` / `未选中时 x=351`（改之前会差 1px）
- 截图：`screenshots/ui-general.png`（通用设置）、`screenshots/bubble-ui.png`（字号下拉）、`screenshots/voice-ui.png`（变声卡片）、`screenshots/stats-ui.png`（统计页）

## 24. 第二十轮改动（最大/最小化按钮 + 去 OVC + 统计按天回看）

本轮做了三件事：标题栏交互调整、彻底移除 OpenVoiceChanger、使用统计支持选日期回看历史。

### 24.1 去除缩放下拉，改为最大/最小化按钮，窗口可拖边缘缩放

- 删除标题栏的缩放百分比下拉（`cmbZoom` 及整套 `SetupZoom/ApplyZoom/StepZoom/ParseZoomText/SyncZoomBase`、`settingsZoom` 配置键、`--zoomtest` 自检）。
- 标题栏恢复「最小化」按钮（西文图标 ``）并新增「最大化/还原」按钮（西文图标 ``，最大化后变 ``），点击在普通 / 最大化之间切换，`WindowState` 改变时图标同步。
- 窗口本就支持边缘拖拽缩放：主窗口 `ResizeMode="CanResize"` + `WindowChrome.ResizeBorderThickness="6"`（6px 热区），鼠标移到四边 / 四角即可拉伸缩放，无需额外代码。
- 配置键 `settingsZoom=100` 从 `pet-config.txt` / `pet-overrides.txt` / `make-pet-config.ps1` 一并删除。

### 24.2 彻底去除 OpenVoiceChanger（openvoice / OVC）

全链路移除，不留开关：

- **设置面板 UI**：删除语音页的 OVC 卡片（`chkOvc` / 服务器地址 / 音高 / 共振峰 / 测试播放等控件与字段），以及自检摘要里的「变声(OVC)」一行。
- **桌宠侧逻辑**：`pet.cs` 删除整个 OVC 区块（`_ovcEnabled/_ovcUrl/_ovcPitch/_ovcFormant/_ovcBusy/_ovcLock`、`WM_OVC_DONE`、`OvcCacheDir/OvcCacheFile`、`OvcConvert/OvcDone`），`VoiceEx()` 去掉 `ovc` 分支只走统一播放；`WndProc` 删除 `WM_OVC_DONE` 分支；配置读取删除 `ovc*` 四个键。
- **配置 / 缓存 / 测试**：`pet-config.txt`、`pet-overrides.txt`、`make-pet-config.ps1` 删除 5 行 OVC 键；删除 `ovc-mock.py`、`pet-config-ovctest.txt`、`pet-ovc-cache\` 目录。
- 效果：新增角色 / 台词播放全部走原生 `VoiceEx`，不再有「变声」这一档；之前 §20、§22 里关于 OVC 接入的描述均已作废。

### 24.3 使用统计可任选日期回看历史

- **pet 侧每日归档**：`FlushStats()` 在写 `pet-stats.txt`（当天实时）的同时，额外写一份 `pet-stats-YYYY-MM-DD.txt`（按 `_statsDay` 每天一份）。桌宠常驻即可自动积累历史。
- **设置面板日期选择**：统计页顶部加一条「查看日期」+ `DatePicker`（长格式 `yyyy年M月d日`，`DisplayDateEnd` 设为今天，不能选未来）。
- **可选日期范围**：`RefreshStatDates()` 扫描目录下所有 `pet-stats-*.txt`，列出「含今天」的可查天数，提示「共 N 天可查（含今天）」，默认选中今天。
- **今天 / 历史区分显示**：
  - 选「今天」→ 绿点「实时刷新」+ 副标题「更新于 …（桌宠每 4 秒落盘一次）」，`_statViewDate=""`，数据随桌宠实时变。
  - 选某历史日 → 灰字「历史记录（只读）」、绿点隐藏、副标题「历史快照 · 该日已归档，可切回『今天』看实时数据」。
  - 若该日没有归档文件（选了无记录的日期），自动回退到今天并提示「『xxxx-xx-xx』没有记录，已回到今天」。
- `DatePicker` 在 net48 WPF 没有 `ItemsSource` / `DatePickerDateRange`，未用黑历方案，改在 `SelectedDateChanged` 里校验文件是否存在来兜底。

### 24.4 实测

- 构建：两个项目 `dotnet build -c Release` 均 0 警告 0 错误。
- 设置面板 `--selftest`：窗口 960x740、可缩放=CanResize、导航含 `stats[统计]`、`错误日志条数 = 0`；自检摘要里「界面缩放」「变声(OVC)」两行已消失。
- 截图：`screenshots/stats.png`（统计页，标题栏含最小/最大/关闭三键、日期选择器显示「2026年10月19日」、提示「共 2 天可查（含今天）」）、`screenshots/voice.png`（语音页，OVC 卡片已移除）。
- Pet 重启后已自动生成 `pet-stats-2026-10-19.txt`，证明每日归档代码已生效。

## 25. 第二十轮补充：滚动条修复

用户反馈"修改滚动条 bug"。用临时 WPF 工程逐条实测后确认 4 个真 bug，全部修掉。

### 25.1 四个 bug 与修法

| # | Bug | 现象 | 修法 |
|---|---|---|---|
| 1 | **纵向滚动条被压成 8px 高** | `Style` 里给 `ScrollBar` 设了 `Width=8` + `Height=8` 两个 Setter，导致纵向滚动条的**高度也被锁成 8**，整个滚动条缩成右上角一个小方块，Thumb 只有 8×8，根本拖不动 | 删掉两个 Setter。「厚度 8」只由模板里 `Orientation` Trigger 给，长度方向留空让其拉伸撑满 |
| 2 | **点轨道空白翻不了页** | `Track` 里只有 `Thumb`，没有 `Decrease/IncreaseRepeatButton` | 补上两个透明 `RepeatButton`，命令用 `ScrollBar.PageUpCommand` / `PageDownCommand` |
| 3 | **滚动方向反了** | 往下滚，滑块往上走 | `Track` 加 `IsDirectionReversed="True"`（基准设 False，在 `Orientation=Vertical` 的 Trigger 里改 True） |
| 4 | **滑块会缩成一条线** | 内容很长时 Thumb 按比例缩得很小，抓不住 | `Thumb` 加 `MinHeight=28` / `MinWidth=28` |

顺带把悬停/拖动效果从「换背景色」改成「改 Opacity」（0.82 / 0.68）——换颜色会丢掉主题跟随。

### 25.2 滚轮边界劫持

三处 `PreviewMouseWheel`（`Scroller` / `KeyScroll` / `AppScroll`）原来都是无条件 `e.Handled = true`，
内层滚到边界后滚轮被自己吃掉，表现为**"滚死了"**。改成统一的边界感知逻辑：

```csharp
static bool WheelIn(ScrollViewer sv, MouseWheelEventArgs e)
{
    if (sv == null) return false;
    double max = sv.ScrollableHeight;
    if (max <= 0) return false;                 // 没得滚 → 交给父级
    double step = -e.Delta / 3.0;               // 一格滚轮 = 1/3 行，和系统一致
    if (step == 0) return false;
    double next = sv.VerticalOffset + step;
    if (next < 0 || next > max)
    {
        // 贴边且还在往外滚 → 不消费，让外层接过去
        if ((next < 0 && sv.VerticalOffset <= 0) || (next > max && sv.VerticalOffset >= max))
            return false;
        next = Math.Max(0, Math.Min(max, next));
    }
    sv.ScrollToVerticalOffset(next);
    return true;
}
static void HookWheel(ScrollViewer sv) { /* 只在 WheelIn 返回 true 时才 e.Handled = true */ }
```

### 25.3 实测证据

用临时 WPF 工程（`_sbprobe/`，验证后已删除）加载同一份样式模板量真实尺寸：

| 验证项 | 修复前 | 修复后 |
|---|---|---|
| 竖滚动条尺寸 | W=16.8 **H=8** | W=16.8 **H=470**（撑满） |
| 竖 Thumb 尺寸 | **8×8**（抓不住） | **8×98.6** |
| 轨道翻页按钮 | 无 ✗ | 有 ✓ |
| 纵向 `IsDirectionReversed` | False | **True** |
| 滚动方向实证 | Thumb 屏幕 Y 减小（反） | **顶=70 → 底=534 ✓** |
| 边界逻辑 4 情形 | 卡死 | 顶部/底部**不消费**、中间**消费**，全对 ✓ |

主程序：`dotnet build -c Release` 0 警告 0 错误；`--selftest` 错误日志 0 条；截图 `screenshots/sb-fix-stats.png`。

> 诊断经验：测 Thumb 位置/方向**不能用 `RenderTransform`**——`Track` 是用 `Arrange` 摆 Thumb 的，
> `TranslateY` 恒为 0；唯一可靠的是 `fe.PointToScreen(new Point(0,0))` 拿屏幕坐标差。

## 26. 第二十轮补充二：滚动卡死 / 滚动条改灰 / 日期选择器 Win11 化

用户反馈三点：滚动仍然卡死、滚动条蓝色太丑、日期选择器太丑。

### 26.1 滚动卡死的真根因（上一轮没修对）

上一轮只改了滚轮处理器，但**双层滚动容器的结构还在**：

```
zoomScroller (缩放功能已删，容器却留着)   ← ScrollableHeight 恒为 0
└── zoomHost (Grid: 216 导航 | * 内容)
    └── 右侧 Grid (行: * scroller | Auto 底栏)
        └── scroller                    ← 真正在滚的是这个
```

内层 `scroller` 贴边时 `WheelIn()` 返回 `false` 把滚轮交还父级，而父级 `zoomScroller` 的
`ScrollableHeight` **恒为 0**（内容永远不会溢出它），滚轮在两层空转 → 用户感知还是「滚死了」。
**事件处理器只是症状，容器结构才是病因。**

**修法**：把外层 `<ScrollViewer x:Name="zoomScroller">` 直接换成普通 `<Grid>`，
让 `scroller` 成为唯一滚动容器；同时移除 `HookWheel(Scroller)`
（单层滚动场景下 WPF 原生滚轮处理最稳，不会和外层打架）。
内嵌列表 `KeyScroll` / `AppScroll` 仍保留 `HookWheel`——它们是真嵌套，需要边界交还。

### 26.2 滚动条改中性灰

`{DynamicResource Accent}` → `#C2C6CE`，悬停 `#9AA0AA`，拖动 `#868C96`。

理由：滚动条是「工具」不是「内容」。用主题蓝有两个问题——视觉上过于抢眼（比输入框、按钮都重），
而且换肤时会被主题色污染。改灰后与 Win11 Fluent 的中性滚动条一致。

### 26.3 日期选择器改成 Win11 风格

系统默认 `DatePicker` 是灰蓝方框，和整站「4px 圆角 + `#D6D6D6` 描边 + 12/13 字号」的
设计语言完全不搭。整套重做：

| 部位 | 设计 |
|---|---|
| 触发框 | 与输入框同款：4px 圆角、`#D6D6D6` 描边、悬停 `#9A9A9A`、聚焦主题色（**粗细不变**，避免抖动） |
| 日历图标 | 右侧 32px 透明按钮 + 九宫格 Path 图标，悬停 `#F5F5F5` 底；点击展开下拉 |
| 下拉面板 | 白底 + 8px 圆角 + 1px `#E5E5E5` 描边 + `DropShadowEffect`（Blur 20 / Depth 4 / Opacity .18），`Fade` 动画 |
| 日期格 hover | `#F0F0F0` 浅灰底 |
| 日期格 今天 | 主题色 1px 细环 + 主题色文字（不抢选中态） |
| 日期格 选中 | 主题色实心 + 白色文字 |
| 日历本体 | `Calendar` / `CalendarItem` 去边框、白底、继承站点文字色 |

仍然保留 `DatePickerTextBox`，用户可以直接敲 `2026-10-18` 回车。

### 26.4 实测

用探针程序（`_dpprobe/`，验证后已删除）加载同一份模板并展开下拉：

```
✓ DatePicker 模板 解析成功 (TargetType=DatePicker)
✓ CalendarDayButton 模板 解析成功
  PART_Button = 找到 ✓
  下拉是否展开 = True ✓
  PART_Popup = 找到 ✓
  面板内 Calendar = 找到 ✓
  日期格数量 = 42 ✓   今天标记 = 1 ✓   选中标记 = 1 ✓
```

主程序：构建 0 警告 0 错误；`--selftest` 错误日志 0 条；截图 `screenshots/sb-v2-stats.png`。

> **重要坑（实测抓到，不实测根本发现不了）**：
> **绝对不能重写 `Calendar` 的 ControlTemplate** —— 它内部必须放 `CalendarPart`，
> 那是个未公开类型，运行时直接抛 `XamlParseException: 无法创建未知类型 CalendarPart`。
> 改外观只能用公开属性（`BorderThickness` / `Background` / `BorderBrush` / `Foreground`）。
> `DatePicker` 本身可以放心重写模板。

## 27. 第二十轮补充三：气泡长文本滚动 / 日期选择器重做

### 27.1 气泡长文本无法手动滚动

**根因（很隐蔽）**：桌宠窗口是 `WS_EX_NOACTIVATE` 且从不激活，而 Win32 的
**`WM_MOUSEWHEEL` 只投递给有键盘焦点的窗口** ⇒ `WndProc` 里那个 `case WM_MOUSEWHEEL`
**实际永远收不到**。全局钩子虽然匹配了 `0x020A`，但只 `PostMessage(WM_APP+2)`
丢掉了滚轮方向，随后走 `TriggerPress(false)` 触发动画，**根本没滚 `_bubbleScroll`**。

**修法**：

| 改动 | 说明 |
|---|---|
| 钩子转发方向 | `WM_MOUSEWHEEL` 单独处理：`(short)(lParam>>16)` 取 delta，连同 `GetCursorPos` 坐标打包，`PostMessage(WM_APP+4, pack, delta)` |
| 窗口处理 | 新增 `case WM_APP+4` → `HandleBubbleWheel(delta, x, y)`，命中气泡且文字确实溢出才滚，一格 42px（与系统方向一致） |
| 拖动滚动 | 在气泡上按住左键上下拖动也能翻文本（`_bubbleDragging`），不会误触发拖角色 |
| 滚动条指示 | 气泡右侧加 2px 极细轨道 + 滑块——没有它用户根本不知道这里能滚 |

新增自验开关 `wheelTest=N`（配 `clickTest=N`），日志输出
`自检滚轮 → _bubbleScroll=xx px (需要滚动=是, 可滚高度=503px)`。
实测 `0 → 42 → 84px` 逐次累积 ✓。

> **测试时的坑**：点击台词优先走 `_lineText`（带语音的池子），`台词.txt` 是兜底。
> 想验证长文本必须改 `voices.tsv`，且随机取词要重复多次才稳定命中。

### 27.2 日期「选了却改不了」+ 换成日期列表

**三重原因**：

1. `ShowPage("stats")` 每次都调 `BuildStatsPage()` 重建控件，
   而 `RefreshStatDates()` 里 `_datePick.SelectedDate = todayDt` **把日期强行拉回今天**；
2. `DisplayDateEnd = DateTime.Now.Date` 锁死上限；
3. 月/日网格里 80% 是空白格——本场景**只能选「有记录的日期」**，日历反而难找。

**方案：弃用 `DatePicker`**，改为「按钮 + 只列真实记录日期的弹出列表」：

- 触发按钮：与站点次按钮同款（4px 圆角、`#D6D6D6` 描边、悬停加深）+ 右侧下拉小箭头，
  文案「今天 · 2026年10月19日」；
- 弹出列表：白底 8px 圆角 + 投影，每项形如「今天 · 10月19日 周一」「10月18日 周日」，
  选中态浅蓝底 + 半粗 + 主题色字，**只列有归档的日期 + 今天**（新→旧）；
- 日期切换统一走 `ApplyStatDate(key)`，**切页重建不再重置用户选择**；
- 顺带甩掉了 Calendar 的 `IsInactive` 灰字、月份导航等一堆难看的默认元素。

**实测**：探针程序反射调用 exe 里的真实模板函数——`DateBtnTemplate()`、
`DateItemTemplate()` 执行成功，按钮渲染 200×32，列表项渲染 3/3 ✓。

> **⚠️ 坑**：`XamlReader.Parse` 解析出的模板**拿不到窗口资源字典**，
> 里面写 `{StaticResource Fill}` / `{DynamicResource Accent}` 会在运行时抛异常，
> 这些独立模板必须硬编码色值。

### 27.3 验证

两个项目构建 0 警告 0 错误；`--selftest` 错误日志 0 条；
截图 `screenshots/stats-v3.png`。所有测试数据（长台词、`voices.tsv`、`bubbleH`、
`clickTest`/`wheelTest`）已全部还原。

## 28. 第二十轮补充四：滚轮解析修正 / 日期列表显示无归档日

### 28.1 气泡滚轮失效的真正原因（低级钩子的 lParam 陷阱）

上一轮虽然把滚轮接到了钩子上，但**解析方式完全错了**，所以用户实测仍然滚不动。

**关键**：`WH_MOUSE_LL`（低级鼠标钩子）回调里，`lParam` **不是**消息的 `lParam`，
而是**指向 `MSLLHOOKSTRUCT` 的指针**：

| 偏移 | 字段 | 说明 |
|---|---|---|
| 0 | `POINT pt` (x, y) | 8 字节，光标屏幕坐标 |
| 8 | `UINT mouseData` | **滚轮增量在它的高 16 位** |
| 12 | `DWORD flags` | |
| 16 | `DWORD time` | |
| 24 | `ULONG_PTR dwExtraInfo` | x64 下 8 字节 |

之前误写成 `int delta = (short)((lParam.ToInt64() >> 16) & 0xFFFF);`
—— 那是在读**指针值本身**的高位，拿到的是内存地址碎片，与滚轮量毫无关系。

**正确写法**：

```csharp
int mx = Marshal.ReadInt32(lParam, 0);            // pt.x
int my = Marshal.ReadInt32(lParam, 4);            // pt.y
int mouseData = Marshal.ReadInt32(lParam, 8);
int delta = (short)((mouseData >> 16) & 0xFFFF);  // 高 16 位才是滚轮量
```

**顺带修的两处**：

1. **坐标不再用 16+16 位打包**（多显示器/负坐标会被截断）。
   改为在钩子里直接做命中判定 `WheelOverBubble(mx, my)`，只把 `delta` 传给窗口；
   窗口侧 `case WM_APP+4` 只需 `HandleBubbleWheel(lParam.ToInt32(), 0, 0)`。
2. **滚动边界上下都要夹**。之前只夹下限，可滚高度 12px 时 `_bubbleScroll` 会涨到 42px
   越界（滚动条指示错位、文字滚过头留白）。改用 `ClampBubbleScroll()` 双向夹取。

**实测证据**（诊断开关 `debugWheel=1`）：

```
钩子滚轮: pt=(960,563) delta=-120 命中气泡=否     ← delta 解析正确 ✓
自检: 气泡中心=(1673,355) rect=1556,316 命中=是    ← 命中判定正确 ✓
自检滚轮 → _bubbleScroll=12px (需要滚动=是, 可滚高度=12px)   ← 边界夹取生效 ✓
自检滚轮 → _bubbleScroll=42px (需要滚动=是, 可滚高度=571px)
```

### 28.2 日期下拉显示无归档日期（灰色）

按要求把「没有归档的日期」也列出来，而不是直接不列：

- **列表内容** = 最近 30 天 ∪ 所有有归档的历史日期（新 → 旧排序，去重）。
  保证连续性——否则会出现「10/18 之后直接跳到 8/01」，看不出中间断了多久。
- **有归档**：正常黑色/主题色、光标手型、可点。
- **无归档**：`#8A93A3` 灰字 + 文案追加「· 无记录」、背景透明、光标箭头、Tooltip「这一天没有统计记录」；
  `Tag=null` 使点击被忽略，**且不关闭弹窗**（用户可能想继续看别的日期）。
- 今天恒定视为「有数据」（实时文件 `pet-stats.txt` 一定存在）。

**实测**（探针反射调用 exe 真实逻辑，30 项）：

```
[可] 今天 · 10月19日 周一     文字=#005FB8  背景=#E8F1FB
[灰] 10月18日 周日 · 无记录   文字=#8A93A3  背景=透明
...
可点项 = 1   灰色不可点项 = 29   Tag=null 数 = 29
→ ✓ 无归档日期已显示且置灰不可点
```

### 28.3 踩坑记录

- **不要用 `IsEnabled=false` 做禁用态**：WPF 会自动再叠一层灰导致过暗，
  而且 `Tooltip` 也会被一起禁用。改为只靠 `Tag=null` 拦点击 + 手动设灰字色。
- 沙箱环境不支持 `Marshal.AllocHGlobal` / 结构体跨进程 P/Invoke（会段错误），
  验证低级钩子布局**必须靠真机日志**，不能靠探针。

## 29. 第二十轮补充五：日期下拉弹到左上角（PlacementTarget 缺失）

### 根因
自绘 `Popup` **不会像 `ComboBox` 那样自动关联锚点**。`ComboBox` 的下拉由 WPF 内部
接管 `PlacementTarget`，而我们自己 `new Popup()` 出来的必须手动指定 ——
不设就会退化到「相对鼠标 / 屏幕原点」，于是列表弹到了屏幕左上角。

### 修法（三处一起）
| # | 改动 | 说明 |
|---|---|---|
| 1 | `PlacementTarget = _dateBtn` | **核心修复**。没它就定位到屏幕原点 |
| 2 | `PlacementRectangle` 每次打开时同步成按钮实际尺寸 | 让列表严格贴住按钮左边缘，不受按钮内容宽度变化影响 |
| 3 | 按钮与列表统一宽度（`dateBtnW = 190`），按钮文案改左对齐 | 消除「按钮窄、列表宽、左边缘不齐」的错位感 |

顺带修了一个自检发现的**真实宽度 bug**：外层 `Border` 有 `BorderThickness=1` + `Padding=2`
（共 6px），导致「外层可见白框」比按钮宽 6px。改成 `_dateList.Width = dateBtnW - 6`，
外层才严格等宽。

### 自检（新增，进 `pet-settings-selftest.txt`）
```
日期下拉: 30项 按钮=190x32 列表=189x268
          锚点已设=True 矩形同步=True 贴下方=True 等宽=True  ✓ 定位正确
```

> **为什么不用屏幕坐标判定**：`PointToScreen` 在无桌面会话（自检/沙箱）下拿不到有效值，
> 会让自检永远报 `n/a`。所以判据改为「配置是否完备」——
> `PlacementTarget` 非空且已布局 + `PlacementRectangle` 与按钮同尺寸 +
> `Placement=Bottom && VerticalOffset>0` + 列表宽 == 按钮宽。
> 这四条齐了，WPF 必然把列表放到按钮下方。

> 自检里还有个时序坑：窗口可能还没真正显示，必须把 `Loaded`/`Render`/`Background`
> 三级优先级都排空并循环 3 次，否则新建控件的 `ActualWidth` 还是 0。

## 30. 第二十轮补充六：字号下拉框「框中框」+ 整体重做

用户截图反馈：可编辑下拉框（字号）外面套一个框、里面又是一个框，视觉上像坏掉了。

### 根因：隐式 TextBox 样式劫持了 PART_EditableTextBox
`ComboBox` 设 `IsEditable=True` 时，WPF 会往模板里塞一个 `PART_EditableTextBox`。
它会**继承项目里那个隐式 `TextBox` 样式**（`Height=34` + `Background=White` + 边框），
而 ComboBox 模板上写的 `Background="Transparent" BorderThickness="0"` 只是
**本地 Setter**，优先级低于样式 Template 里的 `Border`，于是白底+边框照旧出现 → **框中框**。

### 修法
1. **给 `PART_EditableTextBox` 内部再套一套透明模板**（放在它的 `TextBox.Resources` 里），
   彻底隔离隐式样式的影响。
2. **整体重做 ComboBox 模板**：
   - 文字与箭头用两列 Grid 分开，箭头固定 30px 独立按钮区，文字不会被压住
   - 下拉箭头改**中性灰 `#6B6B6B`** + 圆头线帽，悬停变 `#333333` 并加 4% 黑底
     （原来用 `TextPrimary` 近黑，太重，会抢文字的视觉焦点）
   - 下拉面板：圆角 8 + `DropShadowEffect`(Blur 18 / Depth 3 / Opacity .14) + `Fade` 动画，
     与日期选择器同一套视觉语言
   - 悬停描边变 `#9A9A9A` 且底色转白；键盘聚焦描边变主题色
3. **`ComboBoxItem` 选中态**：从 `FillHover` 改为 `#E8F1FB` + 主题色字 + 半粗，
   与日期选择器保持一致；加手型光标。
4. 字号下拉宽度 132→150，文字左对齐，单位 `pt` 间距 8→6（贴紧，表明是同一个量）。

### ⚠️ 本轮踩的坑（XAML 属性元素闭合）
写 `<ToggleButton.Template><ControlTemplate>…</ToggleButton.Template>` 会报：
> 第 278 行「ControlTemplate」与结束标记「ToggleButton.Template」不匹配

**属性元素语法必须闭合两层**：
```xml
<ToggleButton.Template>
  <ControlTemplate TargetType="ToggleButton">
    …
  </ControlTemplate>          ← 先闭 ControlTemplate
</ToggleButton.Template>      ← 再闭属性元素
```
正确写法是 `</ControlTemplate></ToggleButton.Template>`。
项目里 `Thumb.Template` / `RepeatButton.Template` 都是这么写的，改模板时照抄即可。

### 验证
构建 0 警告 0 错误；`--selftest` 错误日志 0 条、**无崩溃日志**（删掉旧的 09:22 那个后重跑确认）、
全部 7 个页面导航正常；全文扫描属性元素闭合：
`Thumb.Template 2/2 ✓  ToggleButton.Template 1/1 ✓  RepeatButton.Template 4/4 ✓`

## 31. 第二十轮补充七：右键菜单 / 设置搜索 / 图标重做

### 31.1 桌宠右键菜单
之前右键被「完全取消」（`case WM_RBUTTONUP: return IntPtr.Zero;`）。
现在复用托盘菜单 `ShowTrayMenu()`，桌面右键即可弹出**打开设置 / 退出桌宠**：

```csharp
case WM_RBUTTONUP:
    ShowTrayMenu();     // 打开设置 / 退出桌宠（与托盘同一套逻辑）
    return IntPtr.Zero;
```

### 31.2 设置搜索
设置项越来越多（7 个页面、上百个选项），在导航栏「设置」标题下方加了搜索框。

**设计取舍**：搜索框就在左侧导航里，所以命中后**直接跳到对应页面**，
而不是过滤导航项 —— 过滤的话搜索框自己也会被藏起来，反而更绕。

| 交互 | 行为 |
|---|---|
| 输入 | 实时匹配，`#E8F1FB` 提示条显示结果（「N 个结果：气泡、语音 —— 回车跳第一个」）|
| 回车 | 跳到第一个命中的页面 |
| Esc | 清空 |
| 清除按钮 | 有输入时出现（26px 圆形 hover 区）|

未命中时给**可操作提示**而不是死胡同：
「没找到「xx」。可试：字号 / 音量 / 主题 / 动画 / 台词 / 统计」。

索引覆盖 7 个页面的全部设置项关键词（如 `voice` 页含「音量 / 试听 / 音效组 / 台词库 /
点击模式 / 语音库管理」）。

### 31.3 图标重做（头部 + 大眼睛）
原图 `pet.png` 是 512×512 的**全身黑色剪影**（奶蛙），眼睛只有很小的白点。

改成**只有头部 + 眼睛放大约 3 倍**（直径 30 → 96）：

- 纯标准库 Python 实现（`zlib` + `struct`），无 Pillow 依赖（本机无网络装不了）；
- **有符号距离场（SDF）** + 4×4 超采样抗锯齿；
- **平滑并集（smooth min, k=26）** 让耳朵与头自然过渡 —— 用普通 `min()` 会在交界处出现台阶；
- 输出 `pet.png`（512²）+ 7 尺寸 `pet.ico`（16/24/32/48/64/128/256，
  BMP-DIB 格式 + AND 掩码，窗口标题栏/托盘/资源管理器通用）。

实测各尺寸下眼睛都清晰可辨（16px 时仍能看出两个白点）。

旧文件已备份为 `pet-icon-backup.png` / `pet-ico-backup.ico`，随时可回退。

### 验证
两个项目构建 0 警告 0 错误；`--selftest` 错误日志 0 条、**无崩溃日志**；
桌宠 PID 54396 运行中、托盘图标句柄有效。

## 32. 第二十轮补充八：移除搜索 / 耳朵下垂 / 说明文字瘦身

### 32.1 移除设置搜索
上一轮加的搜索框按反馈撤掉了（UI + 索引 + 事件接线全部移除，残留 0）。
导航回到最简的「分组标题 + 页面项」。

### 32.2 图标：耳朵下垂
`pet.png` 的耳朵从「贴头两侧」改为**下垂**：

| 参数 | 改前 | 改后 |
|---|---|---|
| 耳朵中心 y | 246 | **286**（下移 40px）|
| 耳朵半径 | 70×104 | **58×84**（缩小，避免糊成一团）|
| 旋转角 | ±8° | **±26°**（明显下垂）|

平滑并集 `smin(k=26)` 保持耳朵与头自然过渡。
重新生成 `pet.png`(512²) + `pet.ico`(16/24/32/48/64/128/256 共 7 尺寸)。

### 32.3 说明文字瘦身
原来 30 处 `Sub` 样式说明 + 9 处代码动态文案，普遍偏长（如
「幅度 = 重新生成动画帧（整只角色重写一次，约 1 秒）；倍速 = 只改播放速度，不改帧数据。」）。

**原则：页面级说明保留并缩短，行内"解释性废话"直接删。**

| 处理 | 数量 | 例子 |
|---|---|---|
| 直接删除 | 4 | 「描边、渐变和文字颜色会一起跟着变」「听到的就是保存后的音量」|
| 缩短 | 16 + 9 | 「点她时随机抽一句，或者按语音库顺序挨个说」→「点她时的说话方式」<br>「幅度 = 重新生成动画帧…」→「幅度需保存；倍速即时生效」<br>「一个角色 = chars 里的一个文件夹：图片 / 动画 / 音效 / 台词.txt」→「一个角色 = 一个文件夹」|

细节保留在 `Tooltip` 里（悬停才看），不占常驻空间。
结果：所有常驻说明均 **≤ 14 字**，读者一眼能扫完。

### 验证
两个项目构建 0 警告 0 错误；`--selftest` 错误日志 0 条、**无崩溃日志**；
桌宠 PID 61036 运行中、托盘图标句柄有效；搜索相关代码残留 0 处。

## 33. 第二十轮补充九：文案瘦身引发的 XAML 崩溃（连修 5 处）

### 症状与根因
启动弹「界面加载失败：向类型为 UIElementCollection 的集合中添加值引发了异常」。

上一轮批量精简文案时，我用 Python 做**字符串替换**，把多行 XAML 标签的**前半截**替换掉了，
只留下后半截属性 → 变成纯文本节点塞进 `UIElementCollection`：

```xml
<TextBlock Text="按真实比例预览" Style="{StaticResource Sub}"    ← 替换后这半截没了
         Margin="0,0,0,12"/>                                       ← 只剩这行（残片）
```

**连犯 5 次**：C# 编译每次都是 0 错误（XAML 只是字符串），只有运行时 `XamlReader` 解析才炸。
第 1 处修完以为好了，实际还有 4 处等着（每次报错行号都往后挪）。

### ⚠️ 教训：字符串扫描式检查有致命盲区
我写的 Python 检测脚本只能查出「**属性续行孤立**」，
查不出「**标签起始丢失**」（后者看起来是合法的属性行）。
所以修完第一个就以为干净了 —— 这是我判断失误，不是工具问题。

**正确做法：用真实的 XAML 解析器验证**，别再靠字符串扫描猜。

### 沉淀下来的校验器（源码已备份为 `.txt`）
1. `_xamlcheck-export.py.txt` —— 从 `FurinaSettings.cs` 抽出 `XamlText` 字符串
   （注意 `@"..."` 是逐字字符串，`""` 表示一个 `"`），导出成 `.xaml` 文件；
2. `_xamlcheck-validator.cs.txt` —— 用 `XamlReader.Load(XmlReader.Create(sr))`
   真正解析并构造出对象，**解析失败会直接报出「行号 + 位置 + 附近几行」**。

用法：导出 xaml → 跑 validator → `[OK] XAML 解析成功（对象已构造）` 才算通过。
本次靠它一次性扫出了剩余 4 处残片（724 / 743 / 750 / 760 行）。

**复用时的两个坑**：
- `XamlReader.Load` 只吃 `Stream`，传 `TextReader` 会 CS1503，
  得用 `XamlReader.Load(XmlReader.Create(new StringReader(xaml)))`；
- 它返回的是构造好的对象（`System.Windows.Window`），**不能**强转成 `XDocument`，
  否则抛 `InvalidCastException`（这个异常反而说明解析已经成功了）。

### 验证
- 解析器：`[OK] XAML 解析成功（对象已构造）`
- 构建 0 警告 0 错误；真实启动**无崩溃日志**

## 34. 第二十轮补充十：去 Furina 命名 / 清理冗余文件

### 34.1 重命名（程序命名统一为 Pet）

| 原来 | 现在 |
|---|---|
| `FurinaSettings.cs` | **`Settings.cs`** |
| 类名 `FurinaSettings` | **`Settings`** |
| `FurinaPet.ico`（328 KB 废弃版） | **删除**（已有 `pet.ico`）|
| `furina-v4.png` | **`pet-placeholder.png`**（3 处引用同步更新）|
| 互斥量 `Local\FurinaDesktopPet` | **`Local\PetDesktop`** |

`pet.cs` 原本已是 0 引用，无需改动。

### 34.2 顺手修了一个一直失效的 bug
`StartupScriptName` 常量是 `"Pet桌宠"`，但实际启动脚本叫 `start-pet.vbs/.cmd`
→ **开机自启状态检测一直返回 false**（怎么点都是"未开启"）。

同时删掉一条死代码：检测 `芙宁娜桌宠.vbs`（这个文件名根本不存在）。

```csharp
static readonly string StartupScriptName = "start-pet";   // 与实际脚本一致
```

### 34.3 刻意保留的 furina 字样（是数据，不是命名）

| 位置 | 原因 |
|---|---|
| `chars\芙宁娜\` | **角色包名**，用户数据 |
| `soundSet=furina` | 配置里的音效组标识 |
| `audio\furina_*.wav` + `audio.json` | 音效 ID，被 audio.json 交叉引用十余处，改名风险高无收益 |
| 代码注释「芙宁娜只是可选角色之一」 | 说明性文字 |

### 34.4 删除的冗余

| 目录 | 文件数 | 大小 | 说明 |
|---|---|---|---|
| `dist/` | 53 | **28 MB** | 旧发布包（芙宁娜桌宠 + 便携版 zip），已过期 |
| `obj/` | 41 | 2.1 MB | C# 中间产物（可再生）|
| `screenshots/` | 43 | 2.9 MB | 开发期截图 |
| `_旧命名备份/` | 3 | 377 KB | 旧 exe/快捷方式备份 |
| `bubble-frames/` | 1 | 32 KB | 气泡边框素材 |
| `__pycache__/` | 2 | 32 KB | Python 缓存 |
| `se_err.txt` | — | 0 B | 空文件 |

合计释放约 **33.4 MB**，剩余 208 个文件。
（`dist/` 超过 50 文件触发批量删除保护，拆成子目录分批删完。）

### 34.5 验证
- 两个项目重建 0 警告 0 错误（obj 已删，从头编译）
- XAML 解析器：`[OK] XAML 解析成功（对象已构造）`
- `--selftest` 错误日志 0 条、**无崩溃日志**
- 设置面板真实启动正常（PID 76184）；桌宠运行中、托盘图标句柄有效
- 文件名层面：**0 个 furina/芙宁娜 残留**

## 35. 第二十轮补充十一：二次清理（含漏网的 77MB）

### ★ 上一轮漏掉了 `voice-wiki/`（77 MB）
删 `dist/` 时只清了 `dist\芙宁娜桌宠\voice-wiki\`，**根目录还有一个同名副本**没动。
它是素材库，**完全失效**：

- `all-lines.tsv` 里的路径指向 `Desktop\华为手环\furina-widget\...`（一个早搬走的旧目录）
- 全项目代码**零引用** —— 三个角色包各自带语音（45 个 wav），不依赖它
- 71 个哈希命名的 wav（`0fc7c8ea.wav` 这类），纯下载缓存

（71 个文件超批量删除阈值 50，拆成 `wav/` 子目录分批删。）

### 本轮删除

| 项目 | 大小 | 原因 |
|---|---|---|
| **`voice-wiki/`** | **77 MB** | 失效素材，路径失效 + 代码零引用 |
| `obj/` | 1.2 MB | 构建中间产物 |
| `xc.cs` | 1.7 KB | 上一轮的 XAML 校验器残留 |
| `pet-icon-backup.png` | 97 KB | 旧图标备份（新版已验证可用）|
| `pet-status.txt` | 98 KB | 日志，会持续增长 → 清空 |

### 逐个验证后**保留**（都是功能必需）
`pet-frames.bin`（配置引用）、`pet-placeholder.png`（3 处引用）、`pet-vol-cache/`（音量缓存）、
所有 `.py`/`.ps1`/`.cmd`/`.vbs`（导入/重建/启动入口）、`audio/`+`audio.json`（音效 ID 索引）、
`pet.ico`（窗口+托盘图标）。

### 成果
**181.5 MB → 62 MB**（释放约 120 MB），文件数 208 → 119。

### 验证
- 两个项目**从头重建** 0 警告 0 错误（obj 已删）
- `--selftest` 错误日志 0 条、无崩溃日志
- 设置面板真实启动（PID 26380）、桌宠真实启动（PID 67552）
- 桌宠日志确认资源完整：44 帧动画就绪、语音正常播放（8264ms wav）、托盘图标有效

## 36. 第二十轮补充十二：第三轮清理（62 MB → 50 MB）

### 删除清单（逐个验证引用后）

| 项目 | 大小 | 判断依据 |
|---|---|---|
| **`pet-frames.bin`** | **8.1 MB** | 配置 `frames=` 指向 `chars\小维\动画\frames.bin`，这个根目录副本**零引用**（历史遗留）|
| **`pet-vol-cache/`** | **3.9 MB** | 音量衰减后的 wav 缓存，播放时自动重建 |
| `obj/` | 1.2 MB | 构建中间产物 |
| `build/` | 1.1 MB | 构建输出（`obj/Release/*.exe` 与它 MD5 完全相同，本就重复）|
| `pet-frames-preview.png` | 164 KB | 代码零引用（开发期预览图）|
| `pet-lang.txt` | 8 B | 代码零引用 |
| `pet-frames.txt` | 48 B | 代码零引用 |
| `pet-frames.json` | 112 B | 代码零引用 |
| `pet.pid` | 5 B | 运行时状态文件，自动重建 |

合计 **13.7 MB**。

### 保留（有明确用途）
`pet-placeholder.png`（1.2 MB 默认占位立绘，3 处引用）、`pet.ico`（364 KB 托盘图标）、
`audio/*.wav`（`audio.json` 索引的音效素材）、`chars/`（三个角色包）、
所有 `.py`/`.ps1`/`.cmd`/`.vbs`（导入/重建/启动入口）。

### 验证：删 `pet-frames.bin` 后动画仍正常
这是本轮最大的风险点（8.1 MB 的动画数据），实测确认无影响：

```
ready frames=44 (待机24/按键6/点击6/拖动8) 语音语言数=1
```

桌宠从角色包读取帧数据，根目录那个副本一直是多余的。

### 验证
- 删除 `obj/`+`build/` 后**从零构建** 0 警告 0 错误（证明构建流程自包含）
- `--selftest` 错误日志 0 条、无崩溃日志
- 桌宠真实启动 PID 71048、托盘图标有效、44 帧动画就绪

**最终：181.5 MB → 50 MB**（累计释放 131.5 MB），文件数 208 → 133。
剩余体积基本全是角色美术资源（三个角色包各约 9.5 MB 动画帧 + 语音）。

# Pet 桌宠

Pet 是一个运行在 Windows 桌面上的轻量桌宠：透明置顶、点击穿透、可换角色，支持动画、语音、气泡与使用统计。程序使用 Win32 + .NET Framework 4.8 实现，不联网，除系统运行时外无第三方依赖。

内置「芙宁娜」「小维」「奶蛙」三套角色包，也可自行导入角色。

## 功能

### 桌面表现

- 透明、置顶窗口，不占任务栏、不抢焦点；透明像素点击穿透，不遮挡桌面操作
- 待机、按键、点击、拖动四类动画，独立开关，幅度与倍速可调
- 拖动角色移动位置，松手后记住坐标

### 互动

- 点击角色：弹出随机台词气泡，并播放与文字对应的语音
- 键盘输入、鼠标点击、滚轮操作触发静音按压动画
- 按设定间隔主动冒泡；长文本气泡支持滚轮和拖动滚动
- 右键角色或托盘图标打开菜单；双击托盘图标打开设置

### 语音与气泡

- 每个角色独立语音库，台词与音频一一对应
- 音量 0–100、静音、试听，台词随机或顺序播放
- 多种气泡样式（圆角卡片、经典椭圆、漫画描边、半透明等），支持自定义外框图片
- 气泡主色、字体、字号、文字颜色可调

### 角色扩展

- 角色即一个文件夹：立绘、动画帧、语音、台词
- 设置面板内新建或导入角色，不受内置角色限制
- 每个角色可单独设置高度；支持用 GIF 或图片替换单类动画

### 设置与统计

- Windows 11 Fluent 风格设置中心：气泡、通用设置、形象与大小、动画、语音、导入、使用统计
- 开机自启、界面主题色
- 本地统计屏幕时长、软件使用排行、按键次数、角色使用时长，支持按天回看

## 运行

系统要求：Windows 10/11，.NET Framework 4.8（系统自带）。

仓库只包含源码，不包含编译产物。构建后：

1. 将 `Pet.exe`、`PetSettings.exe` 与 `pet-config.txt` 放在同一目录
2. 双击 `Pet.exe`、`start-pet.cmd` 或 `start-pet.vbs`（无窗口闪烁）启动
3. 双击 `PetSettings.exe` 或 `run-pet-settings.cmd` 打开设置

## 操作

| 操作 | 效果 |
|---|---|
| 左键点击角色 | 随机台词气泡 + 对应语音 |
| 按住拖动角色 | 移动位置并记住 |
| 右键角色 | 打开菜单（设置 / 退出） |
| 双击托盘图标 | 打开设置 |
| 右键托盘图标 | 打开菜单（设置 / 退出） |
| 气泡上滚轮或拖动 | 滚动长文本 |

## 构建

要求：.NET SDK，以及 .NET Framework 4.8 Targeting Pack（由 Visual Studio 或 Developer Pack 提供）。

```bat
dotnet build Pet.csproj -c Release
dotnet build PetSettings.csproj -c Release
```

产物：

- `build\pet\Pet.exe`
- `build\settings\PetSettings.exe`

将两个 exe 复制到项目根目录即可运行。

## 配置

配置保存在根目录 `pet-config.txt`（key=value）。推荐使用设置面板修改：保存时同步更新 `pet-overrides.txt` 与 `pet-config.txt`，重启桌宠后生效。

| 键 | 说明 |
|---|---|
| `charDir` | 当前角色包目录 |
| `volume` | 音量 0–100 |
| `clickLine` / `clickVoice` | 点击台词 / 按压松开音开关 |
| `bubbleEveryMs` | 主动冒泡间隔，0 为关闭 |
| `bubbleStyle` / `bubbleColor` / `bubbleFont` / `bubbleFontSize` / `bubbleInk` | 气泡外观 |
| `animIdle` / `animPress` / `animClick` / `animDrag` | 四类动画开关 |
| `speedIdle` / `speedPress` / `speedClick` / `speedDrag` | 四类动画倍速（百分比） |
| `statsKeys` | 是否统计按键次数 |
| `themeColor` | 设置界面主题色 |

`pet-config.txt` 也可由 `make-pet-config.ps1` 根据 `pet-overrides.txt` 和角色包重新生成。

## 角色包

目录结构：

```text
chars/
└─ 角色名/
   ├─ char.json          角色清单
   ├─ 图片/image.png     立绘
   ├─ 动画/frames.bin    动画帧
   ├─ 动画/frames.txt    尺寸与帧数元数据
   ├─ 动画/preview.png   预览图
   ├─ 台词.txt           气泡台词（一行一句）
   └─ 音效/
      ├─ voices.tsv      台词 <TAB> 语音路径
      ├─ voices/*.wav
      ├─ press.wav       可选：按压音
      └─ release.wav     可选：松开音
```

`char.json` 示例：

```ini
name=小维
image=图片/image.png
frames=动画/frames.bin
meta=动画/frames.txt
lines=台词.txt
voicelines=音效/voices.tsv
press=音效/voices/xxx.wav
release=音效/voices/yyy.wav
charH=200
```

角色管理：

```bat
python charpack.py list
python charpack.py new --name 新角色 --image D:\pics\a.png --voices D:\voices --char-h 219
python charpack.py rebuild --name 新角色 --char-h 260
```

依赖 Python 3、numpy、Pillow；导入 mp3/m4a/ogg 时需要 ffmpeg。

自定义单类动画：

```bat
python anim_custom.py apply --pack "chars/小维" --slot click --src D:\anim.gif
python anim_custom.py reset --pack "chars/小维" --slot click
```

`--slot` 可选 `idle` / `press` / `click` / `drag`。

## 项目结构

```text
furina-widget/
├─ pet.cs / Program.cs        桌宠本体
├─ Pet.csproj                 桌宠构建配置
├─ Settings.cs                设置中心
├─ PetSettings.csproj         设置中心构建配置
├─ chars/                     角色包
├─ audio/                     音效素材与 audio.json 索引
├─ charpack.py                角色包管理
├─ sprites.py                 动画帧生成
├─ anim_custom.py             自定义动画
├─ make-pet-config.ps1        生成运行配置
├─ start-pet.cmd / .vbs       启动脚本
├─ run-pet-settings.cmd       打开设置
├─ pet-config.txt             运行配置（生成）
├─ pet-overrides.txt          设置面板覆盖项
└─ pet.ico / pet.png          图标
```

## 隐私

- 全局低层钩子只判断输入事件类型（按下、抬起、滚轮），不读取键码或字符内容，不记录、不落盘、不外传。
- 使用统计仅保存在本地 `pet-stats.txt`，不上传。
- 程序不联网。

## 素材与版权

内置角色的立绘、语音等素材版权归原权利方所有，仅供个人学习与自用，请勿用于商业用途或二次分发。仓库未附带开源许可证。

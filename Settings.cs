// ============================================================================
// Q Pet · 设置（独立 exe，不需要 PowerShell）
// 设计：Windows 11 Fluent —— 左导航 + 设置卡片 + 4px 圆角 + #005FB8 强调色
// 编译：dotnet build PetSettings.csproj -c Release   （产物 build\settings\PetSettings.exe）
//       旧方式仍可用：csc /target:winexe /out:PetSettings.exe /r:PresentationFramework.dll
//        /r:PresentationCore.dll /r:WindowsBase.dll /r:System.Xaml.dll Settings.cs
// 运行：双击 PetSettings.exe（或托盘双击）；--selftest 自检；--page N 直达某页；
//        --shot <png> --page N 无界面渲染某页；--dialog-test 弹一次退出确认框
// ============================================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using Ellipse = System.Windows.Shapes.Ellipse;
using XamlReader = System.Windows.Markup.XamlReader;

static class Settings
{
    static string Here;
    static Dictionary<string, string> Cur = new Dictionary<string, string>();
    static Window Win;

    // 控件
    static ListBox Nav;
    static ComboBox CmbChar, CmbSet, CmbEvery, CmbFont, CmbClickMode;
    static TextBox TxtVoiceSearch;
    static TextBlock PhVoiceSearch;
    // 气泡颜色 / 字号
    static Button BtnBubbleColor;
    static WrapPanel BubbleColorHost;
    static Button BtnBubbleInk;
    static WrapPanel BubbleInkHost;
    static string _bubbleInkSel = "auto";                          // "auto" 或 #RRGGBB
    static ComboBox CmbFontSize;                          // 字号：Word 风格下拉（可选可输）
    static TextBlock LblFontSize;
    // 「有未保存的更改」
    static TextBlock LblDirty;
    static string _savedSnapshot = "";
    static DispatcherTimer _dirtyTimer;
    // 界面缩放（Word / 浏览器那套：Ctrl+滚轮、Ctrl+±、Ctrl+0）
    static TextBlock LblBubSize;
    static Color _bubbleColorSel = Color.FromRgb(0x8F, 0xA6, 0xD8);   // 当前选中的气泡主色
    static double _bubbleFontPtSel = 10.5;                            // 选中的气泡字号
    // 通用设置
    static StackPanel UiContent;
    static WrapPanel ThemeColorHost;
    static CheckBox ChkAutoStart;
    static CheckBox ChkShowPet;                                        // 桌面显示：是否打开桌宠
    static bool _showPetSel = true;                                    // 与上面的开关同步（切换即生效）
    static Color _themeColor = Color.FromRgb(0x00, 0x5F, 0xB8);        // 界面主题色
    static readonly string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    // 显示名统一为 Q Pet；exe 文件名保留 Pet/PetSettings，兼容启动脚本与配置
    public const string PetExeName = "Pet.exe";
    public const string PetSettingsExeName = "PetSettings.exe";
    static readonly string StartupScriptName = "start-pet";   // 与实际脚本 start-pet.vbs / .cmd 一致
    static readonly string RunValueName = "Pet 桌宠";          // 沿用旧值，避免已有开机自启失效
    static StackPanel VoiceListPanel;
    static TextBlock LblVoiceCount;
    static Button BtnRandomLine;
    static List<VoiceLine> VoiceLines = new List<VoiceLine>();

    class VoiceLine { public string Text, Path; }

    // 气泡外框样式
    static WrapPanel BubbleStyleHost;
    static Button BtnUpBubble;
    static TextBlock LblBubHint;
    static string _bubbleStyleSel = "card";   // 当前选中的样式
    static string _bubbleFrameSel = "";        // 自定义外框图片（绝对/相对都行，随时规范成相对）
    static string BubbleDir { get { return Path.Combine(Here, "bubble-frames"); } }
    static Button BtnSave, BtnCancel, BtnQuit, BtnMin, BtnClose, BtnMax, BtnPreview;
    static Image ImgChar, ImgBub, ImgSize;
    static Slider SldSize, SldBub, SldVol;
    static TextBlock LblSize, LblBub, LblSizeCap, LblVol, LblVoiceHint;
    static CheckBox ChkClick, ChkBub;
    static StackPanel PageChar, PageVoice, PageBubble, PageAnim, PageStats, PageUi, PageAbout;
    static ScrollViewer Scroller;
    static MediaPlayer PreviewPlayer;

    class CharEntry
    {
        public string Name, Dir, Image, Voices, Lines, Frames, Meta;
        public int CharH = 219, VoiceCount;
        public double AmpIdle = 1, AmpPress = 1, AmpClick = 1, AmpDrag = 1;
        public bool HasFrames { get { return !string.IsNullOrEmpty(Frames) && File.Exists(Frames); } }
    }
    static List<CharEntry> ChList = new List<CharEntry>();
    static CharEntry SelectedChar { get { return CmbChar != null && CmbChar.SelectedIndex >= 0 && CmbChar.SelectedIndex < ChList.Count ? ChList[CmbChar.SelectedIndex] : null; } }

    // 动画页的卡片（每类动画：开关 / 幅度 / 倍速 / 预览）
    class AnimCard
    {
        public string Key, Title;
        public CheckBox Toggle;
        public Slider Amp, Speed;
        public TextBlock AmpLbl, SpeedLbl;
        public AnimPreview Preview;
    }
    static List<AnimCard> AnimCards = new List<AnimCard>();
    static string _animCardsDir = "";
    static readonly string[] AnimKeys = { "idle", "press", "click", "drag" };

    // 真帧预览：把 frames.bin 里的某一段循环播放（PBGRA 直接做成 BitmapSource）
    class AnimPreview
    {
        public Image View;
        byte[] _data;
        int _w, _h, _fb, _start, _count, _idx, _hold;
        double _acc, _speed = 1;
        bool _pause;
        readonly DispatcherTimer _timer;

        public AnimPreview(double boxH)
        {
            View = new Image { Stretch = Stretch.Uniform, Height = boxH, Margin = new Thickness(4) };
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _timer.Tick += (s, e) => Step();
        }

        public void Load(byte[] data, int w, int h, int start, int count, double speed, bool pauseAtEnd)
        {
            _data = data; _w = w; _h = h; _fb = w * h * 4;
            _start = start; _count = Math.Max(1, count);
            _speed = speed <= 0 ? 1 : speed; _pause = pauseAtEnd;
            _idx = 0; _acc = 0; _hold = 0;
            Show(0);
            _timer.Start();
        }

        void Show(int i)
        {
            if (_data == null || _w <= 0) return;
            int off = _fb * (_start + i);
            if (off < 0 || off + _fb > _data.Length) return;
            // 用 IntPtr 重载做单帧 BitmapSource：不依赖 WritePixels 的缓冲区校验
            System.Runtime.InteropServices.GCHandle gh =
                System.Runtime.InteropServices.GCHandle.Alloc(_data, System.Runtime.InteropServices.GCHandleType.Pinned);
            try
            {
                IntPtr p = new IntPtr(gh.AddrOfPinnedObject().ToInt64() + off);
                // ⚠️ stride 是**一行**的字节数（w*4），不是整帧大小；
                //    传成整帧会让 WIC 以为要 stride*h 字节，直接报「缓冲区不足」。
                BitmapSource bs = BitmapSource.Create(_w, _h, 96, 96, PixelFormats.Pbgra32, null, p, _fb, _w * 4);
                bs.Freeze();
                View.Source = bs;
            }
            finally { gh.Free(); }
        }

        void Step()
        {
            if (_data == null) return;
            if (_hold > 0) { _hold--; return; }
            _acc += _speed;
            if (_acc < 1) return;
            int adv = (int)_acc; _acc -= adv;
            _idx += adv;
            if (_idx >= _count)
            {
                _idx = 0;
                if (_pause) _hold = 14;      // 按压/点击/拖动播完停半秒再重播
            }
            Show(_idx);
        }

        public void SetSpeed(double v) { _speed = v <= 0 ? 1 : v; }
        public void Stop() { _timer.Stop(); }
    }
    [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    static readonly int[] EveryVals = { 0, 300000, 600000, 1800000 };
    static readonly string[] EveryNames = { "不主动冒", "5 分钟", "10 分钟", "30 分钟" };

    [STAThread]
    static void Main(string[] args)
    {
        Here = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
        bool selfTest = false;
        bool dialogTest = false;
        int page = 0, shotH = 740, shotScroll = 0;
        string shotPath = "";
        string themeTest = "";
        string showPetTest = "";
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--selftest" || args[i] == "-selftest") selfTest = true;
            else if (args[i] == "--page" && i + 1 < args.Length) int.TryParse(args[++i], out page);
            else if (args[i] == "--shot" && i + 1 < args.Length) shotPath = args[++i];
            else if (args[i] == "--shot-height" && i + 1 < args.Length) int.TryParse(args[++i], out shotH);
            else if (args[i] == "--shot-scroll" && i + 1 < args.Length) int.TryParse(args[++i], out shotScroll);
            else if (args[i] == "--dialog-test") dialogTest = true;
            else if (args[i] == "--themetest" && i + 1 < args.Length) themeTest = args[++i];
            else if (args[i] == "--showpet" && i + 1 < args.Length) showPetTest = args[++i];
        }
        LoadOverrides();
        try
        {
            Win = (Window)System.Windows.Markup.XamlReader.Parse(XamlText);
        }
        catch (Exception ex)
        {
            MessageBox.Show("界面加载失败：" + ex.Message, "Q Pet · 设置");
            return;
        }
        try { Bind(); }
        catch (Exception ex)
        {
            try { File.WriteAllText(Path.Combine(Here, "pet-settings-crash.log"), DateTime.Now.ToString("s") + "\r\n" + ex); }
            catch (Exception) { }
            return;
        }
        // 圆角窗口：Win11 用 DWM 官方圆角（DWMWA_WINDOW_CORNER_PREFERENCE = 33, ROUND = 2）
        Win.SourceInitialized += (s, e) =>
        {
            try
            {
                IntPtr h = new System.Windows.Interop.WindowInteropHelper(Win).Handle;
                int pref = 2;
                DwmSetWindowAttribute(h, 33, ref pref, sizeof(int));
            }
            catch (Exception) { }
        };
        if (selfTest) { SelfTest(); return; }
        // 开发自验：「桌面显示」开关不做界面也能验一遍（写盘 + 启停进程 + 结果落文件）
        if (showPetTest != "")
        {
            SetShowPet(showPetTest == "1");
            System.Threading.Thread.Sleep(1500);        // 等进程真的起来 / 退掉
            var sb = new StringBuilder();
            sb.AppendLine("桌面显示开关实测：" + (showPetTest == "1" ? "打开" : "关闭"));
            sb.AppendLine("  pet-config.txt     showPet=" + ReadKey(Path.Combine(Here, "pet-config.txt"), "showPet"));
            sb.AppendLine("  pet-overrides.txt  showPet=" + ReadKey(Path.Combine(Here, "pet-overrides.txt"), "showPet"));
            sb.AppendLine("  运行的 Pet.exe 进程数 = " + Process.GetProcessesByName("Pet").Length);
            File.WriteAllText(Path.Combine(Here, "pet-showpet-test.txt"), sb.ToString(), new UTF8Encoding(false));
            return;
        }
        // 无界面截图：给「改完 UI 想确认有没有被挤掉」用的（不弹窗、不依赖 DWM 缩放）
        if (shotPath != "")
        {
            SelectPage(page);
            _shotScroll = shotScroll;
            Shot(shotPath, 960, Math.Max(400, shotH));
            return;
        }
        if (page > 0) SelectPage(page);
        // 开发自验：--themetest E06C8B 直接套一个主题色并截图（验证换肤是否生效）
        if (themeTest != "")
        {
            ShowPage("ui");
            ApplyThemeColor(WpfParseHex(themeTest, _themeColor));
            BuildGeneralPage();
            Shot(Path.Combine(Here, "pet-theme-test.png"), 960, 740);
            return;
        }
        // 自检用：启动后直接弹「退出」确认框（只为了截图看样式，取消后自己关掉）
        if (dialogTest) Win.Loaded += (s, e) => { try { QuitPet(); } catch (Exception ex) { LogErr("dlgtest", ex); } Win.Close(); };
        var app = new Application();
        app.Run(Win);
    }

    // ---------------- 读取当前覆盖值 ----------------
    static string CurOf(string key, string def)
    {
        string v;
        return Cur.TryGetValue(key, out v) && v != "" ? v : def;
    }
    static void LoadOverrides()
    {
        string p = Path.Combine(Here, "pet-overrides.txt");
        if (!File.Exists(p)) return;
        string txt = File.ReadAllText(p, Encoding.UTF8);
        foreach (Match m in Regex.Matches(txt, @"([A-Za-z0-9_]+)=([^\r\n]*?)(?=\s+[A-Za-z0-9_]+=|\s*$)"))
            Cur[m.Groups[1].Value] = m.Groups[2].Value.Trim();
    }

    // 读 key=value 文件里的某一个键（开发自验用：确认开关真的写进配置文件了）
    static string ReadKey(string path, string key)
    {
        try
        {
            if (!File.Exists(path)) return "（文件不存在）";
            foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                int eq = raw.IndexOf('=');
                if (eq <= 0) continue;
                if (raw.Substring(0, eq).Trim() == key) return raw.Substring(eq + 1).Trim();
            }
            return "（没有这个键）";
        }
        catch (Exception) { return "（读取失败）"; }
    }

    static int CurSize
    {
        get
        {
            CharEntry e = SelectedChar;
            if (e != null && e.CharH > 0) return e.CharH;
            int v; return int.TryParse(CurOf("charH", "219"), out v) ? v : 219;
        }
    }
    static int CurBubH { get { int v; return int.TryParse(CurOf("bubbleH", "110"), out v) ? v : 110; } }
    static int CurEvery { get { int v; return int.TryParse(CurOf("bubbleEveryMs", "300000"), out v) ? v : 300000; } }
    static string CurCharDir { get { return CurOf("charDir", ""); } }
    static string CurFont { get { return CurOf("bubbleFont", "YouYuan"); } }
    static bool CurClick { get { return CurOf("clickVoice", "0") == "1"; } }
    static int CurVol { get { int v; return int.TryParse(CurOf("volume", "100"), out v) ? Math.Max(0, Math.Min(100, v)) : 100; } }
    static double CurSpeed
    {
        get
        {
            double v;
            if (!double.TryParse(CurOf("animSpeed", "1"), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) v = 1;
            return Math.Max(0.5, Math.Min(2.0, v));
        }
    }
    static bool CurAnim(string key) { return CurOf(key, "1") == "1"; }
    static string CurBubbleStyle { get { string s = CurOf("bubbleStyle", "card"); return s == "" ? "card" : s; } }
    static string CurBubbleFrame
    {
        get
        {
            string p = CurOf("bubbleFrame", "");
            if (p == "") return "";
            if (!Path.IsPathRooted(p)) p = Path.Combine(Here, p);
            return p;
        }
    }
    static int CurSpeedPct(string key)
    {
        int v;
        if (!int.TryParse(CurOf(key, "100"), out v)) v = 100;
        return Math.Max(50, Math.Min(200, v));
    }

    // ---------------- 页面切换 ----------------
    static bool _navBusy = false;

    static void SelectPage(int index)
    {
        string[] keys = { "bubble", "ui", "char", "anim", "voice", "import", "stats", "about" };
        if (index >= 0 && index < keys.Length) ShowPage(keys[index]);
    }

    static void ShowPage(string key)
    {
        if (_navBusy) return;
        _navBusy = true;
        try
        {
            foreach (object o in Nav.Items)
            {
                ListBoxItem it = o as ListBoxItem;
                if (it != null && (it.Tag as string) == key) { if (Nav.SelectedItem != it) Nav.SelectedItem = it; break; }
            }
            PageBubble.Visibility = key == "bubble" ? Visibility.Visible : Visibility.Collapsed;
            PageUi.Visibility = key == "ui" ? Visibility.Visible : Visibility.Collapsed;
            PageChar.Visibility = key == "char" ? Visibility.Visible : Visibility.Collapsed;
            PageAnim.Visibility = key == "anim" ? Visibility.Visible : Visibility.Collapsed;
            PageVoice.Visibility = key == "voice" ? Visibility.Visible : Visibility.Collapsed;
            PageImport.Visibility = key == "import" ? Visibility.Visible : Visibility.Collapsed;
            PageStats.Visibility = key == "stats" ? Visibility.Visible : Visibility.Collapsed;
            PageAbout.Visibility = key == "about" ? Visibility.Visible : Visibility.Collapsed;

            if (key == "stats") { BuildStatsPage(); StartLiveStats(); } else StopLiveStats();
            if (key == "import") RefreshImport();
            if (key == "voice") LoadVoiceLibrary();
            if (key == "bubble") { BuildBubbleColorTiles(); BuildBubbleInkTiles(); DrawBubblePreview(); }
            if (key == "ui") BuildGeneralPage();
            if (key == "about") BuildAboutPage();
            if (key == "bubble") DrawBubblePreview();
            if (key == "anim") BuildAnimPage(); else StopAnimPreviews();
            Scroller.ScrollToTop();
        }
        finally { _navBusy = false; }
    }

    static void StopAnimPreviews()
    {
        foreach (AnimCard c in AnimCards) if (c.Preview != null) c.Preview.Stop();
    }

    static void Bind()
    {
        Nav = (ListBox)Win.FindName("nav");
        CmbChar = (ComboBox)Win.FindName("cmbChar"); CmbSet = (ComboBox)Win.FindName("cmbSet");
        CmbEvery = (ComboBox)Win.FindName("cmbEvery"); CmbFont = (ComboBox)Win.FindName("cmbFont");
        BtnSave = (Button)Win.FindName("btnSave");
        BtnCancel = (Button)Win.FindName("btnCancel"); BtnQuit = (Button)Win.FindName("btnQuit");
        BtnMin = (Button)Win.FindName("btnMin"); BtnClose = (Button)Win.FindName("btnClose");
        BtnMax = (Button)Win.FindName("btnMax");
        ImgChar = (Image)Win.FindName("imgChar"); ImgBub = (Image)Win.FindName("imgBub");
        ImgSize = (Image)Win.FindName("imgSize");
        SldSize = (Slider)Win.FindName("sldSize"); SldBub = (Slider)Win.FindName("sldBub");
        LblSize = (TextBlock)Win.FindName("lblSize"); LblBub = (TextBlock)Win.FindName("lblBub");
        LblSizeCap = (TextBlock)Win.FindName("lblSizeCap");
        ChkClick = (CheckBox)Win.FindName("chkClick"); ChkBub = (CheckBox)Win.FindName("chkBub");
        SldVol = (Slider)Win.FindName("sldVol");
        LblVol = (TextBlock)Win.FindName("lblVol");
        LblVoiceHint = (TextBlock)Win.FindName("lblVoiceHint");
        BtnPreview = (Button)Win.FindName("btnPreview");
        BtnRandomLine = (Button)Win.FindName("btnRandomLine");
        CmbClickMode = (ComboBox)Win.FindName("cmbClickMode");
        TxtVoiceSearch = (TextBox)Win.FindName("txtVoiceSearch");
        PhVoiceSearch = (TextBlock)Win.FindName("phVoiceSearch");
        VoiceListPanel = (StackPanel)Win.FindName("voiceList");
        LblVoiceCount = (TextBlock)Win.FindName("lblVoiceCount");
        BubbleStyleHost = (WrapPanel)Win.FindName("bubbleStyleHost");
        BtnBubbleColor = (Button)Win.FindName("btnBubbleColor");
        BubbleColorHost = (WrapPanel)Win.FindName("bubbleColorHost");
        BtnBubbleInk = (Button)Win.FindName("btnBubbleInk");
        BubbleInkHost = (WrapPanel)Win.FindName("bubbleInkHost");
        CmbFontSize = (ComboBox)Win.FindName("cmbFontSize");
        LblFontSize = (TextBlock)Win.FindName("lblFontSize");
        LblBubSize = (TextBlock)Win.FindName("lblBubSize");
        LblDirty = (TextBlock)Win.FindName("lblDirty");
        BtnUpBubble = (Button)Win.FindName("btnUpBubble");
        LblBubHint = (TextBlock)Win.FindName("lblBubHint");
        BtnUpBubble.Click += (s, e) => UploadBubbleFrame();
        _themeColor = WpfParseHex(CurOf("themeColor", ""), _themeColor);
        _showPetSel = CurOf("showPet", "1") != "0";
        ApplyThemeColor(_themeColor);
        BtnBubbleColor.Click += (s, e) =>
        {
            Color picked;
            if (ColorDlg.Ask(_bubbleColorSel, out picked))
            {
                _bubbleColorSel = picked;
                _bubAccent = picked;
                BuildBubbleColorTiles();
                BuildBubbleStyleTiles();
                DrawBubblePreview();
            }
        };
        _bubbleInkSel = CurOf("bubbleInk", "auto");
        BtnBubbleInk.Click += (s, e) =>
        {
            Color picked;
            if (ColorDlg.Ask(WpfParseHex(_bubbleInkSel, Color.FromRgb(0x1A, 0x1A, 0x1A)), out picked))
            {
                _bubbleInkSel = HexOf(picked);
                BuildBubbleInkTiles();
                DrawBubblePreview();
            }
        };
        _bubbleStyleSel = CurBubbleStyle;
        _bubbleColorSel = WpfParseHex(CurOf("bubbleColor", ""), _bubbleColorSel);
        _bubAccent = _bubbleColorSel;
        {
            float fsz;
            if (float.TryParse(CurOf("bubbleFontSize", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out fsz))
                _bubbleFontPtSel = Math.Max(4, Math.Min(72, fsz));
            else _bubbleFontPtSel = 10.5f;
        }
        SetupFontSize();     // 解析完配置再填字号下拉框（顺序反了会显示默认值）
        _bubbleFrameSel = CurBubbleFrame;
        if (_bubbleStyleSel == "custom" && (_bubbleFrameSel == "" || !File.Exists(_bubbleFrameSel)))
            _bubbleStyleSel = "card";
        BtnPreview.Click += (s, e) => PreviewVoice();
        BtnRandomLine.Click += (s, e) => PlayRandomLine();
        TxtVoiceSearch.TextChanged += (s, e) =>
        {
            if (PhVoiceSearch != null)
                PhVoiceSearch.Visibility = string.IsNullOrEmpty(TxtVoiceSearch.Text)
                    ? Visibility.Visible : Visibility.Collapsed;
            RefreshVoiceList();
        };
        PageChar = (StackPanel)Win.FindName("pageChar");
        PageVoice = (StackPanel)Win.FindName("pageVoice"); PageBubble = (StackPanel)Win.FindName("pageBubble");
        PageAnim = (StackPanel)Win.FindName("pageAnim");
        PageStats = (StackPanel)Win.FindName("pageStats");
        PageUi = (StackPanel)Win.FindName("pageUi");
        PageAbout = (StackPanel)Win.FindName("pageAbout");
        UiContent = (StackPanel)Win.FindName("uiContent");
        PageImport = (StackPanel)Win.FindName("pageImport");
        ImportList = (StackPanel)Win.FindName("importList");
        TxtName = (TextBox)Win.FindName("txtName");
        LblImg = (TextBlock)Win.FindName("lblImg"); LblVoices = (TextBlock)Win.FindName("lblVoices");
        LblImportMsg = (TextBlock)Win.FindName("lblImportMsg");
        BtnPickImg = (Button)Win.FindName("btnPickImg"); BtnPickVoices = (Button)Win.FindName("btnPickVoices");
        BtnImport = (Button)Win.FindName("btnImport");
        BtnPickImg.Click += (s, e) => PickImportImage();
        BtnPickVoices.Click += (s, e) => PickImportVoices();
        BtnImport.Click += (s, e) => DoImport();
        Scroller = (ScrollViewer)Win.FindName("scroller");
        // 不接管主内容区的滚轮：外层滚动容器已移除，scroller 是唯一滚动区，
        // WPF 原生滚轮处理在单层场景下最稳（不会和外层空转打架）。
        // 内嵌列表（KeyScroll/AppScroll）才需要 HookWheel。

        // 窗口 / 任务栏 / 标题栏图标：用她的大头照（PNG 解码最稳；ICO 有些解码器不认）
        try
        {
            string png = Path.Combine(Here, "pet.png");
            if (!File.Exists(png)) png = Path.Combine(Here, "icon-final.png");
            if (!File.Exists(png)) png = Path.Combine(Here, "pet-placeholder.png");
            if (File.Exists(png))
            {
                var bi = new BitmapImage();
                bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.UriSource = new Uri(png); bi.EndInit(); bi.Freeze();
                Win.Icon = bi;
                var iconImg = (Image)Win.FindName("imgIcon");
                if (iconImg != null) iconImg.Source = bi;
            }
        }
        catch (Exception ex) { LogErr("icon", ex); }

        // 角色候选（默认 + 导入的角色包 + 本目录其它 PNG）
        ReloadChars();
        ReloadSfxGroups();
        CmbClickMode.Items.Add("随机一句");
        CmbClickMode.Items.Add("顺序播放");
        CmbClickMode.SelectedIndex = CurOf("clickLineMode", "random") == "seq" ? 1 : 0;

        foreach (string e in EveryNames) CmbEvery.Items.Add(e);
        int ei = 1; for (int i = 0; i < EveryVals.Length; i++) if (EveryVals[i] == CurEvery) ei = i;
        CmbEvery.SelectedIndex = ei;

        // 字体候选（只列本机真装了的）
        string[] cands = { "YouYuan", "Noto Sans SC Medium", "Noto Sans SC", "DengXian", "Microsoft YaHei UI", "KaiTi", "SimHei", "FangSong" };
        var installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (FontFamily ff in Fonts.SystemFontFamilies) installed.Add(ff.Source);
        var fonts = new List<string>();
        if (installed.Contains(CurFont)) fonts.Add(CurFont); else fonts.Add(CurFont);
        foreach (string c in cands) if (installed.Contains(c) && !fonts.Contains(c)) fonts.Add(c);
        foreach (string f in fonts) CmbFont.Items.Add(f);
        CmbFont.SelectedIndex = 0;

        int sz = Math.Max(180, Math.Min(400, CurSize));
        SldSize.Value = sz; LblSize.Text = sz + " px";
        int bh = Math.Max(70, Math.Min(220, CurBubH));
        SldBub.Value = bh; LblBub.Text = bh + " px";
        ChkClick.IsChecked = CurClick;
        ChkBub.IsChecked = CurBubH > 0;
        SyncBubbleEnabled();

        // 音量（语音页）
        int vol = CurVol;
        SldVol.Value = vol; LblVol.Text = vol + "%";
        LblVoiceHint.Text = vol == 0 ? "当前静音：只做动画，不出声" : "所有台词与音效都会按这个音量播放";

        // 动画页：卡片在代码里生成（每类动画一块：预览 / 开关 / 幅度 / 倍速）
        BuildAnimPage();

        // 事件
        Nav.SelectionChanged += (s, e) =>
        {
            try
            {
                ListBoxItem it = Nav.SelectedItem as ListBoxItem;
                string key = it != null ? it.Tag as string : null;
                if (!string.IsNullOrEmpty(key)) ShowPage(key);
            }
            catch (Exception ex) { LogErr("nav", ex); }
        };
        CmbChar.SelectionChanged += (s, e) =>
        {
            try
            {
                // 每个角色有自己的高度：切角色时滑块跟着走，保存时只重出这个角色的动画
                CharEntry ce = SelectedChar;
                if (ce != null && ce.Dir != "" && ce.CharH >= 160 && ce.CharH <= 520)
                {
                    SldSize.Value = ce.CharH;
                    LblSize.Text = ce.CharH + " px";
                }
                ShowCharPreview(); ShowSizePreview();
                if (PageAnim != null && PageAnim.Visibility == Visibility.Visible) BuildAnimPage();
                if (PageVoice != null && PageVoice.Visibility == Visibility.Visible) LoadVoiceLibrary();
            }
            catch (Exception ex) { LogErr("charsel", ex); }
        };
        CmbFont.SelectionChanged += (s, e) => DrawBubblePreview();
        SldSize.ValueChanged += (s, e) => { LblSize.Text = (int)SldSize.Value + " px"; ShowSizePreview(); };
        SldBub.ValueChanged += (s, e) => { LblBub.Text = (int)SldBub.Value + " px"; };
        // 气泡关掉后，下面这些（字体 / 颜色 / 字号 / 外框）改了也看不到 ⇒ 整体置灰
        ChkBub.Checked += (s, e) => SyncBubbleEnabled();
        ChkBub.Unchecked += (s, e) => SyncBubbleEnabled();

        // 音量：拖动时实时改预览音量（如果正在试听，能立刻听出差别）
        SldVol.ValueChanged += (s, e) =>
        {
            int v = (int)SldVol.Value;
            LblVol.Text = v + "%";
            LblVoiceHint.Text = v == 0 ? "当前静音：只做动画，不出声" : "所有台词与音效都会按这个音量播放";
            if (PreviewPlayer != null) PreviewPlayer.Volume = v / 100.0;
        };
        BtnPreview.Click += (s, e) => PreviewVoice();

        BtnCancel.Click += (s, e) => Win.Close();
        BtnMin.Click += (s, e) => Win.WindowState = WindowState.Minimized;
        BtnMax.Click += (s, e) => Win.WindowState = (Win.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized);
        BtnClose.Click += (s, e) => Win.Close();
        Win.StateChanged += (s, e) =>
        {
            if (BtnMax != null) BtnMax.Content = (Win.WindowState == WindowState.Maximized) ? "\uE923" : "\uE922";
        };
        BtnQuit.Click += (s, e) => QuitPet();
        BtnSave.Click += (s, e) => Save();

        ShowCharPreview();
        ShowSizePreview();
        BuildBubbleStyleTiles();
        BuildBubbleColorTiles();
        BuildBubbleInkTiles();
        DrawBubblePreview();
        ShowPage("char");
        StartDirtyWatch();          // 底部「有未保存的更改」提示

        // Esc = 取消（等于点「取消」）；下拉展开时先让 Esc 收下拉
        Win.PreviewKeyDown += (s, e) =>
        {
            if (e.Key != Key.Escape) return;
            ComboBox cb = Keyboard.FocusedElement as ComboBox;
            if (cb != null && cb.IsDropDownOpen) return;
            e.Handled = true;
            Win.Close();
        };

        // 有改动没保存就关窗 → 先问一句，别白改
        Win.Closing += (s, e) =>
        {
            try
            {
                if (_savedSnapshot == "" || Snapshot() == _savedSnapshot) return;
                if (!Dlg.Ask("还有没保存的更改",
                        "你改了设置但还没点「保存更改」。现在关掉的话，这些改动不会生效。",
                        "不保存退出", "继续编辑", true))
                    e.Cancel = true;
            }
            catch (Exception) { }
        };

        Win.Closed += (s, e) =>
        {
            StopLiveStats();
            if (_dirtyTimer != null) { _dirtyTimer.Stop(); _dirtyTimer = null; }
            try { if (PreviewPlayer != null) { PreviewPlayer.Close(); PreviewPlayer = null; } } catch (Exception) { }
        };
    }

    static void LogErr(string where, Exception ex)
    {
        try { File.AppendAllText(Path.Combine(Here, "pet-settings-err.log"), DateTime.Now.ToString("HH:mm:ss") + "  [" + where + "] " + ex.Message + "\r\n"); }
        catch (Exception) { }
    }

    // ---------------- 预览 ----------------
    static BitmapImage LoadImg(string path)
    {
        try
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.UriSource = new Uri(path, UriKind.Absolute);
            bi.EndInit();
            bi.Freeze();
            return bi;
        }
        catch (Exception) { return null; }
    }
    static void ShowCharPreview()
    {
        // 列表刷新时会短暂出现 SelectedIndex = -1（之前没防越界 ⇒ 删角色会崩窗口）
        int i = CmbChar.SelectedIndex;
        if (ImgChar == null || i < 0 || i >= ChList.Count) return;
        ImgChar.Source = LoadImg(ChList[i].Image);
    }

    // 大小预览：按选择的像素高度等比展示，并给出实际角色/窗口尺寸
    static void ShowSizePreview()
    {
        try
        {
            int i = CmbChar.SelectedIndex;
            if (i < 0 || i >= ChList.Count) return;
            int h = (int)SldSize.Value;
            var src = LoadImg(ChList[i].Image);
            if (ImgSize == null) return;
            ImgSize.Source = src;
            // 预览容器 250px 高：400px 时占满
            double ph = 250.0 * h / 400.0;
            ImgSize.Height = ph;
            if (LblSizeCap != null)
            {
                int w = (int)Math.Round(h * 320.0 / 300.0);
                int fh = (int)Math.Round(h * 324.0 / 300.0);
                int bub = ChkBub.IsChecked == true ? (int)SldBub.Value : 0;
                LblSizeCap.Text = string.Format("角色 {0} × {1} px　·　桌宠窗口 {2} × {3} px（含气泡 {4}）", w, fh, w, fh + bub, bub);
            }
        }
        catch (Exception ex) { LogErr("sizeprev", ex); }
    }

    // 气泡轮廓（预览用）：圆角矩形 + 尾巴，一条闭合路径 ⇒ 描边不会在内部留接缝
    static Geometry BubbleGeometry(double l, double t, double r, double b, double rad,
                                   double tailL, double tailR, double tipX, double tipY)
    {
        var g = new StreamGeometry();
        using (StreamGeometryContext c = g.Open())
        {
            c.BeginFigure(new Point(l + rad, t), true, true);
            c.LineTo(new Point(r - rad, t), true, false);
            c.ArcTo(new Point(r, t + rad), new Size(rad, rad), 0, false, SweepDirection.Clockwise, true, false);
            c.LineTo(new Point(r, b - rad), true, false);
            c.ArcTo(new Point(r - rad, b), new Size(rad, rad), 0, false, SweepDirection.Clockwise, true, false);
            c.LineTo(new Point(tailR, b), true, false);
            c.LineTo(new Point(tipX, tipY), true, false);
            c.LineTo(new Point(tailL, b), true, false);
            c.LineTo(new Point(l + rad, b), true, false);
            c.ArcTo(new Point(l, b - rad), new Size(rad, rad), 0, false, SweepDirection.Clockwise, true, false);
            c.LineTo(new Point(l, t + rad), true, false);
            c.ArcTo(new Point(l + rad, t), new Size(rad, rad), 0, false, SweepDirection.Clockwise, true, false);
        }
        g.Freeze();
        return g;
    }

    // ---------------- 动画页（真实帧预览 + 幅度 / 倍速 / 开关） ----------------
    static string Cap(string s)
    {
        return s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
    }

    static double EntryAmp(CharEntry e, string key)
    {
        if (e == null) return 1;
        if (key == "idle") return e.AmpIdle;
        if (key == "press") return e.AmpPress;
        if (key == "click") return e.AmpClick;
        return e.AmpDrag;
    }

    static void SetEntryAmp(CharEntry e, string key, double v)
    {
        if (e == null) return;
        if (key == "idle") e.AmpIdle = v;
        else if (key == "press") e.AmpPress = v;
        else if (key == "click") e.AmpClick = v;
        else e.AmpDrag = v;
    }

    static void BuildAnimPage()
    {
        if (PageAnim == null) return;
        StopAnimPreviews();
        AnimCards.Clear();
        PageAnim.Children.Clear();
        _animCardsDir = SelectedChar != null ? SelectedChar.Dir : "";

        PageAnim.Children.Add(Txt("动画", 28, "#E4000000", true));
        PageAnim.Children.Add(Txt("左边是真实帧预览", 12, "#99000000"));
        PageAnim.Children.Add(new Border { Height = 16 });

        CharEntry ce = SelectedChar;
        Dictionary<string, string> cm = (ce != null && !string.IsNullOrEmpty(ce.Dir))
            ? ReadManifest(ce.Dir) : new Dictionary<string, string>();
        byte[] data = null;
        int fw = 0, fh = 0, idleN = 24, pressN = 6, clickN = 6, dragN = 8;
        if (ce != null && ce.HasFrames)
        {
            try
            {
                data = File.ReadAllBytes(ce.Frames);
                if (File.Exists(ce.Meta))
                {
                    foreach (string raw in File.ReadAllLines(ce.Meta))
                    {
                        int eq = raw.IndexOf('=');
                        if (eq <= 0) continue;
                        string k = raw.Substring(0, eq).Trim(), v = raw.Substring(eq + 1).Trim();
                        if (k == "w") int.TryParse(v, out fw);
                        else if (k == "h") int.TryParse(v, out fh);
                        else if (k == "idle") int.TryParse(v, out idleN);
                        else if (k == "press") int.TryParse(v, out pressN);
                        else if (k == "click") int.TryParse(v, out clickN);
                        else if (k == "drag") int.TryParse(v, out dragN);
                    }
                }
            }
            catch (Exception ex) { LogErr("animload", ex); data = null; }
        }
        if (fw <= 0 || fh <= 0) data = null;
        int totalFrames = fw > 0 ? data.Length / (fw * fh * 4) : 0;

        string[] titles = { "待机动画", "按键动画", "点击动画", "拖动动画" };
        string[] descs = {
            "呼吸浮动，平时一直在播",
            "键盘 / 鼠标按下时的挤压",
            "点她时的挤压 + 小跳",
            "拖动时的悬空摆动"
        };
        int[] starts = { 0, idleN, idleN + pressN, idleN + pressN + clickN };
        int[] counts = { idleN, pressN, clickN, dragN };

        var grid = new UniformGrid { Columns = 2 };
        for (int i = 0; i < AnimKeys.Length; i++)
        {
            string key = AnimKeys[i];
            double amp = EntryAmp(ce, key);
            bool enabled = CurAnim("anim" + Cap(key));
            int pct = CurSpeedPct("speed" + Cap(key));

            var card = new AnimCard { Key = key, Title = titles[i] };
            var root = new StackPanel();

            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition());
            var box = new Border
            {
                Width = 104, Height = 104, Background = C("#FAFAFA"),
                CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, 12, 0)
            };
            AnimPreview pv = new AnimPreview(96);
            box.Child = pv.View;
            head.Children.Add(box);

            var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            right.Children.Add(Txt(titles[i], 14, "#E4000000", true));
            right.Children.Add(Txt(descs[i], 12, "#99000000"));
            var chk = new CheckBox
            {
                Content = "启用", Style = (Style)Win.FindResource("ToggleSwitch"),
                IsChecked = enabled, Margin = new Thickness(0, 10, 0, 0)
            };
            right.Children.Add(chk);
            Grid.SetColumn(right, 1); head.Children.Add(right);
            root.Children.Add(head);
            root.Children.Add(new Border { Height = 10 });

            // 幅度
            var ampRow = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            ampRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            ampRow.ColumnDefinitions.Add(new ColumnDefinition());
            ampRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
            var al = Txt("幅度", 12, "#99000000"); al.VerticalAlignment = VerticalAlignment.Center;
            ampRow.Children.Add(al);
            var ampSld = new Slider
            {
                Minimum = 20, Maximum = 200, TickFrequency = 10,
                Value = Math.Max(20, Math.Min(200, amp * 100)),
                Style = (Style)Win.FindResource("WinSlider")
            };
            Grid.SetColumn(ampSld, 1); ampRow.Children.Add(ampSld);
            var ampVal = Txt((int)ampSld.Value + "%", 11, "#99000000");
            ampVal.TextAlignment = TextAlignment.Right; ampVal.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(ampVal, 2); ampRow.Children.Add(ampVal);
            ampSld.ValueChanged += (s, e) => ampVal.Text = (int)ampSld.Value + "%";
            root.Children.Add(ampRow);

            // 倍速
            var spdRow = new Grid { Margin = new Thickness(0, 2, 0, 0) };
            spdRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            spdRow.ColumnDefinitions.Add(new ColumnDefinition());
            spdRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
            var sl = Txt("倍速", 12, "#99000000"); sl.VerticalAlignment = VerticalAlignment.Center;
            spdRow.Children.Add(sl);
            var spdSld = new Slider
            {
                Minimum = 50, Maximum = 200, TickFrequency = 10, Value = pct,
                Style = (Style)Win.FindResource("WinSlider")
            };
            Grid.SetColumn(spdSld, 1); spdRow.Children.Add(spdSld);
            var spdVal = Txt("×" + (pct / 100.0).ToString("0.0", CultureInfo.InvariantCulture), 11, "#99000000");
            spdVal.TextAlignment = TextAlignment.Right; spdVal.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(spdVal, 2); spdRow.Children.Add(spdVal);
            spdSld.ValueChanged += (s, e) =>
            {
                double v = spdSld.Value / 100.0;
                spdVal.Text = "×" + v.ToString("0.0", CultureInfo.InvariantCulture);
                pv.SetSpeed(v);
            };
            root.Children.Add(spdRow);

            root.Children.Add(Txt("幅度需保存后生效", 11, "#99000000"));

            var foot = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            var customBtn = new Button
            {
                Content = "自定义动画…", Style = (Style)Win.FindResource("SecondaryButton"),
                Padding = new Thickness(14, 4, 14, 4), MinWidth = 0, Height = 28, FontSize = 12
            };
            BuildCustomMenu(customBtn, ce, key);
            foot.Children.Add(customBtn);
            if (!string.IsNullOrEmpty(ce.Dir) && cm.ContainsKey("custom" + Cap(key)))
            {
                var tag = Txt("已自定义", 11, "#0F7B43");
                tag.Margin = new Thickness(10, 0, 0, 0);
                tag.VerticalAlignment = VerticalAlignment.Center;
                foot.Children.Add(tag);
            }
            root.Children.Add(foot);

            var border = new Border
            {
                Style = (Style)Win.FindResource("Card"),
                Margin = new Thickness(0, 0, 10, 12)
            };
            border.Child = root;
            grid.Children.Add(border);

            card.Toggle = chk; card.Amp = ampSld; card.Speed = spdSld;
            card.AmpLbl = ampVal; card.SpeedLbl = spdVal; card.Preview = pv;
            AnimCards.Add(card);

            if (data != null && starts[i] + counts[i] <= totalFrames)
            {
                try { pv.Load(data, fw, fh, starts[i], counts[i], pct / 100.0, key != "idle"); }
                catch (Exception ex)
                {
                    LogErr("animprev " + key + " w=" + fw + " h=" + fh + " fb=" + (fw * fh * 4)
                        + " len=" + data.Length + " start=" + starts[i] + " n=" + counts[i], ex);
                }
            }
        }
        PageAnim.Children.Add(grid);

        if (data == null)
            PageAnim.Children.Add(Txt("保存后自动生成", 12, "#C42B1C"));

        var tip = new Border { Style = (Style)Win.FindResource("Card") };
        tip.Child = Txt("幅度需保存；倍速即时生效", 12, "#99000000");
        PageAnim.Children.Add(tip);
    }

    // ---------------- 自定义动画帧 ----------------
    static string SlotName(string slot)
    {
        if (slot == "idle") return "待机动画";
        if (slot == "press") return "按键动画";
        if (slot == "click") return "点击动画";
        return "拖动动画";
    }

    static void BuildCustomMenu(Button btn, CharEntry ce, string slot)
    {
        var menu = new ContextMenu();
        var miFile = new MenuItem { Header = "用 GIF / 图片替换…" };
        miFile.Click += (s, e) => PickCustomAnim(ce, slot, false);
        var miDir = new MenuItem { Header = "用帧文件夹替换…" };
        miDir.Click += (s, e) => PickCustomAnim(ce, slot, true);
        var miReset = new MenuItem { Header = "恢复默认动画" };
        miReset.Click += (s, e) => ResetCustomAnim(ce, slot);
        menu.Items.Add(miFile);
        menu.Items.Add(miDir);
        menu.Items.Add(new Separator());
        menu.Items.Add(miReset);
        btn.Click += (s, e) => { menu.PlacementTarget = btn; menu.IsOpen = true; };
    }

    static void PickCustomAnim(CharEntry ce, string slot, bool folder)
    {
        if (ce == null || string.IsNullOrEmpty(ce.Dir)) return;
        string src = "";
        if (folder)
        {
            using (var dlg = new System.Windows.Forms.FolderBrowserDialog())
            {
                dlg.Description = "选择一帧一张图的文件夹（按文件名排序）";
                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                src = dlg.SelectedPath;
            }
        }
        else
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "动画/图片 (*.gif;*.png;*.jpg;*.jpeg;*.webp)|*.gif;*.png;*.jpg;*.jpeg;*.webp|所有文件|*.*"
            };
            if (dlg.ShowDialog() != true) return;
            src = dlg.FileName;
        }
        string err;
        if (RunAnimCustom("apply", ce.Dir, slot, src, out err))
        {
            ReloadChars();
            BuildAnimPage();
            Dlg.Info("已替换动画",
                "「" + ce.Name + "」的" + SlotName(slot) + "已换成自定义帧。\n点「保存更改」重启桌宠后生效。", "知道了");
        }
        else
        {
            Dlg.Info("替换失败", string.IsNullOrEmpty(err)
                ? "anim_custom.py 执行失败，请确认 Python + Pillow 可用。" : err, "好");
        }
    }

    static void ResetCustomAnim(CharEntry ce, string slot)
    {
        if (ce == null || string.IsNullOrEmpty(ce.Dir)) return;
        if (!Dlg.Ask("恢复默认动画？", "「" + ce.Name + "」的" + SlotName(slot) + "会按立绘重新生成，自定义帧会被覆盖。", "恢复", "算了", true)) return;
        string err;
        if (RunAnimCustom("reset", ce.Dir, slot, "", out err))
        {
            ReloadChars();
            BuildAnimPage();
            Dlg.Info("已恢复默认", SlotName(slot) + "已按立绘重新生成。\n点「保存更改」重启桌宠后生效。", "知道了");
        }
        else
        {
            Dlg.Info("恢复失败", string.IsNullOrEmpty(err) ? "anim_custom.py 执行失败。" : err, "好");
        }
    }

    static bool RunAnimCustom(string cmd, string packDir, string slot, string src, out string err)
    {
        err = "";
        try
        {
            string args = "anim_custom.py " + cmd + " --pack \"" + packDir + "\" --slot " + slot;
            if (cmd == "apply") args += " --src \"" + src + "\"";
            var psi = new ProcessStartInfo(FindPython(), args)
            {
                WorkingDirectory = Here, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            Process p = Process.Start(psi);
            string so = p.StandardOutput.ReadToEnd();
            string se = p.StandardError.ReadToEnd();
            p.WaitForExit(120000);
            if (p.ExitCode != 0)
            {
                err = (se + "\n" + so).Trim();
                return false;
            }
            return true;
        }
        catch (Exception ex) { err = ex.Message; LogErr("customanim", ex); return false; }
    }

    // ==================== 气泡外框 / 配色 / 文字色 ====================
    static Color _bubAccent = Color.FromRgb(0x8F, 0xA6, 0xD8);   // 气泡主色（空 = 默认蓝）

    static byte Clamp255(double v) { return (byte)(v < 0 ? 0 : (v > 255 ? 255 : v)); }
    static Color WpfLighten(Color c, double t) { return Color.FromRgb(Clamp255(c.R + (255 - c.R) * t), Clamp255(c.G + (255 - c.G) * t), Clamp255(c.B + (255 - c.B) * t)); }
    static Color WpfDarken(Color c, double t) { return Color.FromRgb(Clamp255(c.R * (1 - t)), Clamp255(c.G * (1 - t)), Clamp255(c.B * (1 - t))); }
    static Color WpfInkFor(Color accent)
    {
        double lum = (0.299 * accent.R + 0.587 * accent.G + 0.114 * accent.B) / 255.0;
        return lum > 0.62 ? WpfDarken(accent, 0.72) : WpfLighten(accent, 0.90);
    }
    public static string HexOf(Color c) { return c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2"); }
    // C# 里生成的选中态描边 / 文字：取当前主题色（换肤时这些卡片会整体重建，取当前值即可）
    static Brush ThemeBr { get { return new SolidColorBrush(_themeColor); } }
    public static Color WpfParseHex(string s, Color fallback)
    {
        if (string.IsNullOrEmpty(s)) return fallback;
        s = s.Trim().TrimStart('#');
        if (s.Length != 6) return fallback;
        byte r, g, b;
        if (byte.TryParse(s.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out r) &&
            byte.TryParse(s.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out g) &&
            byte.TryParse(s.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b))
            return Color.FromRgb(r, g, b);
        return fallback;
    }
    static Brush Br(byte a, byte r, byte g, byte b) { return new SolidColorBrush(Color.FromArgb(a, r, g, b)); }
    static Brush Br(byte r, byte g, byte b) { return new SolidColorBrush(Color.FromRgb(r, g, b)); }
    static Pen Pn(Brush b, double w) { return new Pen(b, w); }

    static readonly string[][] BubbleStyleDefs = {
        new[] { "card",    "圆角卡片", "渐变卡片 + 小尾巴（默认）" },
        new[] { "classic", "经典椭圆", "插件同款：白色椭圆 + 两个小圆点" },
        new[] { "comic",   "漫画描边", "粗黑描边 + 硬阴影" },
        new[] { "glass",   "半透明",   "磨砂玻璃感，能透出桌面" },
    };
    static string BubbleStyleName(string key)
    {
        foreach (string[] d in BubbleStyleDefs) if (d[0] == key) return d[1];
        return key == "custom" ? "自定义" : key;
    }

    // ---- 颜色预设（气泡颜色 / 主题色共用）----
    static readonly string[][] ColorPresets = {
        new[] { "8FA6D8", "默认蓝" }, new[] { "005FB8", "群青" },
        new[] { "3B9BE0", "天蓝" },   new[] { "2FA8A0", "青绿" },
        new[] { "5FA855", "草绿" },   new[] { "C8A227", "芥黄" },
        new[] { "E08A3C", "落日" },   new[] { "E06C8B", "玫粉" },
        new[] { "9B7BD8", "紫罗兰" }, new[] { "6B7280", "石墨" },
        new[] { "3F4A5A", "深灰蓝" }, new[] { "2F3542", "墨黑" },
    };

    static void BuildColorTiles(WrapPanel host, Color cur, Action<Color> onPick, string checkHex, Color? selColor = null)
    {
        if (host == null) return;
        host.Children.Clear();
        foreach (string[] p in ColorPresets)
        {
            Color c = WpfParseHex(p[0], cur);
            host.Children.Add(MakeColorTile(c, p[1], string.Equals(p[0], checkHex, StringComparison.OrdinalIgnoreCase), onPick, selColor));
        }
    }

    static Border MakeColorTile(Color c, string name, bool selected, Action<Color> onPick, Color? selColor = null)
    {
        // 选中态的描边/文字默认用经典蓝；主题色页传入当前主题色，视觉更统一
        Color sel = selColor ?? Color.FromRgb(0x00, 0x5F, 0xB8);
        var selBr = new SolidColorBrush(sel);
        // 选中/未选中的描边粗细保持一致（都是 2px）：否则切换时内容会左右抖 1px
        var b = new Border
        {
            Width = 62, Height = 52, Margin = new Thickness(0, 0, 8, 8),
            Background = selected ? C("#F5F9FF") : Brushes.White,
            BorderBrush = selected ? selBr : C("#EDEFF3"),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(6), Cursor = Cursors.Hand,
            ToolTip = name + " #" + HexOf(c)
        };
        var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        sp.Children.Add(new Border
        {
            Width = 26, Height = 26, CornerRadius = new CornerRadius(13),
            Background = new SolidColorBrush(c), HorizontalAlignment = HorizontalAlignment.Center,
            BorderBrush = C("#00000022"), BorderThickness = new Thickness(1)
        });
        sp.Children.Add(new TextBlock
        {
            Text = name, FontSize = 10.5,
            Foreground = selected ? selBr : C("#7A8290"),
            HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 3, 0, 0)
        });
        b.Child = sp;
        b.MouseLeftButtonUp += (s, e) => { onPick(c); e.Handled = true; };
        b.MouseEnter += (s, e) => { if (!selected) b.Background = C("#FAFCFF"); };
        b.MouseLeave += (s, e) => { if (!selected) b.Background = Brushes.White; };
        return b;
    }

    static void BuildBubbleColorTiles()
    {
        BuildColorTiles(BubbleColorHost, _bubbleColorSel, c =>
        {
            _bubbleColorSel = c;
            _bubAccent = c;
            BuildBubbleColorTiles();
            BuildBubbleStyleTiles();
            DrawBubblePreview();
        }, HexOf(_bubbleColorSel));
    }

    // ---- 文字颜色 ----
    static readonly string[][] InkPresets = {
        new[] { "auto",   "自动" },   new[] { "1A1A1A", "纯黑" },
        new[] { "FFFFFF", "纯白" },   new[] { "203170", "深蓝" },
        new[] { "C2426B", "玫粉" },   new[] { "8A6D1F", "芥黄" },
        new[] { "2E6B4F", "墨绿" },   new[] { "6B4E8A", "紫褐" },
    };

    static void BuildBubbleInkTiles()
    {
        if (BubbleInkHost == null) return;
        BubbleInkHost.Children.Clear();
        string cur = string.IsNullOrEmpty(_bubbleInkSel) ? "auto" : _bubbleInkSel.ToUpperInvariant();
        foreach (string[] p in InkPresets)
        {
            bool sel = string.Equals(p[0], cur, StringComparison.OrdinalIgnoreCase);
            bool isAuto = p[0] == "auto";
            var b = new Border
            {
                Width = 62, Height = 52, Margin = new Thickness(0, 0, 8, 8),
                Background = sel ? C("#F5F9FF") : Brushes.White,
                BorderBrush = sel ? ThemeBr : C("#EDEFF3"),
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(6), Cursor = Cursors.Hand,
                ToolTip = isAuto ? "按气泡底色自动选深浅" : "文字颜色 #" + p[0]
            };
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var sw = new Border
            {
                Width = 26, Height = 26, CornerRadius = new CornerRadius(13),
                HorizontalAlignment = HorizontalAlignment.Center,
                BorderBrush = C("#00000022"), BorderThickness = new Thickness(1),
                Background = isAuto
                    ? (Brush)new LinearGradientBrush(Color.FromRgb(0xFF, 0xFF, 0xFF), Color.FromRgb(0x33, 0x33, 0x33), 90)
                    : new SolidColorBrush(WpfParseHex(p[0], Color.FromRgb(0x1A, 0x1A, 0x1A)))
            };
            sp.Children.Add(sw);
            sp.Children.Add(new TextBlock
            {
                Text = p[1], FontSize = 11,
                Foreground = sel ? ThemeBr : C("#7A8290"),
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 3, 0, 0)
            });
            b.Child = sp;
            string key = p[0];
            b.MouseLeftButtonUp += (s, e) =>
            {
                _bubbleInkSel = key;
                BuildBubbleInkTiles();
                DrawBubblePreview();
                e.Handled = true;
            };
            b.MouseEnter += (s, e) => { if (!sel) b.Background = C("#FAFCFF"); };
            b.MouseLeave += (s, e) => { if (!sel) b.Background = Brushes.White; };
            BubbleInkHost.Children.Add(b);
        }
    }

    // ---- 自定义外框文件 ----
    static List<string> ListBubbleFrames()
    {
        var list = new List<string>();
        try
        {
            if (Directory.Exists(BubbleDir))
                foreach (string f in Directory.GetFiles(BubbleDir))
                {
                    string e = Path.GetExtension(f).ToLowerInvariant();
                    if (e == ".png" || e == ".jpg" || e == ".jpeg" || e == ".webp") list.Add(f);
                }
        }
        catch (Exception) { }
        list.Sort(StringComparer.CurrentCultureIgnoreCase);
        return list;
    }

    static bool PathsEqual(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
        try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
        catch (Exception) { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
    }

    static string RelBubbleFrame(string p)
    {
        if (string.IsNullOrEmpty(p)) return "";
        try
        {
            string full = Path.GetFullPath(p), here = Path.GetFullPath(Here);
            if (full.StartsWith(here, StringComparison.OrdinalIgnoreCase))
                return full.Substring(here.Length).TrimStart('\\', '/');
        }
        catch (Exception) { }
        return p;
    }

    static readonly Dictionary<string, BitmapImage> _frameThumbCache = new Dictionary<string, BitmapImage>();
    static BitmapSource LoadBubbleFrameImage(string path)
    {
        try
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            BitmapImage cached;
            if (_frameThumbCache.TryGetValue(path, out cached)) return cached;
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.UriSource = new Uri(path);
            bi.EndInit();
            bi.Freeze();
            _frameThumbCache[path] = bi;
            return bi;
        }
        catch (Exception) { return null; }
    }

    static readonly Dictionary<string, int> _frameAlphaCache = new Dictionary<string, int>();
    static int CenterAlpha(BitmapSource img)
    {
        string key = img.ToString();
        int cached;
        if (_frameAlphaCache.TryGetValue(key, out cached)) return cached;
        int res = 255;
        try
        {
            int W = img.PixelWidth, H = img.PixelHeight, stride = (W * 4 + 3) & ~3;
            byte[] buf = new byte[Math.Max(4, stride * H)];
            img.CopyPixels(buf, stride, 0);
            double a = 0; int n = 0;
            for (int y = H * 3 / 10; y < H * 7 / 10; y += 2)
                for (int x = W * 3 / 10; x < W * 7 / 10; x += 2)
                {
                    int o = y * stride + x * 4;
                    if (o + 3 >= buf.Length) break;
                    a += buf[o + 3] / 255.0; n++;
                }
            if (n > 0) res = (int)Math.Round(a / n * 255.0);
        }
        catch (Exception) { }
        _frameAlphaCache[key] = res;
        return res;
    }

    static readonly Dictionary<string, Color> _frameInkCache = new Dictionary<string, Color>();
    static Brush BubbleInk(BitmapSource img)
    {
        string key = img.ToString();
        Color cached;
        if (_frameInkCache.TryGetValue(key, out cached)) return new SolidColorBrush(cached);
        double lum = 255;
        try
        {
            int W = img.PixelWidth, H = img.PixelHeight, stride = (W * 4 + 3) & ~3;
            byte[] buf = new byte[Math.Max(4, stride * H)];
            img.CopyPixels(buf, stride, 0);
            double l = 0, a = 0;
            for (int y = H * 3 / 10; y < H * 7 / 10; y += 2)
                for (int x = W * 3 / 10; x < W * 7 / 10; x += 2)
                {
                    int o = y * stride + x * 4;
                    if (o + 3 >= buf.Length) break;
                    double al = buf[o + 3] / 255.0;
                    l += (0.299 * buf[o + 2] + 0.587 * buf[o + 1] + 0.114 * buf[o]) * al;   // BGRA
                    a += al;
                }
            if (a > 0.5) lum = l / a;
        }
        catch (Exception) { }
        Color ink = lum > 132 ? Color.FromRgb(0x1A, 0x1A, 0x1A) : Color.FromRgb(0xF7, 0xF9, 0xFF);
        _frameInkCache[key] = ink;
        return new SolidColorBrush(ink);
    }

    // 九宫格：相邻块共用整数边界，不会有重叠发暗或缝隙亮线
    static void DrawNineSliceWpf(DrawingContext dc, BitmapSource img, Rect dst, double insX, double insY)
    {
        int W = img.PixelWidth, H = img.PixelHeight;
        double scale = Math.Min(dst.Width / W, dst.Height / H);
        int cw = Math.Max(1, (int)Math.Round(Math.Min(insX * W * scale, dst.Width / 2.0 - 1)));
        int ch = Math.Max(1, (int)Math.Round(Math.Min(insY * H * scale, dst.Height / 2.0 - 1)));
        int x0 = (int)Math.Round(dst.Left), y0 = (int)Math.Round(dst.Top);
        int w = Math.Max(2, (int)Math.Round(dst.Width)), h = Math.Max(2, (int)Math.Round(dst.Height));
        int scw = Math.Max(1, (int)Math.Round(insX * W)), sch = Math.Max(1, (int)Math.Round(insY * H));
        int[] dxs = { x0, x0 + cw, x0 + w - cw, x0 + w };
        int[] dys = { y0, y0 + ch, y0 + h - ch, y0 + h };
        int[] sxs = { 0, scw, W - scw, W };
        int[] sys = { 0, sch, H - sch, H };
        for (int j = 0; j < 3; j++)
            for (int i = 0; i < 3; i++)
            {
                int dw = dxs[i + 1] - dxs[i], dh = dys[j + 1] - dys[j];
                int srw = sxs[i + 1] - sxs[i], srh = sys[j + 1] - sys[j];
                if (dw <= 0 || dh <= 0 || srw <= 0 || srh <= 0) continue;
                var crop = new CroppedBitmap(img, new Int32Rect(sxs[i], sys[j], srw, srh));
                crop.Freeze();
                dc.DrawImage(crop, new Rect(dxs[i], dys[j], dw, dh));
            }
    }

    static void BubbleLayout(double w, double h, out double l, out double t, out double r, out double b,
                             out double rad, out double sw, out double tipX, out double tipY,
                             out double tailL, out double tailR)
    {
        sw = Math.Max(1.4, w * 4.6 / 1026.0 * 1.6);
        double tailH = Math.Min(Math.Max(7, h * 0.17), 16);
        l = sw + 1.5; r = w - sw - 1.5; t = sw + 1.5; b = h - tailH - sw - 1.5;
        rad = Math.Max(6, Math.Min(15, (b - t) * 0.32));
        double cx = (l + r) / 2.0;
        tipY = b + tailH;
        tailL = cx - Math.Max(9, w * 0.05);
        tailR = cx + Math.Max(4, w * 0.022);
        tipX = cx - Math.Max(2, w * 0.014);
    }

    static Geometry BubbleGeo(double l, double t, double r, double b, double rad,
                              double tailL, double tailR, double tipX, double tipY)
    {
        return BubbleGeometry(l, t, r, b, Math.Min(rad, Math.Min((r - l) / 2.0, (b - t) / 2.0)), tailL, tailR, tipX, tipY);
    }

    static void DrawCardWpf(DrawingContext dc, double l, double t, double r, double b, double rad,
                            double tailL, double tailR, double tipX, double tipY, double sw)
    {
        Geometry geo = BubbleGeo(l, t, r, b, rad, tailL, tailR, tipX, tipY);
        Brush sh1 = Br(24, 0x2A, 0x3A, 0x66);
        Brush sh2 = Br(15, 0x2A, 0x3A, 0x66);
        Brush fill = new LinearGradientBrush(Colors.White, WpfLighten(_bubAccent, 0.78), 90);
        Pen pen = Pn(Br(_bubAccent.R, _bubAccent.G, _bubAccent.B), sw);
        Pen hl = Pn(Br(130, 0xFF, 0xFF, 0xFF), Math.Max(1.0, sw * 0.7));
        dc.PushTransform(new TranslateTransform(0, 2.5)); dc.DrawGeometry(sh1, null, geo); dc.Pop();
        dc.PushTransform(new TranslateTransform(0, 5)); dc.DrawGeometry(sh2, null, geo); dc.Pop();
        dc.DrawGeometry(fill, pen, geo);
        double ins = rad + 4;
        if (r - l > ins * 2) dc.DrawLine(hl, new Point(l + ins, t + sw * 1.2), new Point(r - ins, t + sw * 1.2));
    }

    static void DrawClassicWpf(DrawingContext dc, double l, double t, double r, double b,
                               double w, double h, out Rect textRect)
    {
        double rx = (r - l) / 2.0, ry = (b - t) / 2.0;
        double cx = (l + r) / 2.0, cy = (t + b) / 2.0;
        double r1 = Math.Max(3, w * 0.048), x1 = cx - rx * 0.74, y1 = h - r1 * 1.25;
        double r2 = Math.Max(2, w * 0.031), x2 = cx - rx * 0.33, y2 = h - r2 * 0.85;
        Color lineC = WpfDarken(_bubAccent, 0.30);
        Color shC = WpfDarken(_bubAccent, 0.45);
        Brush stroke = Br(lineC.R, lineC.G, lineC.B);
        Brush sh = Br(22, shC.R, shC.G, shC.B);
        Pen pen = Pn(stroke, Math.Max(1.8, w * 0.012));
        dc.DrawEllipse(sh, null, new Point(cx, cy + 3), rx, ry);
        dc.DrawEllipse(sh, null, new Point(x1, y1 + 2), r1, r1 * 0.7);
        dc.DrawEllipse(sh, null, new Point(x2, y2 + 2), r2, r2 * 0.7);
        dc.DrawEllipse(Brushes.White, pen, new Point(cx, cy), rx, ry);
        dc.DrawEllipse(Brushes.White, pen, new Point(x1, y1), r1, r1 * 0.7);
        dc.DrawEllipse(Brushes.White, pen, new Point(x2, y2), r2, r2 * 0.7);
        textRect = new Rect(cx - rx * 0.80, cy - ry * 0.74, rx * 1.60, ry * 1.48);
    }

    static void DrawComicWpf(DrawingContext dc, double l, double t, double r, double b,
                             double tailL, double tailR, double tipX, double tipY, double sw)
    {
        Geometry geo = BubbleGeo(l, t, r, b, 4, tailL, tailR, tipX, tipY);
        Color cSh = WpfDarken(_bubAccent, 0.62);
        Color cLn = WpfDarken(_bubAccent, 0.55);
        Brush sh = Br(46, cSh.R, cSh.G, cSh.B);
        Brush line = Br(cLn.R, cLn.G, cLn.B);
        dc.PushTransform(new TranslateTransform(4.5, 4.5));
        dc.DrawGeometry(sh, null, geo);
        dc.Pop();
        dc.DrawGeometry(Brushes.White, Pn(line, Math.Max(2.2, sw * 1.4)), geo);
    }

    static void DrawGlassWpf(DrawingContext dc, double l, double t, double r, double b, double rad,
                             double tailL, double tailR, double tipX, double tipY, double sw)
    {
        Geometry geo = BubbleGeo(l, t, r, b, rad, tailL, tailR, tipX, tipY);
        Brush sh = Br(20, 0x2A, 0x3A, 0x66);
        Color gEnd = WpfLighten(_bubAccent, 0.55);
        Brush fill = new LinearGradientBrush(Color.FromArgb(236, 0xFF, 0xFF, 0xFF),
                                              Color.FromArgb(196, gEnd.R, gEnd.G, gEnd.B), 90);
        Pen pen = Pn(Br(238, 0xFF, 0xFF, 0xFF), Math.Max(1.6, sw * 1.2));
        Pen hl = Pn(Br(150, 0xFF, 0xFF, 0xFF), Math.Max(1.0, sw * 0.8));
        dc.PushTransform(new TranslateTransform(0, 3)); dc.DrawGeometry(sh, null, geo); dc.Pop();
        dc.PushTransform(new TranslateTransform(0, 6)); dc.DrawGeometry(sh, null, geo); dc.Pop();
        dc.PushTransform(new TranslateTransform(0, 9)); dc.DrawGeometry(sh, null, geo); dc.Pop();
        dc.DrawGeometry(fill, pen, geo);
        double ins = rad + 5;
        if (r - l > ins * 2) dc.DrawLine(hl, new Point(l + ins, t + sw * 1.3), new Point(r - ins, t + sw * 1.3));
    }

    static void DrawCustomBubbleWpf(DrawingContext dc, string framePath, double w, double h,
                                    out Rect textRect, out Brush ink)
    {
        textRect = new Rect(w * 0.115, h * 0.13, Math.Max(16, w * 0.77), Math.Max(10, h * 0.60));
        ink = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
        BitmapSource img = LoadBubbleFrameImage(framePath);
        if (img == null) return;
        DrawNineSliceWpf(dc, img, new Rect(0, 0, w, h), 0.26, 0.30);
        ink = BubbleInk(img);
        if (CenterAlpha(img) < 110)
        {
            Rect back = new Rect(textRect.Left - 3, textRect.Top - 3, textRect.Width + 6, textRect.Height + 6);
            dc.DrawRoundedRectangle(Br(150, 0xFF, 0xFF, 0xFF), null, back, 7, 7);
        }
    }

    // 在给定矩形里画气泡外框；textRect 返回文字该放的位置
    static void DrawBubbleWpf(DrawingContext dc, string style, string framePath,
                              double w, double h, out Rect textRect, out Brush ink)
    {
        double l, t, r, b, rad, sw, tipX, tipY, tailL, tailR;
        BubbleLayout(w, h, out l, out t, out r, out b, out rad, out sw, out tipX, out tipY, out tailL, out tailR);
        double bw = r - l, bh = b - t;
        double pad = Math.Max(6, Math.Min(w, h) * 0.10);
        textRect = new Rect(l + pad, t + pad * 0.85, Math.Max(16, bw - pad * 2), Math.Max(12, bh - pad * 1.7));
        ink = new SolidColorBrush(WpfInkFor(_bubAccent));

        if (style == "classic") { DrawClassicWpf(dc, l, t, r, b, w, h, out textRect); ink = new SolidColorBrush(WpfDarken(_bubAccent, 0.55)); return; }
        if (style == "comic") { DrawComicWpf(dc, l, t, r, b, tailL, tailR, tipX, tipY, sw); ink = new SolidColorBrush(WpfDarken(_bubAccent, 0.78)); return; }
        if (style == "glass") { DrawGlassWpf(dc, l, t, r, b, rad, tailL, tailR, tipX, tipY, sw); ink = new SolidColorBrush(WpfDarken(_bubAccent, 0.72)); return; }
        if (style == "custom") { DrawCustomBubbleWpf(dc, framePath, w, h, out textRect, out ink); return; }
        DrawCardWpf(dc, l, t, r, b, rad, tailL, tailR, tipX, tipY, sw);
    }

    static BitmapSource RenderBubble(string style, string framePath, int w, int h, string text, double fontPt)
    {
        var dv = new DrawingVisual();
        using (DrawingContext dc = dv.RenderOpen())
        {
            Rect tr;
            Brush ink;
            DrawBubbleWpf(dc, style, framePath, w, h, out tr, out ink);
            if (!string.IsNullOrEmpty(text) && tr.Width > 14 && tr.Height > 8)
            {
                string fn = CmbFont != null && CmbFont.SelectedIndex >= 0
                    ? CmbFont.Items[CmbFont.SelectedIndex].ToString() : CurFont;
                Typeface tf = new Typeface(new FontFamily(fn), FontStyles.Normal,
                    FontWeights.Normal, FontStretches.Normal);
                FormattedText ft = new FormattedText(text, CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight, tf, fontPt, ink, 1.0);
                ft.MaxTextWidth = Math.Max(20, tr.Width);
                ft.MaxTextHeight = Math.Max(8, tr.Height);
                ft.TextAlignment = TextAlignment.Center;
                ft.Trimming = TextTrimming.CharacterEllipsis;
                dc.DrawText(ft, new Point(tr.Left, tr.Top + Math.Max(0, (tr.Height - ft.Height) / 2.0)));
            }
        }
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    // 读当前角色的某一帧（气泡预览里垫一个"宠物本体"，用来看高度对比）
    static BitmapSource PetFrameShot(int frameIndex)
    {
        try
        {
            CharEntry ce = SelectedChar;
            if (ce == null || !ce.HasFrames) return null;
            byte[] data = File.ReadAllBytes(ce.Frames);
            int fw = 0, fh = 0;
            if (File.Exists(ce.Meta))
                foreach (string raw in File.ReadAllLines(ce.Meta))
                {
                    int eq = raw.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = raw.Substring(0, eq).Trim(), v = raw.Substring(eq + 1).Trim();
                    if (k == "w") int.TryParse(v, out fw);
                    else if (k == "h") int.TryParse(v, out fh);
                }
            if (fw <= 0 || fh <= 0) return null;
            int fb = fw * fh * 4;
            int off = fb * Math.Max(0, frameIndex);
            if (off + fb > data.Length) off = 0;
            if (off + fb > data.Length) return null;
            System.Runtime.InteropServices.GCHandle gh =
                System.Runtime.InteropServices.GCHandle.Alloc(data, System.Runtime.InteropServices.GCHandleType.Pinned);
            try
            {
                // ⚠️ stride 是一行字节数（fw*4），不是整帧大小
                IntPtr p = new IntPtr(gh.AddrOfPinnedObject().ToInt64() + off);
                BitmapSource bs = BitmapSource.Create(fw, fh, 96, 96, PixelFormats.Pbgra32, null, p, fb, fw * 4);
                bs.Freeze();
                return bs;
            }
            finally { gh.Free(); }
        }
        catch (Exception ex) { LogErr("petshot", ex); return null; }
    }

    // 外框卡片：缩略图 + 名称，选中项有蓝色描边
    static Border StyleTile(string key, string title, string tip, string framePath)
    {
        bool sel = _bubbleStyleSel == key && (key != "custom"
            || (!string.IsNullOrEmpty(framePath) && PathsEqual(framePath, _bubbleFrameSel)));
        // 同理：粗细固定 2px，只换颜色，避免选中瞬间缩略图位移
        var b = new Border
        {
            Width = 168, Margin = new Thickness(0, 0, 10, 10),
            Background = sel ? C("#F5F9FF") : Brushes.White,
            BorderBrush = sel ? ThemeBr : C("#EDEFF3"),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 6, 8, 6),
            Cursor = Cursors.Hand,
            ToolTip = tip
        };
        var sp = new StackPanel();
        sp.Children.Add(new Image
        {
            Source = RenderBubble(key, framePath, 150, 58, "你好呀", 11),
            Width = 150, Height = 58, Stretch = Stretch.Uniform
        });
        sp.Children.Add(new TextBlock
        {
            Text = title, FontSize = 12, FontWeight = FontWeights.SemiBold,
            Foreground = sel ? ThemeBr : C("#3A3F49"),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis
        });
        b.Child = sp;
        b.MouseLeftButtonUp += (s, e) =>
        {
            _bubbleStyleSel = key;
            _bubbleFrameSel = framePath;
            BuildBubbleStyleTiles();
            DrawBubblePreview();
            e.Handled = true;
        };
        b.MouseEnter += (s, e) => { if (!sel) { b.Background = C("#FAFCFF"); b.BorderBrush = C("#CBD9EE"); } };
        b.MouseLeave += (s, e) => { if (!sel) { b.Background = Brushes.White; b.BorderBrush = C("#EDEFF3"); } };
        if (key == "custom" && !string.IsNullOrEmpty(framePath))
        {
            var del = new MenuItem { Header = "删除这个外框" };
            del.Click += (s, e) => DeleteBubbleFrame(framePath);
            b.ContextMenu = new ContextMenu();
            b.ContextMenu.Items.Add(del);
        }
        return b;
    }

    static void BuildBubbleStyleTiles()
    {
        if (BubbleStyleHost == null) return;
        BubbleStyleHost.Children.Clear();
        for (int i = 0; i < BubbleStyleDefs.Length; i++)
            BubbleStyleHost.Children.Add(StyleTile(BubbleStyleDefs[i][0], BubbleStyleDefs[i][1], BubbleStyleDefs[i][2], ""));
        foreach (string fp in ListBubbleFrames())
            BubbleStyleHost.Children.Add(StyleTile("custom", Path.GetFileNameWithoutExtension(fp),
                "自定义外框（右键删除）", fp));
        var up = new Border
        {
            Width = 168, Height = 78, Margin = new Thickness(0, 0, 10, 10),
            Background = C("#FCFCFD"), BorderBrush = C("#D8DDE5"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Cursor = Cursors.Hand, ToolTip = "上传自己的气泡外框图片"
        };
        var ut = new TextBlock
        {
            Text = "+ 上传外框", FontSize = 12, Foreground = C("#6B7280"),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
        up.Child = ut;
        up.MouseLeftButtonUp += (s, e) => { UploadBubbleFrame(); e.Handled = true; };
        up.MouseEnter += (s, e) => { up.Background = C("#F4F8FF"); up.BorderBrush = C("#9CC2F0"); };
        up.MouseLeave += (s, e) => { up.Background = C("#FCFCFD"); up.BorderBrush = C("#D8DDE5"); };
        BubbleStyleHost.Children.Add(up);
    }

    static void UploadBubbleFrame()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "图片 (*.png;*.jpg;*.jpeg;*.webp)|*.png;*.jpg;*.jpeg;*.webp|所有文件|*.*",
            Title = "选择气泡外框图片（透明 PNG 最佳）"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            Directory.CreateDirectory(BubbleDir);
            string baseName = SanitizeName(Path.GetFileNameWithoutExtension(dlg.FileName));
            if (string.IsNullOrEmpty(baseName)) baseName = "外框";
            string ext = Path.GetExtension(dlg.FileName).ToLowerInvariant();
            string dst = Path.Combine(BubbleDir, baseName + ext);
            int n = 2;
            while (File.Exists(dst) && !PathsEqual(dst, dlg.FileName))
            {
                dst = Path.Combine(BubbleDir, baseName + " (" + n + ")" + ext);
                n++;
            }
            if (!PathsEqual(dst, dlg.FileName)) File.Copy(dlg.FileName, dst, true);
            _bubbleStyleSel = "custom";
            _bubbleFrameSel = dst;
            BuildBubbleStyleTiles();
            DrawBubblePreview();
            LblBubHint.Text = "已添加外框：" + Path.GetFileName(dst) + "。点「保存更改」重启桌宠后生效。";
        }
        catch (Exception ex) { LogErr("upbubble", ex); Dlg.Info("上传失败", ex.Message, "好"); }
    }

    static void DeleteBubbleFrame(string path)
    {
        if (!Dlg.Ask("删除这个外框？",
                Path.GetFileName(path) + " 会从 bubble-frames 里删掉，且无法恢复。",
                "删除", "算了", true)) return;
        try
        {
            File.Delete(path);
            if (PathsEqual(path, _bubbleFrameSel)) { _bubbleStyleSel = "card"; _bubbleFrameSel = ""; }
            BuildBubbleStyleTiles();
            DrawBubblePreview();
            LblBubHint.Text = "外框已删除。";
        }
        catch (Exception ex) { LogErr("delbubble", ex); }
    }

    // ---------------- 通用设置：主题色 + 开机自启动 ----------------
    // 主题色：Accent 画刷会被样式封箱冻结（Style.Seal 会冻结 Setter 里的 Freezable），
    // 直接改 Color 会抛「只读状态」异常。正确做法：XAML 里全用 {DynamicResource Accent}，
    // 这里整体替换 Win.Resources["Accent"] 实例，所有引用自动跟着换。
    static void ApplyThemeColor(Color c)
    {
        try
        {
            _themeColor = c;
            var nb = new SolidColorBrush(c);
            if (Win.Resources.Contains("Accent")) Win.Resources["Accent"] = nb;
            else Win.Resources.Add("Accent", nb);
        }
        catch (Exception ex) { LogErr("theme", ex); }
    }

    static bool AutoStartEnabled()
    {
        try
        {
            using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
            {
                if (k != null)
                {
                    object v = k.GetValue(RunValueName);
                    if (v != null && !string.IsNullOrEmpty(v.ToString())) return true;
                }
            }
        }
        catch (Exception) { }
        // 旧的「启动文件夹」方式也算开机自启
        try
        {
            string dir = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            if (File.Exists(Path.Combine(dir, StartupScriptName + ".vbs"))) return true;
            if (File.Exists(Path.Combine(dir, StartupScriptName + ".vbs.disabled"))) return true;
        }
        catch (Exception) { }
        return false;
    }

    static void SetAutoStart(bool on)
    {
        try
        {
            string dir = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            string vbs = Path.Combine(dir, StartupScriptName + ".vbs");
            string dis = vbs + ".disabled";
            using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKeyPath))
            {
                if (k == null) return;
                if (on)
                {
                    k.SetValue(RunValueName, "\"" + Path.Combine(Here, PetExeName) + "\"",
                        Microsoft.Win32.RegistryValueKind.String);
                    // 启动文件夹里那份挪开，免得开机起来两个
                    if (File.Exists(vbs)) File.Move(vbs, dis);
                }
                else
                {
                    k.DeleteValue(RunValueName, false);
                }
            }
        }
        catch (Exception ex) { LogErr("autostart", ex); Dlg.Info("设置开机自启动失败", ex.Message, "好"); }
    }

    static void BuildGeneralPage()
    {
        if (UiContent == null) return;
        UiContent.Children.Clear();

        // ---- 桌面显示：总开关（关掉后 Pet.exe 根本不启动）----
        // 这个开关**切换即生效**：直接写盘 + 启停进程，所以不进「未保存」判定（见 Snapshot）。
        var cardShow = new Border { Style = (Style)Win.FindResource("Card") };
        var spShow = new StackPanel();
        spShow.Children.Add(Txt("桌面显示", 14, "#E4000000", true));
        spShow.Children.Add(Txt("关掉后她不在桌面出现", 12, "#99000000"));
        ChkShowPet = new CheckBox
        {
            Content = "在桌面显示桌宠", FontSize = 13, Margin = new Thickness(0, 10, 0, 0),
            Style = (Style)Win.FindResource("ToggleSwitch")
        };
        ChkShowPet.IsChecked = _showPetSel;      // 先设值再挂事件，避免重建页面时误触发
        ChkShowPet.Checked += (s, e) => SetShowPet(true);
        ChkShowPet.Unchecked += (s, e) => SetShowPet(false);
        spShow.Children.Add(ChkShowPet);
        var tipShow = Txt("切换立即生效", 11, "#99000000");
        tipShow.ToolTip = "关掉后桌宠进程会退出，不再占内存、也不统计按键。\n想再打开：双击 PetSettings.exe → 通用设置 → 桌面显示。";
        tipShow.Margin = new Thickness(0, 6, 0, 0);
        spShow.Children.Add(tipShow);
        cardShow.Child = spShow;
        UiContent.Children.Add(cardShow);

        var card1 = new Border { Style = (Style)Win.FindResource("Card") };
        var sp1 = new StackPanel();
        sp1.Children.Add(Txt("主题颜色", 14, "#E4000000", true));
        sp1.Children.Add(Txt("按钮、选中态等界面元素的主色", 12, "#99000000"));
        ThemeColorHost = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        sp1.Children.Add(ThemeColorHost);
        var pickRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
        var bCustom = new Button { Content = "自定义颜色…", Style = (Style)Win.FindResource("SecondaryButton") };
        bCustom.Click += (s, e) =>
        {
            Color picked;
            if (ColorDlg.Ask(_themeColor, out picked)) { ApplyThemeColor(picked); BuildGeneralPage(); }
        };
        pickRow.Children.Add(bCustom);
        var bReset = new Button { Content = "恢复默认", Style = (Style)Win.FindResource("SecondaryButton"), Margin = new Thickness(8, 0, 0, 0) };
        bReset.Click += (s, e) => { ApplyThemeColor(Color.FromRgb(0x00, 0x5F, 0xB8)); BuildGeneralPage(); };
        pickRow.Children.Add(bReset);
        sp1.Children.Add(pickRow);
        card1.Child = sp1;
        UiContent.Children.Add(card1);

        BuildColorTiles(ThemeColorHost, _themeColor, c =>
        {
            ApplyThemeColor(c);
            BuildGeneralPage();
        }, HexOf(_themeColor), _themeColor);

        var card2 = new Border { Style = (Style)Win.FindResource("Card") };
        var sp2 = new StackPanel();
        sp2.Children.Add(Txt("开机自启动", 14, "#E4000000", true));
        sp2.Children.Add(Txt("开机后自动出现在桌面", 12, "#99000000"));
        ChkAutoStart = new CheckBox
        {
            Content = "开机自动启动桌宠", FontSize = 13, Margin = new Thickness(0, 10, 0, 0),
            Style = (Style)Win.FindResource("ToggleSwitch")
        };
        ChkAutoStart.IsChecked = AutoStartEnabled();
        ChkAutoStart.Checked += (s, e) => SetAutoStart(true);
        ChkAutoStart.Unchecked += (s, e) => SetAutoStart(false);
        sp2.Children.Add(ChkAutoStart);
        var tipAuto = Txt("设置立即生效", 11, "#99000000");
        tipAuto.ToolTip = "写入当前用户的注册表启动项；原来放在「启动」文件夹里的旧方式会自动挪开，避免开机起两个。";
        tipAuto.Margin = new Thickness(0, 6, 0, 0);
        sp2.Children.Add(tipAuto);
        card2.Child = sp2;
        UiContent.Children.Add(card2);
    }

    // ---------------- 字号（Word 风格下拉） ----------------
    // Word 的字号档位（磅值），可选也可自己敲
    static readonly string[] FontSizePresets = {
        "8", "9", "10", "10.5", "11", "12", "14", "16", "18", "20", "22", "24", "26", "28", "36"
    };

    static double CurFontPt
    {
        get
        {
            string s = CmbFontSize != null ? (CmbFontSize.Text ?? "").Trim() : "";
            s = s.Replace("pt", "").Replace("PT", "").Trim();
            double v;
            if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) v = _bubbleFontPtSel;
            return Math.Max(4, Math.Min(72, v));
        }
    }

    static void SetupFontSize()
    {
        if (CmbFontSize == null) return;
        CmbFontSize.Items.Clear();
        foreach (string s in FontSizePresets) CmbFontSize.Items.Add(s);
        string cur = _bubbleFontPtSel.ToString("0.0", CultureInfo.InvariantCulture);
        if (cur.EndsWith(".0", StringComparison.Ordinal)) cur = cur.Substring(0, cur.Length - 2);
        CmbFontSize.Text = cur;
        CmbFontSize.SelectionChanged += (s, e) =>
        {
            if (CmbFontSize.SelectedIndex < 0) return;
            _bubbleFontPtSel = (float)CurFontPt;
            DrawBubblePreview();
        };
        CmbFontSize.LostFocus += (s, e) => { _bubbleFontPtSel = (float)CurFontPt; DrawBubblePreview(); };
        CmbFontSize.PreviewKeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter) { _bubbleFontPtSel = (float)CurFontPt; DrawBubblePreview(); }
        };
    }

    // 气泡关掉时把跟它有关的控件一起置灰（改了也看不到，别让人白调）
    static void SyncBubbleEnabled()
    {
        bool on = ChkBub != null && ChkBub.IsChecked == true;
        if (SldBub != null) SldBub.IsEnabled = on;
        if (CmbEvery != null) CmbEvery.IsEnabled = on;
        if (CmbFont != null) CmbFont.IsEnabled = on;
        if (CmbFontSize != null) CmbFontSize.IsEnabled = on;
        if (BtnBubbleColor != null) BtnBubbleColor.IsEnabled = on;
        if (BtnBubbleInk != null) BtnBubbleInk.IsEnabled = on;
        if (BtnUpBubble != null) BtnUpBubble.IsEnabled = on;
        if (BubbleColorHost != null) { BubbleColorHost.IsEnabled = on; BubbleColorHost.Opacity = on ? 1 : 0.45; }
        if (BubbleInkHost != null) { BubbleInkHost.IsEnabled = on; BubbleInkHost.Opacity = on ? 1 : 0.45; }
        if (BubbleStyleHost != null) { BubbleStyleHost.IsEnabled = on; BubbleStyleHost.Opacity = on ? 1 : 0.45; }
        if (LblBubHint != null) LblBubHint.Opacity = on ? 1 : 0.45;
        if (LblFontSize != null) LblFontSize.Opacity = on ? 1 : 0.45;
    }

    // ---------------- 未保存提醒 ----------------
    // 把界面上所有设置拍成一个字符串，跟"上次保存的样子"比一比就知道脏不脏
    static string Snapshot()
    {
        try
        {
            CharEntry ce = SelectedChar;
            var d = CollectOverrides(ce != null && !string.IsNullOrEmpty(ce.Dir) ? RelToHere(ce.Dir) : CurCharDir);
            var sb = new StringBuilder();
            foreach (KeyValuePair<string, string> kv in d)
            {
                // showPet 是「切换即生效」的开关（SetShowPet 立刻写盘），不算未保存的改动
                if (kv.Key == "showPet") continue;
                sb.Append(kv.Key).Append('=').Append(kv.Value).Append(';');
            }
            return sb.ToString();
        }
        catch (Exception)
        {
            // 取不到（比如某个下拉还没选）就当作"没变"，宁可漏报也别一直误报"未保存"
            return _savedSnapshot;
        }
    }

    static void StartDirtyWatch()
    {
        _savedSnapshot = Snapshot();
        if (LblDirty == null) return;
        _dirtyTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _dirtyTimer.Tick += (s, e) =>
        {
            try
            {
                bool dirty = Snapshot() != _savedSnapshot;
                LblDirty.Visibility = dirty ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception) { }
        };
        _dirtyTimer.Start();
    }

    static void MarkSaved() { _savedSnapshot = Snapshot(); }

    static void DrawBubblePreview()
    {
        try
        {
            int bh = (ChkBub != null && ChkBub.IsChecked == true) ? (int)SldBub.Value : 0;
            BitmapSource pet = PetFrameShot(6);
            int pw = pet != null ? pet.PixelWidth : 0, ph = pet != null ? pet.PixelHeight : 0;
            int bw = 234;                                   // 桌宠气泡区的实际宽度（按角色宽度）
            int w = Math.Max(bw, pw);
            int h = (bh > 0 ? bh : 0) + (ph > 0 ? 10 + ph : 0);
            if (h < 60) h = 60;

            var dv = new DrawingVisual();
            using (DrawingContext dc = dv.RenderOpen())
            {
                dc.DrawRectangle(C("#FAFBFD"), null, new Rect(0, 0, w, h));
                if (bh > 0)
                {
                    Rect tr; Brush inkB;
                    dc.PushTransform(new TranslateTransform((w - bw) / 2.0, 0));
                    DrawBubbleWpf(dc, _bubbleStyleSel, _bubbleFrameSel, bw, bh, out tr, out inkB);
                    // 文字颜色：用户指定了就用指定的，否则跟着外框自动
                    if (!string.IsNullOrEmpty(_bubbleInkSel) && _bubbleInkSel != "auto")
                        inkB = new SolidColorBrush(WpfParseHex(_bubbleInkSel, Colors.Black));
                    string fn = CmbFont.SelectedIndex >= 0 ? CmbFont.Items[CmbFont.SelectedIndex].ToString() : CurFont;
                    Typeface tf = new Typeface(new FontFamily(fn), FontStyles.Normal,
                        FontWeights.Normal, FontStretches.Normal);
                    double pt = CurFontPt;
                    FormattedText ft = new FormattedText("你好呀", CultureInfo.CurrentUICulture,
                        FlowDirection.LeftToRight, tf, pt, inkB, 1.0);
                    ft.MaxTextWidth = Math.Max(30, tr.Width);
                    ft.MaxTextHeight = Math.Max(12, tr.Height);
                    ft.TextAlignment = TextAlignment.Center;
                    ft.Trimming = TextTrimming.CharacterEllipsis;
                    dc.DrawText(ft, new Point(tr.Left, tr.Top + Math.Max(0, (tr.Height - ft.Height) / 2.0)));
                    dc.Pop();
                }
                if (pet != null)
                {
                    dc.DrawImage(pet, new Rect((w - pw) / 2.0, (bh > 0 ? bh + 10 : 0), pw, ph));
                    // 角色脚底画一条基准线，标明"气泡是从她头顶上长出来的"
                    Pen line = new Pen(C("#C9D2E0"), 1) { DashStyle = DashStyles.Dash };
                    double baseY = (bh > 0 ? bh + 10 : 0) + ph - 1;
                    dc.DrawLine(line, new Point(4, baseY), new Point(w - 4, baseY));
                }
            }
            var rtb = new RenderTargetBitmap(Math.Max(2, w), Math.Max(2, h), 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            if (ImgBub != null) ImgBub.Source = rtb;
            if (LblBubSize != null)
                LblBubSize.Text = ph > 0
                    ? "气泡 " + (bh > 0 ? bh + " px" : "已关闭") + " ＋ 角色 " + ph + " px（上下各留 10 px 间距）"
                    : "气泡 " + (bh > 0 ? bh + " px" : "已关闭");
        }
        catch (Exception ex) { LogErr("bubprev", ex); }
    }

    // ---------------- 语音库 / 试听 ----------------
    static string _previewPath = "";
    static readonly Random _rndUI = new Random();

    static void ReloadSfxGroups()
    {
        if (CmbSet == null) return;
        string cur = CurOf("sfxGroup", "auto");
        CmbSet.Items.Clear();
        CmbSet.Items.Add("跟随角色（推荐）");
        int sel = 0;
        foreach (CharEntry e in ChList)
        {
            if (string.IsNullOrEmpty(e.Dir)) continue;
            CmbSet.Items.Add(e.Name);
            if (string.Equals(cur, e.Name, StringComparison.CurrentCulture)) sel = CmbSet.Items.Count - 1;
        }
        CmbSet.SelectedIndex = sel;
    }

    static void LoadVoiceLibrary()
    {
        VoiceLines.Clear();
        try
        {
            CharEntry ce = SelectedChar;
            if (ce != null && !string.IsNullOrEmpty(ce.Voices) && File.Exists(ce.Voices))
            {
                string bd = string.IsNullOrEmpty(ce.Dir) ? Here : ce.Dir;
                foreach (string ln in File.ReadAllLines(ce.Voices))
                {
                    int tab = ln.IndexOf('\t');
                    if (tab <= 0) continue;
                    string text = ln.Substring(0, tab).Trim();
                    string p = ln.Substring(tab + 1).Trim();
                    if (p != "" && !Path.IsPathRooted(p)) p = Path.Combine(bd, p);
                    if (text != "" && File.Exists(p)) VoiceLines.Add(new VoiceLine { Text = text, Path = p });
                }
            }
        }
        catch (Exception ex) { LogErr("voicelib", ex); }
        if (LblVoiceCount != null) LblVoiceCount.Text = "共 " + VoiceLines.Count + " 条";
        RefreshVoiceList();
    }

    static Border _voiceRowPlaying;

    // 台词库列表：一行一张"卡片"，行与行之间有明确间隔；整行可点即试听
    static void RefreshVoiceList()
    {
        if (VoiceListPanel == null) return;
        VoiceListPanel.Children.Clear();
        _voiceRowPlaying = null;
        string q = TxtVoiceSearch != null ? TxtVoiceSearch.Text.Trim() : "";
        int shown = 0;
        foreach (VoiceLine v in VoiceLines)
        {
            if (q != "" && v.Text.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0) continue;
            shown++;
            VoiceLine vv = v;
            var row = new Border
            {
                Background = Brushes.White,
                BorderBrush = C("#E7EAF0"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 0, 0, 6),
                Padding = new Thickness(12, 8, 8, 8),
                Cursor = Cursors.Hand
            };
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var no = new TextBlock
            {
                Text = shown.ToString("00"), FontFamily = new FontFamily("Consolas"), FontSize = 11,
                Foreground = C("#8A93A3"), VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            g.Children.Add(no);

            var t = Txt(v.Text, 13, "#1A1A1A");
            t.TextTrimming = TextTrimming.CharacterEllipsis;
            t.VerticalAlignment = VerticalAlignment.Center;
            t.Margin = new Thickness(2, 0, 12, 0);
            t.ToolTip = v.Text;
            Grid.SetColumn(t, 1); g.Children.Add(t);

            var play = new Border
            {
                Width = 30, Height = 30, CornerRadius = new CornerRadius(15),
                Background = C("#F1F5FC"), VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = "\uE768", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 12,
                    Foreground = C("#2563AB"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(1, 0, 0, 0)
                }
            };
            Grid.SetColumn(play, 2); g.Children.Add(play);
            row.Child = g;

            Action act = () =>
            {
                PlayVoiceFile(vv.Path);
                LblVoiceHint.Text = "正在试听：" + vv.Text;
                if (_voiceRowPlaying != null && _voiceRowPlaying != row)
                {
                    _voiceRowPlaying.Background = Brushes.White;
                    _voiceRowPlaying.BorderBrush = C("#E7EAF0");
                }
                _voiceRowPlaying = row;
                row.Background = C("#F4F9FF");
                row.BorderBrush = C("#9CC2F0");
            };

            row.MouseLeftButtonUp += (s, e) => { act(); e.Handled = true; };
            row.MouseEnter += (s, e) =>
            {
                if (row == _voiceRowPlaying) return;
                row.Background = C("#F8FBFF");
                row.BorderBrush = C("#CFE0F7");
            };
            row.MouseLeave += (s, e) =>
            {
                if (row == _voiceRowPlaying) return;
                row.Background = Brushes.White;
                row.BorderBrush = C("#E7EAF0");
            };
            VoiceListPanel.Children.Add(row);
            if (shown >= 400) break;
        }
        if (LblVoiceCount != null)
            LblVoiceCount.Text = q == ""
                ? "共 " + VoiceLines.Count + " 条"
                : "匹配 " + shown + " / " + VoiceLines.Count + " 条";
        if (shown == 0)
            VoiceListPanel.Children.Add(Txt(VoiceLines.Count == 0
                ? "当前角色还没有语音（建角色时带上语音文件夹即可）" : "没有匹配的台词", 12, "#99000000"));
    }

    static void PlayVoiceFile(string path)
    {
        try
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            if (PreviewPlayer == null) PreviewPlayer = new MediaPlayer();
            PreviewPlayer.Close();
            PreviewPlayer.Open(new Uri(path));
            PreviewPlayer.Volume = SldVol.Value / 100.0;
            PreviewPlayer.Play();
        }
        catch (Exception ex) { LogErr("playvoice", ex); }
    }

    static void PlayRandomLine()
    {
        if (VoiceLines.Count == 0) return;
        VoiceLine v = VoiceLines[_rndUI.Next(VoiceLines.Count)];
        PlayVoiceFile(v.Path);
        LblVoiceHint.Text = "随机来一句：" + v.Text;
    }

    // 按压/松开音可以来自「跟随角色」或指定音效组（另一个角色包）
    static string FirstVoiceLine()
    {
        if (VoiceLines.Count > 0) return VoiceLines[0].Path;
        return "";
    }

    // 试听：从**当前角色的语音库**里随机抽一条来播（和桌宠自己点出来的同源）
    static void PreviewVoice()
    {
        try
        {
            if (VoiceLines.Count == 0) LoadVoiceLibrary();
            if (VoiceLines.Count == 0)
            {
                LblVoiceHint.Text = "当前角色没有语音可试听";
                return;
            }
            VoiceLine v = VoiceLines[_rndUI.Next(VoiceLines.Count)];
            _previewPath = v.Path;
            PlayVoiceFile(v.Path);
            LblVoiceHint.Text = "正在试听：" + v.Text;
        }
        catch (Exception ex) { LogErr("preview", ex); }
    }

    // ---------------- 保存 ----------------
    static string FindPython()
    {
        string local = Path.Combine(Here, @"tools\python\python.exe");
        if (File.Exists(local)) return local;
        string dsh = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            @".dsh\dsh-runtimes\dsh-primary-runtime\dependencies\python\python.exe");
        if (File.Exists(dsh)) return dsh;
        return "python.exe";
    }

    // 把界面上的全部设置收成 key=value（写入 pet-overrides.txt，同时用来补丁 pet-config.txt）
    static Dictionary<string, string> CollectOverrides(string charDir)
    {
        var d = new Dictionary<string, string>();
        d["charDir"] = charDir;
        d["showPet"] = _showPetSel ? "1" : "0";
        d["soundSet"] = "furina";
        d["clickVoice"] = ChkClick.IsChecked == true ? "1" : "0";
        d["bubbleH"] = (ChkBub.IsChecked == true ? (int)SldBub.Value : 0).ToString(CultureInfo.InvariantCulture);
        d["bubbleEveryMs"] = EveryVals[CmbEvery.SelectedIndex].ToString(CultureInfo.InvariantCulture);
        d["bubbleFont"] = CmbFont.Items[CmbFont.SelectedIndex].ToString();
        d["volume"] = ((int)SldVol.Value).ToString(CultureInfo.InvariantCulture);
        d["sfxGroup"] = (CmbSet != null && CmbSet.SelectedIndex > 0)
            ? CmbSet.Items[CmbSet.SelectedIndex].ToString() : "auto";
        d["clickLineMode"] = (CmbClickMode != null && CmbClickMode.SelectedIndex == 1) ? "seq" : "random";
        d["bubbleColor"] = HexOf(_bubbleColorSel);
        d["bubbleInk"] = string.IsNullOrEmpty(_bubbleInkSel) ? "auto" : _bubbleInkSel;
        d["themeColor"] = HexOf(_themeColor);
        d["bubbleFontSize"] = CurFontPt.ToString("0.0", CultureInfo.InvariantCulture);
        d["bubbleStyle"] = string.IsNullOrEmpty(_bubbleStyleSel) ? "card" : _bubbleStyleSel;
        d["bubbleFrame"] = (_bubbleStyleSel == "custom") ? RelBubbleFrame(_bubbleFrameSel) : "";
        foreach (AnimCard c in AnimCards)
        {
            d["anim" + Cap(c.Key)] = (c.Toggle != null && c.Toggle.IsChecked == true) ? "1" : "0";
            d["speed" + Cap(c.Key)] = ((int)c.Speed.Value).ToString(CultureInfo.InvariantCulture);
        }
        return d;
    }

    static void WriteOverrides(Dictionary<string, string> d)
    {
        var lines = new List<string>();
        foreach (KeyValuePair<string, string> kv in d) lines.Add(kv.Key + "=" + kv.Value);
        File.WriteAllLines(Path.Combine(Here, "pet-overrides.txt"), lines.ToArray(), new UTF8Encoding(false));
    }

    // ---------------- 角色包读写 ----------------
    static Dictionary<string, string> ReadManifest(string dir)
    {
        var m = new Dictionary<string, string>();
        string p = Path.Combine(dir, "char.json");
        if (!File.Exists(p)) return m;
        foreach (string raw in File.ReadAllLines(p, Encoding.UTF8))
        {
            string line = raw.Trim();
            if (line == "" || line.StartsWith("#")) continue;
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            m[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
        }
        return m;
    }

    static void WriteManifest(string dir, Dictionary<string, string> m)
    {
        string[] order = { "name", "image", "frames", "meta", "lines", "voicelines", "press", "release", "charH", "voices", "created" };
        var lines = new List<string>();
        foreach (string k in order) if (m.ContainsKey(k)) lines.Add(k + "=" + m[k]);
        foreach (KeyValuePair<string, string> kv in m) if (Array.IndexOf(order, kv.Key) < 0) lines.Add(kv.Key + "=" + kv.Value);
        File.WriteAllLines(Path.Combine(dir, "char.json"), lines.ToArray(), new UTF8Encoding(false));
    }

    static string ResolvePack(string dir, Dictionary<string, string> m, string key)
    {
        string v;
        if (!m.TryGetValue(key, out v) || v == "") return "";
        return Path.IsPathRooted(v) ? v : Path.Combine(dir, v);
    }

    static double ReadAmp(Dictionary<string, string> m, string key)
    {
        string s;
        double v;
        if (m.TryGetValue(key, out s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v))
            return Math.Max(0.2, Math.Min(2.0, v));
        return 1.0;
    }

    static string RelToHere(string p)
    {
        string h = Here.TrimEnd('\\') + "\\";
        return p.StartsWith(h, StringComparison.OrdinalIgnoreCase) ? p.Substring(h.Length) : p;
    }

    static string FindPackImage(string dir)
    {
        foreach (string sub in new string[] { "图片", "" })
            foreach (string ext in new string[] { ".png", ".jpg", ".jpeg", ".webp", ".bmp" })
            {
                string p = Path.Combine(dir, sub, "image" + ext);
                if (File.Exists(p)) return p;
            }
        return "";
    }

    // 只重出这个角色自己的动画（写到 chars\<角色>\动画\），不再动全局帧
    static bool BuildPackFrames(CharEntry e, int size)
    {
        try
        {
            string animDir = Path.Combine(e.Dir, "动画");
            Directory.CreateDirectory(animDir);
            DateTime started = DateTime.Now;
            var psi = new ProcessStartInfo(FindPython(),
                "sprites.py --src \"" + e.Image + "\" --char-h " + size + " --out-dir \"" + animDir + "\""
                + " --amp-idle " + e.AmpIdle.ToString("0.00", CultureInfo.InvariantCulture)
                + " --amp-press " + e.AmpPress.ToString("0.00", CultureInfo.InvariantCulture)
                + " --amp-click " + e.AmpClick.ToString("0.00", CultureInfo.InvariantCulture)
                + " --amp-drag " + e.AmpDrag.ToString("0.00", CultureInfo.InvariantCulture))
            {
                WorkingDirectory = Here, UseShellExecute = false, CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            Process pr = Process.Start(psi);
            pr.WaitForExit(180000);
            string frames = Path.Combine(animDir, "frames.bin"), meta = Path.Combine(animDir, "frames.txt");
            // 必须确认是本次写出来的：生成失败时旧文件还在，只看存在会误判成功
            if (!File.Exists(frames) || !File.Exists(meta)) return false;
            if (File.GetLastWriteTime(frames) < started.AddSeconds(-2)) return false;
            e.Frames = frames; e.Meta = meta; e.CharH = size;

            Dictionary<string, string> m = ReadManifest(e.Dir);
            m["name"] = e.Name;
            m["image"] = "图片/" + Path.GetFileName(e.Image);
            m["frames"] = "动画/frames.bin";
            m["meta"] = "动画/frames.txt";
            if (!m.ContainsKey("lines")) m["lines"] = "台词.txt";
            if (!m.ContainsKey("voicelines")) m["voicelines"] = "音效/voices.tsv";
            m["charH"] = size.ToString(CultureInfo.InvariantCulture);
            m["ampIdle"] = e.AmpIdle.ToString("0.00", CultureInfo.InvariantCulture);
            m["ampPress"] = e.AmpPress.ToString("0.00", CultureInfo.InvariantCulture);
            m["ampClick"] = e.AmpClick.ToString("0.00", CultureInfo.InvariantCulture);
            m["ampDrag"] = e.AmpDrag.ToString("0.00", CultureInfo.InvariantCulture);
            WriteManifest(e.Dir, m);
            return true;
        }
        catch (Exception ex) { LogErr("frames", ex); return false; }
    }

    static void Save()
    {
        try
        {
            BtnSave.IsEnabled = false; BtnSave.Content = "应用中…";
            CharEntry ce = SelectedChar;
            if (ce == null || string.IsNullOrEmpty(ce.Dir))
            {
                BtnSave.IsEnabled = true; BtnSave.Content = "保存更改";
                Dlg.Info("没有可选角色", "chars 目录里还没有角色包。\n先去「导入」页新建一个角色吧。", "好");
                return;
            }
            int size = (int)SldSize.Value;

            // 幅度滑块 → 角色包里的动画参数（只有真的改了才重新生成）
            // 只有动画页上展示的确实是当前角色时，才把幅度滑块同步进角色包
            bool ampChanged = false;
            if (_animCardsDir == ce.Dir)
            {
                foreach (AnimCard c in AnimCards)
                {
                    double v = Math.Max(0.2, Math.Min(2.0, c.Amp.Value / 100.0));
                    if (Math.Abs(v - EntryAmp(ce, c.Key)) > 0.001) { SetEntryAmp(ce, c.Key, v); ampChanged = true; }
                }
            }

            // 大小 / 幅度变了（或动画缺失）→ 只重出**这个角色**的动画
            if (size != ce.CharH || ampChanged || !ce.HasFrames)
            {
                if (!BuildPackFrames(ce, size))
                {
                    BtnSave.IsEnabled = true; BtnSave.Content = "保存更改";
                    Dlg.Info("动画生成失败",
                        "没能生成「" + ce.Name + "」的动画帧。\n请确认 Python + Pillow 可用，或运行 charpack.py rebuild 重建。", "好");
                    return;
                }
            }

            Dictionary<string, string> ov = CollectOverrides(RelToHere(ce.Dir));
            WriteOverrides(ov);
            PatchConfig(ov);
            MarkSaved();                       // 保存后清掉「未保存」提示，关窗不再拦
            RestartPet();
            Dlg.Info("已保存并应用",
                "角色 " + ce.Name + "　·　" + size + "px　·　音量 " + ov["volume"] + "%\n"
                + (_showPetSel ? "桌宠正在重启，稍等一下就能看到效果。"
                               : "桌宠现在是关闭状态（通用设置 → 桌面显示），打开后才会出现。"), "知道了");
            Win.Close();
        }
        catch (Exception ex)
        {
            BtnSave.IsEnabled = true; BtnSave.Content = "保存更改";
            LogErr("save", ex);
            Dlg.Info("保存失败", ex.Message, "好");
        }
    }

    // 把设置补丁进 pet-config.txt：已有的键就地改，缺的键追加（老配置文件也能平滑升级）
    static void PatchConfig(Dictionary<string, string> kv)
    {
        string p = Path.Combine(Here, "pet-config.txt");
        var outLines = File.Exists(p) ? new List<string>(File.ReadAllLines(p, Encoding.UTF8)) : new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < outLines.Count; i++)
        {
            string line = outLines[i];
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            string k = line.Substring(0, eq).Trim();
            string v;
            if (kv.TryGetValue(k, out v) && !seen.Contains(k)) { outLines[i] = k + "=" + v; seen.Add(k); }
        }
        foreach (KeyValuePair<string, string> e in kv)
            if (!seen.Contains(e.Key)) outLines.Add(e.Key + "=" + e.Value);
        File.WriteAllLines(p, outLines.ToArray(), new UTF8Encoding(false));
    }

    // 只把指定的几个键补进 pet-overrides.txt（不动其它行；没有就追加）。
    // 给「切换即生效」的开关用：不能走 CollectOverrides→WriteOverrides，
    // 否则会把界面上其它还没保存的改动一起写盘。
    static void PatchOverrides(Dictionary<string, string> kv)
    {
        string p = Path.Combine(Here, "pet-overrides.txt");
        var outLines = File.Exists(p) ? new List<string>(File.ReadAllLines(p, Encoding.UTF8)) : new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < outLines.Count; i++)
        {
            string line = outLines[i];
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            string k = line.Substring(0, eq).Trim();
            string v;
            if (kv.TryGetValue(k, out v) && !seen.Contains(k)) { outLines[i] = k + "=" + v; seen.Add(k); }
        }
        foreach (KeyValuePair<string, string> e in kv)
            if (!seen.Contains(e.Key)) outLines.Add(e.Key + "=" + e.Value);
        File.WriteAllLines(p, outLines.ToArray(), new UTF8Encoding(false));
    }

    // 桌面显示总开关：写盘 + 立即启停桌宠（不等「保存更改」，因为这个开关不涉及角色/动画）
    static void SetShowPet(bool on)
    {
        _showPetSel = on;
        try
        {
            var one = new Dictionary<string, string>();
            one["showPet"] = on ? "1" : "0";
            PatchOverrides(one);
            PatchConfig(one);
            if (on) RestartPet(); else StopPet();
            if (LblDirty != null) LblDirty.Visibility = Snapshot() != _savedSnapshot ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex) { LogErr("showpet", ex); }
    }

    // 只停不启（关闭桌宠 / 重启前的第一步）
    static void StopPet()
    {
        try
        {
            string pidf = Path.Combine(Here, "pet.pid");
            if (File.Exists(pidf))
            {
                int pid;
                if (int.TryParse(File.ReadAllText(pidf).Trim(), out pid))
                {
                    try { Process.GetProcessById(pid).Kill(); } catch (Exception) { }
                }
            }
            foreach (var pr in Process.GetProcessesByName("Pet"))
            {
                try { pr.Kill(); } catch (Exception) { }
            }
        }
        catch (Exception ex) { LogErr("stoppet", ex); }
    }

    static void RestartPet()
    {
        try
        {
            StopPet();
            System.Threading.Thread.Sleep(600);
            if (!_showPetSel) return;          // 桌宠已被关掉：只停不启，否则又冒出来了
            string exe = Path.Combine(Here, PetExeName);
            if (File.Exists(exe)) Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = Here });
        }
        catch (Exception ex) { LogErr("restart", ex); }
    }

    static void QuitPet()
    {
        if (!Dlg.Ask("让她先休息一下？",
            "退出后她会从桌面消失，按键统计和语音也会一起停下。\n下次双击 Pet.exe，她就又回来了。",
            "退出桌宠", "再陪一会儿", true)) return;
        StopPet();
        Win.Close();
    }

    // ---------------- 导入页（角色包：图像 + 语音 + 名字） ----------------
    static StackPanel PageImport, ImportList;
    static TextBox TxtName;
    static TextBlock LblImg, LblVoices, LblImportMsg;
    static Button BtnPickImg, BtnPickVoices, BtnImport;
    static string _importImg = "", _importVoiceDir = "";

    static string CharsDir { get { return Path.Combine(Here, "chars"); } }
    static string SanitizeName(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var sb = new StringBuilder();
        foreach (char c in s.Trim())
        {
            if ("\\/:*?\"<>|".IndexOf(c) >= 0) continue;
            sb.Append(c);
        }
        string r = sb.ToString().Trim().TrimEnd('.');
        return r.Length > 40 ? r.Substring(0, 40) : r;
    }
    static string FindFfmpeg()
    {
        string local = Path.Combine(Here, @"tools\ffmpeg.exe");
        if (File.Exists(local)) return local;
        string pf = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (string dir in pf.Split(';'))
        {
            if (dir.Trim() == "") continue;
            try { string c = Path.Combine(dir.Trim(), "ffmpeg.exe"); if (File.Exists(c)) return c; } catch (Exception) { }
        }
        string known = @"D:\f\ffmpeg-7.1-essentials_build\bin\ffmpeg.exe";
        if (File.Exists(known)) return known;
        return "";
    }
    static bool ConvertToWav(string src, string dst)
    {
        try
        {
            string ff = FindFfmpeg();
            if (ff == "") return false;
            var psi = new ProcessStartInfo(ff, "-y -v error -i \"" + src + "\" -ar 48000 -ac 1 -c:a pcm_s16le \"" + dst + "\"")
            { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            var pr = Process.Start(psi); pr.WaitForExit(60000);
            return File.Exists(dst);
        }
        catch (Exception) { return false; }
    }

    static void PickImportImage()
    {
        var d = new Microsoft.Win32.OpenFileDialog();
        d.Filter = "图片 (*.png;*.jpg;*.webp)|*.png;*.jpg;*.jpeg;*.webp|所有文件|*.*";
        if (d.ShowDialog() != true) return;
        _importImg = d.FileName;
        LblImg.Text = "已选择：" + Path.GetFileName(_importImg);
    }
    static void PickImportVoices()
    {
        // WPF 没有目录选择器：用 OpenFileDialog 的“假文件名”取所在目录
        var d = new Microsoft.Win32.OpenFileDialog
        {
            ValidateNames = false, CheckFileExists = false, CheckPathExists = true,
            FileName = "选择此文件夹"
        };
        if (d.ShowDialog() != true) return;
        string dir = Path.GetDirectoryName(d.FileName);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
        _importVoiceDir = dir;
        int n = 0;
        foreach (string f in Directory.GetFiles(dir))
        {
            string e = Path.GetExtension(f).ToLowerInvariant();
            if (e == ".wav" || e == ".mp3" || e == ".m4a" || e == ".ogg") n++;
        }
        LblVoices.Text = "已选择：" + dir + "（" + n + " 个音频）";
    }

    static string SafeFile(string s)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s.Trim();
    }

    // 「新建角色」= 新建 chars\<名字>\，把用户给的图片 / 语音放进 图片 / 音效，
    // 生成 动画，再写一份相对路径的 char.json。以后加角色都走这里。
    static void DoImport()
    {
        try
        {
            string name = SanitizeName(TxtName.Text);
            if (name == "") { LblImportMsg.Text = "请先填写角色名称。"; return; }
            if (_importImg == "") { LblImportMsg.Text = "请选择一张角色图片（每个角色包都需要）。"; return; }

            string dir = Path.Combine(CharsDir, name);
            string imgDir = Path.Combine(dir, "图片");
            string animDir = Path.Combine(dir, "动画");
            string vdir = Path.Combine(dir, "音效", "voices");
            Directory.CreateDirectory(imgDir);
            Directory.CreateDirectory(animDir);
            Directory.CreateDirectory(vdir);

            string img = Path.Combine(imgDir, "image" + Path.GetExtension(_importImg).ToLowerInvariant());
            File.Copy(_importImg, img, true);

            var rows = new List<KeyValuePair<string, string>>();
            int ok = 0, skipped = 0;
            if (_importVoiceDir != "")
            {
                var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string f in Directory.GetFiles(_importVoiceDir))
                {
                    string e = Path.GetExtension(f).ToLowerInvariant();
                    if (e != ".wav" && e != ".mp3" && e != ".m4a" && e != ".ogg") continue;
                    string text = Path.GetFileNameWithoutExtension(f).Trim();
                    if (text == "") continue;
                    string stem = SafeFile(text);
                    string uniq = stem;
                    for (int k = 2; used.Contains(uniq); k++) uniq = stem + "_" + k;
                    used.Add(uniq);
                    string dst = Path.Combine(vdir, uniq + ".wav");
                    bool good;
                    if (e == ".wav") { try { File.Copy(f, dst, true); good = true; } catch (Exception) { good = false; } }
                    else good = ConvertToWav(f, dst);
                    if (good && File.Exists(dst))
                    {
                        rows.Add(new KeyValuePair<string, string>(text, "音效/voices/" + uniq + ".wav"));
                        ok++;
                    }
                    else skipped++;
                }
            }
            using (var w = new StreamWriter(Path.Combine(dir, "音效", "voices.tsv"), false, new UTF8Encoding(false)))
                foreach (var r in rows) w.Write(r.Key + "\t" + r.Value + "\r\n");
            using (var w = new StreamWriter(Path.Combine(dir, "台词.txt"), false, new UTF8Encoding(false)))
                foreach (var r in rows) w.Write(r.Key + "\r\n");

            int size = (int)SldSize.Value;
            var m = new Dictionary<string, string>();
            m["name"] = name;
            m["image"] = "图片/" + Path.GetFileName(img);
            m["frames"] = "动画/frames.bin";
            m["meta"] = "动画/frames.txt";
            m["lines"] = "台词.txt";
            m["voicelines"] = "音效/voices.tsv";
            if (rows.Count > 0) m["press"] = rows[0].Value;
            if (rows.Count > 1) m["release"] = rows[1].Value;
            m["charH"] = size.ToString(CultureInfo.InvariantCulture);
            m["voices"] = rows.Count.ToString(CultureInfo.InvariantCulture);
            WriteManifest(dir, m);

            var entry = new CharEntry
            {
                Name = name, Dir = dir, Image = img, Voices = Path.Combine(dir, "音效", "voices.tsv"),
                Lines = Path.Combine(dir, "台词.txt"), Frames = Path.Combine(animDir, "frames.bin"),
                Meta = Path.Combine(animDir, "frames.txt"), CharH = size, VoiceCount = rows.Count
            };
            LblImportMsg.Text = "正在生成动画…";
            if (!BuildPackFrames(entry, size)) { LblImportMsg.Text = "动画生成失败：确认 Python/Pillow 可用。"; return; }

            ApplyPack(entry);
            LblImportMsg.Text = "已创建并应用：" + name + "（语音 " + ok + " 条"
                + (skipped > 0 ? "，跳过 " + skipped + " 条（需要 ffmpeg）" : "") + "）";
            TxtName.Text = ""; _importImg = ""; _importVoiceDir = "";
            LblImg.Text = "未选择"; LblVoices.Text = "未选择";
            ReloadChars();
            RefreshImport();
        }
        catch (Exception ex)
        {
            LogErr("import", ex);
            LblImportMsg.Text = "导入失败：" + ex.Message;
        }
    }

    // 应用某个角色包：只写 charDir，帧 / 台词 / 语音都由角色自己的 char.json 决定
    static void ApplyPack(CharEntry e)
    {
        if (e == null || string.IsNullOrEmpty(e.Dir)) return;
        Dictionary<string, string> ov = CollectOverrides(RelToHere(e.Dir));
        WriteOverrides(ov);
        PatchConfig(ov);
        RestartPet();
    }

    static bool IsCurrent(CharEntry e)
    {
        string cur = CurCharDir;
        if (string.IsNullOrEmpty(cur) || e == null || e.Dir == "") return false;
        string c = Path.IsPathRooted(cur) ? cur : Path.Combine(Here, cur);
        try
        {
            return string.Equals(Path.GetFullPath(c).TrimEnd('\\'), Path.GetFullPath(e.Dir).TrimEnd('\\'),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception) { return false; }
    }

    static void DeletePack(CharEntry e)
    {
        if (e == null) return;
        if (!Dlg.Ask("删除角色「" + e.Name + "」？",
            "整个文件夹都会被删掉（图片 / 动画 / 音效），无法恢复。", "删除", "算了", true)) return;
        bool wasCurrent = IsCurrent(e);
        try { Directory.Delete(e.Dir, true); } catch (Exception ex) { LogErr("delpack", ex); }
        ReloadChars();
        if (wasCurrent && ChList.Count > 0)
        {
            CmbChar.SelectedIndex = 0;
            ApplyPack(ChList[0]);
            LblImportMsg.Text = "已删除，切到「" + ChList[0].Name + "」。";
        }
        RefreshImport();
    }

    // 角色候选：chars\* 里所有带 char.json 的文件夹（一个角色一个文件夹）
    static void ReloadChars()
    {
        string cur = CurCharDir;
        string curFull = "";
        if (!string.IsNullOrEmpty(cur))
        {
            string c = Path.IsPathRooted(cur) ? cur : Path.Combine(Here, cur);
            try { curFull = Path.GetFullPath(c); } catch (Exception) { }
        }
        ChList.Clear();
        try
        {
            if (Directory.Exists(CharsDir))
            {
                foreach (string d in Directory.GetDirectories(CharsDir))
                {
                    if (!File.Exists(Path.Combine(d, "char.json"))) continue;
                    Dictionary<string, string> m = ReadManifest(d);
                    string nm = m.ContainsKey("name") && m["name"] != "" ? m["name"] : Path.GetFileName(d);
                    string img = FindPackImage(d);
                    if (img == "") img = Path.Combine(Here, "pet-placeholder.png");
                    int h, vc;
                    if (!int.TryParse(m.ContainsKey("charH") ? m["charH"] : "", out h) || h <= 0) h = 219;
                    if (!int.TryParse(m.ContainsKey("voices") ? m["voices"] : "", out vc)) vc = 0;
                    ChList.Add(new CharEntry
                    {
                        Name = nm, Dir = d, Image = img,
                        Voices = ResolvePack(d, m, "voicelines"),
                        Lines = ResolvePack(d, m, "lines"),
                        Frames = ResolvePack(d, m, "frames"),
                        Meta = ResolvePack(d, m, "meta"),
                        CharH = h, VoiceCount = vc,
                        AmpIdle = ReadAmp(m, "ampIdle"), AmpPress = ReadAmp(m, "ampPress"),
                        AmpClick = ReadAmp(m, "ampClick"), AmpDrag = ReadAmp(m, "ampDrag")
                    });
                }
            }
        }
        catch (Exception) { }
        ChList.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCulture));

        if (ChList.Count == 0)
        {
            // 一个角色包都没有：别让面板空掉，给一个占位项（不能保存）
            ChList.Add(new CharEntry { Name = "（还没有角色包）", Dir = "", Image = Path.Combine(Here, "pet-placeholder.png") });
        }

        CmbChar.Items.Clear();
        int sel = 0;
        for (int i = 0; i < ChList.Count; i++)
        {
            CmbChar.Items.Add(ChList[i].Name);
            if (curFull != "" && ChList[i].Dir != "")
            {
                try
                {
                    if (string.Equals(Path.GetFullPath(ChList[i].Dir), curFull, StringComparison.OrdinalIgnoreCase)) sel = i;
                }
                catch (Exception) { }
            }
        }
        if (sel >= 0 && sel < CmbChar.Items.Count) CmbChar.SelectedIndex = sel;
        ShowCharPreview(); ShowSizePreview();
    }

    static void RefreshImport()
    {
        if (ImportList == null) return;
        ImportList.Children.Clear();
        ImportList.Children.Add(Txt("一个角色 = 一个文件夹", 12, "#99000000"));
        ImportList.Children.Add(new Border { Height = 8 });

        foreach (CharEntry e in ChList)
        {
            if (string.IsNullOrEmpty(e.Dir)) continue;
            var row = new Grid { Margin = new Thickness(0, 5, 0, 5) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var tb = Txt(e.Name, 13, "#E4000000", true);
            tb.VerticalAlignment = VerticalAlignment.Center;
            tb.TextTrimming = TextTrimming.CharacterEllipsis;
            Grid.SetColumn(tb, 0); row.Children.Add(tb);

            string info = "语音 " + e.VoiceCount + " · " + e.CharH + "px" + (e.HasFrames ? "" : " · ⚠ 缺动画");
            var tc = Txt(info, 12, IsCurrent(e) ? "#0F7B43" : "#99000000");
            tc.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(tc, 1); row.Children.Add(tc);

            var sp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            CharEntry ce = e;
            var bUse = new Button
            {
                Content = IsCurrent(e) ? "使用中" : "应用",
                Style = (Style)Win.FindResource("SecondaryButton"),
                Margin = new Thickness(0, 0, 8, 0),
                IsEnabled = !IsCurrent(e)
            };
            bUse.Click += (s, ev) => { ApplyPack(ce); LblImportMsg.Text = "已应用：" + ce.Name + "（桌宠正在重启）"; ReloadChars(); RefreshImport(); };
            var bDel = new Button { Content = "删除", Style = (Style)Win.FindResource("GhostButton") };
            bDel.Click += (s, ev) => DeletePack(ce);
            sp.Children.Add(bUse); sp.Children.Add(bDel);
            Grid.SetColumn(sp, 2); row.Children.Add(sp);
            ImportList.Children.Add(row);
        }
    }

    // ---------------- 统计页（内容动态生成 · 实时刷新） ----------------
    static Brush C(string hex) { try { return (Brush)new BrushConverter().ConvertFromString(hex); } catch (Exception) { return Brushes.Black; } }
    static TextBlock Txt(string text, double size, string hex, bool bold = false)
    {
        var t = new TextBlock { Text = text, FontSize = size, Foreground = C(hex), TextWrapping = TextWrapping.Wrap };
        if (bold) t.FontWeight = FontWeights.SemiBold;
        return t;
    }
    static Border Card(UIElement child)
    {
        var b = new Border
        {
            Background = Brushes.White, BorderBrush = C("#E5E5E5"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4), Padding = new Thickness(20, 16, 20, 16), Margin = new Thickness(0, 0, 0, 12)
        };
        b.Child = child;
        return b;
    }
    static string FmtSec(int s)
    {
        if (s < 60) return s + " 秒";
        if (s < 3600) return (s / 60) + " 分 " + (s % 60) + " 秒";
        return (s / 3600) + " 小时 " + ((s % 3600) / 60) + " 分";
    }

    static string FmtShort(int s)
    {
        if (s <= 0) return "—";
        if (s < 60) return s + "秒";
        if (s < 3600) return (s / 60) + "分";
        return (s / 3600) + "时" + ((s % 3600) / 60).ToString("00") + "分";
    }
    static string VkName(int vk)
    {
        if (vk >= 65 && vk <= 90) return ((char)vk).ToString();
        if (vk >= 48 && vk <= 57) return ((char)vk).ToString();
        if (vk >= 96 && vk <= 105) return "小键盘" + (vk - 96);
        if (vk >= 112 && vk <= 135) return "F" + (vk - 111);
        switch (vk)
        {
            case 8: return "Backspace"; case 9: return "Tab"; case 13: return "Enter";
            case 16: return "Shift"; case 17: return "Ctrl"; case 18: return "Alt";
            case 20: return "CapsLock"; case 27: return "Esc"; case 32: return "空格";
            case 37: return "←"; case 38: return "↑"; case 39: return "→"; case 40: return "↓";
            case 46: return "Delete"; case 45: return "Insert"; case 36: return "Home"; case 35: return "End";
            case 33: return "PageUp"; case 34: return "PageDown";
            case 186: return ";"; case 187: return "="; case 188: return ","; case 189: return "-";
            case 190: return "."; case 191: return "/"; case 192: return "`"; case 219: return "[";
            case 220: return "\\"; case 221: return "]"; case 222: return "'";
            case 160: return "左Shift"; case 161: return "右Shift";
            case 162: return "左Ctrl"; case 163: return "右Ctrl";
            case 164: return "左Alt"; case 165: return "右Alt";
            case 91: return "Win"; case 92: return "Win(右)"; case 93: return "菜单键";
        }
        return "VK" + vk;
    }

    class BarRow
    {
        public Grid Root;
        public ColumnDefinition C0, C1;
        public TextBlock Val;
        public void SetValue(int val, int max, string text)
        {
            double frac = (max <= 0 || val <= 0) ? 0 : Math.Max(0.02, Math.Min(1.0, (double)val / max));
            C0.Width = new GridLength(frac, GridUnitType.Star);
            C1.Width = new GridLength(1 - frac, GridUnitType.Star);
            if (Val != null) Val.Text = text;
        }
    }

    static BarRow MakeBar(string name, int val, int max, string valText, double nameW, double valW = 96)
    {
        var row = new BarRow();
        var g = new Grid { Margin = new Thickness(0, 4, 0, 4), Height = 26 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(nameW) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(valW) });
        var n = Txt(name, 12, "#E4000000");
        n.VerticalAlignment = VerticalAlignment.Center;
        n.TextTrimming = TextTrimming.CharacterEllipsis;
        n.ToolTip = name;
        Grid.SetColumn(n, 0); g.Children.Add(n);

        var barWrap = new Grid { Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        barWrap.Children.Add(new Border { Height = 8, CornerRadius = new CornerRadius(4), Background = C("#EEF0F4") });
        var fgGrid = new Grid();
        var c0 = new ColumnDefinition(); var c1 = new ColumnDefinition();
        fgGrid.ColumnDefinitions.Add(c0); fgGrid.ColumnDefinitions.Add(c1);
        var fg = new Border { Height = 8, CornerRadius = new CornerRadius(4) };
        fg.Background = new LinearGradientBrush(Color.FromRgb(0x57, 0x8B, 0xE8), Color.FromRgb(0x2F, 0x6B, 0xD6), 0);
        Grid.SetColumn(fg, 0); fgGrid.Children.Add(fg);
        barWrap.Children.Add(fgGrid);
        Grid.SetColumn(barWrap, 1); g.Children.Add(barWrap);

        var v = Txt(valText, 12, "#99000000");
        v.VerticalAlignment = VerticalAlignment.Center;
        v.TextAlignment = TextAlignment.Right;
        Grid.SetColumn(v, 2); g.Children.Add(v);

        row.Root = g; row.C0 = c0; row.C1 = c1; row.Val = v;
        row.SetValue(val, max, valText);
        return row;
    }

    static ScrollViewer KeyScroll, AppScroll;

    // 滚轮处理：内层优先滚，但到边界后必须把滚轮"还"给父级，
    // 否则内层滚到头之后继续滚滚轮什么也不发生（会被自己吃掉）。
    // 返回 true 表示这次事件已被消费。
    static bool WheelIn(ScrollViewer sv, MouseWheelEventArgs e)
    {
        if (sv == null) return false;
        double max = sv.ScrollableHeight;
        if (max <= 0) return false;                      // 本来就没什么可滚的，交给父级
        double step = -e.Delta / 3.0;                    // 一格滚轮 = 1/3 行，和系统一致
        if (step == 0) return false;
        double next = sv.VerticalOffset + step;
        if (next < 0 || next > max)
        {
            // 已经贴边且还在往外滚 → 不消费，让外层 ScrollViewer 接过去
            if ((next < 0 && sv.VerticalOffset <= 0) || (next > max && sv.VerticalOffset >= max))
                return false;
            next = Math.Max(0, Math.Min(max, next));
        }
        sv.ScrollToVerticalOffset(next);
        return true;
    }

    static void HookWheel(ScrollViewer sv)
    {
        if (sv == null) return;
        sv.PreviewMouseWheel += (s, e) =>
        {
            // 消费前先把事件标记为未处理失败的情况排除：只有真的滚动了才 Handled
            if (!WheelIn(sv, e)) return;                 // 交回父级继续处理
            e.Handled = true;
        };
    }
    static StackPanel KeyListPanel, AppListPanel, CharListPanel;
    static TextBlock LblStatDay, LblStatScreen, LblStatActive, LblStatIdle, LblStatKeys, LblStatUpdated;
    static Button _dateBtn;                    // 日期下拉触发按钮（替代 DatePicker）
    static ListBox _dateList;                    // 弹出列表：只列真实有记录的日期 + 今天
    static Popup _datePopup;
    static TextBlock _dateNote;
    static TextBlock _liveTxt;
    static Ellipse _liveDot;
    static string _statViewDate = "";   // "" = 今天（实时刷新）
    static List<BarRow> _appRows = new List<BarRow>();
    static List<BarRow> _keyRows = new List<BarRow>();
    static List<BarRow> _charRows = new List<BarRow>();
    static string _appSig = "", _keySig = "", _charSig = "";
    static DispatcherTimer _liveTimer;

    // 把本机已有的「按天统计文件」列进日期选择器（含今天），没有历史时只有今天可选
    // 扫出所有真实有归档记录的日期（+ 今天），新 -> 旧。
    // 存进 _statDays 供列表用；不直接改当前选择，避免切页回来时被强行拉回今天。
    static List<DateTime> _statDays = new List<DateTime>();
    static Dictionary<string, bool> _statHasData = new Dictionary<string, bool>();

    static void RefreshStatDates()
    {
        var dates = new List<DateTime>();
        try
        {
            foreach (string f in Directory.GetFiles(Here, "pet-stats-*.txt"))
            {
                string name = Path.GetFileNameWithoutExtension(f);
                if (name.Length < 10) continue;
                string d = name.Substring(name.Length - 10);
                DateTime dt;
                if (DateTime.TryParseExact(d, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
                    dates.Add(dt);
            }
        }
        catch (Exception) { }
        DateTime todayDt = DateTime.Today;

        // 列表要同时包含「最近 N 天」和「所有有归档的历史日期」：
        //  - 有归档的：可点，能看真实数据
        //  - 没归档的：置灰不可点（用户明确要求显示出来，而不是直接不列）
        // 近 N 天保证日历是连续的（不会 10/18 之后直接跳到 8/01，看不出中间断了多久）
        const int recentDays = 30;
        var all = new List<DateTime>();
        var seen = new HashSet<string>();
        for (int i = 0; i < recentDays; i++)
        {
            DateTime d = todayDt.AddDays(-i);
            if (seen.Add(d.ToString("yyyy-MM-dd"))) all.Add(d);
        }
        foreach (var d in dates)
            if (seen.Add(d.ToString("yyyy-MM-dd"))) all.Add(d);
        all.Sort(); all.Reverse();                            // 新 -> 旧
        _statDays = all;
        // 记录哪些日期真有归档（用于置灰不可点的那些）
        _statHasData.Clear();
        foreach (var d in dates) _statHasData[d.ToString("yyyy-MM-dd")] = true;
        _statHasData[todayDt.ToString("yyyy-MM-dd")] = true;   // 今天一定有（实时文件）

        if (_dateNote != null)
            _dateNote.Text = dates.Count > 1 ? "共 " + dates.Count + " 天可查" : "今天还没有历史记录";
        FillDateList();
    }

    static string StatDateLabel(DateTime d)
    {
        bool isToday = d.Date == DateTime.Today;
        string wk = WeekCn(d.DayOfWeek);
        return (isToday ? "今天 · " : "") + d.ToString("M月d日") + " " + wk;
    }

    static string WeekCn(DayOfWeek w)
    {
        switch (w)
        {
            case DayOfWeek.Sunday: return "周日";
            case DayOfWeek.Monday: return "周一";
            case DayOfWeek.Tuesday: return "周二";
            case DayOfWeek.Wednesday: return "周三";
            case DayOfWeek.Thursday: return "周四";
            case DayOfWeek.Friday: return "周五";
            default: return "周六";
        }
    }

    // 填弹出列表。选中项 = 当前查看的日期（今天时 _statViewDate 为空）
    static void FillDateList()
    {
        if (_dateList == null) return;
        string view = string.IsNullOrEmpty(_statViewDate) ? DateTime.Now.ToString("yyyy-MM-dd") : _statViewDate;
        _dateList.Items.Clear();
        foreach (var d in _statDays)
        {
            string key = d.ToString("yyyy-MM-dd");
            bool isToday = d.Date == DateTime.Today;
            bool sel = key == view;
            // 没有归档的日期：置灰、不可点、光标不加手型，悬停时说明原因
            bool hasData = _statHasData.ContainsKey(key);
            var item = new ListBoxItem
            {
                Content = StatDateLabel(d) + (hasData ? "" : "  ·  无记录"),
                Tag = hasData ? key : null,      // Tag=null ⇒ 选中时直接忽略
                Padding = new Thickness(12, 7, 12, 7),
                Margin = new Thickness(4, 0, 4, 0),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                FontSize = 13,
                Cursor = hasData ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow,
                ToolTip = hasData ? null : "这一天没有统计记录"
            };
            if (!hasData)
            {
                // 灰：明确不可用。#8A93A3 白底约 3.5:1，作"禁用态"语义色可以
                //（禁用态本就不承载可读信息，只需明显区别于可选项）
                item.Foreground = C("#8A93A3");
                item.FontWeight = FontWeights.Normal;
                item.Background = Brushes.Transparent;    // 压掉 ListBox 默认选中底色
                // 不用 IsEnabled=false：WPF 会自动再叠一层灰导致过暗，且 Tooltip 也会被禁用。
                // 这里只靠 Tag=null 拦点击（见 SelectionChanged），视觉上「灰 + 无手型」已足够表达不可用。
            }
            else if (sel)
            {
                item.Background = C("#E8F1FB");
                item.FontWeight = FontWeights.SemiBold;
                item.Foreground = C("#005FB8");
            }
            else if (isToday)
            {
                item.FontWeight = FontWeights.SemiBold;
                item.Foreground = C("#E4000000");
            }
            else
            {
                item.Foreground = C("#E4000000");
            }
            _dateList.Items.Add(item);
        }
        UpdateDateButtonText();
    }

    static void UpdateDateButtonText()
    {
        if (_dateBtn == null) return;
        DateTime d = DateTime.Today;
        if (!string.IsNullOrEmpty(_statViewDate))
            DateTime.TryParseExact(_statViewDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d);
        bool isToday = d.Date == DateTime.Today;
        var tb = _dateBtn.Content as TextBlock;
        if (tb != null)
        {
            tb.Text = (isToday ? "今天 · " : "") + d.ToString("yyyy年M月d日");
            tb.Foreground = C("#E4000000");
        }
    }

    // 统一切换查看的日期。切页重建时不会调它，所以用户的选择能跨页保留。
    static void ApplyStatDate(string key)
    {
        string today = DateTime.Now.ToString("yyyy-MM-dd");
        _statViewDate = (key == today) ? "" : key;      // 空串 = 今天（走实时刷新）
        UpdateDateButtonText();
        UpdateStats(true);
    }

    // 日期按钮模板：与输入框/次按钮同款，右侧加一个下拉小箭头
    static ControlTemplate DateBtnTemplate()
    {
        return (ControlTemplate)XamlReader.Parse(
            @"<ControlTemplate TargetType=""Button""
        xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""
        xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">
        <Border x:Name=""bd"" Background=""#F7F8FA"" BorderBrush=""#D6D6D6""
                BorderThickness=""1"" CornerRadius=""4"" SnapsToDevicePixels=""True"">
          <Grid>
            <Grid.ColumnDefinitions>
              <ColumnDefinition Width=""*""/>
              <ColumnDefinition Width=""Auto""/>
            </Grid.ColumnDefinitions>
            <ContentPresenter Grid.Column=""0"" Margin=""12,0,8,0"" VerticalAlignment=""Center""/>
            <Path Grid.Column=""1"" Data=""M 0 0 L 4 4 L 8 0"" Stroke=""#666666"" StrokeThickness=""1.4""
                  HorizontalAlignment=""Right"" VerticalAlignment=""Center"" Margin=""0,0,12,0""/>
          </Grid>
        </Border>
        <ControlTemplate.Triggers>
          <Trigger Property=""IsMouseOver"" Value=""True"">
            <Setter TargetName=""bd"" Property=""Background"" Value=""#EFF1F5""/>
          </Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>");
    }

    // 弹出列表里每一项的模板：4px 圆角，hover 浅灰（选中态底色由代码设置 Background）
    static ControlTemplate DateItemTemplate()
    {
        return (ControlTemplate)XamlReader.Parse(
            @"<ControlTemplate TargetType=""ListBoxItem""
        xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""
        xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">
        <Border x:Name=""bd"" Background=""Transparent"" CornerRadius=""4"" SnapsToDevicePixels=""True""
                Margin=""4,1"">
          <ContentPresenter Margin=""10,7,10,7"" VerticalAlignment=""Center""/>
        </Border>
        <ControlTemplate.Triggers>
          <Trigger Property=""IsMouseOver"" Value=""True"">
            <Setter TargetName=""bd"" Property=""Background"" Value=""#F0F0F0""/>
          </Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>");
    }


    // ---------------- 关于 ----------------
    // 版本号来自两个 exe 的文件属性（csproj 里的 FileVersion / InformationalVersion）。
    // 这里刻意同时显示 Pet.exe 与 PetSettings.exe：只要两者不一致，就说明有一个没跟着更新。
    const string RepoUrl = "https://github.com/xiezaji/q-pet";
    const string WebUrl = "https://xiezaji.github.io/q-pet/";

    static void BuildAboutPage()
    {
        if (PageAbout == null) return;
        PageAbout.Children.Clear();

        PageAbout.Children.Add(new TextBlock { Text = "关于", Style = (Style)Win.FindResource("H1") });
        PageAbout.Children.Add(new TextBlock
        {
            Text = "版本、目录与链接",
            Style = (Style)Win.FindResource("Sub"),
            Margin = new Thickness(0, 4, 0, 16)
        });

        // ---- 版本 ----
        string vSet = ExeVersion("PetSettings.exe");
        string vPet = ExeVersion("Pet.exe");
        var spVer = new StackPanel();
        spVer.Children.Add(new TextBlock { Text = "版本", Style = (Style)Win.FindResource("H2") });
        spVer.Children.Add(VersionRow("设置面板", "PetSettings.exe", vSet, BuiltAt("PetSettings.exe")));
        spVer.Children.Add(new Border { Style = (Style)Win.FindResource("Divider") });
        spVer.Children.Add(VersionRow("桌宠本体", "Pet.exe", vPet, BuiltAt("Pet.exe")));
        if (vSet != vPet || vSet == "未找到" || vPet == "未找到")
        {
            var warn = Txt("两个 exe 版本不一致，重新运行 build.cmd 可对齐", 11, "#C42B1C");
            warn.Margin = new Thickness(0, 10, 0, 0);
            warn.ToolTip = "根目录的 Pet.exe 与 PetSettings.exe 应当来自同一次编译。\n不一致时，界面里的新功能可能对桌宠本体不生效。";
            spVer.Children.Add(warn);
        }
        PageAbout.Children.Add(Card(spVer));

        // ---- 程序目录 ----
        var spDir = new StackPanel();
        spDir.Children.Add(new TextBlock { Text = "程序目录", Style = (Style)Win.FindResource("H2") });
        var tipDir = Txt("配置、角色包、统计都在这里", 12, "#99000000");
        tipDir.Margin = new Thickness(0, 3, 0, 0);
        spDir.Children.Add(tipDir);
        var tbPath = new TextBox
        {
            Text = Here, IsReadOnly = true, Height = 32, Margin = new Thickness(0, 12, 0, 0),
            ToolTip = "可以选中复制。整个文件夹能直接拷到别的电脑或 U 盘，配置全是相对路径。"
        };
        spDir.Children.Add(tbPath);
        var btnDir = new Button
        {
            Content = "打开文件夹", Style = (Style)Win.FindResource("SecondaryButton"),
            HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0)
        };
        btnDir.Click += (s, e) => OpenExternal(Here);
        spDir.Children.Add(btnDir);
        PageAbout.Children.Add(Card(spDir));

        // ---- 链接 ----
        var spLink = new StackPanel();
        spLink.Children.Add(new TextBlock { Text = "链接", Style = (Style)Win.FindResource("H2") });
        var linkTip = Txt("遇到问题先看仓库里的说明", 12, "#99000000");
        linkTip.Margin = new Thickness(0, 3, 0, 0);
        spLink.Children.Add(linkTip);
        var linkRow = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        AddLinkButton(linkRow, "GitHub 仓库", RepoUrl);
        AddLinkButton(linkRow, "在线试玩", WebUrl);
        AddLinkButton(linkRow, "查看许可证", RepoUrl + "/blob/main/LICENSE");
        spLink.Children.Add(linkRow);
        PageAbout.Children.Add(Card(spLink));

        // ---- 说明 ----
        var spAbout = new StackPanel();
        spAbout.Children.Add(new TextBlock { Text = "关于本程序", Style = (Style)Win.FindResource("H2") });
        var intro = Txt("Q Pet 是本地桌宠：不联网、无账号，也不需要安装。", 13, "#E4000000");
        intro.Margin = new Thickness(0, 8, 0, 0);
        spAbout.Children.Add(intro);
        var privacy = Txt("使用统计只写在本目录的 txt 里。", 12, "#99000000");
        privacy.Margin = new Thickness(0, 4, 0, 0);
        spAbout.Children.Add(privacy);
        var copy = Txt("© 2026 Q Pet contributors　·　代码 MIT，角色素材版权归原权利方", 11, "#99000000");
        copy.Margin = new Thickness(0, 10, 0, 0);
        spAbout.Children.Add(copy);
        PageAbout.Children.Add(Card(spAbout));
    }

    // 版本行：四列固定宽度，两个 exe 的版本号与构建时间能对齐
    static Grid VersionRow(string label, string exe, string ver, string built)
    {
        var g = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var l = Txt(label, 12, "#99000000");
        l.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(l);
        var n = Txt(exe, 13, "#E4000000");
        Grid.SetColumn(n, 1);
        g.Children.Add(n);
        var v = Txt(ver, 13, "#E4000000");
        Grid.SetColumn(v, 2);
        g.Children.Add(v);
        var b = Txt(built, 12, "#99000000");
        Grid.SetColumn(b, 3);
        g.Children.Add(b);
        return g;
    }

    static void AddLinkButton(Panel host, string text, string url)
    {
        var b = new Button
        {
            Content = text, Style = (Style)Win.FindResource("SecondaryButton"),
            Margin = new Thickness(0, 0, 8, 8)
        };
        b.Click += (s, e) => OpenExternal(url);
        host.Children.Add(b);
    }

    // 读 exe 的文件版本：ProductVersion 在有 InformationalVersion 时就是「1.0.0」这种短格式
    static string ExeVersion(string file)
    {
        try
        {
            string p = Path.Combine(Here, file);
            if (!File.Exists(p)) return "未找到";
            FileVersionInfo vi = FileVersionInfo.GetVersionInfo(p);
            string v = vi.ProductVersion;
            if (string.IsNullOrEmpty(v)) v = vi.FileVersion;
            if (string.IsNullOrEmpty(v)) return "未知";
            // 有些 SDK 版本会把「+提交号」拼进 ProductVersion，界面上不需要
            int plus = v.IndexOf('+');
            if (plus > 0) v = v.Substring(0, plus);
            return v.Trim();
        }
        catch (Exception ex) { LogErr("about-version", ex); return "读取失败"; }
    }

    static string BuiltAt(string file)
    {
        try
        {
            string p = Path.Combine(Here, file);
            if (!File.Exists(p)) return "";
            return "构建于 " + File.GetLastWriteTime(p).ToString("yyyy-MM-dd HH:mm");
        }
        catch (Exception) { return ""; }
    }

    static void OpenExternal(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception ex) { LogErr("open-external", ex); Dlg.Info("打不开", ex.Message, "好"); }
    }

    static void BuildStatsPage()
    {
        if (PageStats == null) return;
        PageStats.Children.Clear();
        _appRows.Clear(); _keyRows.Clear(); _charRows.Clear();
        _appSig = ""; _keySig = ""; _charSig = "";

        // 标题 / 说明的间距与其它页对齐（H1 下 4、说明下 20）
        var hStat = Txt("使用统计", 28, "#E4000000", true);
        hStat.Margin = new Thickness(0, 0, 0, 4);
        PageStats.Children.Add(hStat);
        var sStat = Txt("只存本机，不联网", 12, "#99000000");
        sStat.Margin = new Thickness(0, 0, 0, 20);
        sStat.ToolTip = "数据全部写在 pet-stats.txt，不上传。";
        PageStats.Children.Add(sStat);

        // ---- 日期选择：回看历史 ----
        var dateBar = new Grid();
        dateBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        dateBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        dateBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var dateLbl = Txt("查看日期", 13, "#E4000000", true);
        dateLbl.VerticalAlignment = VerticalAlignment.Center;
        // 不用 DatePicker：它的下拉是「月/日网格」，而本场景只能选「有记录的日期」，
        // 用日历反而要在一堆空白格里翻找。改成「按钮 + 只列真实记录日期的列表」，
        // 更贴合场景也更干净，同时一次性解决三个毛病：
        //   ① 切页时 BuildStatsPage 重建控件并把日期强行拉回今天 → 现在保留用户选择
        //   ② DisplayDateEnd 锁死上限、选完就难改 → 现在列表里随时可换
        //   ③ 月/日网格 80% 是空白 → 现在只列可选项
        const int dateBtnW = 190;      // 与下面弹出列表同宽，避免"按钮窄、列表宽"的错位感
        _dateBtn = new Button
        {
            Width = dateBtnW, Height = 32, FontSize = 13, Cursor = System.Windows.Input.Cursors.Hand,
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Left,   // 文案左对齐，和列表项文字对齐
            Style = (Style)Win.FindResource("SecondaryButton"),
            Content = new TextBlock { Text = "今天", FontSize = 13, VerticalAlignment = VerticalAlignment.Center }
        };
        _dateBtn.Template = DateBtnTemplate();
        Grid.SetColumn(dateLbl, 0); Grid.SetColumn(_dateBtn, 1);
        _dateNote = Txt("", 12, "#99000000");
        _dateNote.VerticalAlignment = VerticalAlignment.Center;
        _dateNote.Margin = new Thickness(12, 0, 0, 0);
        Grid.SetColumn(_dateNote, 2);
        dateBar.Children.Add(dateLbl); dateBar.Children.Add(_dateBtn); dateBar.Children.Add(_dateNote);

        // 弹出列表（自绘 Popup：独立 HWND，RenderTargetBitmap 截不到，只能真实交互验证）
        _datePopup = new Popup
        {
            AllowsTransparency = true, StaysOpen = false,
            Placement = PlacementMode.Bottom, VerticalOffset = 4,
            Focusable = false, PopupAnimation = PopupAnimation.Fade,
            // ⚠ 必须显式指定锚点：自绘 Popup 不会像 ComboBox 那样自动关联，
            // 不设就会退化到相对鼠标/屏幕原点 → 列表跑到左上角（用户实测反馈的）。
            PlacementTarget = _dateBtn,
            HorizontalOffset = 0
        };
        // ListBox 自身宽度 = 按钮宽 - 外框(Border 1x2 + Padding 2x2 = 6)，
        // 这样「外层可见白框」才和按钮严格等宽（自检里量的是外层）
        _dateList = new ListBox
        {
            Width = dateBtnW - 6, MaxHeight = 262,
            BorderThickness = new Thickness(0), Background = Brushes.White,
            Padding = new Thickness(0, 4, 0, 4)
        };
        var lbItem = new Style(typeof(ListBoxItem));
        lbItem.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        lbItem.Setters.Add(new Setter(Control.TemplateProperty, DateItemTemplate()));
        _dateList.ItemContainerStyle = lbItem;
        _dateList.SelectionChanged += (s, e) =>
        {
            var it = _dateList.SelectedItem as ListBoxItem;
            if (it == null || it.Tag == null)
            {
                // 点到灰色项：仅撤掉选中高亮，不关弹窗、不切日期
                if (it != null) it.IsSelected = false;
                return;
            }
            string key = it.Tag as string;
            _datePopup.IsOpen = false;
            _dateList.SelectedItem = null;
            ApplyStatDate(key);
        };
        _datePopup.Child = new Border
        {
            Background = Brushes.White, CornerRadius = new CornerRadius(8),
            BorderBrush = C("#E5E5E5"), BorderThickness = new Thickness(1),
            Padding = new Thickness(2), Child = _dateList,
            Effect = new DropShadowEffect { BlurRadius = 22, ShadowDepth = 4, Opacity = 0.18, Direction = 270 }
        };
        _dateBtn.Click += (s, e) =>
        {
            FillDateList();
            // 每次打开时把锚点矩形同步成按钮的实际尺寸（等宽等左边，视觉才对齐）
            _datePopup.PlacementRectangle = new Rect(0, 0, _dateBtn.ActualWidth, _dateBtn.ActualHeight);
            _datePopup.IsOpen = !_datePopup.IsOpen;
        };

        PageStats.Children.Add(dateBar);
        PageStats.Children.Add(new Border { Height = 12 });
        RefreshStatDates();

        // ---- 今日概览（带实时角标）----
        var wrap = new StackPanel();
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        LblStatDay = Txt("今天 · —", 14, "#E4000000", true);
        head.Children.Add(LblStatDay);
        var live = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        var dot = new Ellipse { Width = 7, Height = 7, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        dot.Fill = C("#12A150");
        var pulse = new DoubleAnimation(0.3, 1.0, TimeSpan.FromMilliseconds(900))
        { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
        dot.BeginAnimation(UIElement.OpacityProperty, pulse);
        var liveTxt = Txt("实时刷新", 12, "#0F7B43");
        _liveTxt = liveTxt;
        _liveDot = dot;
        liveTxt.VerticalAlignment = VerticalAlignment.Center;
        live.Children.Add(dot); live.Children.Add(liveTxt);
        Grid.SetColumn(live, 1); head.Children.Add(live);
        wrap.Children.Add(head);
        wrap.Children.Add(new Border { Height = 12 });

        var g3 = new Grid();
        for (int c = 0; c < 3; c++) g3.ColumnDefinitions.Add(new ColumnDefinition());
        string[] labels = { "屏幕使用", "活跃", "挂机" };
        LblStatScreen = Txt("—", 22, "#E4000000", true);
        LblStatActive = Txt("—", 22, "#E4000000", true);
        LblStatIdle = Txt("—", 22, "#E4000000", true);
        TextBlock[] vals = { LblStatScreen, LblStatActive, LblStatIdle };
        for (int c = 0; c < 3; c++)
        {
            var sp = new StackPanel();
            sp.Children.Add(Txt(labels[c], 12, "#99000000"));
            sp.Children.Add(vals[c]);
            Grid.SetColumn(sp, c);
            g3.Children.Add(sp);
        }
        wrap.Children.Add(g3);
        wrap.Children.Add(new Border { Height = 10 });
        LblStatUpdated = Txt("—", 11, "#99000000");
        wrap.Children.Add(LblStatUpdated);
        PageStats.Children.Add(Card(wrap));

        // ---- 按键使用（滚轮查看 + 实时刷新）----
        var spKey = new StackPanel();
        var keyHead = new Grid();
        keyHead.ColumnDefinitions.Add(new ColumnDefinition());
        keyHead.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var keyTitle = Txt("按键使用", 14, "#E4000000", true);
        keyHead.Children.Add(keyTitle);
        LblStatKeys = Txt("共 0 次", 12, "#99000000");
        LblStatKeys.VerticalAlignment = VerticalAlignment.Center;
        LblStatKeys.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(LblStatKeys, 1); keyHead.Children.Add(LblStatKeys);
        spKey.Children.Add(keyHead);
        spKey.Children.Add(Txt("滚轮查看全部，每秒刷新", 12, "#99000000"));
        spKey.Children.Add(new Border { Height = 8 });

        KeyScroll = new ScrollViewer
        {
            Height = 280,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            PanningMode = PanningMode.VerticalOnly
        };
        KeyListPanel = new StackPanel();
        KeyScroll.Content = KeyListPanel;
        // 内层优先滚：滚到尽头也不会"跳"到外层页面
        HookWheel(KeyScroll);
        var keyBox = new Border
        {
            Background = C("#FCFCFD"), BorderBrush = C("#EEF0F4"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 8, 12, 8), Child = KeyScroll
        };
        spKey.Children.Add(keyBox);
        PageStats.Children.Add(Card(spKey));

        // ---- 角色陪伴时长 ----
        var spChar = new StackPanel();
        var chHead = new Grid();
        chHead.ColumnDefinitions.Add(new ColumnDefinition());
        chHead.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        chHead.Children.Add(Txt("角色陪伴时长", 14, "#E4000000", true));
        var chTip = Txt("今日 / 累计", 11, "#99000000");
        chTip.ToolTip = "累计时长写在 pet-char-time.txt，清空今日统计不会把它清零。";
        chTip.VerticalAlignment = VerticalAlignment.Center;
        chTip.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(chTip, 1); chHead.Children.Add(chTip);
        spChar.Children.Add(chHead);
        spChar.Children.Add(new Border { Height = 6 });
        CharListPanel = new StackPanel();
        spChar.Children.Add(CharListPanel);
        PageStats.Children.Add(Card(spChar));

        // ---- 软件使用（滚动查看全部）----
        var spApp = new StackPanel();
        spApp.Children.Add(Txt("软件使用", 14, "#E4000000", true));
        spApp.Children.Add(Txt("滚轮查看全部，每秒刷新", 12, "#99000000"));
        spApp.Children.Add(new Border { Height = 6 });
        AppScroll = new ScrollViewer
        {
            Height = 240,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            PanningMode = PanningMode.VerticalOnly
        };
        AppListPanel = new StackPanel();
        AppScroll.Content = AppListPanel;
        HookWheel(AppScroll);
        spApp.Children.Add(new Border
        {
            Background = C("#FCFCFD"), BorderBrush = C("#EEF0F4"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 8, 12, 8), Child = AppScroll
        });
        PageStats.Children.Add(Card(spApp));

        // ---- 操作 ----
        var btns = new StackPanel { Orientation = Orientation.Horizontal };
        var bRef = new Button { Content = "立即刷新", Style = (Style)Win.FindResource("SecondaryButton"), Margin = new Thickness(0, 0, 8, 0) };
        bRef.Click += (s, e) => UpdateStats(true);
        var bClr = new Button { Content = "清空统计", Style = (Style)Win.FindResource("SecondaryButton") };
        bClr.Click += (s, e) =>
        {
            if (!Dlg.Ask("清空今天的统计？", "屏幕时长、软件使用、按键次数和角色今日时长都会归零（角色累计时长保留）。", "清空", "算了", true)) return;
            try { File.WriteAllText(Path.Combine(Here, "pet-stats-reset.txt"), DateTime.Now.ToString("s")); }
            catch (Exception ex) { LogErr("clrstats", ex); }
            // 桌宠 1 秒内会看到重置请求；这里等它清完再刷新界面
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1600) };
            t.Tick += (s2, e2) => { t.Stop(); UpdateStats(true); };
            t.Start();
        };
        btns.Children.Add(bRef); btns.Children.Add(bClr);
        PageStats.Children.Add(btns);

        UpdateStats(true);
    }

    static void UpdateStats(bool rebuild)
    {
        if (PageStats == null || KeyListPanel == null) return;
        int screen = 0, active = 0, idle = 0;
        var apps = new Dictionary<string, int>();
        var keys = new Dictionary<int, int>();
        var charDay = new Dictionary<string, int>();
        var charTotal = new Dictionary<string, int>();
        string day = "—";
        string today = DateTime.Now.ToString("yyyy-MM-dd");
        string viewDay = _statViewDate;
        bool isToday = string.IsNullOrEmpty(viewDay) || viewDay == today;
        string sf = isToday ? Path.Combine(Here, "pet-stats.txt")
                            : Path.Combine(Here, "pet-stats-" + viewDay + ".txt");
        try
        {
            if (File.Exists(sf))
            {
                foreach (string line in File.ReadAllLines(sf))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = line.Substring(0, eq), v = line.Substring(eq + 1);
                    if (k == "day") day = v;
                    else if (k == "screen") int.TryParse(v, out screen);
                    else if (k == "active") int.TryParse(v, out active);
                    else if (k == "idle") int.TryParse(v, out idle);
                    else if (k == "app") { int b = v.LastIndexOf('|'); if (b > 0) { int n; if (int.TryParse(v.Substring(b + 1), out n)) apps[v.Substring(0, b)] = n; } }
                    else if (k == "key") { int b = v.LastIndexOf('|'); if (b > 0) { int vk, n; if (int.TryParse(v.Substring(0, b), out vk) && int.TryParse(v.Substring(b + 1), out n)) keys[vk] = n; } }
                    else if (k == "char") { int b = v.LastIndexOf('|'); if (b > 0) { int n; if (int.TryParse(v.Substring(b + 1), out n)) charDay[v.Substring(0, b)] = n; } }
                }
            }
        }
        catch (Exception) { }
        try
        {
            string ct = Path.Combine(Here, "pet-char-time.txt");
            if (File.Exists(ct))
                foreach (string line in File.ReadAllLines(ct))
                {
                    int eq = line.LastIndexOf('=');
                    if (eq <= 0) continue;
                    int n;
                    if (int.TryParse(line.Substring(eq + 1), out n)) charTotal[line.Substring(0, eq).Trim()] = n;
                }
        }
        catch (Exception) { }

        LblStatDay.Text = (isToday ? "今天 · " : "历史 · ") + day;
        LblStatScreen.Text = FmtSec(screen);
        LblStatActive.Text = FmtSec(active);
        LblStatIdle.Text = FmtSec(idle);
        if (_liveTxt != null)
        {
            if (isToday) { _liveTxt.Text = "实时刷新"; _liveTxt.Foreground = C("#0F7B43"); if (_liveDot != null) _liveDot.Visibility = Visibility.Visible; }
            else { _liveTxt.Text = "历史记录（只读）"; _liveTxt.Foreground = C("#99000000"); if (_liveDot != null) _liveDot.Visibility = Visibility.Collapsed; }
        }
        LblStatUpdated.Text = isToday
            ? "更新于 " + DateTime.Now.ToString("HH:mm:ss") + "（桌宠每 4 秒落盘一次）"
            : "历史快照 · 该日已归档，可切回「今天」看实时数据";
        int totalKeys = keys.Values.Sum();
        LblStatKeys.Text = "共 " + totalKeys + " 次 · " + keys.Count + " 个按键";

        // 角色陪伴时长（今日 + 累计）
        var charNames = new HashSet<string>();
        foreach (string n in charDay.Keys) charNames.Add(n);
        foreach (string n in charTotal.Keys) charNames.Add(n);
        foreach (CharEntry ce in ChList) if (!string.IsNullOrEmpty(ce.Dir)) charNames.Add(ce.Name);
        var charList = new List<KeyValuePair<string, int>>();
        foreach (string n in charNames)
        {
            int t;
            charTotal.TryGetValue(n, out t);
            charList.Add(new KeyValuePair<string, int>(n, t));
        }
        charList.Sort((a, b) => b.Value != a.Value ? b.Value.CompareTo(a.Value) : string.Compare(a.Key, b.Key, StringComparison.CurrentCulture));
        string charSig = string.Join(",", charList.ConvertAll(x => x.Key).ToArray());
        if (rebuild || charSig != _charSig)
        {
            _charSig = charSig;
            CharListPanel.Children.Clear();
            _charRows.Clear();
            if (charList.Count == 0) CharListPanel.Children.Add(Txt("还没有角色数据", 12, "#99000000"));
            else
            {
                int mx = Math.Max(1, charList[0].Value);
                foreach (var kv in charList)
                {
                    int d;
                    charDay.TryGetValue(kv.Key, out d);
                    BarRow row = MakeBar(kv.Key, kv.Value, mx,
                        "今日 " + FmtShort(d) + " · 累计 " + FmtShort(kv.Value), 110, 190);
                    _charRows.Add(row); CharListPanel.Children.Add(row.Root);
                }
            }
        }
        else
        {
            int mx = charList.Count > 0 ? Math.Max(1, charList[0].Value) : 1;
            for (int i = 0; i < charList.Count && i < _charRows.Count; i++)
            {
                int d;
                charDay.TryGetValue(charList[i].Key, out d);
                _charRows[i].SetValue(charList[i].Value, mx,
                    "今日 " + FmtShort(d) + " · 累计 " + FmtShort(charList[i].Value));
            }
        }

        // 软件（全部，滚动查看）
        var topApps = apps.OrderByDescending(x => x.Value).Take(40).ToList();
        string appSig = string.Join(",", topApps.Select(x => x.Key).ToArray());
        if (rebuild || appSig != _appSig)
        {
            _appSig = appSig;
            double saved = AppScroll != null ? AppScroll.VerticalOffset : 0;
            AppListPanel.Children.Clear();
            _appRows.Clear();
            if (topApps.Count == 0) AppListPanel.Children.Add(Txt("还没有数据", 12, "#99000000"));
            else
            {
                int mx = topApps[0].Value;
                foreach (var kv in topApps)
                {
                    BarRow row = MakeBar(kv.Key, kv.Value, mx, FmtSec(kv.Value), 130);
                    _appRows.Add(row); AppListPanel.Children.Add(row.Root);
                }
            }
            if (!rebuild && AppScroll != null) AppScroll.ScrollToVerticalOffset(saved);
        }
        else
        {
            int mx = topApps.Count > 0 ? topApps[0].Value : 0;
            for (int i = 0; i < topApps.Count && i < _appRows.Count; i++)
                _appRows[i].SetValue(topApps[i].Value, mx, FmtSec(topApps[i].Value));
        }

        // 按键（全部，按次数降序；同一批键只更新数值，避免滚动位置每秒被重置）
        var ordered = keys.OrderByDescending(x => x.Value).ThenBy(x => x.Key).ToList();
        string keySig = string.Join(",", ordered.Select(x => x.Key).ToArray());
        if (rebuild || keySig != _keySig)
        {
            _keySig = keySig;
            double saved = KeyScroll != null ? KeyScroll.VerticalOffset : 0;
            KeyListPanel.Children.Clear();
            _keyRows.Clear();
            if (ordered.Count == 0) KeyListPanel.Children.Add(Txt("还没有数据（敲几下键盘就有了）", 12, "#99000000"));
            else
            {
                int mx = ordered[0].Value;
                foreach (var kv in ordered)
                {
                    BarRow row = MakeBar(VkName(kv.Key), kv.Value, mx, kv.Value + " 次", 120);
                    _keyRows.Add(row); KeyListPanel.Children.Add(row.Root);
                }
            }
            if (!rebuild && KeyScroll != null) KeyScroll.ScrollToVerticalOffset(saved);
        }
        else
        {
            int mx = ordered.Count > 0 ? ordered[0].Value : 0;
            for (int i = 0; i < ordered.Count && i < _keyRows.Count; i++)
                _keyRows[i].SetValue(ordered[i].Value, mx, ordered[i].Value + " 次");
        }
    }

    static void StartLiveStats()
    {
        if (_liveTimer == null)
        {
            _liveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
            _liveTimer.Tick += (s, e) =>
            {
                try { if (PageStats != null && PageStats.Visibility == Visibility.Visible) UpdateStats(false); }
                catch (Exception ex) { LogErr("live", ex); }
            };
        }
        _liveTimer.Start();
    }

    static void StopLiveStats()
    {
        if (_liveTimer != null) _liveTimer.Stop();
    }

    // 无界面截图（--shot 路径）：把指定页渲染成 PNG，用来确认布局没被挤掉
    static int _shotScroll = 0;

    static void Shot(string path, int w, int h)
    {
        try
        {
            Win.WindowStartupLocation = WindowStartupLocation.Manual;
            Win.Left = -4000; Win.Top = -4000;
            Win.Width = w; Win.Height = h;
            Win.ShowInTaskbar = false;
            Win.Show();
            Win.UpdateLayout();
            // 把 Loaded / Render 优先级的排队任务都跑完，再抓
            Win.Dispatcher.Invoke(new Action(() => { }), DispatcherPriority.Loaded);
            Win.Dispatcher.Invoke(new Action(() => { }), DispatcherPriority.Render);
            if (_shotScroll > 0 && Scroller != null) Scroller.ScrollToVerticalOffset(_shotScroll);
            Win.UpdateLayout();
            var root = (FrameworkElement)Win.Content;
            int rw = (int)Math.Round(root.ActualWidth), rh = (int)Math.Round(root.ActualHeight);
            if (rw <= 0 || rh <= 0) { rw = w; rh = h; }
            var rtb = new RenderTargetBitmap(rw, rh, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(root);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using (FileStream fs = File.Create(path)) enc.Save(fs);
            Console.WriteLine("shot " + path + " " + rw + "x" + rh);
        }
        catch (Exception ex) { LogErr("shot", ex); }
        finally { try { Win.Close(); } catch (Exception) { } }
    }


    // ---------------- 自检 ----------------
    static void SelfTest()
    {
        var sb = new StringBuilder();
        sb.AppendLine("自检通过（独立 exe · WPF · Win11 Fluent）：");
        sb.AppendLine("  窗口 " + Win.Width + "x" + Win.Height + "，最小 " + Win.MinWidth + "x" + Win.MinHeight + "，可缩放=" + Win.ResizeMode);
        sb.AppendLine("  导航项 " + Nav.Items.Count + " 个（含分组标题）");
        var rows = new List<string>();
        for (int i = 0; i < Nav.Items.Count; i++)
        {
            ListBoxItem it = Nav.Items[i] as ListBoxItem;
            string key = it != null ? it.Tag as string : null;
            if (string.IsNullOrEmpty(key)) continue;
            ShowPage(key);
            rows.Add(key + "["
                + (PageChar.Visibility == Visibility.Visible ? "形象" : "")
                + (PageVoice.Visibility == Visibility.Visible ? "语音" : "")
                + (PageBubble.Visibility == Visibility.Visible ? "气泡" : "")
                + (PageUi.Visibility == Visibility.Visible ? "通用" : "")
                + (PageAnim.Visibility == Visibility.Visible ? "动画" : "")
                + (PageStats.Visibility == Visibility.Visible ? "统计" : "")
                + (PageAbout.Visibility == Visibility.Visible ? "关于" : "")
                + (PageImport.Visibility == Visibility.Visible ? "导入" : "") + "]");
        }
        sb.AppendLine("  导航切换: " + string.Join(" ", rows.ToArray()));

        // ---- 日期下拉定位自检：列表必须出现在按钮【下方】且左对齐 ----
        // （用户反馈过弹到左上角：根因是自绘 Popup 没设 PlacementTarget）
        try
        {
            ShowPage("stats");
            // 自检里窗口可能还没真正显示，必须把布局优先级全部排空，
            // 否则 _dateBtn.ActualWidth 还是 0（BuildStatsPage 刚创建的控件）
            Win.Show();
            Win.UpdateLayout();
            for (int i = 0; i < 3; i++)
            {
                Win.Dispatcher.Invoke(new Action(() => { }), DispatcherPriority.Loaded);
                Win.Dispatcher.Invoke(new Action(() => { }), DispatcherPriority.Render);
                Win.Dispatcher.Invoke(new Action(() => { }), DispatcherPriority.Background);
                Win.UpdateLayout();
            }
            if (_datePopup != null && _dateBtn != null && _dateList != null)
            {
                FillDateList();
                _datePopup.PlacementRectangle = new Rect(0, 0, _dateBtn.ActualWidth, _dateBtn.ActualHeight);
                _datePopup.IsOpen = true;
                Win.UpdateLayout();
                Win.Dispatcher.Invoke(new Action(() => { }), DispatcherPriority.Background);
                Win.UpdateLayout();

                var content = _datePopup.Child as FrameworkElement;
                // 屏幕坐标(PointToScreen)在无桌面会话下不可用（沙箱限制），
                // 所以判据改为「配置是否完备」——这才是决定定位对错的因：
                //   PlacementTarget 非空 + 已加载 → WPF 才会相对按钮定位而不是屏幕原点
                //   PlacementRectangle 与按钮同尺寸 → 列表紧贴按钮左边缘
                //   Placement=Bottom + VerticalOffset>0 → 出现在按钮下方
                //   列表宽 == 按钮宽 → 左右完全对齐
                var tgt = _datePopup.PlacementTarget as FrameworkElement;
                bool targetOk = tgt != null && tgt.ActualWidth > 0;   // 非空且已布局
                bool rectOk = Math.Abs(_datePopup.PlacementRectangle.Width - _dateBtn.ActualWidth) < 1
                           && Math.Abs(_datePopup.PlacementRectangle.Height - _dateBtn.ActualHeight) < 1;
                bool placeOk = _datePopup.Placement == PlacementMode.Bottom && _datePopup.VerticalOffset > 0;
                bool wOk = content != null && Math.Abs(content.ActualWidth - _dateBtn.ActualWidth) <= 2;
                bool all = targetOk && rectOk && placeOk && wOk;
                sb.AppendLine("  日期下拉: " + _dateList.Items.Count + "项"
                    + " 按钮=" + (int)_dateBtn.ActualWidth + "x" + (int)_dateBtn.ActualHeight
                    + " 列表=" + (content != null ? (int)content.ActualWidth + "x" + (int)content.ActualHeight : "?"));
                sb.AppendLine("            锚点已设=" + targetOk + " 矩形同步=" + rectOk
                    + " 贴下方=" + placeOk + " 等宽=" + wOk + (all ? "  ✓ 定位正确" : "  ✗"));
                _datePopup.IsOpen = false;
            }
            else sb.AppendLine("  日期下拉: 控件未就绪 ✗");
        }
        catch (Exception ex) { sb.AppendLine("  日期下拉自检异常: " + ex.Message); }
        sb.AppendLine("  角色候选 " + ChList.Count + "，字体候选 " + CmbFont.Items.Count);
        sb.AppendLine("  大小=" + (int)SldSize.Value + "px  点击出声=" + (ChkClick.IsChecked == true)
            + "  气泡=" + (ChkBub.IsChecked == true) + " 高" + (int)SldBub.Value);
        sb.AppendLine("  音量=" + (int)SldVol.Value + "%  试听按钮=" + (BtnPreview != null));
        sb.AppendLine("  气泡: 样式=" + _bubbleStyleSel + " 颜色=#" + HexOf(_bubbleColorSel)
            + " 字号=" + CurFontPt.ToString("0.0", CultureInfo.InvariantCulture) + "pt"
            + "  外框文件 " + ListBubbleFrames().Count + " 个");
        sb.AppendLine("  文字颜色=" + (string.IsNullOrEmpty(_bubbleInkSel) ? "auto" : "#" + _bubbleInkSel));
        sb.AppendLine("  主题色=#" + HexOf(_themeColor) + "  开机自启动=" + AutoStartEnabled());
        var animInfo = new List<string>();
        foreach (AnimCard c in AnimCards)
            animInfo.Add(c.Key + "=" + (c.Toggle.IsChecked == true ? "开" : "关") + "/" + (int)c.Amp.Value + "%/" + (int)c.Speed.Value + "%");
        sb.AppendLine("  动画卡片 " + AnimCards.Count + " 张: " + string.Join(" ", animInfo.ToArray()));
        var sfxNames = new List<string>();
        foreach (object o in CmbSet.Items) sfxNames.Add(o.ToString());
        sb.AppendLine("  音效组 " + CmbSet.Items.Count + " 个: " + string.Join(" / ", sfxNames.ToArray()));
        sb.AppendLine("  台词库 " + VoiceLines.Count + " 条；点击模式=" + (CmbClickMode.SelectedIndex == 1 ? "顺序" : "随机"));
        sb.AppendLine("  按键列表行=" + _keyRows.Count + "  软件行=" + _appRows.Count + "  实时定时器=" + (_liveTimer != null));
        sb.AppendLine("  立绘预览=" + (ImgChar.Source != null) + "  大小预览=" + (ImgSize.Source != null) + "  气泡预览=" + (ImgBub.Source != null));
        // 未保存提示：动一下音量，看快照能不能识别变化、还原后是否归零
        try
        {
            string snap0 = Snapshot();
            int v0 = (int)SldVol.Value;
            SldVol.Value = v0 == 100 ? 88 : 100;
            bool saw = Snapshot() != snap0;
            SldVol.Value = v0;
            bool back = Snapshot() == snap0;
            sb.AppendLine("  未保存检测: 识别改动=" + saw + "  还原归零=" + back
                + "  提示标签=" + (LblDirty != null));
        }
        catch (Exception ex) { sb.AppendLine("  未保存检测异常: " + ex.Message); }
        var icoImg = (Image)Win.FindName("imgIcon");
        sb.AppendLine("  窗口图标=" + (Win.Icon != null) + "  标题栏图标=" + (icoImg != null && icoImg.Source != null));
        // 通用设置 → 桌面显示开关（没有它就没法关掉桌宠）
        ShowPage("ui");
        sb.AppendLine("  桌面显示开关=" + (ChkShowPet != null
            ? (ChkShowPet.IsChecked == true ? "已打开" : "已关闭") : "✗ 未创建"));
        string elog = Path.Combine(Here, "pet-settings-err.log");
        int errs = File.Exists(elog) ? File.ReadAllLines(elog).Length : 0;
        sb.AppendLine("  错误日志条数 = " + errs + (errs == 0 ? " ✓ 无异常" : " ✗"));
        string outp = Path.Combine(Here, "pet-settings-selftest.txt");
        try { File.WriteAllText(outp, sb.ToString(), new UTF8Encoding(false)); } catch (Exception) { }
        Console.WriteLine(sb.ToString());
        Win.Close();
    }

    // ---------------- XAML ----------------
    const string XamlText = @"<Window xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""
        xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml""
        Title=""Q Pet · 设置"" Width=""960"" Height=""740"" MinWidth=""780"" MinHeight=""540""
        WindowStartupLocation=""CenterScreen"" Background=""#F3F3F3""
        WindowStyle=""None"" ResizeMode=""CanResize""
        FontFamily=""Microsoft YaHei UI, Segoe UI"" FontSize=""13""
        UseLayoutRounding=""True"" SnapsToDevicePixels=""True"">
  <WindowChrome.WindowChrome>
    <WindowChrome CaptionHeight=""48"" ResizeBorderThickness=""6"" GlassFrameThickness=""0""
                  CornerRadius=""0"" UseAeroCaptionButtons=""False""/>
  </WindowChrome.WindowChrome>
  <Window.Resources>
    <SolidColorBrush x:Key=""AppBg"" Color=""#F3F3F3""/>
    <SolidColorBrush x:Key=""CardBg"" Color=""#FFFFFF""/>
    <SolidColorBrush x:Key=""CardStroke"" Color=""#E5E5E5""/>
    <SolidColorBrush x:Key=""RowStroke"" Color=""#EDEDED""/>
    <SolidColorBrush x:Key=""TextPrimary"" Color=""#E4000000""/>
    <SolidColorBrush x:Key=""TextSecondary"" Color=""#99000000""/>
    <SolidColorBrush x:Key=""Accent"" Color=""#005FB8""/>
    <SolidColorBrush x:Key=""Fill"" Color=""#FBFBFB""/>
    <SolidColorBrush x:Key=""FillStroke"" Color=""#D6D6D6""/>
    <SolidColorBrush x:Key=""FillHover"" Color=""#F0F0F0""/>

    <Style x:Key=""H1"" TargetType=""TextBlock"">
      <Setter Property=""FontSize"" Value=""28""/><Setter Property=""FontWeight"" Value=""SemiBold""/>
      <Setter Property=""Foreground"" Value=""{StaticResource TextPrimary}""/>
    </Style>
    <Style x:Key=""H2"" TargetType=""TextBlock"">
      <Setter Property=""FontSize"" Value=""14""/><Setter Property=""FontWeight"" Value=""SemiBold""/>
      <Setter Property=""Foreground"" Value=""{StaticResource TextPrimary}""/>
    </Style>
    <Style x:Key=""Sub"" TargetType=""TextBlock"">
      <Setter Property=""FontSize"" Value=""12""/><Setter Property=""Foreground"" Value=""{StaticResource TextSecondary}""/>
      <Setter Property=""TextWrapping"" Value=""Wrap""/>
    </Style>
    <Style x:Key=""Card"" TargetType=""Border"">
      <Setter Property=""Background"" Value=""{StaticResource CardBg}""/>
      <Setter Property=""BorderBrush"" Value=""{StaticResource CardStroke}""/>
      <Setter Property=""BorderThickness"" Value=""1""/><Setter Property=""CornerRadius"" Value=""4""/>
      <Setter Property=""Padding"" Value=""20,16""/><Setter Property=""Margin"" Value=""0,0,0,12""/>
    </Style>
    <Style x:Key=""Divider"" TargetType=""Border"">
      <Setter Property=""Height"" Value=""1""/><Setter Property=""Background"" Value=""{StaticResource RowStroke}""/>
      <Setter Property=""Margin"" Value=""0,12""/>
    </Style>

    <!-- 键盘焦点圈（只在键盘导航时出现，画在控件外围，不影响布局）-->
    <Style x:Key=""FocusRing"">
      <Setter Property=""Control.Template"">
        <Setter.Value><ControlTemplate>
          <Rectangle Margin=""-3"" StrokeThickness=""2"" RadiusX=""5"" RadiusY=""5""
                     Stroke=""{DynamicResource Accent}"" SnapsToDevicePixels=""True""/>
        </ControlTemplate></Setter.Value>
      </Setter>
    </Style>

    <!-- 输入框：和下拉框同一套圆角/描边，聚焦时描边变主题色（不改粗细，免得内容抖）-->
    <Style TargetType=""TextBox"">
      <Setter Property=""Height"" Value=""34""/><Setter Property=""Padding"" Value=""11,0""/>
      <Setter Property=""VerticalContentAlignment"" Value=""Center""/>
      <Setter Property=""Foreground"" Value=""{StaticResource TextPrimary}""/>
      <Setter Property=""CaretBrush"" Value=""{DynamicResource Accent}""/>
      <Setter Property=""FocusVisualStyle"" Value=""{StaticResource FocusRing}""/>
      <Setter Property=""Template""><Setter.Value><ControlTemplate TargetType=""TextBox"">
        <Border x:Name=""b"" Background=""White"" BorderBrush=""{StaticResource FillStroke}""
                BorderThickness=""1"" CornerRadius=""4"">
          <ScrollViewer x:Name=""PART_ContentHost"" Margin=""{TemplateBinding Padding}""
                        VerticalAlignment=""Center"" Focusable=""False""/>
        </Border>
        <ControlTemplate.Triggers>
          <Trigger Property=""IsMouseOver"" Value=""True""><Setter TargetName=""b"" Property=""BorderBrush"" Value=""#C4C4C4""/></Trigger>
          <Trigger Property=""IsKeyboardFocused"" Value=""True"">
            <Setter TargetName=""b"" Property=""BorderBrush"" Value=""{DynamicResource Accent}""/>
          </Trigger>
          <Trigger Property=""IsEnabled"" Value=""False"">
            <Setter TargetName=""b"" Property=""Background"" Value=""#F5F5F5""/>
            <Setter Property=""Foreground"" Value=""{StaticResource TextSecondary}""/>
          </Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate></Setter.Value></Setter>
    </Style>

    <Style x:Key=""PrimaryButton"" TargetType=""Button"">
      <Setter Property=""Background"" Value=""{DynamicResource Accent}""/>
      <Setter Property=""Foreground"" Value=""White""/>
      <Setter Property=""Height"" Value=""32""/><Setter Property=""Padding"" Value=""18,0""/>
      <Setter Property=""Cursor"" Value=""Hand""/><Setter Property=""BorderThickness"" Value=""0""/>
      <Setter Property=""FocusVisualStyle"" Value=""{StaticResource FocusRing}""/>
      <Setter Property=""Template""><Setter.Value><ControlTemplate TargetType=""Button"">
        <Border x:Name=""b"" Background=""{TemplateBinding Background}"" CornerRadius=""4"">
          <ContentPresenter HorizontalAlignment=""Center"" VerticalAlignment=""Center"" Margin=""{TemplateBinding Padding}""/>
        </Border>
        <ControlTemplate.Triggers>
          <Trigger Property=""IsMouseOver"" Value=""True""><Setter TargetName=""b"" Property=""Opacity"" Value=""0.9""/></Trigger>
          <Trigger Property=""IsPressed"" Value=""True""><Setter TargetName=""b"" Property=""Opacity"" Value=""0.8""/></Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate></Setter.Value></Setter>
    </Style>
    <Style x:Key=""SecondaryButton"" TargetType=""Button"">
      <Setter Property=""Background"" Value=""{StaticResource Fill}""/>
      <Setter Property=""Foreground"" Value=""{StaticResource TextPrimary}""/>
      <Setter Property=""Height"" Value=""32""/><Setter Property=""Padding"" Value=""16,0""/>
      <Setter Property=""Cursor"" Value=""Hand""/><Setter Property=""BorderThickness"" Value=""0""/>
      <Setter Property=""FocusVisualStyle"" Value=""{StaticResource FocusRing}""/>
      <Setter Property=""Template""><Setter.Value><ControlTemplate TargetType=""Button"">
        <Border x:Name=""b"" Background=""{TemplateBinding Background}"" BorderBrush=""{StaticResource FillStroke}""
                BorderThickness=""1"" CornerRadius=""4"">
          <ContentPresenter HorizontalAlignment=""Center"" VerticalAlignment=""Center"" Margin=""{TemplateBinding Padding}""/>
        </Border>
        <ControlTemplate.Triggers>
          <Trigger Property=""IsMouseOver"" Value=""True""><Setter TargetName=""b"" Property=""Background"" Value=""{StaticResource FillHover}""/></Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate></Setter.Value></Setter>
    </Style>
    <Style x:Key=""GhostButton"" TargetType=""Button"">
      <Setter Property=""Background"" Value=""Transparent""/>
      <Setter Property=""Foreground"" Value=""{StaticResource TextSecondary}""/>
      <Setter Property=""Height"" Value=""32""/><Setter Property=""Padding"" Value=""14,0""/>
      <Setter Property=""Cursor"" Value=""Hand""/><Setter Property=""BorderThickness"" Value=""0""/>
      <Setter Property=""FocusVisualStyle"" Value=""{StaticResource FocusRing}""/>
      <Setter Property=""Template""><Setter.Value><ControlTemplate TargetType=""Button"">
        <Border x:Name=""b"" Background=""{TemplateBinding Background}"" CornerRadius=""4"">
          <ContentPresenter HorizontalAlignment=""Center"" VerticalAlignment=""Center"" Margin=""{TemplateBinding Padding}""/>
        </Border>
        <ControlTemplate.Triggers>
          <Trigger Property=""IsMouseOver"" Value=""True"">
            <Setter TargetName=""b"" Property=""Background"" Value=""#14C42B1C""/>
            <Setter Property=""Foreground"" Value=""#C42B1C""/>
          </Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate></Setter.Value></Setter>
    </Style>
    <!-- 标题栏按钮：细线图标 + 悬停底色 -->
    <Style x:Key=""CaptionButton"" TargetType=""Button"">
      <Setter Property=""Width"" Value=""46""/><Setter Property=""Height"" Value=""48""/>
      <Setter Property=""Background"" Value=""Transparent""/><Setter Property=""Foreground"" Value=""{StaticResource TextPrimary}""/>
      <Setter Property=""FontFamily"" Value=""Segoe MDL2 Assets""/><Setter Property=""FontSize"" Value=""10""/>
      <Setter Property=""Cursor"" Value=""Hand""/><Setter Property=""BorderThickness"" Value=""0""/>
      <Setter Property=""FocusVisualStyle"" Value=""{StaticResource FocusRing}""/>
      <Setter Property=""Template""><Setter.Value><ControlTemplate TargetType=""Button"">
        <Border x:Name=""b"" Background=""{TemplateBinding Background}"">
          <ContentPresenter HorizontalAlignment=""Center"" VerticalAlignment=""Center""/>
        </Border>
        <ControlTemplate.Triggers>
          <Trigger Property=""IsMouseOver"" Value=""True""><Setter TargetName=""b"" Property=""Background"" Value=""#0A000000""/></Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate></Setter.Value></Setter>
    </Style>
    <Style x:Key=""CaptionClose"" TargetType=""Button"" BasedOn=""{StaticResource CaptionButton}"">
      <Setter Property=""Template""><Setter.Value><ControlTemplate TargetType=""Button"">
        <Border x:Name=""b"" Background=""{TemplateBinding Background}"">
          <ContentPresenter HorizontalAlignment=""Center"" VerticalAlignment=""Center""/>
        </Border>
        <ControlTemplate.Triggers>
          <Trigger Property=""IsMouseOver"" Value=""True"">
            <Setter TargetName=""b"" Property=""Background"" Value=""#E81123""/>
            <Setter Property=""Foreground"" Value=""White""/>
          </Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate></Setter.Value></Setter>
    </Style>

    <!-- 滚动条：细窄圆角，thumb 用主题色，悬停加深，无箭头按钮。
         三个必须遵守的点（都是实测踩出来的）：
         (1) 绝不能给 ScrollBar 设 Width/Height 的 Setter。纵向滚动条的「高度」必须留空
              让它拉伸撑满，设成 8 会把整个滚动条压成 8px 高的小方块，完全没法用。
              「厚度 8」只由模板里 Orientation Trigger 给。
         (2) Track 必须有 Decrease/IncreaseRepeatButton，否则点轨道空白处翻不了页。
         (3) 纵向必须 IsDirectionReversed=True，否则往下滚滑块往上走（方向反）。
         配色：中性灰阶（#C2C6CE / 悬停 #9AA0AA / 拖动 #868C96），刻意不用主题色——
         滚动条是「工具」不是「内容」，用蓝色会在换肤时被主题污染且视觉过重。 -->
    <Style TargetType=""ScrollBar"">
      <Setter Property=""Template""><Setter.Value><ControlTemplate TargetType=""ScrollBar"">
        <Grid x:Name=""Bg"" Background=""Transparent"">
          <Track x:Name=""PART_Track"" Focusable=""False"" IsDirectionReversed=""False"">
            <Track.DecreaseRepeatButton>
              <RepeatButton Command=""ScrollBar.PageUpCommand"" Focusable=""False"">
                <RepeatButton.Template><ControlTemplate TargetType=""RepeatButton"">
                  <Border Background=""Transparent""/>
                </ControlTemplate></RepeatButton.Template>
              </RepeatButton>
            </Track.DecreaseRepeatButton>
            <Track.Thumb>
              <Thumb x:Name=""Thumb"" Background=""#C2C6CE"">
                <Thumb.Template><ControlTemplate TargetType=""Thumb"">
                  <Border x:Name=""Bd"" Background=""{TemplateBinding Background}""
                          CornerRadius=""4"" SnapsToDevicePixels=""True""/>
                  <ControlTemplate.Triggers>
                    <Trigger Property=""IsMouseOver"" Value=""True"">
                      <Setter TargetName=""Bd"" Property=""Background"" Value=""#9AA0AA""/>
                    </Trigger>
                    <Trigger Property=""IsDragging"" Value=""True"">
                      <Setter TargetName=""Bd"" Property=""Background"" Value=""#868C96""/>
                    </Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate></Thumb.Template>
              </Thumb>
            </Track.Thumb>
            <Track.IncreaseRepeatButton>
              <RepeatButton Command=""ScrollBar.PageDownCommand"" Focusable=""False"">
                <RepeatButton.Template><ControlTemplate TargetType=""RepeatButton"">
                  <Border Background=""Transparent""/>
                </ControlTemplate></RepeatButton.Template>
              </RepeatButton>
            </Track.IncreaseRepeatButton>
          </Track>
        </Grid>
        <ControlTemplate.Triggers>
          <Trigger Property=""Orientation"" Value=""Horizontal"">
            <Setter TargetName=""PART_Track"" Property=""Height"" Value=""8""/>
            <Setter TargetName=""PART_Track"" Property=""VerticalAlignment"" Value=""Bottom""/>
            <Setter TargetName=""Thumb"" Property=""Height"" Value=""8""/>
            <Setter TargetName=""Thumb"" Property=""MinWidth"" Value=""28""/>
            <Setter TargetName=""Thumb"" Property=""VerticalAlignment"" Value=""Center""/>
          </Trigger>
          <Trigger Property=""Orientation"" Value=""Vertical"">
            <!-- 纵向要反向，否则往下滚滑块往上走 -->
            <Setter TargetName=""PART_Track"" Property=""IsDirectionReversed"" Value=""True""/>
            <Setter TargetName=""PART_Track"" Property=""Width"" Value=""8""/>
            <Setter TargetName=""PART_Track"" Property=""HorizontalAlignment"" Value=""Right""/>
            <Setter TargetName=""Thumb"" Property=""Width"" Value=""8""/>
            <!-- 内容很长时滑块不能缩成一条线，否则抓不住 -->
            <Setter TargetName=""Thumb"" Property=""MinHeight"" Value=""28""/>
            <Setter TargetName=""Thumb"" Property=""HorizontalAlignment"" Value=""Right""/>
          </Trigger>
          <Trigger Property=""IsEnabled"" Value=""False"">
            <Setter TargetName=""Bg"" Property=""Opacity"" Value=""0.3""/>
          </Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate></Setter.Value></Setter>
    </Style>

    <!-- 下拉框：高 36（避免 j/g 这类字母被裁），可编辑时像 Word 一样能直接敲
         ⚠ 三个坑（都实测踩过，别再犯）：
         (1) IsEditable=True 时，隐式 TextBox 样式（Height=34 + 白底 + 边框）会作用到
             PART_EditableTextBox 上 → 变成「框中框」。必须在它内部再套一套透明模板。
         (2) 文字左边距要留够，否则会被右侧下拉箭头压住。
         (3) 箭头用中性灰 #6B6B6B，不要用 TextPrimary（太重，会抢文字的视觉焦点）。 -->
    <Style TargetType=""ComboBox"">
      <Setter Property=""Height"" Value=""36""/><Setter Property=""MinWidth"" Value=""120""/>
      <Setter Property=""Foreground"" Value=""{StaticResource TextPrimary}""/>
      <Setter Property=""FocusVisualStyle"" Value=""{StaticResource FocusRing}""/>
      <Setter Property=""Template""><Setter.Value><ControlTemplate TargetType=""ComboBox"">
        <Grid>
          <Border x:Name=""bg"" Background=""{StaticResource Fill}"" BorderBrush=""{StaticResource FillStroke}""
                  BorderThickness=""1"" CornerRadius=""4"" SnapsToDevicePixels=""True"">
            <Grid>
              <Grid.ColumnDefinitions>
                <ColumnDefinition Width=""*""/>
                <ColumnDefinition Width=""Auto""/>
              </Grid.ColumnDefinitions>
              <ContentPresenter x:Name=""selBox"" Grid.Column=""0"" Margin=""12,0,8,0""
                                Content=""{TemplateBinding SelectionBoxItem}""
                                VerticalAlignment=""Center"" IsHitTestVisible=""False""/>
              <!-- 可编辑时显示这个（Word 式手输）。内部自带透明模板，避免继承隐式 TextBox 样式 -->
              <TextBox x:Name=""PART_EditableTextBox"" Grid.Column=""0"" Margin=""12,0,8,0""
                       VerticalAlignment=""Center"" Visibility=""Collapsed""
                       Background=""Transparent"" BorderThickness=""0"" Padding=""0""
                       FontSize=""{TemplateBinding FontSize}""
                       Foreground=""{TemplateBinding Foreground}""
                       CaretBrush=""{DynamicResource Accent}""
                       IsReadOnly=""{Binding IsReadOnly, RelativeSource={RelativeSource TemplatedParent}}"">
                <TextBox.Resources>
                  <Style TargetType=""TextBox"">
                    <Setter Property=""Height"" Value=""Auto""/>
                    <Setter Property=""Padding"" Value=""0""/>
                    <Setter Property=""Template""><Setter.Value><ControlTemplate TargetType=""TextBox"">
                      <Border Background=""Transparent"" BorderThickness=""0"" Padding=""0"">
                        <ScrollViewer x:Name=""PART_ContentHost"" VerticalAlignment=""Center"" Focusable=""False""/>
                      </Border>
                    </ControlTemplate></Setter.Value></Setter>
                  </Style>
                </TextBox.Resources>
              </TextBox>
              <!-- 下拉箭头：中性灰，悬停加深 -->
              <ToggleButton x:Name=""tg"" Grid.Column=""1"" Focusable=""False"" ClickMode=""Press"" Width=""30""
                            IsChecked=""{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}"">
                <ToggleButton.Template><ControlTemplate TargetType=""ToggleButton"">
                  <Border x:Name=""ab"" Background=""Transparent"" CornerRadius=""0,4,4,0"">
                    <Path x:Name=""ar"" Data=""M 0 0 L 4 4 L 8 0"" Stroke=""#6B6B6B"" StrokeThickness=""1.5""
                          HorizontalAlignment=""Center"" VerticalAlignment=""Center""
                          SnapsToDevicePixels=""True"" StrokeStartLineCap=""Round"" StrokeEndLineCap=""Round""/>
                  </Border>
                  <ControlTemplate.Triggers>
                    <Trigger Property=""IsMouseOver"" Value=""True"">
                      <Setter TargetName=""ab"" Property=""Background"" Value=""#0A000000""/>
                      <Setter TargetName=""ar"" Property=""Stroke"" Value=""#333333""/>
                    </Trigger>
                  </ControlTemplate.Triggers>
                </ControlTemplate></ToggleButton.Template>
              </ToggleButton>
            </Grid>
          </Border>
          <!-- 下拉面板：圆角 8 + 轻投影，与日期选择器同一套视觉语言 -->
          <Popup x:Name=""PART_Popup"" IsOpen=""{TemplateBinding IsDropDownOpen}""
                 Placement=""Bottom"" AllowsTransparency=""True"" Focusable=""False""
                 PopupAnimation=""Fade"" VerticalOffset=""4"">
            <Border Background=""White"" BorderBrush=""{StaticResource CardStroke}"" BorderThickness=""1""
                    CornerRadius=""8"" Margin=""0,4,0,0"" MinWidth=""{TemplateBinding ActualWidth}""
                    Padding=""4"">
              <Border.Effect>
                <DropShadowEffect BlurRadius=""18"" ShadowDepth=""3"" Opacity=""0.14"" Direction=""270""/>
              </Border.Effect>
              <ScrollViewer MaxHeight=""280""><ItemsPresenter/></ScrollViewer>
            </Border>
          </Popup>
        </Grid>
        <ControlTemplate.Triggers>
          <Trigger Property=""IsEditable"" Value=""True"">
            <Setter TargetName=""PART_EditableTextBox"" Property=""Visibility"" Value=""Visible""/>
            <Setter TargetName=""selBox"" Property=""Visibility"" Value=""Hidden""/>
          </Trigger>
          <Trigger Property=""IsMouseOver"" Value=""True"">
            <Setter TargetName=""bg"" Property=""BorderBrush"" Value=""#9A9A9A""/>
            <Setter TargetName=""bg"" Property=""Background"" Value=""White""/>
          </Trigger>
          <Trigger Property=""IsKeyboardFocusWithin"" Value=""True"">
            <Setter TargetName=""bg"" Property=""BorderBrush"" Value=""{DynamicResource Accent}""/>
            <Setter TargetName=""bg"" Property=""Background"" Value=""White""/>
          </Trigger>
          <Trigger Property=""IsEnabled"" Value=""False"">
            <Setter TargetName=""bg"" Property=""Opacity"" Value=""0.45""/>
          </Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate></Setter.Value></Setter>
    </Style>
    <Style TargetType=""ComboBoxItem"">
      <Setter Property=""Padding"" Value=""12,9""/><Setter Property=""FontSize"" Value=""13""/>
      <Setter Property=""MinHeight"" Value=""36""/>
      <Setter Property=""Cursor"" Value=""Hand""/>
      <Setter Property=""Template""><Setter.Value><ControlTemplate TargetType=""ComboBoxItem"">
        <Border x:Name=""b"" Background=""Transparent"" Padding=""{TemplateBinding Padding}"" MinHeight=""36"" CornerRadius=""4"">
          <ContentPresenter VerticalAlignment=""Center""/>
        </Border>
        <ControlTemplate.Triggers>
          <Trigger Property=""IsHighlighted"" Value=""True""><Setter TargetName=""b"" Property=""Background"" Value=""#F0F0F0""/></Trigger>
          <!-- 选中态：主题色浅底 + 主题色字 + 半粗，和日期选择器保持一致 -->
          <Trigger Property=""IsSelected"" Value=""True"">
            <Setter TargetName=""b"" Property=""Background"" Value=""#E8F1FB""/>
            <Setter Property=""Foreground"" Value=""{DynamicResource Accent}""/>
            <Setter Property=""FontWeight"" Value=""SemiBold""/>
          </Trigger>
          <Trigger Property=""IsEnabled"" Value=""False""><Setter Property=""Foreground"" Value=""#8A93A3""/></Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate></Setter.Value></Setter>
    </Style>

    <Style x:Key=""NavItem"" TargetType=""ListBoxItem"">
      <Setter Property=""Padding"" Value=""0""/><Setter Property=""Margin"" Value=""8,2""/>
      <Setter Property=""Cursor"" Value=""Hand""/><Setter Property=""Foreground"" Value=""{StaticResource TextPrimary}""/>
      <Setter Property=""Template""><Setter.Value><ControlTemplate TargetType=""ListBoxItem"">
        <Border x:Name=""b"" Background=""Transparent"" CornerRadius=""4"" Height=""40"">
          <Grid>
            <Border x:Name=""rail"" Width=""3"" HorizontalAlignment=""Left"" Background=""{DynamicResource Accent}""
                    CornerRadius=""2"" Margin=""2,8,0,8"" Visibility=""Hidden""/>
            <ContentPresenter Margin=""18,0,0,0"" VerticalAlignment=""Center""/>
          </Grid>
        </Border>
        <ControlTemplate.Triggers>
          <Trigger Property=""IsMouseOver"" Value=""True""><Setter TargetName=""b"" Property=""Background"" Value=""#0A000000""/></Trigger>
          <Trigger Property=""IsSelected"" Value=""True"">
            <Setter TargetName=""b"" Property=""Background"" Value=""#0F000000""/>
            <Setter TargetName=""rail"" Property=""Visibility"" Value=""Visible""/>
          </Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate></Setter.Value></Setter>
    </Style>

    <Style x:Key=""NavHeader"" TargetType=""ListBoxItem"">
      <Setter Property=""IsEnabled"" Value=""False""/>
      <Setter Property=""Focusable"" Value=""False""/>
      <Setter Property=""Template""><Setter.Value><ControlTemplate TargetType=""ListBoxItem"">
        <TextBlock Text=""{TemplateBinding Content}"" FontSize=""11.5"" Foreground=""{StaticResource TextSecondary}""
                   Margin=""26,16,0,4""/>
      </ControlTemplate></Setter.Value></Setter>
    </Style>

    <Style x:Key=""WinSlider"" TargetType=""Slider"">
      <Setter Property=""Height"" Value=""32""/>
      <Setter Property=""Template""><Setter.Value><ControlTemplate TargetType=""Slider"">
        <Grid VerticalAlignment=""Center"">
          <Border Height=""4"" Background=""{StaticResource FillStroke}"" CornerRadius=""2""/>
          <Track x:Name=""PART_Track"">
            <Track.DecreaseRepeatButton>
              <RepeatButton Command=""Slider.DecreaseLarge"" Focusable=""False"">
                <RepeatButton.Template><ControlTemplate TargetType=""RepeatButton"">
                  <Border Background=""{DynamicResource Accent}"" Height=""4"" CornerRadius=""2""/>
                </ControlTemplate></RepeatButton.Template>
              </RepeatButton>
            </Track.DecreaseRepeatButton>
            <Track.IncreaseRepeatButton>
              <RepeatButton Command=""Slider.IncreaseLarge"" Focusable=""False"">
                <RepeatButton.Template><ControlTemplate TargetType=""RepeatButton"">
                  <Border Background=""Transparent"" Height=""4""/>
                </ControlTemplate></RepeatButton.Template>
              </RepeatButton>
            </Track.IncreaseRepeatButton>
            <Track.Thumb>
              <Thumb Width=""16"" Height=""16"">
                <Thumb.Template><ControlTemplate TargetType=""Thumb"">
                  <Ellipse Fill=""White"" Stroke=""#B3B3B3"" StrokeThickness=""1""/>
                </ControlTemplate></Thumb.Template>
              </Thumb>
            </Track.Thumb>
          </Track>
        </Grid>
      </ControlTemplate></Setter.Value></Setter>
    </Style>
    <!-- 开关（CheckBox 的 Win11 胶囊样式）-->
    <Style x:Key=""ToggleSwitch"" TargetType=""CheckBox"">
      <Setter Property=""Cursor"" Value=""Hand""/>
      <Setter Property=""FocusVisualStyle"" Value=""{StaticResource FocusRing}""/>
      <Setter Property=""Foreground"" Value=""{StaticResource TextPrimary}""/>
      <Setter Property=""VerticalAlignment"" Value=""Center""/>
      <Setter Property=""Template""><Setter.Value><ControlTemplate TargetType=""CheckBox"">
        <StackPanel Orientation=""Horizontal"" Background=""Transparent"">
          <Grid Width=""40"" Height=""20"" VerticalAlignment=""Center"">
            <Border x:Name=""track"" CornerRadius=""10"" Background=""#D6D6D6""/>
            <Ellipse x:Name=""knob"" Width=""14"" Height=""14"" Fill=""White"" HorizontalAlignment=""Left"" Margin=""3,0,0,0""/>
          </Grid>
          <ContentPresenter Margin=""10,0,0,0"" VerticalAlignment=""Center""/>
        </StackPanel>
        <ControlTemplate.Triggers>
          <Trigger Property=""IsChecked"" Value=""True"">
            <Setter TargetName=""track"" Property=""Background"" Value=""{DynamicResource Accent}""/>
            <Setter TargetName=""knob"" Property=""HorizontalAlignment"" Value=""Right""/>
            <Setter TargetName=""knob"" Property=""Margin"" Value=""0,0,3,0""/>
          </Trigger>
          <Trigger Property=""IsMouseOver"" Value=""True"">
            <Setter TargetName=""track"" Property=""Opacity"" Value=""0.88""/>
          </Trigger>
          <Trigger Property=""IsEnabled"" Value=""False"">
            <Setter Property=""Opacity"" Value=""0.45""/>
          </Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate></Setter.Value></Setter>
    </Style>

  </Window.Resources>

  <Grid>
    <Grid.RowDefinitions><RowDefinition Height=""48""/><RowDefinition Height=""*""/></Grid.RowDefinitions>

    <!-- 自定义标题栏（替代系统那条丑边）-->
    <Grid Grid.Row=""0"" Background=""#F3F3F3"">
      <StackPanel Orientation=""Horizontal"" VerticalAlignment=""Center"" Margin=""16,0,0,0"">
        <Image x:Name=""imgIcon"" Width=""18"" Height=""18"" Margin=""0,0,8,0"" RenderOptions.BitmapScalingMode=""HighQuality""/>
        <TextBlock Text=""Q Pet"" FontWeight=""SemiBold"" FontSize=""13"" Foreground=""{StaticResource TextPrimary}""/>
        <TextBlock Text=""·  设置"" FontSize=""13"" Foreground=""{StaticResource TextSecondary}"" Margin=""6,0,0,0""/>
      </StackPanel>
      <StackPanel Orientation=""Horizontal"" HorizontalAlignment=""Right"" WindowChrome.IsHitTestVisibleInChrome=""True"">
        <Button x:Name=""btnMin"" Content=""&#xE921;"" Style=""{StaticResource CaptionButton}"" FontSize=""9""/>
        <Button x:Name=""btnMax"" Content=""&#xE922;"" Style=""{StaticResource CaptionButton}"" FontSize=""9"" WindowChrome.IsHitTestVisibleInChrome=""True""/>
        <Button x:Name=""btnClose"" Content=""&#xE8BB;"" Style=""{StaticResource CaptionClose}""
                WindowChrome.IsHitTestVisibleInChrome=""True""/>
      </StackPanel>
    </Grid>

    <!-- 外层只做布局（导航 | 内容两列），不做滚动。
         缩放功能已移除，这里曾是个 zoomScroller 会造成双层滚动嵌套、
         内层贴边时滚轮在外层空转 → 表现为「滚死了」。真正的滚动容器只有右侧的 scroller。 -->
    <Grid Grid.Row=""1"">
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width=""216""/>
        <ColumnDefinition Width=""*""/>
      </Grid.ColumnDefinitions>

      <!-- 左导航 -->
      <StackPanel Grid.Column=""0"" Margin=""0,12,0,0"">
        <TextBlock Text=""设置"" FontSize=""12"" Foreground=""{StaticResource TextSecondary}"" Margin=""24,8,0,12""/>
        <ListBox x:Name=""nav"" BorderThickness=""0"" Background=""Transparent"" ItemContainerStyle=""{StaticResource NavItem}"">
          <ListBoxItem Content=""常规"" Style=""{StaticResource NavHeader}""/>
          <ListBoxItem Content=""气泡"" Tag=""bubble""/>
          <ListBoxItem Content=""通用设置"" Tag=""ui""/>
          <ListBoxItem Content=""角色"" Style=""{StaticResource NavHeader}""/>
          <ListBoxItem Content=""形象与大小"" Tag=""char"" IsSelected=""True""/>
          <ListBoxItem Content=""动画"" Tag=""anim""/>
          <ListBoxItem Content=""语音"" Tag=""voice""/>
          <ListBoxItem Content=""导入"" Tag=""import""/>
          <ListBoxItem Content=""统计"" Style=""{StaticResource NavHeader}""/>
          <ListBoxItem Content=""使用统计"" Tag=""stats""/>
          <ListBoxItem Content=""其他"" Style=""{StaticResource NavHeader}""/>
          <ListBoxItem Content=""关于"" Tag=""about""/>
        </ListBox>
      </StackPanel>

      <!-- 右侧内容 -->
      <Grid Grid.Column=""1"">
        <Grid.RowDefinitions><RowDefinition Height=""*""/><RowDefinition Height=""Auto""/></Grid.RowDefinitions>
        <ScrollViewer x:Name=""scroller"" Grid.Row=""0"" VerticalScrollBarVisibility=""Auto"" Padding=""24,20,32,12"">
          <StackPanel>
            <!-- 形象 -->
            <StackPanel x:Name=""pageChar"">
              <TextBlock Text=""形象与大小"" Style=""{StaticResource H1}"" Margin=""0,0,0,4""/>
              <TextBlock Text=""选择角色并调整大小"" Style=""{StaticResource Sub}"" Margin=""0,0,0,20""/>
              <Border Style=""{StaticResource Card}"">
                <StackPanel>
                  <Grid>
                    <Grid.ColumnDefinitions><ColumnDefinition Width=""*""/><ColumnDefinition Width=""Auto""/></Grid.ColumnDefinitions>
                    <StackPanel VerticalAlignment=""Center"">
                      <TextBlock Text=""角色"" FontWeight=""SemiBold""/>
                      
                    </StackPanel>
                    <StackPanel Grid.Column=""1"" Orientation=""Horizontal"" VerticalAlignment=""Center"">
                      <ComboBox x:Name=""cmbChar"" Width=""260""/>
                    </StackPanel>
                  </Grid>
                  <Border Style=""{StaticResource Divider}""/>
                  <Border Background=""#FAFAFA"" CornerRadius=""4"" Padding=""12"" Height=""200"">
                    <Image x:Name=""imgChar"" Stretch=""Uniform""/>
                  </Border>
                </StackPanel>
              </Border>

              <!-- 大小（与形象合并到同一页）-->
              <Border Style=""{StaticResource Card}"">
                <StackPanel>
                  <Grid>
                    <Grid.ColumnDefinitions><ColumnDefinition Width=""*""/><ColumnDefinition Width=""260""/></Grid.ColumnDefinitions>
                    <StackPanel VerticalAlignment=""Center"">
                      <TextBlock Text=""角色高度"" FontWeight=""SemiBold""/>
                      <TextBlock Text=""160–520 像素"" Style=""{StaticResource Sub}"" Margin=""0,3,0,0""/>
                    </StackPanel>
                    <StackPanel Grid.Column=""1"" VerticalAlignment=""Center"">
                      <Slider x:Name=""sldSize"" Minimum=""180"" Maximum=""400"" TickFrequency=""20"" Style=""{StaticResource WinSlider}""/>
                      <TextBlock x:Name=""lblSize"" Text=""210 px"" FontSize=""12"" Foreground=""{StaticResource TextSecondary}""
                                 HorizontalAlignment=""Right"" Margin=""0,4,0,0""/>
                    </StackPanel>
                  </Grid>
                  <Border Style=""{StaticResource Divider}""/>
                  <Border Background=""#FAFAFA"" CornerRadius=""4"" Padding=""8"" Height=""250"">
                    <StackPanel VerticalAlignment=""Center"" HorizontalAlignment=""Center"">
                      <Image x:Name=""imgSize"" Stretch=""Uniform"" VerticalAlignment=""Center"" HorizontalAlignment=""Center""/>
                    </StackPanel>
                  </Border>
                  <TextBlock x:Name=""lblSizeCap"" Style=""{StaticResource Sub}"" Margin=""0,10,0,0"" Text=""—""/>
                </StackPanel>
              </Border>
            </StackPanel>

            <!-- 语音 -->
            <StackPanel x:Name=""pageVoice"" Visibility=""Collapsed"">
              <TextBlock Text=""语音"" Style=""{StaticResource H1}"" Margin=""0,0,0,4""/>
              <TextBlock Text=""台词与音效"" Style=""{StaticResource Sub}"" Margin=""0,0,0,20""/>

              <Border Style=""{StaticResource Card}"">
                <StackPanel>
                  <Grid>
                    <Grid.ColumnDefinitions><ColumnDefinition Width=""*""/><ColumnDefinition Width=""Auto""/></Grid.ColumnDefinitions>
                    <StackPanel VerticalAlignment=""Center"">
                      <TextBlock Text=""音效组"" FontWeight=""SemiBold""/>
                      
                    </StackPanel>
                    <ComboBox x:Name=""cmbSet"" Grid.Column=""1"" Width=""240"" VerticalAlignment=""Center""/>
                  </Grid>
                  <Border Style=""{StaticResource Divider}""/>
                  <Grid>
                    <Grid.ColumnDefinitions><ColumnDefinition Width=""*""/><ColumnDefinition Width=""Auto""/></Grid.ColumnDefinitions>
                    <StackPanel VerticalAlignment=""Center"">
                      <TextBlock Text=""点击模式"" FontWeight=""SemiBold""/>
                      <TextBlock Text=""点她时的说话方式"" Style=""{StaticResource Sub}"" Margin=""0,3,0,0""/>
                    </StackPanel>
                    <ComboBox x:Name=""cmbClickMode"" Grid.Column=""1"" Width=""180"" VerticalAlignment=""Center""/>
                  </Grid>
                  <Border Style=""{StaticResource Divider}""/>
                  <CheckBox x:Name=""chkClick"" Content=""点击她时播放语音（不勾选则只有动画）"" FontSize=""13"" Style=""{StaticResource ToggleSwitch}""/>
                </StackPanel>
              </Border>

              <Border Style=""{StaticResource Card}"">
                <StackPanel>
                  <Grid>
                    <Grid.ColumnDefinitions><ColumnDefinition Width=""*""/><ColumnDefinition Width=""230""/></Grid.ColumnDefinitions>
                    <StackPanel VerticalAlignment=""Center"">
                      <TextBlock Text=""音量"" FontWeight=""SemiBold""/>
                      <!-- 功能性提示：静音警告 / 试听状态由代码写入（不是说明文字），所以初始留空 -->
                      <TextBlock x:Name=""lblVoiceHint"" Text="""" Style=""{StaticResource Sub}"" Margin=""0,3,0,0""/>
                      
                    </StackPanel>
                    <StackPanel Grid.Column=""1"" VerticalAlignment=""Center"">
                      <Slider x:Name=""sldVol"" Minimum=""0"" Maximum=""100"" TickFrequency=""5"" Style=""{StaticResource WinSlider}""/>
                      <TextBlock x:Name=""lblVol"" Text=""100%"" FontSize=""12"" Foreground=""{StaticResource TextSecondary}""
                                 HorizontalAlignment=""Right"" Margin=""0,4,0,0""/>
                    </StackPanel>
                  </Grid>
                  <Border Style=""{StaticResource Divider}""/>
                  <StackPanel Orientation=""Horizontal"">
                    <Button x:Name=""btnPreview"" Content=""试听"" Style=""{StaticResource SecondaryButton}""/>
                    
                  </StackPanel>
                </StackPanel>
              </Border>


              <Border Style=""{StaticResource Card}"">
                <StackPanel>
                  <Grid>
                    <Grid.ColumnDefinitions><ColumnDefinition Width=""*""/><ColumnDefinition Width=""Auto""/></Grid.ColumnDefinitions>
                    <StackPanel Orientation=""Horizontal"">
                      <TextBlock Text=""台词库"" FontWeight=""SemiBold""/>
                      <TextBlock x:Name=""lblVoiceCount"" Text=""—"" Style=""{StaticResource Sub}"" Margin=""10,0,0,0"" VerticalAlignment=""Center""/>
                    </StackPanel>
                    <Button x:Name=""btnRandomLine"" Content=""随机来一句"" Grid.Column=""1"" Style=""{StaticResource SecondaryButton}""/>
                  </Grid>
                  
                  <Grid Margin=""0,8,0,0"">
                    <TextBox x:Name=""txtVoiceSearch"" Height=""34"" Padding=""11,0"" VerticalContentAlignment=""Center""/>
                    <TextBlock x:Name=""phVoiceSearch"" Text=""搜索台词…"" IsHitTestVisible=""False""
                               Margin=""13,0,0,0"" VerticalAlignment=""Center"" FontSize=""12.5""
                               Foreground=""#767C86""/>
                  </Grid>
                  <Border Style=""{StaticResource Divider}""/>
                  <Border Background=""#FCFCFD"" BorderBrush=""#EEF0F4"" BorderThickness=""1"" CornerRadius=""6"" Padding=""10,6"">
                    <ScrollViewer Height=""300"" VerticalScrollBarVisibility=""Auto"" HorizontalScrollBarVisibility=""Disabled"">
                      <StackPanel x:Name=""voiceList""/>
                    </ScrollViewer>
                  </Border>
                </StackPanel>
              </Border>
            </StackPanel>

            <StackPanel x:Name=""pageAnim"" Visibility=""Collapsed""/>

            <!-- UI 设置（占位，先不加功能）-->
            <StackPanel x:Name=""pageUi"" Visibility=""Collapsed"">
              <TextBlock Text=""通用设置"" Style=""{StaticResource H1}"" Margin=""0,0,0,4""/>
              <TextBlock Text=""显示、外观与启动"" Style=""{StaticResource Sub}"" Margin=""0,0,0,20""/>
              <StackPanel x:Name=""uiContent""/>
            </StackPanel>

            <!-- 气泡 -->
            <StackPanel x:Name=""pageBubble"" Visibility=""Collapsed"">
              <TextBlock Text=""气泡"" Style=""{StaticResource H1}"" Margin=""0,0,0,4""/>
              <TextBlock Text=""对话气泡"" Style=""{StaticResource Sub}"" Margin=""0,0,0,20""/>
              <Border Style=""{StaticResource Card}"">
                <StackPanel>
                  <CheckBox x:Name=""chkBub"" Content=""启用气泡"" FontSize=""13"" Style=""{StaticResource ToggleSwitch}""/>
                  <Border Style=""{StaticResource Divider}""/>
                  <Grid>
                    <Grid.ColumnDefinitions><ColumnDefinition Width=""*""/><ColumnDefinition Width=""220""/></Grid.ColumnDefinitions>
                    <StackPanel VerticalAlignment=""Center"">
                      <TextBlock Text=""高度"" FontWeight=""SemiBold""/>
                      <TextBlock Text=""气泡区域高度（像素）"" Style=""{StaticResource Sub}"" Margin=""0,3,0,0""/>
                    </StackPanel>
                    <StackPanel Grid.Column=""1"" VerticalAlignment=""Center"">
                      <Slider x:Name=""sldBub"" Minimum=""70"" Maximum=""220"" TickFrequency=""10"" Style=""{StaticResource WinSlider}""/>
                      <TextBlock x:Name=""lblBub"" Text=""110 px"" FontSize=""12"" Foreground=""{StaticResource TextSecondary}""
                                 HorizontalAlignment=""Right"" Margin=""0,4,0,0""/>
                    </StackPanel>
                  </Grid>
                  <Border Style=""{StaticResource Divider}""/>
                  <Grid>
                    <Grid.ColumnDefinitions><ColumnDefinition Width=""*""/><ColumnDefinition Width=""Auto""/></Grid.ColumnDefinitions>
                    <StackPanel VerticalAlignment=""Center"">
                      <TextBlock Text=""主动冒泡"" FontWeight=""SemiBold""/>
                      <TextBlock Text=""自动冒泡间隔"" Style=""{StaticResource Sub}"" Margin=""0,3,0,0""/>
                    </StackPanel>
                    <ComboBox x:Name=""cmbEvery"" Grid.Column=""1"" Width=""180"" VerticalAlignment=""Center""/>
                  </Grid>
                  <Border Style=""{StaticResource Divider}""/>
                  <Grid Margin=""0,8,0,4"">
                    <Grid.ColumnDefinitions><ColumnDefinition Width=""*""/><ColumnDefinition Width=""Auto""/></Grid.ColumnDefinitions>
                    <StackPanel VerticalAlignment=""Center"">
                      <TextBlock Text=""字体"" FontWeight=""SemiBold""/>
                      <TextBlock Text=""气泡文字使用的字体"" Style=""{StaticResource Sub}"" Margin=""0,3,0,0""/>
                    </StackPanel>
                    <ComboBox x:Name=""cmbFont"" Grid.Column=""1"" Width=""220"" Height=""42"" VerticalAlignment=""Center""/>
                  </Grid>
                  <Border Style=""{StaticResource Divider}""/>
                  <Grid Margin=""0,8,0,4"">
                    <Grid.ColumnDefinitions><ColumnDefinition Width=""*""/><ColumnDefinition Width=""Auto""/></Grid.ColumnDefinitions>
                    <StackPanel VerticalAlignment=""Center"">
                      <TextBlock Text=""颜色"" FontWeight=""SemiBold""/>
                      
                    </StackPanel>
                    <Button x:Name=""btnBubbleColor"" Content=""自定义颜色…"" Grid.Column=""1"" Style=""{StaticResource SecondaryButton}"" VerticalAlignment=""Center""/>
                  </Grid>
                  <WrapPanel x:Name=""bubbleColorHost"" Margin=""0,10,0,0""/>
                  <Border Style=""{StaticResource Divider}""/>
                  <Grid Margin=""0,8,0,4"">
                    <Grid.ColumnDefinitions><ColumnDefinition Width=""*""/><ColumnDefinition Width=""Auto""/></Grid.ColumnDefinitions>
                    <StackPanel VerticalAlignment=""Center"">
                      <TextBlock Text=""文字颜色"" FontWeight=""SemiBold""/>
                      <TextBlock Text=""留空则自动选色"" Style=""{StaticResource Sub}"" Margin=""0,3,0,0""/>
                    </StackPanel>
                    <Button x:Name=""btnBubbleInk"" Content=""自定义文字颜色…"" Grid.Column=""1"" Style=""{StaticResource SecondaryButton}"" VerticalAlignment=""Center""/>
                  </Grid>
                  <WrapPanel x:Name=""bubbleInkHost"" Margin=""0,10,0,0""/>
                  <Border Style=""{StaticResource Divider}""/>
                  <Grid Margin=""0,8,0,4"">
                    <Grid.ColumnDefinitions><ColumnDefinition Width=""*""/><ColumnDefinition Width=""220""/></Grid.ColumnDefinitions>
                    <StackPanel VerticalAlignment=""Center"">
                      <TextBlock Text=""字号"" FontWeight=""SemiBold""/>
                      <TextBlock Text=""可直接输入"" Style=""{StaticResource Sub}"" Margin=""0,3,0,0""/>
                    </StackPanel>
                    <StackPanel Grid.Column=""1"" Orientation=""Horizontal"" VerticalAlignment=""Center"" HorizontalAlignment=""Right"">
                      <ComboBox x:Name=""cmbFontSize"" Width=""150"" IsEditable=""True"" VerticalAlignment=""Center""
                                HorizontalContentAlignment=""Left""
                                ToolTip=""字号（pt）。字号固定，不随气泡高度缩放；长文本不会自动滚动，鼠标在气泡上滚轮手动查看。""/>
                      <!-- 单位紧贴下拉框，说明它属于同一个量；拉开距离会显得是两件事 -->
                      <TextBlock x:Name=""lblFontSize"" Text=""pt"" FontSize=""12"" Foreground=""{StaticResource TextSecondary}""
                                 VerticalAlignment=""Center"" Margin=""6,0,0,0""/>
                    </StackPanel>
                  </Grid>
                </StackPanel>
              </Border>
              <Border Style=""{StaticResource Card}"">
                <StackPanel>
                  <Grid>
                    <Grid.ColumnDefinitions><ColumnDefinition Width=""*""/><ColumnDefinition Width=""Auto""/></Grid.ColumnDefinitions>
                    <StackPanel VerticalAlignment=""Center"">
                      <TextBlock Text=""外框样式"" FontWeight=""SemiBold""/>
                      <TextBlock Text=""内置 4 种，也可自定义"" Style=""{StaticResource Sub}"" Margin=""0,3,0,0""/>
                    </StackPanel>
                    <Button x:Name=""btnUpBubble"" Content=""上传外框…"" Grid.Column=""1"" Style=""{StaticResource SecondaryButton}"" VerticalAlignment=""Center""/>
                  </Grid>
                  <Border Style=""{StaticResource Divider}""/>
                  <WrapPanel x:Name=""bubbleStyleHost""/>
                  <TextBlock x:Name=""lblBubHint"" Style=""{StaticResource Sub}"" Margin=""0,2,0,0""
                             Text=""四边画框效果最好""/>
                </StackPanel>
              </Border>
              <Border Style=""{StaticResource Card}"">
                <StackPanel>
                  <TextBlock Text=""预览"" Style=""{StaticResource H2}"" Margin=""0,0,0,4""/>
                  <TextBlock Text=""按真实比例预览"" Style=""{StaticResource Sub}"" Margin=""0,0,0,12""/>
                  <Border Background=""#FAFAFA"" CornerRadius=""4"" Padding=""12"" Height=""380"">
                    <Image x:Name=""imgBub"" Stretch=""Uniform""/>
                  </Border>
                  <TextBlock x:Name=""lblBubSize"" Style=""{StaticResource Sub}"" Margin=""0,8,0,0"" Text=""—""/>
                </StackPanel>
              </Border>
            </StackPanel>

            <!-- 统计（内容由 C# 动态生成）-->
            <StackPanel x:Name=""pageStats"" Visibility=""Collapsed""/>

            <!-- 关于（内容由 BuildAboutPage 生成） -->
            <StackPanel x:Name=""pageAbout"" Visibility=""Collapsed""/>

            <!-- 新建角色：一个角色一个文件夹，把用户给的东西传进去 -->
            <StackPanel x:Name=""pageImport"" Visibility=""Collapsed"">
              <TextBlock Text=""新建角色"" Style=""{StaticResource H1}"" Margin=""0,0,0,4""/>
              <TextBlock Text=""新建一个角色包"" Style=""{StaticResource Sub}"" Margin=""0,0,0,20""/>
              <Border Style=""{StaticResource Card}"">
                <StackPanel>
                  <TextBlock Text=""角色名称"" FontWeight=""SemiBold""/>
                  <TextBlock Text=""角色名"" Style=""{StaticResource Sub}"" Margin=""0,3,0,8""/>
                  <TextBox x:Name=""txtName"" Height=""34"" Padding=""10,0"" VerticalContentAlignment=""Center"" MaxLength=""40""/>
                  <Border Style=""{StaticResource Divider}""/>
                  <Grid>
                    <Grid.ColumnDefinitions><ColumnDefinition Width=""*""/><ColumnDefinition Width=""Auto""/></Grid.ColumnDefinitions>
                    <StackPanel VerticalAlignment=""Center"">
                      <TextBlock Text=""角色图像"" FontWeight=""SemiBold""/>
                      <TextBlock Text=""透明背景 PNG"" Style=""{StaticResource Sub}"" Margin=""0,3,0,0""/>
                    </StackPanel>
                    <Button x:Name=""btnPickImg"" Content=""选择图片…"" Style=""{StaticResource SecondaryButton}"" Grid.Column=""1"" VerticalAlignment=""Center""/>
                  </Grid>
                  <TextBlock x:Name=""lblImg"" Style=""{StaticResource Sub}"" Margin=""0,8,0,0"" Text=""未选择""/>
                  <Border Style=""{StaticResource Divider}""/>
                  <Grid>
                    <Grid.ColumnDefinitions><ColumnDefinition Width=""*""/><ColumnDefinition Width=""Auto""/></Grid.ColumnDefinitions>
                    <StackPanel VerticalAlignment=""Center"">
                      <TextBlock Text=""语音文件夹"" FontWeight=""SemiBold""/>
                      <TextBlock Text=""仅支持 wav"" Style=""{StaticResource Sub}"" Margin=""0,3,0,0""/>
                    </StackPanel>
                    <Button x:Name=""btnPickVoices"" Content=""选择文件夹…"" Style=""{StaticResource SecondaryButton}"" Grid.Column=""1"" VerticalAlignment=""Center""/>
                  </Grid>
                  <TextBlock x:Name=""lblVoices"" Style=""{StaticResource Sub}"" Margin=""0,8,0,0"" Text=""未选择""/>
                  <Border Style=""{StaticResource Divider}""/>
                  <Button x:Name=""btnImport"" Content=""创建角色"" Style=""{StaticResource PrimaryButton}"" HorizontalAlignment=""Left""/>
                  <TextBlock x:Name=""lblImportMsg"" Style=""{StaticResource Sub}"" Margin=""0,10,0,0""
                             Text=""会重启桌宠""/>
                </StackPanel>
              </Border>
              <Border Style=""{StaticResource Card}"">
                <StackPanel>
                  <TextBlock Text=""已有角色"" Style=""{StaticResource H2}"" Margin=""0,0,0,8""/>
                  <StackPanel x:Name=""importList""/>
                </StackPanel>
              </Border>
            </StackPanel>
          </StackPanel>
        </ScrollViewer>

        <Border Grid.Row=""1"" Background=""{StaticResource AppBg}"" BorderBrush=""{StaticResource CardStroke}""
                BorderThickness=""0,1,0,0"" Padding=""24,12"">
          <Grid>
            <Grid.ColumnDefinitions>
              <ColumnDefinition Width=""Auto""/><ColumnDefinition Width=""Auto""/><ColumnDefinition Width=""*""/>
            </Grid.ColumnDefinitions>
            <Button x:Name=""btnQuit"" Content=""退出桌宠"" Style=""{StaticResource GhostButton}"" Grid.Column=""0""/>
            <!-- 有改动但没保存时亮出来，免得改了一堆直接关掉 -->
            <TextBlock x:Name=""lblDirty"" Grid.Column=""1"" Text=""● 有未保存的更改"" FontSize=""12""
                       Foreground=""#B26A00"" VerticalAlignment=""Center"" Margin=""16,0,0,0""
                       Visibility=""Collapsed""/>
            <StackPanel Grid.Column=""2"" Orientation=""Horizontal"" HorizontalAlignment=""Right"">
              <Button x:Name=""btnCancel"" Content=""取消"" Style=""{StaticResource SecondaryButton}"" Margin=""0,0,8,0""/>
              <Button x:Name=""btnSave"" Content=""保存更改"" Style=""{StaticResource PrimaryButton}""/>
            </StackPanel>
          </Grid>
        </Border>
      </Grid>
    </Grid>
  </Grid>
</Window>";
}

// ============================================================================
// 自绘对话框（替代系统 MessageBox）
// 圆角卡片 + 柔和投影 + 淡入放大 + Esc 取消；危险操作（退出/删除/清空）用红色主按钮
// ============================================================================
static class Dlg
{
    public static void Info(string title, string body, string okText = "好")
    {
        Show(title, body, string.IsNullOrEmpty(okText) ? "好" : okText, null, false);
    }

    public static bool Ask(string title, string body, string okText, string cancelText, bool danger = false)
    {
        return Show(title, body, string.IsNullOrEmpty(okText) ? "确定" : okText,
            string.IsNullOrEmpty(cancelText) ? "取消" : cancelText, danger);
    }

    public static Window ActiveOwnerPublic() { return ActiveOwner(); }

    static Window ActiveOwner()
    {
        try
        {
            if (Application.Current != null)
                foreach (Window w in Application.Current.Windows)
                    if (w.IsActive) return w;
        }
        catch (Exception) { }
        return null;
    }

    static Button MakeButton(string text, Color bg, Color fg, Color? border)
    {
        var b = new Button
        {
            Content = text, Height = 34, MinWidth = 92, Padding = new Thickness(18, 0, 18, 0),
            FontSize = 12.5, Cursor = Cursors.Hand, BorderThickness = new Thickness(0),
            Background = new SolidColorBrush(bg), Foreground = new SolidColorBrush(fg)
        };
        var tpl = new ControlTemplate(typeof(Button));
        var bd = new FrameworkElementFactory(typeof(Border));
        bd.Name = "bd";
        bd.SetValue(Border.BackgroundProperty, new SolidColorBrush(bg));
        bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        if (border.HasValue)
        {
            bd.SetValue(Border.BorderBrushProperty, new SolidColorBrush(border.Value));
            bd.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        }
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        bd.AppendChild(cp);
        tpl.VisualTree = bd;
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(UIElement.OpacityProperty, 0.86));
        tpl.Triggers.Add(hover);
        var pressed = new Trigger { Property = Button.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(UIElement.OpacityProperty, 0.72));
        tpl.Triggers.Add(pressed);
        b.Template = tpl;
        return b;
    }

    static bool Show(string title, string body, string okText, string cancelText, bool danger)
    {
        bool result = false;
        Window owner = ActiveOwner();
        var win = new Window
        {
            Title = title,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            SnapsToDevicePixels = true,
            UseLayoutRounding = true
        };
        if (owner != null) win.Owner = owner;

        var card = new Border
        {
            Width = 430,
            Margin = new Thickness(26),
            Background = Brushes.White,
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(26, 24, 26, 20)
        };
        card.Effect = new DropShadowEffect
        {
            BlurRadius = 34, ShadowDepth = 6, Direction = 270, Opacity = 0.20, Color = Colors.Black
        };

        var root = new StackPanel();
        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition());
        var badge = new Border
        {
            Width = 42, Height = 42, CornerRadius = new CornerRadius(21),
            Background = new SolidColorBrush(danger ? Color.FromRgb(0xFD, 0xEC, 0xEA) : Color.FromRgb(0xE9, 0xF2, 0xFE)),
            Margin = new Thickness(0, 2, 14, 0), VerticalAlignment = VerticalAlignment.Top
        };
        badge.Child = new TextBlock
        {
            Text = danger ? "\uE7E8" : "\uE946",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 19,
            Foreground = new SolidColorBrush(danger ? Color.FromRgb(0xC4, 0x2B, 0x1C) : Color.FromRgb(0x00, 0x5F, 0xB8)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        head.Children.Add(badge);

        var textCol = new StackPanel();
        textCol.Children.Add(new TextBlock
        {
            Text = title, FontSize = 17, FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)), TextWrapping = TextWrapping.Wrap
        });
        textCol.Children.Add(new TextBlock
        {
            Text = body, FontSize = 12.5, Foreground = new SolidColorBrush(Color.FromArgb(0x8A, 0x00, 0x00, 0x00)),
            TextWrapping = TextWrapping.Wrap, LineHeight = 20, Margin = new Thickness(0, 8, 0, 0)
        });
        Grid.SetColumn(textCol, 1);
        head.Children.Add(textCol);
        root.Children.Add(head);

        var btns = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 22, 0, 0)
        };
        Button cancelBtn = null;
        if (cancelText != null)
        {
            cancelBtn = MakeButton(cancelText, Color.FromRgb(0xFB, 0xFB, 0xFB), Color.FromRgb(0x1A, 0x1A, 0x1A), Color.FromRgb(0xD6, 0xD6, 0xD6));
            cancelBtn.Margin = new Thickness(0, 0, 8, 0);
            cancelBtn.IsCancel = true;
            cancelBtn.Click += (s, e) => { result = false; win.Close(); };
            btns.Children.Add(cancelBtn);
        }
        Button okBtn = MakeButton(okText,
            danger ? Color.FromRgb(0xC4, 0x2B, 0x1C) : Color.FromRgb(0x00, 0x5F, 0xB8), Colors.White, null);
        okBtn.Click += (s, e) => { result = true; win.Close(); };
        btns.Children.Add(okBtn);
        root.Children.Add(btns);

        card.Child = root;
        win.Content = card;

        // 淡入 + 轻微放大（以卡片中心为轴）
        card.Opacity = 0;
        var st = new ScaleTransform(0.94, 0.94);
        card.RenderTransform = st;
        card.RenderTransformOrigin = new Point(0.5, 0.5);

        var ease = new BackEase { Amplitude = 0.25, EasingMode = EasingMode.EaseOut };
        win.Loaded += (s, e) =>
        {
            card.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160))
                { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
            st.BeginAnimation(ScaleTransform.ScaleXProperty,
                new DoubleAnimation(0.94, 1, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease });
            st.BeginAnimation(ScaleTransform.ScaleYProperty,
                new DoubleAnimation(0.94, 1, TimeSpan.FromMilliseconds(200))
                { EasingFunction = new BackEase { Amplitude = 0.25, EasingMode = EasingMode.EaseOut } });
            // 默认焦点给「取消」：手快点回车不会误退出
            if (cancelBtn != null) cancelBtn.Focus(); else okBtn.Focus();
        };
        win.PreviewKeyDown += (s, e) =>
        {
            if (e.Key == Key.Escape) { result = false; win.Close(); e.Handled = true; }
            else if (e.Key == Key.Enter && !danger) { result = true; win.Close(); e.Handled = true; }
        };

        win.ShowDialog();
        return result;
    }
}

// ============================================================================
// 自绘取色器：R/G/B 滑杆 + hex 输入 + 实时预览（WPF 4.8 没有内置 ColorPicker）
// ============================================================================
static class ColorDlg
{
    public static bool Ask(Color initial, out Color result)
    {
        Color picked = initial;
        bool okFlag = false;
        Window owner = Dlg.ActiveOwnerPublic();
        var win = new Window
        {
            Title = "选择颜色",
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            UseLayoutRounding = true
        };
        if (owner != null) win.Owner = owner;

        var card = new Border
        {
            Width = 400, Margin = new Thickness(26), Background = Brushes.White,
            CornerRadius = new CornerRadius(12), Padding = new Thickness(24, 20, 24, 18)
        };
        card.Effect = new DropShadowEffect { BlurRadius = 34, ShadowDepth = 6, Direction = 270, Opacity = 0.20, Color = Colors.Black };

        var root = new StackPanel();
        root.Children.Add(new TextBlock
        {
            Text = "选择颜色", FontSize = 17, FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A))
        });

        Color cur = initial;
        var preview = new Border
        {
            Height = 56, Margin = new Thickness(0, 14, 0, 14), CornerRadius = new CornerRadius(8),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB)), BorderThickness = new Thickness(1)
        };
        var hexBox = new TextBox { Height = 32, FontFamily = new FontFamily("Consolas"), MaxLength = 6 };
        Action upd = () =>
        {
            preview.Background = new SolidColorBrush(cur);
            if (!hexBox.IsFocused) hexBox.Text = Settings.HexOf(cur);
        };
        upd();
        root.Children.Add(preview);

        Slider mkSlider(byte v, Action<byte> set)
        {
            var sl = new Slider { Minimum = 0, Maximum = 255, Value = v, Margin = new Thickness(0, 4, 0, 0) };
            sl.ValueChanged += (s2, e2) => { set((byte)sl.Value); upd(); };
            return sl;
        }
        root.Children.Add(new TextBlock { Text = "R 红", FontSize = 11.5, Foreground = new SolidColorBrush(Color.FromArgb(0x99, 0x00, 0x00, 0x00)) });
        root.Children.Add(mkSlider(cur.R, v => cur = Color.FromRgb(v, cur.G, cur.B)));
        root.Children.Add(new TextBlock { Text = "G 绿", FontSize = 11.5, Foreground = new SolidColorBrush(Color.FromArgb(0x99, 0x00, 0x00, 0x00)) });
        root.Children.Add(mkSlider(cur.G, v => cur = Color.FromRgb(cur.R, v, cur.B)));
        root.Children.Add(new TextBlock { Text = "B 蓝", FontSize = 11.5, Foreground = new SolidColorBrush(Color.FromArgb(0x99, 0x00, 0x00, 0x00)) });
        root.Children.Add(mkSlider(cur.B, v => cur = Color.FromRgb(cur.R, cur.G, v)));

        var hexRow = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        hexRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        hexRow.ColumnDefinitions.Add(new ColumnDefinition());
        hexRow.Children.Add(new TextBlock { Text = "#", FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        hexBox.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(hexBox, 1); hexRow.Children.Add(hexBox);
        hexBox.TextChanged += (s2, e2) =>
        {
            Color c = Settings.WpfParseHex(hexBox.Text, cur);
            if (!Settings.HexOf(c).Equals(hexBox.Text.Trim().TrimStart('#'), StringComparison.OrdinalIgnoreCase)) return;
            cur = c; preview.Background = new SolidColorBrush(cur);
        };
        root.Children.Add(hexRow);

        var btns = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 20, 0, 0)
        };
        var cancel = new Button
        {
            Content = "取消", Height = 34, MinWidth = 92, Padding = new Thickness(18, 0, 18, 0),
            FontSize = 12.5, Cursor = Cursors.Hand, BorderThickness = new Thickness(0),
            Background = new SolidColorBrush(Color.FromRgb(0xFB, 0xFB, 0xFB)),
            Foreground = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)), Margin = new Thickness(0, 0, 8, 0)
        };
        cancel.IsCancel = true;
        cancel.Click += (s2, e2) => { win.Close(); };
        btns.Children.Add(cancel);
        var ok = new Button
        {
            Content = "确定", Height = 34, MinWidth = 92, Padding = new Thickness(18, 0, 18, 0),
            FontSize = 12.5, Cursor = Cursors.Hand, BorderThickness = new Thickness(0),
            Background = new SolidColorBrush(Color.FromRgb(0x00, 0x5F, 0xB8)), Foreground = Brushes.White
        };
        ok.Click += (s2, e2) => { picked = cur; okFlag = true; win.DialogResult = true; win.Close(); };
        btns.Children.Add(ok);
        root.Children.Add(btns);

        card.Child = root;
        win.Content = card;
        card.Opacity = 0;
        win.Loaded += (s2, e2) => card.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
        win.ShowDialog();
        result = picked;
        return okFlag;
    }
}

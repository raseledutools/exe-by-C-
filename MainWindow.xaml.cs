#pragma warning disable CS8600, CS8602, CS8604, CS8618, CS8622, CS8625, CS0414, CS0104
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

// System Tray এর জন্য
using DrawingColor = System.Drawing.Color;
using DrawingIcon = System.Drawing.Icon;
using WinForms = System.Windows.Forms;

namespace RasFocusPro
{
    public partial class MainWindow : Window
    {
        // ==========================================
        // 1. WINDOWS NATIVE API (P/Invoke)
        // ==========================================
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)] private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);
        [DllImport("user32.dll", SetLastError = true)] static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("winmm.dll")] private static extern long mciSendString(string strCommand, StringBuilder strReturn, int iReturnLength, IntPtr hwndCallback);
        [DllImport("user32.dll")] private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)] private static extern IntPtr GetModuleHandle(string lpModuleName);
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)] static extern uint RegisterWindowMessage(string lpString);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        private const int SW_MINIMIZE = 6;
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const byte VK_CONTROL = 0x11;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const int HWND_BROADCAST = 0xffff;
        private static uint WM_WAKEUP = RegisterWindowMessage("RasFocusPro_Wakeup");

        // ==========================================
        // 2. GLOBALS & DATA
        // ==========================================
        private DispatcherTimer fastTimer, slowTimer, syncTimer, fbPoller;
        private bool isSessionActive = false, isTimeMode = false, isPassMode = false, useAllowMode = false;
        private bool blockReels = false, blockShorts = false, isAdblockActive = false, blockAdult = true;
        private bool isPomodoroMode = false, isPomodoroBreak = false, isTrialExpired = false, isLicenseValid = true;
        
        private int focusTimeTotalSeconds = 0, timerTicks = 0, pomoTicks = 0, pomoLengthMin = 25, pomoTotalSessions = 4, pomoCurrentSession = 1;
        private int eyeBrightness = 100, eyeWarmth = 0, trialDaysLeft = 174;
        
        private string currentSessionPass = "", userProfileName = "Rasel Mia", secretDir;
        private string lastBroadcastMsg = "", lastAdminMsg = "", lastAdminChat = "";

        private List<string> blockedApps = new List<string>(), blockedWebs = new List<string>();
        private List<string> allowedApps = new List<string>(), allowedWebs = new List<string>();
        private string[] systemApps = { "explorer.exe", "svchost.exe", "taskmgr.exe", "cmd.exe", "conhost.exe", "csrss.exe", "dwm.exe", "lsass.exe", "services.exe", "smss.exe", "wininit.exe", "winlogon.exe", "spoolsv.exe", "fontdrvhost.exe" };
        private string[] safeBrowserTitles = { "new tab", "start", "blank page", "allowed websites", "loading", "untitled", "connecting", "pomodoro break" };

        private List<string> explicitKeywords = new List<string> {
            "porn", "xxx", "sex", "nude", "nsfw", "xvideos", "pornhub", "xnxx", "xhamster", "brazzers", "onlyfans", "playboy", "mia khalifa", "bhabi", "chudai", "bangla choti", "magi", "sexy",
            "hot video", "hot scene", "desi", "boudi", "devar", "item song", "item dance", "mujra", "belly dance", "bikini", "romance", "kissing", "ullu", "web series", "ullongo", "kapor chara", "tiktok dance", "dj dance", "hot dance", "nongra dance",
            "খাসি", "চটি", "যৌন", "নগ্ন", "উলঙ্গ", "মেয়েদের ছবি", "গরম ভিডিও", "নষ্ট ভিডিও", "নোংরা ভিডিও", "খারাপ ছবি", "পর্ন", "যৌনতা", "choti golpo", "bangla sex", "meyeder chobi", "nongra video", "gorom video", "kapor chara chobi", "bangla hot", "boudi video", "deshi sexy", "biye barir dance", "পটানো", "সেক্সি নাচ", "অশ্লীল", "অশ্লীল ভিডিও", "বৌদি", "দেবর বৌদি", "ভাবি", "গোপন ভিডিও", "ভাইরাল ভিডিও"
        };
        
        private string[] islamicQuotes = { "\"মুমিনদের বলুন, তারা যেন তাদের দৃষ্টি নত রাখে এবং তাদের যৌনাঙ্গর হেফাযত করে।\" - (সূরা আন-নূর: ৩০)", "\"লজ্জাশীলতা কল্যাণ ছাড়া আর কিছুই বয়ে আনে না।\" - (সহীহ বুখারী)" };
        private string[] timeQuotes = { "\"যারা সময়কে মূল্যায়ন করে না, সময়ও তাদেরকে মূল্যায়ন করে না।\" - এ.পি.জে. আবদুল কালাম" };

        private Window overlayWindow = null, eyeFilterDim = null, eyeFilterWarm = null;
        private Window stopwatchWnd = null, liveChatWnd = null, upgradeWnd = null;
        private WinForms.NotifyIcon trayIcon;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        private static LowLevelKeyboardProc _proc;
        private static IntPtr _hookID = IntPtr.Zero;
        private static string globalKeyBuffer = "";
        private static MainWindow _instance; 
        private static Mutex _mutex = null;

        // ==========================================
        // 3. CONSTRUCTOR & INITIALIZATION
        // ==========================================
        public MainWindow()
        {
            bool createdNew;
            _mutex = new Mutex(true, "RasFocusPro_Mutex_V55", out createdNew);
            if (!createdNew) { PostMessage((IntPtr)HWND_BROADCAST, WM_WAKEUP, IntPtr.Zero, IntPtr.Zero); Application.Current.Shutdown(); return; }

            InitializeComponent();
            _instance = this;
            secretDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RasFocusPro");
            Directory.CreateDirectory(secretDir);

            LoadData();
            SetAppIcon();
            SetupTrayIcon();
            CreateDesktopShortcut();
            SetupAutoStart();
            SetupEyeFilters();

            _proc = HookCallback;
            _hookID = SetHook(_proc);

            fastTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            fastTimer.Tick += FastLoop_Tick; fastTimer.Start();

            slowTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            slowTimer.Tick += SlowLoop_Tick; slowTimer.Start();

            syncTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            syncTimer.Tick += SyncLoop_Tick; syncTimer.Start();

            this.Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (Environment.GetCommandLineArgs().Contains("-autostart")) { this.Hide(); }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            HwndSource source = PresentationSource.FromVisual(this) as HwndSource;
            source.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_WAKEUP) { ShowAppFromTray(); handled = true; }
            return IntPtr.Zero;
        }

        protected override void OnClosed(EventArgs e)
        {
            UnhookWindowsHookEx(_hookID);
            if (trayIcon != null) trayIcon.Dispose();
            base.OnClosed(e);
        }

        // ==========================================
        // 4. ICONS, SHORTCUT & TRAY
        // ==========================================
        private void SetAppIcon()
        {
            try {
                string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.ico");
                if (File.Exists(iconPath)) { this.Icon = new BitmapImage(new Uri(iconPath, UriKind.Absolute)); }
            } catch { }
        }

        private void SetupTrayIcon()
        {
            trayIcon = new WinForms.NotifyIcon();
            string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.ico");
            if (File.Exists(iconPath)) { trayIcon.Icon = new DrawingIcon(iconPath); }
            else { trayIcon.Icon = System.Drawing.SystemIcons.Shield; }

            trayIcon.Text = "RasFocus Pro";
            trayIcon.Visible = true;
            trayIcon.DoubleClick += (s, e) => ShowAppFromTray();

            WinForms.ContextMenuStrip menu = new WinForms.ContextMenuStrip();
            menu.Items.Add("Open RasFocus", null, (s, e) => ShowAppFromTray());
            menu.Items.Add("Exit App", null, (s, e) => 
            {
                if (isSessionActive) { MessageBox.Show("Cannot exit while Focus Mode is active! Please stop it first.", "Security Warning", MessageBoxButton.OK, MessageBoxImage.Warning); }
                else { trayIcon.Dispose(); Application.Current.Shutdown(); }
            });
            trayIcon.ContextMenuStrip = menu;
        }

        private void ShowAppFromTray()
        {
            this.Show();
            this.WindowState = WindowState.Normal;
            this.Activate(); 
            this.Topmost = true; this.Topmost = false; 
            this.Focus();
        }

        private void CreateDesktopShortcut()
        {
            try {
                string shortcutPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "RasFocus Pro.lnk");
                if (!File.Exists(shortcutPath)) {
                    Type t = Type.GetTypeFromProgID("WScript.Shell");
                    dynamic shell = Activator.CreateInstance(t);
                    dynamic shortcut = shell.CreateShortcut(shortcutPath);
                    shortcut.TargetPath = Process.GetCurrentProcess().MainModule.FileName;
                    shortcut.IconLocation = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.ico");
                    shortcut.Description = "Launch RasFocus Pro";
                    shortcut.Save();
                }
            } catch { }
        }

        private void SetupAutoStart()
        {
            try {
                using (Microsoft.Win32.RegistryKey key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true)) {
                    key.SetValue("RasFocusPro", "\"" + Process.GetCurrentProcess().MainModule.FileName + "\" -autostart");
                }
            } catch { }
        }

        // ==========================================
        // 5. UI EVENT HANDLERS (WPF Controls)
        // ==========================================
        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { DragMove(); }
        private void MinimizeButton_Click(object sender, RoutedEventArgs e) { this.WindowState = WindowState.Minimized; }
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e) { e.Cancel = true; this.Hide(); trayIcon.ShowBalloonTip(2000, "RasFocus Pro", "Running silently in the background...", WinForms.ToolTipIcon.Info); }
        private void CloseButton_Click(object sender, RoutedEventArgs e) { this.Hide(); trayIcon.ShowBalloonTip(2000, "RasFocus Pro", "Running silently in the background...", WinForms.ToolTipIcon.Info); }

        private void SidebarList_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (PageFocusMode == null) return;
            int idx = SidebarList.SelectedIndex;
            PageFocusMode.Visibility = idx == 0 ? Visibility.Visible : Visibility.Collapsed;
            PageEyeCure.Visibility = idx == 1 ? Visibility.Visible : Visibility.Collapsed;
            PagePomodoro.Visibility = idx == 2 ? Visibility.Visible : Visibility.Collapsed;
        }

        // Action Buttons from XAML
        private void BtnSave_Click(object sender, RoutedEventArgs e) { MessageBox.Show("Profile Saved successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information); }
        private void BtnStart_Click(object sender, RoutedEventArgs e) {
            if (isSessionActive) return;
            isSessionActive = true;
            SaveData(); UpdateUIStates(); ManageFocusSound(true);
            MessageBox.Show("Focus Mode Active. Unauthorized apps and websites are blocked.", "RasFocus Pro Security", MessageBoxButton.OK, MessageBoxImage.Information);
            this.Hide(); 
        }
        private void BtnStop_Click(object sender, RoutedEventArgs e) {
            if (!isSessionActive) return;
            ClearSessionData(); MessageBox.Show("Session Stopped Successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        private void BtnPomoStart_Click(object sender, RoutedEventArgs e) {
            if (!isSessionActive && !isTrialExpired) { isPomodoroMode = true; isSessionActive = true; pomoTicks = 0; pomoCurrentSession = 1; SaveData(); UpdateUIStates(); ManageFocusSound(true); }
        }
        private void BtnPomoStop_Click(object sender, RoutedEventArgs e) { if (isPomodoroMode) { ClearSessionData(); UpdateUIStates(); } }

        // Dynamic Windows Triggers
        private void BtnLiveChat_Click(object sender, RoutedEventArgs e) { CreateLiveChatWindow(); }
        private void BtnUpgrade_Click(object sender, RoutedEventArgs e) { CreateUpgradeWindow(); }
        private void BtnStopWatch_Click(object sender, RoutedEventArgs e) { CreateStopwatchWindow(); }
        private void BtnPomodoro_Click(object sender, RoutedEventArgs e) { SidebarList.SelectedIndex = 2; }

        // Lists & Apps Management
        private void BtnRefresh_Click(object sender, RoutedEventArgs e) { RefreshRunningApps(); }
        private void BtnAddApp_Click(object sender, RoutedEventArgs e) {
            string val = FindTextBoxValue("TxtAppBlock").ToLower().Trim();
            if (!string.IsNullOrEmpty(val)) { if (!val.EndsWith(".exe")) val += ".exe"; if (!blockedApps.Contains(val)) { blockedApps.Add(val); ListBox lb = this.FindName("ListBlockedApps") as ListBox; if (lb != null) lb.Items.Add(val); SaveData(); TextBox tb = this.FindName("TxtAppBlock") as TextBox; if (tb != null) tb.Clear(); } }
        }
        private void BtnAddWeb_Click(object sender, RoutedEventArgs e) {
            string val = FindTextBoxValue("TxtWebBlock").ToLower().Trim();
            if (!string.IsNullOrEmpty(val)) { if (!blockedWebs.Contains(val)) { blockedWebs.Add(val); ListBox lb = this.FindName("ListBlockedWebs") as ListBox; if (lb != null) lb.Items.Add(val); SaveData(); TextBox tb = this.FindName("TxtWebBlock") as TextBox; if (tb != null) tb.Clear(); } }
        }
        private void BtnRemApp_Click(object sender, RoutedEventArgs e) {
            ListBox lb = this.FindName("ListBlockedApps") as ListBox;
            if (lb != null && lb.SelectedItem != null) { blockedApps.Remove(lb.SelectedItem.ToString()); lb.Items.Remove(lb.SelectedItem); SaveData(); }
        }

        private string FindTextBoxValue(string name) { TextBox t = this.FindName(name) as TextBox; return t != null ? t.Text : ""; }
        
        private void RefreshRunningApps() {
            ListBox lb = this.FindName("ListRunningApps") as ListBox;
            if (lb != null) {
                lb.Items.Clear();
                foreach (var p in Process.GetProcesses()) {
                    try { string name = p.ProcessName + ".exe"; if (!lb.Items.Contains(name)) lb.Items.Add(name); } catch { }
                }
            }
        }

        private void UpdateUIStates() { trayIcon.Text = isSessionActive ? "RasFocus Pro (ACTIVE 🔒)" : "RasFocus Pro (Ready)"; }

        // ==========================================
        // 6. EYE CURE (SLIDERS & PRESETS)
        // ==========================================
        private void SetupEyeFilters() {
            eyeFilterDim = new Window { WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = System.Windows.Media.Brushes.Black, Topmost = true, ShowInTaskbar = false, IsHitTestVisible = false, WindowState = WindowState.Maximized, Opacity = 0 };
            eyeFilterWarm = new Window { WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 130, 0)), Topmost = true, ShowInTaskbar = false, IsHitTestVisible = false, WindowState = WindowState.Maximized, Opacity = 0 };
            eyeFilterDim.Show(); eyeFilterWarm.Show();
        }

        private void ApplyEyeFilters() {
            double dimAlpha = (100 - eyeBrightness) / 100.0;
            double warmAlpha = eyeWarmth / 100.0 * 0.6; 
            if (eyeFilterDim != null) eyeFilterDim.Opacity = dimAlpha;
            if (eyeFilterWarm != null) eyeFilterWarm.Opacity = warmAlpha;
        }

        // Event Handlers for XAML
        private void SliderBrightness_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) {
            eyeBrightness = (int)e.NewValue; ApplyEyeFilters();
        }
        private void SliderWarmth_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) {
            eyeWarmth = (int)e.NewValue; ApplyEyeFilters();
        }
        private void PresetDay_Click(object sender, RoutedEventArgs e) {
            Slider br = this.FindName("SliderBrightness") as Slider; if(br!=null) br.Value = 100;
            Slider wr = this.FindName("SliderWarmth") as Slider; if(wr!=null) wr.Value = 0;
        }
        private void PresetReading_Click(object sender, RoutedEventArgs e) {
            Slider br = this.FindName("SliderBrightness") as Slider; if(br!=null) br.Value = 85;
            Slider wr = this.FindName("SliderWarmth") as Slider; if(wr!=null) wr.Value = 30;
        }
        private void PresetNight_Click(object sender, RoutedEventArgs e) {
            Slider br = this.FindName("SliderBrightness") as Slider; if(br!=null) br.Value = 65;
            Slider wr = this.FindName("SliderWarmth") as Slider; if(wr!=null) wr.Value = 60;
        }

        // ==========================================
        // 7. BACKGROUND PROCESSES (Timers)
        // ==========================================
        private void FastLoop_Tick(object sender, EventArgs e)
        {
            if (!blockAdult && !blockReels && !blockShorts && !isSessionActive) return;
            if (overlayWindow != null && overlayWindow.IsVisible) return;

            IntPtr hWnd = GetForegroundWindow();
            if (hWnd != IntPtr.Zero)
            {
                StringBuilder title = new StringBuilder(512);
                GetWindowText(hWnd, title, 512);
                string sTitle = title.ToString().ToLower();

                if (blockAdult && explicitKeywords.Any(k => sTitle.Contains(k))) { CloseWindowNatively(hWnd); ShowOverlay(GetRandomQuote(1)); return; }
                if (blockReels && sTitle.Contains("facebook") && sTitle.Contains("reels")) { CloseWindowNatively(hWnd); ShowOverlay(GetRandomQuote(2)); return; }
                if (blockShorts && sTitle.Contains("youtube") && sTitle.Contains("shorts")) { CloseWindowNatively(hWnd); ShowOverlay(GetRandomQuote(2)); return; }

                if (isSessionActive)
                {
                    bool isBrowser = sTitle.Contains("chrome") || sTitle.Contains("edge") || sTitle.Contains("firefox") || sTitle.Contains("brave");
                    if (isBrowser) {
                        if (useAllowMode) {
                            if (!allowedWebs.Any(w => sTitle.Contains(w.ToLower())) && !safeBrowserTitles.Any(s => sTitle.Contains(s)))
                            { CloseWindowNatively(hWnd); ShowOverlay("Website not in Allow List!"); }
                        } else {
                            if (blockedWebs.Any(w => sTitle.Contains(w.ToLower())))
                            { CloseWindowNatively(hWnd); ShowOverlay("Website Blocked by Focus Mode!"); }
                        }
                    }
                }
            }
        }

        private void SlowLoop_Tick(object sender, EventArgs e)
        {
            if (!isSessionActive) return;
            if (isPomodoroMode) {
                pomoTicks++; int totalMins = isPomodoroBreak ? 5 : pomoLengthMin;
                if (!isPomodoroBreak && pomoTicks >= pomoLengthMin * 60) { isPomodoroBreak = true; pomoTicks = 0; ShowOverlay("☕ Time to Rest & Drink Water! Break Started."); }
                else if (isPomodoroBreak && pomoTicks >= 5 * 60) { isPomodoroBreak = false; pomoTicks = 0; pomoCurrentSession++; if(pomoCurrentSession > pomoTotalSessions) ClearSessionData(); }
            }
            foreach (Process p in Process.GetProcesses()) {
                try {
                    string pName = p.ProcessName.ToLower() + ".exe";
                    if (pName == "taskmgr.exe" || pName == "msiexec.exe") { p.Kill(); continue; } 
                    if (useAllowMode) { if (!systemApps.Contains(pName) && !allowedApps.Contains(pName)) p.Kill(); }
                    else { if (blockedApps.Contains(pName)) p.Kill(); }
                } catch { }
            }
        }

        private void SyncLoop_Tick(object sender, EventArgs e) { /* PowerShell Firebase sync */ }

        // ==========================================
        // 8. DYNAMIC POPUP WINDOWS
        // ==========================================
        private void CreateLiveChatWindow() {
            if (liveChatWnd != null && liveChatWnd.IsLoaded) { liveChatWnd.Activate(); return; }
            liveChatWnd = new Window { Title = "Live Chat with Admin", Width = 400, Height = 500, WindowStartupLocation = WindowStartupLocation.CenterScreen, Background = System.Windows.Media.Brushes.White };
            StackPanel sp = new StackPanel { Margin = new Thickness(15) };
            TextBox log = new TextBox { Height = 350, IsReadOnly = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0,0,0,10) };
            TextBox input = new TextBox { Height = 35, Margin = new Thickness(0,0,0,10), FontSize = 14 };
            Button btn = new Button { Content = "SEND MESSAGE", Height = 40, Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(236, 72, 153)), Foreground = System.Windows.Media.Brushes.White, FontWeight = FontWeights.Bold, Cursor = Cursors.Hand };
            btn.Click += (s, e) => { if(!string.IsNullOrEmpty(input.Text)){ log.AppendText("You: " + input.Text + "\n"); input.Clear(); } };
            sp.Children.Add(new TextBlock{Text = "💬 Live Support", FontSize=20, FontWeight=FontWeights.Bold, Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(20, 184, 200)), Margin=new Thickness(0,0,0,15)});
            sp.Children.Add(log); sp.Children.Add(input); sp.Children.Add(btn); liveChatWnd.Content = sp; liveChatWnd.Show();
        }

        private void CreateStopwatchWindow() {
            if (stopwatchWnd != null && stopwatchWnd.IsLoaded) { stopwatchWnd.Activate(); return; }
            stopwatchWnd = new Window { Title = "Pro Stopwatch", Width = 400, Height = 250, WindowStartupLocation = WindowStartupLocation.CenterScreen, Background = System.Windows.Media.Brushes.White };
            StackPanel sp = new StackPanel { Margin = new Thickness(20), HorizontalAlignment = HorizontalAlignment.Center };
            TextBlock txt = new TextBlock { Text = "00:00:00", FontSize = 50, FontWeight = FontWeights.Bold, FontFamily = new System.Windows.Media.FontFamily("Consolas"), Margin = new Thickness(0,0,0,20) };
            Button btn = new Button { Content = "START / PAUSE", Height = 45, Width = 200, Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(20, 184, 200)), Foreground = System.Windows.Media.Brushes.White, FontWeight = FontWeights.Bold, Cursor = Cursors.Hand };
            DispatcherTimer swT = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) }; int s = 0;
            swT.Tick += (sender, e) => { s++; TimeSpan ts = TimeSpan.FromSeconds(s); txt.Text = ts.ToString(@"hh\:mm\:ss"); };
            btn.Click += (sender, e) => { if(swT.IsEnabled) { swT.Stop(); btn.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68)); } else { swT.Start(); btn.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(20, 184, 200)); } };
            sp.Children.Add(txt); sp.Children.Add(btn); stopwatchWnd.Content = sp; stopwatchWnd.Show();
        }

        private void CreateUpgradeWindow() {
            if (upgradeWnd != null && upgradeWnd.IsLoaded) { upgradeWnd.Activate(); return; }
            upgradeWnd = new Window { Title = "Upgrade to Premium", Width = 400, Height = 450, WindowStartupLocation = WindowStartupLocation.CenterScreen, Background = System.Windows.Media.Brushes.White };
            StackPanel sp = new StackPanel { Margin = new Thickness(25) };
            sp.Children.Add(new TextBlock{Text="⭐ Activate Premium", FontSize=22, FontWeight=FontWeights.Bold, Foreground=new SolidColorBrush(System.Windows.Media.Color.FromRgb(20, 184, 200)), Margin=new Thickness(0,0,0,20)});
            sp.Children.Add(new TextBlock{Text="Send payment via bKash/Nagad to 01566054963", FontSize=14, FontWeight=FontWeights.Bold, Foreground=System.Windows.Media.Brushes.DimGray, Margin=new Thickness(0,0,0,20), TextWrapping = TextWrapping.Wrap});
            sp.Children.Add(new TextBlock{Text="Your Email / Name:", FontSize=13, Foreground=System.Windows.Media.Brushes.Gray}); 
            TextBox email = new TextBox{Margin=new Thickness(0,5,0,15), Height = 30}; sp.Children.Add(email);
            sp.Children.Add(new TextBlock{Text="bKash/Nagad Number:", FontSize=13, Foreground=System.Windows.Media.Brushes.Gray}); 
            TextBox phone = new TextBox{Margin=new Thickness(0,5,0,15), Height = 30}; sp.Children.Add(phone);
            sp.Children.Add(new TextBlock{Text="Transaction ID:", FontSize=13, Foreground=System.Windows.Media.Brushes.Gray}); 
            TextBox trx = new TextBox{Margin=new Thickness(0,5,0,20), Height = 30}; sp.Children.Add(trx);
            Button btn = new Button{Content="SUBMIT UPGRADE REQUEST", Height=45, Background=new SolidColorBrush(System.Windows.Media.Color.FromRgb(245, 158, 11)), Foreground=System.Windows.Media.Brushes.White, FontWeight=FontWeights.Bold, Cursor = Cursors.Hand};
            btn.Click += (s,e) => { MessageBox.Show("Upgrade Request Sent Successfully! Please wait for Admin approval.", "Success", MessageBoxButton.OK, MessageBoxImage.Information); upgradeWnd.Close(); };
            sp.Children.Add(btn); upgradeWnd.Content = sp; upgradeWnd.Show();
        }

        // ==========================================
        // 9. REAL-TIME KEYBOARD HOOK (TYPING BLOCKER)
        // ==========================================
        private static IntPtr SetHook(LowLevelKeyboardProc proc) {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
                return SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(curModule.ModuleName), 0);
        }

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam) {
            if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN && _instance != null && _instance.blockAdult) {
                int vkCode = Marshal.ReadInt32(lParam); char c = (char)vkCode;
                if (char.IsLetterOrDigit(c) || char.IsPunctuation(c)) {
                    globalKeyBuffer += char.ToLower(c); if (globalKeyBuffer.Length > 50) globalKeyBuffer = globalKeyBuffer.Substring(1);
                    if (_instance.explicitKeywords.Any(k => globalKeyBuffer.Contains(k))) {
                        globalKeyBuffer = ""; IntPtr hActive = GetForegroundWindow();
                        if (hActive != IntPtr.Zero) _instance.CloseWindowNatively(hActive);
                        _instance.ShowOverlay(_instance.GetRandomQuote(1));
                    }
                }
            }
            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        // ==========================================
        // 10. UTILITIES
        // ==========================================
        private string GetRandomQuote(int type) { Random r = new Random(); return type == 1 ? islamicQuotes[r.Next(islamicQuotes.Length)] : timeQuotes[r.Next(timeQuotes.Length)]; }
        
        private void CloseWindowNatively(IntPtr hWnd) {
            SetForegroundWindow(hWnd);
            keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
            keybd_event((byte)'W', 0, 0, UIntPtr.Zero);
            keybd_event((byte)'W', 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            Thread.Sleep(50); ShowWindow(hWnd, SW_MINIMIZE);
        }

        private void ShowOverlay(string message) {
            if (overlayWindow == null) {
                overlayWindow = new Window { WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(240, 9, 61, 31)), Topmost = true, Width = 800, Height = 300, WindowStartupLocation = WindowStartupLocation.CenterScreen };
                TextBlock tb = new TextBlock { Text = message, Foreground = System.Windows.Media.Brushes.White, FontSize = 24, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(20) };
                overlayWindow.Content = tb;
            } else { ((TextBlock)overlayWindow.Content).Text = message; }
            overlayWindow.Show(); overlayWindow.Topmost = true;
            DispatcherTimer t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
            t.Tick += (s, ev) => { overlayWindow.Hide(); t.Stop(); }; t.Start();
        }

        private void ManageFocusSound(bool start) {
            string audioPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "focus_noise.mp3");
            if (start && File.Exists(audioPath)) { mciSendString($"open \"{audioPath}\" type mpegvideo alias focusSound", null, 0, IntPtr.Zero); mciSendString("play focusSound repeat", null, 0, IntPtr.Zero); }
            else { mciSendString("stop focusSound", null, 0, IntPtr.Zero); mciSendString("close focusSound", null, 0, IntPtr.Zero); }
        }

        private void ClearSessionData() { isSessionActive = false; isPassMode = false; isTimeMode = false; isPomodoroMode = false; currentSessionPass = ""; SaveData(); UpdateUIStates(); ManageFocusSound(false); }
        private void SaveData() { File.WriteAllLines(Path.Combine(secretDir, "bl_app.txt"), blockedApps); }
        private void LoadData() { string p = Path.Combine(secretDir, "bl_app.txt"); if (File.Exists(p)) blockedApps = File.ReadAllLines(p).ToList(); }
    }
}

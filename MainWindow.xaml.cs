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
        
        // Adult, Dance & Bangla Bad Words
        private List<string> explicitKeywords = new List<string> {
            "porn", "xxx", "sex", "nude", "nsfw", "xvideos", "pornhub", "xnxx", "xhamster", "brazzers", "onlyfans", "playboy", "mia khalifa", "bhabi", "chudai", "bangla choti", "magi", "sexy",
            "hot video", "hot scene", "desi", "boudi", "devar", "item song", "item dance", "mujra", "belly dance", "bikini", "romance", "kissing", "ullu", "web series", "ullongo", "kapor chara", "tiktok dance", "dj dance", "hot dance", "nongra dance",
            "খাসি", "চটি", "যৌন", "নগ্ন", "উলঙ্গ", "মেয়েদের ছবি", "গরম ভিডিও", "নষ্ট ভিডিও", "নোংরা ভিডিও", "খারাপ ছবি", "পর্ন", "যৌনতা", "choti golpo", "bangla sex", "meyeder chobi", "nongra video", "gorom video", "kapor chara chobi", "bangla hot", "boudi video", "deshi sexy", "biye barir dance", "পটানো", "সেক্সি নাচ", "অশ্লীল", "অশ্লীল ভিডিও", "বৌদি", "দেবর বৌদি", "ভাবি", "গোপন ভিডিও", "ভাইরাল ভিডিও"
        };
        
        private string[] islamicQuotes = { "\"মুমিনদের বলুন, তারা যেন তাদের দৃষ্টি নত রাখে এবং তাদের যৌনাঙ্গর হেফাযত করে।\" - (সূরা আন-নূর: ৩০)", "\"লজ্জাশীলতা কল্যাণ ছাড়া আর কিছুই বয়ে আনে না।\" - (সহীহ বুখারী)" };
        private string[] timeQuotes = { "\"যারা সময়কে মূল্যায়ন করে না, সময়ও তাদেরকে মূল্যায়ন করে না।\" - এ.পি.জে. আবদুল কালাম" };

        private Window overlayWindow = null, eyeFilterDim = null, eyeFilterWarm = null;
        private WinForms.NotifyIcon trayIcon;

        // Keyboard Hook Setup
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
            // Single Instance Check & Wakeup Broadcast
            bool createdNew;
            _mutex = new Mutex(true, "RasFocusPro_Mutex_V55", out createdNew);
            if (!createdNew) { PostMessage((IntPtr)HWND_BROADCAST, WM_WAKEUP, IntPtr.Zero, IntPtr.Zero); Application.Current.Shutdown(); return; }

            InitializeComponent();
            _instance = this;
            secretDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RasFocusPro");
            Directory.CreateDirectory(secretDir);

            LoadData();
            
            // Icon & Shortcut Setup
            SetAppIcon();
            SetupTrayIcon();
            CreateDesktopShortcut();
            SetupAutoStart();

            // Setup Transparent Eye Care Overlays
            SetupEyeFilters();

            // Keyboard Hook
            _proc = HookCallback;
            _hookID = SetHook(_proc);

            // Timers
            fastTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            fastTimer.Tick += FastLoop_Tick; fastTimer.Start();

            slowTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            slowTimer.Tick += SlowLoop_Tick; slowTimer.Start();

            syncTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            syncTimer.Tick += SyncLoop_Tick; syncTimer.Start();

            fbPoller = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            fbPoller.Tick += FirebasePoll_Tick; fbPoller.Start();

            this.Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Silent Start Check
            if (Environment.GetCommandLineArgs().Contains("-autostart")) { this.Hide(); }
        }

        // Window Wakeup Logic
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
        // 5. UI EVENT HANDLERS
        // ==========================================
        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { DragMove(); }
        private void MinimizeButton_Click(object sender, RoutedEventArgs e) { this.WindowState = WindowState.Minimized; }
        
        // Window close now hides to tray instead of exiting
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e) { e.Cancel = true; this.Hide(); trayIcon.ShowBalloonTip(2000, "RasFocus Pro", "Running silently in the background...", WinForms.ToolTipIcon.Info); }
        private void CloseButton_Click(object sender, RoutedEventArgs e) { this.Hide(); trayIcon.ShowBalloonTip(2000, "RasFocus Pro", "Running silently in the background...", WinForms.ToolTipIcon.Info); }

        private void SidebarList_SelectionChanged(object sender, SelectionChangedEventArgs e) {
            if (PageFocusMode == null) return;
            int idx = SidebarList.SelectedIndex;
            PageFocusMode.Visibility = idx == 0 ? Visibility.Visible : Visibility.Collapsed;
            PageEyeCure.Visibility = idx == 1 ? Visibility.Visible : Visibility.Collapsed;
            PagePomodoro.Visibility = idx == 2 ? Visibility.Visible : Visibility.Collapsed;
        }

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

        private void UpdateUIStates() { trayIcon.Text = isSessionActive ? "RasFocus Pro (ACTIVE 🔒)" : "RasFocus Pro (Ready)"; }

        // ==========================================
        // 6. REAL-TIME EYE CURE (NIGHT LIGHT)
        // ==========================================
        private void SetupEyeFilters() {
            eyeFilterDim = new Window { WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = System.Windows.Media.Brushes.Black, Topmost = true, ShowInTaskbar = false, IsHitTestVisible = false, WindowState = WindowState.Maximized, Opacity = 0 };
            eyeFilterWarm = new Window { WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 130, 0)), Topmost = true, ShowInTaskbar = false, IsHitTestVisible = false, WindowState = WindowState.Maximized, Opacity = 0 };
            eyeFilterDim.Show(); eyeFilterWarm.Show();
        }

        private void ApplyEyeFilters() {
            double dimAlpha = (100 - eyeBrightness) / 100.0;
            double warmAlpha = eyeWarmth / 100.0 * 0.7; // Max 70% opacity for warmth
            if (eyeFilterDim != null) eyeFilterDim.Opacity = dimAlpha;
            if (eyeFilterWarm != null) eyeFilterWarm.Opacity = warmAlpha;
        }

        // Call these functions from your XAML sliders (Slider_ValueChanged)
        public void UpdateBrightness(int value) { eyeBrightness = value; ApplyEyeFilters(); }
        public void UpdateWarmth(int value) { eyeWarmth = value; ApplyEyeFilters(); }

        // ==========================================
        // 7. BACKGROUND PROCESSES & FIREBASE
        // ==========================================
        private void FastLoop_Tick(object sender, EventArgs e)
        {
            if (!blockAdult && !blockReels && !blockShorts && !isSessionActive) return;
            if (overlayWindow != null && overlayWindow.IsVisible) return;
            IntPtr hWnd = GetForegroundWindow();
            if (hWnd == IntPtr.Zero) return;
            
            StringBuilder title = new StringBuilder(512); GetWindowText(hWnd, title, 512);
            string sTitle = title.ToString().ToLower();

            if (blockAdult && explicitKeywords.Any(k => sTitle.Contains(k))) { CloseWindowNatively(hWnd); ShowOverlay(GetRandomQuote(1)); return; }
            if (blockReels && sTitle.Contains("facebook") && sTitle.Contains("reels")) { CloseWindowNatively(hWnd); ShowOverlay(GetRandomQuote(2)); return; }
            if (blockShorts && sTitle.Contains("youtube") && sTitle.Contains("shorts")) { CloseWindowNatively(hWnd); ShowOverlay(GetRandomQuote(2)); return; }
            if (isSessionActive && (sTitle.Contains("chrome") || sTitle.Contains("edge") || sTitle.Contains("firefox") || sTitle.Contains("brave"))) {
                if (useAllowMode) { if (!allowedWebs.Any(w => sTitle.Contains(w.ToLower())) && !safeBrowserTitles.Any(s => sTitle.Contains(s))) { CloseWindowNatively(hWnd); ShowOverlay("Website not in Allow List!"); } }
                else { if (blockedWebs.Any(w => sTitle.Contains(w.ToLower()))) { CloseWindowNatively(hWnd); ShowOverlay("Website Blocked by Focus Mode!"); } }
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
                try { string n = p.ProcessName.ToLower() + ".exe"; if (n == "taskmgr.exe") { p.Kill(); continue; } 
                if (useAllowMode) { if (!systemApps.Contains(n) && !allowedApps.Contains(n)) p.Kill(); } else { if (blockedApps.Contains(n)) p.Kill(); } } catch { }
            }
        }

        private string GetDeviceID() { return Environment.MachineName; }

        private void SyncLoop_Tick(object sender, EventArgs e) {
            string activeStr = isSessionActive ? "$true" : "$false";
            string url = $"https://firestore.googleapis.com/v1/projects/mywebtools-f8d53/databases/(default)/documents/subscription_requests/{GetDeviceID()}?updateMask.fieldPaths=isSelfControlActive&key=AIzaSyDGd3KAo45UuqmeGFALziz_oKm3htEASHY";
            string cmd = $"$body = @{{ fields = @{{ isSelfControlActive = @{{ booleanValue = {activeStr} }} }} }} | ConvertTo-Json -Depth 5; Invoke-RestMethod -Uri '{url}' -Method Patch -Body $body -ContentType 'application/json'";
            Process.Start(new ProcessStartInfo { FileName = "powershell.exe", Arguments = $"-WindowStyle Hidden -Command \"{cmd}\"", CreateNoWindow = true, UseShellExecute = false });
        }

        // Real-Time Firebase Polling for Admin Messages, Broadcast, and Chat
        private async void FirebasePoll_Tick(object sender, EventArgs e) {
            try {
                using (HttpClient client = new HttpClient()) {
                    string url = $"https://firestore.googleapis.com/v1/projects/mywebtools-f8d53/databases/(default)/documents/subscription_requests/{GetDeviceID()}?key=AIzaSyDGd3KAo45UuqmeGFALziz_oKm3htEASHY";
                    string res = await client.GetStringAsync(url);
                    
                    if (res.Contains("\"stringValue\": \"REVOKED\"")) { isTrialExpired = true; isLicenseValid = false; }
                    else if (res.Contains("\"stringValue\": \"APPROVED\"")) { isLicenseValid = true; isTrialExpired = false; }

                    // Parse Admin Broadcast
                    if(res.Contains("\"broadcastMsg\"")) {
                        string bMsg = ExtractFirebaseString(res, "broadcastMsg");
                        if (!string.IsNullOrEmpty(bMsg) && bMsg != "ACK" && bMsg != lastBroadcastMsg) {
                            lastBroadcastMsg = bMsg; ShowOverlay("📢 ADMIN BROADCAST:\n" + bMsg);
                            ClearFirebaseField("broadcastMsg"); // Ack
                        }
                    }

                    // Parse Admin Warning Message
                    if(res.Contains("\"adminMessage\"")) {
                        string aMsg = ExtractFirebaseString(res, "adminMessage");
                        if (!string.IsNullOrEmpty(aMsg) && aMsg != lastAdminMsg) { lastAdminMsg = aMsg; ShowOverlay("⚠️ ADMIN NOTICE:\n" + aMsg); }
                    }
                }
            } catch { }
        }

        private string ExtractFirebaseString(string json, string field) {
            int p1 = json.IndexOf($"\"{field}\""); if (p1 == -1) return "";
            int p2 = json.IndexOf("\"stringValue\": \"", p1); if (p2 == -1) return "";
            p2 += 16; int p3 = json.IndexOf("\"", p2);
            return p3 != -1 ? json.Substring(p2, p3 - p2) : "";
        }

        private void ClearFirebaseField(string field) {
            string url = $"https://firestore.googleapis.com/v1/projects/mywebtools-f8d53/databases/(default)/documents/subscription_requests/{GetDeviceID()}?updateMask.fieldPaths={field}&key=AIzaSyDGd3KAo45UuqmeGFALziz_oKm3htEASHY";
            string cmd = $"$body = @{{ fields = @{{ {field} = @{{ stringValue = 'ACK' }} }} }} | ConvertTo-Json -Depth 5; Invoke-RestMethod -Uri '{url}' -Method Patch -Body $body -ContentType 'application/json'";
            Process.Start(new ProcessStartInfo { FileName = "powershell.exe", Arguments = $"-WindowStyle Hidden -Command \"{cmd}\"", CreateNoWindow = true, UseShellExecute = false });
        }

        // ==========================================
        // 8. TYPING BLOCKER HOOK
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
        // 9. UTILITIES
        // ==========================================
        private string GetRandomQuote(int type) { Random r = new Random(); return type == 1 ? islamicQuotes[r.Next(islamicQuotes.Length)] : timeQuotes[r.Next(timeQuotes.Length)]; }
        private void CloseWindowNatively(IntPtr hWnd) { SetForegroundWindow(hWnd); keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero); keybd_event((byte)'W', 0, 0, UIntPtr.Zero); keybd_event((byte)'W', 0, KEYEVENTF_KEYUP, UIntPtr.Zero); keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero); Thread.Sleep(50); ShowWindow(hWnd, SW_MINIMIZE); }
        private void ShowOverlay(string msg) {
            if (overlayWindow == null) {
                overlayWindow = new Window { WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(240, 9, 61, 31)), Topmost = true, Width = 800, Height = 300, WindowStartupLocation = WindowStartupLocation.CenterScreen };
                overlayWindow.Content = new TextBlock { Text = msg, Foreground = System.Windows.Media.Brushes.White, FontSize = 26, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(20) };
            } else { ((TextBlock)overlayWindow.Content).Text = msg; }
            overlayWindow.Show(); overlayWindow.Topmost = true;
            DispatcherTimer t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) }; t.Tick += (s, ev) => { overlayWindow.Hide(); t.Stop(); }; t.Start();
        }
        private void ManageFocusSound(bool start) { string aPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "focus_noise.mp3"); if (start && File.Exists(aPath)) { mciSendString($"open \"{aPath}\" type mpegvideo alias focusSound", null, 0, IntPtr.Zero); mciSendString("play focusSound repeat", null, 0, IntPtr.Zero); } else { mciSendString("stop focusSound", null, 0, IntPtr.Zero); mciSendString("close focusSound", null, 0, IntPtr.Zero); } }
        private void ClearSessionData() { isSessionActive = false; isPassMode = false; isPomodoroMode = false; currentSessionPass = ""; SaveData(); UpdateUIStates(); ManageFocusSound(false); }
        private void SaveData() { File.WriteAllLines(Path.Combine(secretDir, "bl_app.txt"), blockedApps); }
        private void LoadData() { string p = Path.Combine(secretDir, "bl_app.txt"); if (File.Exists(p)) blockedApps = File.ReadAllLines(p).ToList(); }
    }
}

#pragma warning disable CS8600, CS8602, CS8604, CS8618, CS8622, CS8625, CS0414
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using System.Reflection;

namespace RasFocusPro
{
    public partial class MainWindow : Window
    {
        // ==========================================
        // WINDOWS NATIVE API (P/Invoke)
        // ==========================================
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true)]
        static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("winmm.dll")]
        private static extern long mciSendString(string strCommand, StringBuilder strReturn, int iReturnLength, IntPtr hwndCallback);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        static extern uint RegisterWindowMessage(string lpString);

        [DllImport("user32.dll")]
        static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        // Constants
        private const int SW_MINIMIZE = 6;
        private const int SW_RESTORE = 9;
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const byte VK_CONTROL = 0x11;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const int HWND_BROADCAST = 0xffff;

        private static uint WM_WAKEUP = RegisterWindowMessage("RasFocusPro_Wakeup");

        // ==========================================
        // GLOBALS & DATA
        // ==========================================
        private DispatcherTimer fastTimer;
        private DispatcherTimer slowTimer;
        private DispatcherTimer syncTimer;
        
        private bool isSessionActive = false, isTimeMode = false, isPassMode = false, useAllowMode = false;
        private bool blockReels = false, blockShorts = false, isAdblockActive = false, blockAdult = true;
        private bool isPomodoroMode = false, isPomodoroBreak = false;
        
        private int focusTimeTotalSeconds = 0, timerTicks = 0;
        private int pomoLengthMin = 25, pomoTotalSessions = 4, pomoCurrentSession = 1, pomoTicks = 0;
        private int eyeBrightness = 100, eyeWarmth = 0;
        
        private string currentSessionPass = "", userProfileName = "Rasel Mia";
        private int trialDaysLeft = 174;
        private bool isLicenseValid = true, isTrialExpired = false;

        private List<string> blockedApps = new List<string>();
        private List<string> blockedWebs = new List<string>();
        private List<string> allowedApps = new List<string>();
        private List<string> allowedWebs = new List<string>();

        private string[] systemApps = { "explorer.exe", "svchost.exe", "taskmgr.exe", "cmd.exe", "conhost.exe", "csrss.exe", "dwm.exe", "lsass.exe", "services.exe", "smss.exe", "wininit.exe", "winlogon.exe", "spoolsv.exe", "fontdrvhost.exe" };
        
        // Adult, Dance & Bangla Bad Words
        private List<string> explicitKeywords = new List<string> {
            "porn", "xxx", "sex", "nude", "nsfw", "xvideos", "pornhub", "xnxx", "xhamster", "brazzers", "onlyfans", "playboy", "mia khalifa", "bhabi", "chudai", "bangla choti", "magi", "sexy",
            "hot video", "hot scene", "desi", "boudi", "devar", "item song", "item dance", "mujra", "belly dance", "bikini", "romance", "kissing", "ullu", "web series", "ullongo", "kapor chara", "tiktok dance", "dj dance", "hot dance", "nongra dance",
            "খাসি", "চটি", "যৌন", "নগ্ন", "উলঙ্গ", "মেয়েদের ছবি", "গরম ভিডিও", "নষ্ট ভিডিও", "নোংরা ভিডিও", "খারাপ ছবি", "পর্ন", "যৌনতা", "choti golpo", "bangla sex", "meyeder chobi", "nongra video", "gorom video", "kapor chara chobi", "bangla hot", "boudi video", "deshi sexy", "biye barir dance", "মাগি", "পটানো", "সেক্সি নাচ", "অশ্লীল", "অশ্লীল ভিডিও", "বৌদি", "দেবর বৌদি", "ভাবি", "গোপন ভিডিও", "ভাইরাল ভিডিও"
        };
        
        private string[] safeBrowserTitles = { "new tab", "start", "blank page", "allowed websites", "loading", "untitled", "connecting", "pomodoro break" };

        private string[] islamicQuotes = { 
            "\"মুমিনদের বলুন, তারা যেন তাদের দৃষ্টি নত রাখে এবং তাদের যৌনাঙ্গর হেফাযত করে।\" - (সূরা আন-নূর: ৩০)", 
            "\"লজ্জাশীলতা কল্যাণ ছাড়া আর কিছুই বয়ে আনে না।\" - (সহীহ বুখারী)" 
        };
        private string[] timeQuotes = { 
            "\"যারা সময়কে মূল্যায়ন করে না, সময়ও তাদেরকে মূল্যায়ন করে না।\" - এ.পি.জে. আবদুল কালাম" 
        };

        private string secretDir;
        private Window overlayWindow = null;
        private Window eyeFilterDim = null;
        private Window eyeFilterWarm = null;

        private System.Windows.Forms.NotifyIcon trayIcon;

        // Keyboard Hook Setup
        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        private static LowLevelKeyboardProc _proc;
        private static IntPtr _hookID = IntPtr.Zero;
        private static string globalKeyBuffer = "";
        private static MainWindow _instance; 
        private static Mutex _mutex = null;

        public MainWindow()
        {
            // 1. Single Instance Check & Wakeup Broadcast (C++ Logic Converted to C#)
            bool createdNew;
            _mutex = new Mutex(true, "RasFocusPro_Mutex_V55", out createdNew);
            if (!createdNew)
            {
                // আগের রান হওয়া উইন্ডো খুঁজবে
                IntPtr hExisting = FindWindow(null, "RasFocus Pro");
                if (hExisting != IntPtr.Zero)
                {
                    PostMessage(hExisting, WM_WAKEUP, IntPtr.Zero, IntPtr.Zero);
                }
                else
                {
                    // ফলব্যাক ব্রডকাস্ট যদি FindWindow মিস করে
                    PostMessage((IntPtr)HWND_BROADCAST, WM_WAKEUP, IntPtr.Zero, IntPtr.Zero);
                }
                Environment.Exit(0); // অ্যাপ এখানেই বন্ধ হয়ে যাবে
                return;
            }

            InitializeComponent();
            _instance = this;
            this.Title = "RasFocus Pro"; // FindWindow এর জন্য টাইটেল ফিক্স করা হলো

            // 2. Set App Icon (Top Left & Tray)
            SetAppIcons();

            // 3. Setup Desktop Shortcut & Registry AutoStart
            CreateDesktopShortcut();
            SetupAutoStart();

            secretDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RasFocusPro");
            Directory.CreateDirectory(secretDir);

            LoadData();
            RefreshRunningApps();
            SetupTrayIcon();
            SetupEyeFilters();

            _proc = HookCallback;
            _hookID = SetHook(_proc);

            fastTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            fastTimer.Tick += FastLoop_Tick;
            fastTimer.Start();

            slowTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            slowTimer.Tick += SlowLoop_Tick;
            slowTimer.Start();

            syncTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            syncTimer.Tick += SyncLoop_Tick;
            syncTimer.Start();

            // Handle Silent Startup from Registry
            string[] args = Environment.GetCommandLineArgs();
            if (args.Contains("-autostart"))
            {
                this.WindowState = WindowState.Minimized;
                this.Hide();
            }
        }

        // ==========================================
        // ICON, SHORTCUT & AUTOSTART LOGIC
        // ==========================================
        private void SetAppIcons()
        {
            try
            {
                string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.ico");
                if (File.Exists(iconPath))
                {
                    this.Icon = new BitmapImage(new Uri(iconPath));
                }
            }
            catch { /* Ignore if icon is missing */ }
        }

        private void CreateDesktopShortcut()
        {
            try
            {
                string shortcutPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "RasFocus Pro.lnk");
                if (!File.Exists(shortcutPath))
                {
                    Type t = Type.GetTypeFromProgID("WScript.Shell");
                    dynamic shell = Activator.CreateInstance(t);
                    var shortcut = shell.CreateShortcut(shortcutPath);
                    shortcut.TargetPath = Process.GetCurrentProcess().MainModule.FileName;
                    shortcut.WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory;
                    shortcut.Description = "Launch RasFocus Pro";
                    shortcut.IconLocation = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.ico");
                    shortcut.Save();
                }
            }
            catch { /* Silently fail if unable to create shortcut */ }
        }

        private void SetupAutoStart()
        {
            try
            {
                string appPath = "\"" + Process.GetCurrentProcess().MainModule.FileName + "\" -autostart";
                RegistryKey key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
                if (key != null)
                {
                    key.SetValue("RasFocusPro", appPath);
                    key.Close();
                }
            }
            catch { /* Silently fail if registry access is denied */ }
        }

        // Catch WM_WAKEUP to bring window to front
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            HwndSource source = PresentationSource.FromVisual(this) as HwndSource;
            source.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_WAKEUP)
            {
                ShowAppFromTray();
                handled = true;
            }
            return IntPtr.Zero;
        }

        protected override void OnClosed(EventArgs e)
        {
            UnhookWindowsHookEx(_hookID);
            if (trayIcon != null) trayIcon.Dispose();
            base.OnClosed(e);
        }

        // ==========================================
        // SYSTEM TRAY LOGIC
        // ==========================================
        private void SetupTrayIcon()
        {
            trayIcon = new System.Windows.Forms.NotifyIcon();
            string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.ico");
            if (File.Exists(iconPath)) { trayIcon.Icon = new System.Drawing.Icon(iconPath); }
            else { trayIcon.Icon = System.Drawing.SystemIcons.Shield; }

            trayIcon.Text = "RasFocus Pro - Focus Manager";
            trayIcon.Visible = true;
            trayIcon.DoubleClick += (s, e) => ShowAppFromTray();

            System.Windows.Forms.ContextMenuStrip menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add("Open RasFocus", null, (s, e) => ShowAppFromTray());
            menu.Items.Add("Exit App", null, (s, e) => 
            {
                if (isSessionActive)
                {
                    System.Windows.MessageBox.Show("Cannot exit while Focus Mode is active! Please stop it first.", "Security Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else
                {
                    trayIcon.Dispose();
                    System.Windows.Application.Current.Shutdown();
                }
            });
            trayIcon.ContextMenuStrip = menu;
        }

        private void ShowAppFromTray()
        {
            this.Show();
            if (this.WindowState == WindowState.Minimized) this.WindowState = WindowState.Normal;
            this.Activate(); 
            this.Topmost = true; 
            this.Topmost = false; 
            this.Focus();
            
            // Native force to foreground
            IntPtr hWnd = new WindowInteropHelper(this).Handle;
            ShowWindow(hWnd, SW_RESTORE);
            SetForegroundWindow(hWnd);
        }

        // ==========================================
        // UI EVENT HANDLERS
        // ==========================================
        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { DragMove(); }
        
        private void CloseButton_Click(object sender, RoutedEventArgs e) 
        { 
            this.Hide(); 
            trayIcon.ShowBalloonTip(2000, "RasFocus Pro", "Running securely in the background...", System.Windows.Forms.ToolTipIcon.Info);
        }

        private void SidebarList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // if (PageFocusMode == null) return;
            int index = SidebarList.SelectedIndex;
            // PageFocusMode.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
            // PageEyeCure.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
            // PagePomodoro.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            // userProfileName = TxtProfileName.Text; 
            System.Windows.MessageBox.Show("Profile Name Saved!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            if (isSessionActive) return;
            
            // Allow Mode logic parsing
            // useAllowMode = (RbAllowList.IsChecked == true);

            isPassMode = true;
            // currentSessionPass = TxtPass.Text; 
            isSessionActive = true;
            
            SaveData();
            UpdateUIStates();
            ManageFocusSound(true);
            
            System.Windows.MessageBox.Show("Focus Mode Active. Unauthorized apps and websites are now blocked.", "RasFocus Pro Security", MessageBoxButton.OK, MessageBoxImage.Information);
            this.Hide(); 
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            if (!isSessionActive) return;

            // Password verification logic goes here
            ClearSessionData();
            System.Windows.MessageBox.Show("Session Stopped Successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnPomoStart_Click(object sender, RoutedEventArgs e)
        {
            if (!isSessionActive && !isTrialExpired)
            {
                isPomodoroMode = true;
                isSessionActive = true;
                pomoTicks = 0;
                pomoCurrentSession = 1;
                SaveData();
                UpdateUIStates();
                ManageFocusSound(true);
            }
        }

        private void BtnPomoStop_Click(object sender, RoutedEventArgs e)
        {
            if (isPomodoroMode)
            {
                ClearSessionData();
                UpdateUIStates();
            }
        }

        private void UpdateUIStates()
        {
            trayIcon.Text = isSessionActive ? "RasFocus Pro (ACTIVE 🔒)" : "RasFocus Pro (Ready)";
        }

        // ==========================================
        // BACKGROUND LOGIC (TIMERS)
        // ==========================================
        private void RefreshRunningApps()
        {
            // ListRunningApps.Items.Clear();
            var apps = Process.GetProcesses().Select(p => p.ProcessName.ToLower() + ".exe").Distinct().OrderBy(n => n);
            // foreach (var app in apps) ListRunningApps.Items.Add(app);
        }

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

                // 1. Explicit Keywords Block
                if (blockAdult && explicitKeywords.Any(k => sTitle.Contains(k)))
                {
                    CloseWindowNatively(hWnd);
                    ShowOverlay(GetRandomQuote(1));
                    return;
                }

                // 2. Social Media Block
                if (blockReels && sTitle.Contains("facebook") && sTitle.Contains("reels")) { CloseWindowNatively(hWnd); ShowOverlay(GetRandomQuote(2)); return; }
                if (blockShorts && sTitle.Contains("youtube") && sTitle.Contains("shorts")) { CloseWindowNatively(hWnd); ShowOverlay(GetRandomQuote(2)); return; }

                // 3. Focus Mode Logic
                if (isSessionActive)
                {
                    bool isBrowser = sTitle.Contains("chrome") || sTitle.Contains("edge") || sTitle.Contains("firefox") || sTitle.Contains("brave");
                    if (isBrowser)
                    {
                        if (useAllowMode)
                        {
                            if (!allowedWebs.Any(w => sTitle.Contains(w.ToLower())) && !sTitle.Contains("allowed websites"))
                            {
                                CloseWindowNatively(hWnd);
                                ShowOverlay("Website not in Allow List!");
                            }
                        }
                        else
                        {
                            if (blockedWebs.Any(w => sTitle.Contains(w.ToLower())))
                            {
                                CloseWindowNatively(hWnd);
                                ShowOverlay("Website Blocked by Focus Mode!");
                            }
                        }
                    }
                }
            }
        }

        private void SlowLoop_Tick(object sender, EventArgs e)
        {
            // Apply Real-time Eye Filters
            ApplyEyeFiltersRealtime();

            if (!isSessionActive) return;

            // Pomodoro Logic
            if (isPomodoroMode)
            {
                pomoTicks++;
                int totalMins = isPomodoroBreak ? 5 : pomoLengthMin;
                int left = (totalMins * 60) - pomoTicks;
                if (left < 0) left = 0;

                TimeSpan t = TimeSpan.FromSeconds(left);
                // LblPomoTime.Text = t.ToString(@"hh\:mm\:ss");

                if (!isPomodoroBreak && pomoTicks >= pomoLengthMin * 60)
                {
                    isPomodoroBreak = true;
                    pomoTicks = 0;
                    ShowOverlay("☕ Time to Rest & Drink Water! Break Started.");
                }
            }

            // Native Process Killer (The Enforcer)
            foreach (Process p in Process.GetProcesses())
            {
                try
                {
                    string pName = p.ProcessName.ToLower() + ".exe";
                    if (pName == "taskmgr.exe" || pName == "msiexec.exe") { p.Kill(); continue; } 

                    if (useAllowMode)
                    {
                        if (!systemApps.Contains(pName) && !allowedApps.Contains(pName)) p.Kill();
                    }
                    else
                    {
                        if (blockedApps.Contains(pName)) p.Kill();
                    }
                }
                catch { /* Ignore access denied */ }
            }
        }

        private void SyncLoop_Tick(object sender, EventArgs e)
        {
            // Firebase Live Tracker Sync and Admin Messages (PowerShell implementation as requested)
            try
            {
                string deviceId = GetDeviceId();
                string activeStr = isSessionActive ? "$true" : "$false";
                string mode = isPomodoroMode ? "Pomodoro" : (isPassMode ? "Password" : "None");
                
                string url = $"https://firestore.googleapis.com/v1/projects/mywebtools-f8d53/databases/(default)/documents/subscription_requests/{deviceId}?updateMask.fieldPaths=isSelfControlActive&updateMask.fieldPaths=activeModeType&key=AIzaSyDGd3KAo45UuqmeGFALziz_oKm3htEASHY";
                string jsonBody = $"{{\"fields\":{{\"isSelfControlActive\":{{\"booleanValue\":{activeStr}}},\"activeModeType\":{{\"stringValue\":\"{mode}\"}}}}}}";
                
                string psCommand = $"-WindowStyle Hidden -Command \"Invoke-RestMethod -Uri '{url}' -Method Patch -Body '{jsonBody}' -ContentType 'application/json'\"";
                Process.Start(new ProcessStartInfo("powershell.exe", psCommand) { CreateNoWindow = true, UseShellExecute = false });
            }
            catch { /* Silently fail if network issue */ }
        }

        private string GetDeviceId()
        {
            return Environment.MachineName; // Simplified device ID for C#
        }

        // ==========================================
        // UTILITIES & NATIVE HELPERS
        // ==========================================
        private string GetRandomQuote(int type)
        {
            Random r = new Random();
            if (type == 1) return islamicQuotes[r.Next(islamicQuotes.Length)];
            else return timeQuotes[r.Next(timeQuotes.Length)];
        }

        private void CloseWindowNatively(IntPtr hWnd)
        {
            SetForegroundWindow(hWnd);
            keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
            keybd_event((byte)'W', 0, 0, UIntPtr.Zero);
            keybd_event((byte)'W', 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            Thread.Sleep(50);
            ShowWindow(hWnd, SW_MINIMIZE);
        }

        private void ShowOverlay(string message)
        {
            if (overlayWindow == null)
            {
                overlayWindow = new Window
                {
                    WindowStyle = WindowStyle.None, AllowsTransparency = true,
                    Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(240, 9, 61, 31)),
                    Topmost = true, Width = 800, Height = 300,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen
                };
                TextBlock tb = new TextBlock
                {
                    Text = message, Foreground = System.Windows.Media.Brushes.White, FontSize = 24, FontWeight = FontWeights.Bold,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(20)
                };
                overlayWindow.Content = tb;
            }
            else
            {
                ((TextBlock)overlayWindow.Content).Text = message;
            }
            
            overlayWindow.Show();
            overlayWindow.Topmost = true;

            DispatcherTimer t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
            t.Tick += (s, ev) => { overlayWindow.Hide(); t.Stop(); };
            t.Start();
        }

        private void SetupEyeFilters()
        {
            eyeFilterDim = new Window { WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = System.Windows.Media.Brushes.Black, Topmost = true, ShowInTaskbar = false, IsHitTestVisible = false, WindowState = WindowState.Maximized, Opacity = 0 };
            eyeFilterWarm = new Window { WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 130, 0)), Topmost = true, ShowInTaskbar = false, IsHitTestVisible = false, WindowState = WindowState.Maximized, Opacity = 0 };
            eyeFilterDim.Show(); eyeFilterWarm.Show();
        }

        private void ApplyEyeFiltersRealtime()
        {
            if (eyeFilterDim != null && eyeFilterWarm != null)
            {
                // UI থেকে eyeBrightness এবং eyeWarmth স্লাইডারের ভ্যালু রিড করে এখানে বসবে
                double dimOpacity = (100 - eyeBrightness) / 100.0;
                double warmOpacity = (eyeWarmth / 100.0) * 0.5; // max 50% opacity for warmth

                eyeFilterDim.Opacity = dimOpacity;
                eyeFilterDim.Visibility = dimOpacity > 0 ? Visibility.Visible : Visibility.Hidden;
                
                eyeFilterWarm.Opacity = warmOpacity;
                eyeFilterWarm.Visibility = warmOpacity > 0 ? Visibility.Visible : Visibility.Hidden;
            }
        }

        private void ManageFocusSound(bool start)
        {
            string audioPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "focus_noise.mp3");
            if (start && File.Exists(audioPath))
            {
                mciSendString($"open \"{audioPath}\" type mpegvideo alias focusSound", null, 0, IntPtr.Zero);
                mciSendString("play focusSound repeat", null, 0, IntPtr.Zero);
            }
            else
            {
                mciSendString("stop focusSound", null, 0, IntPtr.Zero);
                mciSendString("close focusSound", null, 0, IntPtr.Zero);
            }
        }

        private void ClearSessionData()
        {
            isSessionActive = false;
            isPassMode = false;
            isPomodoroMode = false;
            currentSessionPass = "";
            SaveData();
            UpdateUIStates();
            ManageFocusSound(false);
        }

        // ==========================================
        // REAL-TIME KEYBOARD HOOK (TYPING BLOCKER)
        // ==========================================
        private static IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                return SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN && _instance != null && _instance.blockAdult)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                char c = (char)vkCode;
                if (char.IsLetterOrDigit(c) || char.IsPunctuation(c))
                {
                    globalKeyBuffer += char.ToLower(c);
                    if (globalKeyBuffer.Length > 50) globalKeyBuffer = globalKeyBuffer.Substring(1);

                    foreach (string kw in _instance.explicitKeywords)
                    {
                        if (globalKeyBuffer.Contains(kw))
                        {
                            globalKeyBuffer = ""; // Reset buffer
                            IntPtr hActive = GetForegroundWindow();
                            if (hActive != IntPtr.Zero) _instance.CloseWindowNatively(hActive);
                            
                            // Show Islamic Quote immediately on bad word typing
                            _instance.ShowOverlay(_instance.GetRandomQuote(1));
                            break;
                        }
                    }
                }
            }
            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        // ==========================================
        // DATA SAVE/LOAD
        // ==========================================
        private void SaveData()
        {
            File.WriteAllLines(Path.Combine(secretDir, "bl_app.txt"), blockedApps);
        }

        private void LoadData()
        {
            string path = Path.Combine(secretDir, "bl_app.txt");
            if (File.Exists(path))
            {
                blockedApps = File.ReadAllLines(path).ToList();
            }
        }
    }
}

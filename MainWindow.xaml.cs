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

        [DllImport("user32.dll", SetLastError = true)]
        static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("winmm.dll", CharSet = CharSet.Auto)]
        private static extern long mciSendString(string strCommand, string strReturn, int iReturnLength, IntPtr hwndCallback);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        // Constants
        private const int SW_MINIMIZE = 6;
        private const int SW_RESTORE = 9;
        private const byte VK_CONTROL = 0x11;
        private const uint KEYEVENTF_KEYUP = 0x0002;

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

        // 100% Safe App Directory Finder
        private string AppBaseDir 
        {
            get 
            {
                string path = AppDomain.CurrentDomain.BaseDirectory;
                if (string.IsNullOrEmpty(path) || path.Contains("Temp"))
                {
                    path = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName ?? "") ?? @"C:\";
                }
                return path;
            }
        }

        public MainWindow()
        {
            try
            {
                if (Application.Current != null)
                {
                    Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                }

                // ========================================================
                // ULTIMATE CRASH-FREE SINGLE INSTANCE CHECK
                // ========================================================
                Process currentProcess = Process.GetCurrentProcess();
                Process[] runningProcesses = Process.GetProcessesByName(currentProcess.ProcessName);

                if (runningProcesses.Length > 1)
                {
                    // যদি আগে থেকেই অ্যাপ চলে, তবে আগের উইন্ডো খুঁজে বের করে সামনে আনবে
                    IntPtr hWnd = FindWindow(null, "RasFocus Pro");
                    if (hWnd != IntPtr.Zero)
                    {
                        ShowWindow(hWnd, SW_RESTORE);
                        SetForegroundWindow(hWnd);
                    }
                    
                    // কোনো মেসেজ বা ইভেন্ট ট্রিগার না করেই ডিরেক্ট কিল করে দেবে (No Crash)
                    Environment.Exit(0);
                    return;
                }

                InitializeComponent();
                this.Title = "RasFocus Pro";
                
                this.Loaded += MainWindow_Loaded;
            }
            catch (Exception) { /* Silent fail */ }
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                SetAppIcons();
                CreateDesktopShortcut();
                SetupAutoStart();

                secretDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RasFocusPro");
                Directory.CreateDirectory(secretDir);

                LoadData();
                RefreshRunningApps();
                SetupTrayIcon();
                SetupEyeFilters(); 

                fastTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
                fastTimer.Tick += FastLoop_Tick;
                fastTimer.Start();

                slowTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                slowTimer.Tick += SlowLoop_Tick;
                slowTimer.Start();

                syncTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
                syncTimer.Tick += SyncLoop_Tick;
                syncTimer.Start();

                string[] args = Environment.GetCommandLineArgs();
                if (args.Contains("-autostart"))
                {
                    this.WindowState = WindowState.Minimized;
                    this.Hide();
                }
            }
            catch (Exception) { /* Silent fail */ }
        }

        // ==========================================
        // ICON, SHORTCUT & AUTOSTART LOGIC
        // ==========================================
        private void SetAppIcons()
        {
            try
            {
                string iconPath = Path.Combine(AppBaseDir, "icon.ico");
                if (File.Exists(iconPath)) { this.Icon = new BitmapImage(new Uri(iconPath)); }
            }
            catch { }
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
                    shortcut.TargetPath = Process.GetCurrentProcess().MainModule?.FileName ?? ""; 
                    shortcut.WorkingDirectory = AppBaseDir;
                    shortcut.Description = "Launch RasFocus Pro";
                    shortcut.IconLocation = Path.Combine(AppBaseDir, "icon.ico");
                    shortcut.Save();
                }
            }
            catch { }
        }

        private void SetupAutoStart()
        {
            try
            {
                string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (string.IsNullOrEmpty(exePath)) return;

                string appPath = "\"" + exePath + "\" -autostart"; 
                RegistryKey key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
                if (key != null) { key.SetValue("RasFocusPro", appPath); key.Close(); }
            }
            catch { }
        }

        protected override void OnClosed(EventArgs e)
        {
            if (trayIcon != null) trayIcon.Dispose();
            base.OnClosed(e);
            Application.Current.Shutdown();
        }

        // ==========================================
        // SYSTEM TRAY LOGIC
        // ==========================================
        private void SetupTrayIcon()
        {
            try
            {
                trayIcon = new System.Windows.Forms.NotifyIcon();
                string iconPath = Path.Combine(AppBaseDir, "icon.ico");
                if (File.Exists(iconPath)) { trayIcon.Icon = new System.Drawing.Icon(iconPath); }
                else { trayIcon.Icon = System.Drawing.SystemIcons.Shield; }

                trayIcon.Text = "RasFocus Pro - Focus Manager";
                trayIcon.Visible = true;
                trayIcon.DoubleClick += (s, e) => ShowAppFromTray();

                System.Windows.Forms.ContextMenuStrip menu = new System.Windows.Forms.ContextMenuStrip();
                menu.Items.Add("Open RasFocus", null, (s, e) => ShowAppFromTray());
                menu.Items.Add("Exit App", null, (s, e) => 
                {
                    if (isSessionActive) { System.Windows.MessageBox.Show("Cannot exit while Focus Mode is active! Please stop it first.", "Security Warning", MessageBoxButton.OK, MessageBoxImage.Warning); }
                    else { trayIcon.Dispose(); Application.Current.Shutdown(); }
                });
                trayIcon.ContextMenuStrip = menu;
            }
            catch { }
        }

        private void ShowAppFromTray()
        {
            try
            {
                this.Show();
                if (this.WindowState == WindowState.Minimized) this.WindowState = WindowState.Normal;
                this.Activate(); 
                this.Topmost = true; 
                this.Topmost = false; 
                this.Focus();
                
                IntPtr hWnd = new WindowInteropHelper(this).Handle;
                ShowWindow(hWnd, SW_RESTORE);
                SetForegroundWindow(hWnd);
            }
            catch { }
        }

        // ==========================================
        // UI EVENT HANDLERS
        // ==========================================
        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { DragMove(); }
        
        private void CloseButton_Click(object sender, RoutedEventArgs e) 
        { 
            this.Hide(); 
            if (trayIcon != null) { trayIcon.ShowBalloonTip(2000, "RasFocus Pro", "Running securely in the background...", System.Windows.Forms.ToolTipIcon.Info); }
        }

        private void SidebarList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PageFocusMode == null || PageEyeCure == null || PagePomodoro == null) return;
            int index = SidebarList.SelectedIndex;
            PageFocusMode.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
            PageEyeCure.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
            PagePomodoro.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e) { System.Windows.MessageBox.Show("Profile Name Saved!", "Success", MessageBoxButton.OK, MessageBoxImage.Information); }

        private void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            if (isSessionActive) return;
            isPassMode = true;
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
            if (isPomodoroMode) { ClearSessionData(); UpdateUIStates(); }
        }

        private void UpdateUIStates() { if(trayIcon != null) trayIcon.Text = isSessionActive ? "RasFocus Pro (ACTIVE 🔒)" : "RasFocus Pro (Ready)"; }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e) { this.WindowState = WindowState.Minimized; }
        private void BtnLiveChat_Click(object sender, RoutedEventArgs e) { }
        private void BtnUpgrade_Click(object sender, RoutedEventArgs e) { }
        private void BtnStopWatch_Click(object sender, RoutedEventArgs e) { }
        
        private void BtnAddApp_Click(object sender, RoutedEventArgs e) { string item = ComboAppBlock.Text.Trim(); if(!string.IsNullOrEmpty(item) && !blockedApps.Contains(item)) { blockedApps.Add(item); ListBlockedApps.Items.Add(item); SaveData(); ComboAppBlock.Text = ""; } }
        private void BtnRemApp_Click(object sender, RoutedEventArgs e) { if(ListBlockedApps.SelectedItem != null) { string item = ListBlockedApps.SelectedItem.ToString(); blockedApps.Remove(item); ListBlockedApps.Items.Remove(item); SaveData(); } }
        private void BtnAddWeb_Click(object sender, RoutedEventArgs e) { string item = ComboWebBlock.Text.Trim(); if(!string.IsNullOrEmpty(item) && !blockedWebs.Contains(item)) { blockedWebs.Add(item); ListBlockedWebs.Items.Add(item); SaveData(); ComboWebBlock.Text = ""; } }
        private void BtnAddAllowApp_Click(object sender, RoutedEventArgs e) { string item = ComboAppAllow.Text.Trim(); if(!string.IsNullOrEmpty(item) && !allowedApps.Contains(item)) { allowedApps.Add(item); ListAllowApps.Items.Add(item); SaveData(); ComboAppAllow.Text = ""; } }
        private void BtnRemAllowApp_Click(object sender, RoutedEventArgs e) { if(ListAllowApps.SelectedItem != null) { string item = ListAllowApps.SelectedItem.ToString(); allowedApps.Remove(item); ListAllowApps.Items.Remove(item); SaveData(); } }
        private void BtnAddAllowWeb_Click(object sender, RoutedEventArgs e) { string item = ComboWebAllow.Text.Trim(); if(!string.IsNullOrEmpty(item) && !allowedWebs.Contains(item)) { allowedWebs.Add(item); ListAllowWebs.Items.Add(item); SaveData(); ComboWebAllow.Text = ""; } }
        
        private void BtnRefresh_Click(object sender, RoutedEventArgs e) { RefreshRunningApps(); }

        private void SliderBrightness_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (e != null) { eyeBrightness = (int)e.NewValue; ApplyEyeFiltersRealtime(); } }
        private void SliderWarmth_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) { if (e != null) { eyeWarmth = (int)e.NewValue; ApplyEyeFiltersRealtime(); } }
        private void PresetDay_Click(object sender, RoutedEventArgs e) { SliderBrightness.Value = 100; SliderWarmth.Value = 0; ApplyEyeFiltersRealtime(); }
        private void PresetReading_Click(object sender, RoutedEventArgs e) { SliderBrightness.Value = 85; SliderWarmth.Value = 30; ApplyEyeFiltersRealtime(); }
        private void PresetNight_Click(object sender, RoutedEventArgs e) { SliderBrightness.Value = 60; SliderWarmth.Value = 75; ApplyEyeFiltersRealtime(); }

        // ==========================================
        // BACKGROUND LOGIC (TIMERS)
        // ==========================================
        private void RefreshRunningApps()
        {
            try
            {
                if (ListRunningApps == null) return;
                ListRunningApps.Items.Clear();
                var apps = Process.GetProcesses().Select(p => p.ProcessName.ToLower() + ".exe").Distinct().OrderBy(n => n);
                foreach (var app in apps) { ListRunningApps.Items.Add(app); }
            }
            catch { }
        }

        private void FastLoop_Tick(object sender, EventArgs e)
        {
            try
            {
                if (!blockAdult && !blockReels && !blockShorts && !isSessionActive) return;
                if (overlayWindow != null && overlayWindow.IsVisible) return;

                IntPtr hWnd = GetForegroundWindow();
                if (hWnd != IntPtr.Zero)
                {
                    StringBuilder title = new StringBuilder(512);
                    GetWindowText(hWnd, title, 512);
                    string sTitle = title.ToString().ToLower();

                    if (blockAdult && explicitKeywords.Any(k => sTitle.Contains(k))) 
                    { 
                        CloseWindowNatively(hWnd); 
                        ShowOverlay(GetRandomQuote(1)); 
                        return; 
                    }
                    if (blockReels && sTitle.Contains("facebook") && sTitle.Contains("reels")) { CloseWindowNatively(hWnd); ShowOverlay(GetRandomQuote(2)); return; }
                    if (blockShorts && sTitle.Contains("youtube") && sTitle.Contains("shorts")) { CloseWindowNatively(hWnd); ShowOverlay(GetRandomQuote(2)); return; }

                    if (isSessionActive)
                    {
                        bool isBrowser = sTitle.Contains("chrome") || sTitle.Contains("edge") || sTitle.Contains("firefox") || sTitle.Contains("brave");
                        if (isBrowser)
                        {
                            if (useAllowMode)
                            {
                                if (!allowedWebs.Any(w => sTitle.Contains(w.ToLower())) && !sTitle.Contains("allowed websites"))
                                {
                                    CloseWindowNatively(hWnd); ShowOverlay("Website not in Allow List!");
                                }
                            }
                            else
                            {
                                if (blockedWebs.Any(w => sTitle.Contains(w.ToLower())))
                                {
                                    CloseWindowNatively(hWnd); ShowOverlay("Website Blocked by Focus Mode!");
                                }
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private void SlowLoop_Tick(object sender, EventArgs e)
        {
            try
            {
                ApplyEyeFiltersRealtime();
                if (!isSessionActive) return;

                if (isPomodoroMode)
                {
                    pomoTicks++;
                    int totalMins = isPomodoroBreak ? 5 : pomoLengthMin;
                    int left = (totalMins * 60) - pomoTicks;
                    if (left < 0) left = 0;

                    if (!isPomodoroBreak && pomoTicks >= pomoLengthMin * 60)
                    {
                        isPomodoroBreak = true; pomoTicks = 0;
                        ShowOverlay("☕ Time to Rest & Drink Water! Break Started.");
                    }
                }

                foreach (Process p in Process.GetProcesses())
                {
                    try
                    {
                        string pName = p.ProcessName.ToLower() + ".exe";
                        if (pName == "taskmgr.exe" || pName == "msiexec.exe") { p.Kill(); continue; } 

                        if (useAllowMode) { if (!systemApps.Contains(pName) && !allowedApps.Contains(pName)) p.Kill(); }
                        else { if (blockedApps.Contains(pName)) p.Kill(); }
                    }
                    catch { }
                }
            }
            catch { }
        }

        private void SyncLoop_Tick(object sender, EventArgs e)
        {
            try
            {
                string deviceId = Environment.MachineName;
                string activeStr = isSessionActive ? "$true" : "$false";
                string mode = isPomodoroMode ? "Pomodoro" : (isPassMode ? "Password" : "None");
                string url = $"https://firestore.googleapis.com/v1/projects/mywebtools-f8d53/databases/(default)/documents/subscription_requests/{deviceId}?updateMask.fieldPaths=isSelfControlActive&updateMask.fieldPaths=activeModeType&key=AIzaSyDGd3KAo45UuqmeGFALziz_oKm3htEASHY";
                string jsonBody = $"{{\"fields\":{{\"isSelfControlActive\":{{\"booleanValue\":{activeStr}}},\"activeModeType\":{{\"stringValue\":\"{mode}\"}}}}}}";
                string psCommand = $"-WindowStyle Hidden -Command \"Invoke-RestMethod -Uri '{url}' -Method Patch -Body '{jsonBody}' -ContentType 'application/json'\"";
                Process.Start(new ProcessStartInfo("powershell.exe", psCommand) { CreateNoWindow = true, UseShellExecute = false });
            }
            catch { }
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
            try
            {
                SetForegroundWindow(hWnd);
                keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
                keybd_event((byte)'W', 0, 0, UIntPtr.Zero);
                keybd_event((byte)'W', 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                Thread.Sleep(50); ShowWindow(hWnd, SW_MINIMIZE);
            }
            catch { }
        }

        private void ShowOverlay(string message)
        {
            try
            {
                if (overlayWindow == null)
                {
                    overlayWindow = new Window { WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(240, 9, 61, 31)), Topmost = true, Width = 800, Height = 300, WindowStartupLocation = WindowStartupLocation.CenterScreen };
                    TextBlock tb = new TextBlock { Text = message, Foreground = System.Windows.Media.Brushes.White, FontSize = 24, FontWeight = FontWeights.Bold, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(20) };
                    overlayWindow.Content = tb;
                }
                else { ((TextBlock)overlayWindow.Content).Text = message; }
                overlayWindow.Show(); overlayWindow.Topmost = true;
                DispatcherTimer t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
                t.Tick += (s, ev) => { overlayWindow.Hide(); t.Stop(); }; t.Start();
            }
            catch { }
        }

        private void SetupEyeFilters()
        {
            try
            {
                eyeFilterDim = new Window { WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = System.Windows.Media.Brushes.Black, Topmost = true, ShowInTaskbar = false, IsHitTestVisible = false, WindowState = WindowState.Maximized, Opacity = 0 };
                eyeFilterWarm = new Window { WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 130, 0)), Topmost = true, ShowInTaskbar = false, IsHitTestVisible = false, WindowState = WindowState.Maximized, Opacity = 0 };
                eyeFilterDim.Show(); eyeFilterWarm.Show();
            }
            catch { }
        }

        private void ApplyEyeFiltersRealtime()
        {
            try
            {
                if (eyeFilterDim != null && eyeFilterWarm != null)
                {
                    double dimOpacity = (100 - eyeBrightness) / 100.0;
                    double warmOpacity = (eyeWarmth / 100.0) * 0.5; 
                    eyeFilterDim.Opacity = dimOpacity; eyeFilterDim.Visibility = dimOpacity > 0 ? Visibility.Visible : Visibility.Hidden;
                    eyeFilterWarm.Opacity = warmOpacity; eyeFilterWarm.Visibility = warmOpacity > 0 ? Visibility.Visible : Visibility.Hidden;
                }
            }
            catch { }
        }

        private void ManageFocusSound(bool start)
        {
            try
            {
                string audioPath = Path.Combine(AppBaseDir, "focus_noise.mp3");
                if (start && File.Exists(audioPath)) { mciSendString($"open \"{audioPath}\" type mpegvideo alias focusSound", null, 0, IntPtr.Zero); mciSendString("play focusSound repeat", null, 0, IntPtr.Zero); }
                else { mciSendString("stop focusSound", null, 0, IntPtr.Zero); mciSendString("close focusSound", null, 0, IntPtr.Zero); }
            }
            catch { }
        }

        private void ClearSessionData()
        {
            isSessionActive = false; isPassMode = false; isPomodoroMode = false; currentSessionPass = "";
            SaveData(); UpdateUIStates(); ManageFocusSound(false);
        }

        // ==========================================
        // DATA SAVE/LOAD
        // ==========================================
        private void SaveData()
        {
            try
            {
                File.WriteAllLines(Path.Combine(secretDir, "bl_app.txt"), blockedApps);
                File.WriteAllLines(Path.Combine(secretDir, "bl_web.txt"), blockedWebs);
                File.WriteAllLines(Path.Combine(secretDir, "al_app.txt"), allowedApps);
                File.WriteAllLines(Path.Combine(secretDir, "al_web.txt"), allowedWebs);
            }
            catch { }
        }

        private void LoadData()
        {
            Action<string, List<string>, ListBox> loadList = (fileName, list, uiBox) => {
                try {
                    string path = Path.Combine(secretDir, fileName);
                    if (File.Exists(path)) { 
                        list.AddRange(File.ReadAllLines(path)); 
                        if(uiBox != null) foreach(var item in list) uiBox.Items.Add(item); 
                    }
                } catch { }
            };
            
            try
            {
                loadList("bl_app.txt", blockedApps, ListBlockedApps);
                loadList("bl_web.txt", blockedWebs, ListBlockedWebs);
                loadList("al_app.txt", allowedApps, ListAllowApps);
                loadList("al_web.txt", allowedWebs, ListAllowWebs);
            }
            catch { }
        }
    }
}

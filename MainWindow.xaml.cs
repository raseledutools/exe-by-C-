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
using System.Windows.Threading;

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

        // Constants
        private const int SW_MINIMIZE = 6;
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
        
        private string currentSessionPass = "", userProfileName = "Rasel Mia";

        private List<string> blockedApps = new List<string>();
        private List<string> blockedWebs = new List<string>();
        private List<string> allowedApps = new List<string>();
        private List<string> allowedWebs = new List<string>();

        private string[] systemApps = { "explorer.exe", "svchost.exe", "taskmgr.exe", "cmd.exe", "conhost.exe", "csrss.exe", "dwm.exe", "lsass.exe", "services.exe", "smss.exe", "wininit.exe", "winlogon.exe", "spoolsv.exe", "fontdrvhost.exe" };

        private List<string> explicitKeywords = new List<string> {
            "porn", "xxx", "sex", "nude", "nsfw", "xvideos", "pornhub", "xnxx", "xhamster", "brazzers", "onlyfans", "playboy", "mia khalifa", "bhabi", "chudai", "bangla choti", "magi", "sexy",
            "hot video", "hot scene", "desi", "boudi", "reel", "item song", "item dance", "shorts", "belly dance", "bikini", "romance", "kissing", "ullu", "web series", "ullongo", "kapor chara", "tiktok dance", "dj dance", "hot dance", "nongra dance",
            "খাসি", "চটি", "যৌন", "নগ্ন", "উলঙ্গ", "মেয়েদের ছবি", "গরম ভিডিও", "নষ্ট ভিডিও", "নোংরা ভিডিও", "খারাপ ছবি", "পর্ন", "যৌনতা", "choti golpo", "bangla sex", "meyeder chobi", "nongra video", "gorom video", "kapor chara chobi", "bangla hot", "boudi video", "deshi sexy", "biye barir dance", "মাগি", "পটানো", "সেক্সি নাচ", "অশ্লীল", "অশ্লীল ভিডিও", "বৌদি", "দেবর বৌদি", "ভাবি", "গোপন ভিডিও", "ভাইরাল ভিডিও"
        };

        private string[] islamicQuotes = { 
            "\"মুমিনদের বলুন, তারা যেন তাদের দৃষ্টি নত রাখে এবং তাদের যৌনাঙ্গর হেফাযত করে।\" - (সূরা আন-নূর: ৩০)", 
            "\"লজ্জাশীলতা কল্যাণ ছাড়া আর কিছুই বয়ে আনে না।\" - (সহীহ বুখারী)" 
        };
        private string[] timeQuotes = { 
            "\"যারা সময়কে মূল্যায়ন করে না, সময়ও তাদেরকে মূল্যায়ন করে না।\" - এ.পি.জে. আবদুল কালাম" 
        };

        private string secretDir;
        private Window overlayWindow = null;

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
            // 1. Single Instance Check & Wakeup Broadcast
            bool createdNew;
            _mutex = new Mutex(true, "RasFocusPro_Mutex_V55", out createdNew);
            if (!createdNew)
            {
                PostMessage((IntPtr)HWND_BROADCAST, WM_WAKEUP, IntPtr.Zero, IntPtr.Zero);
                System.Windows.Application.Current.Shutdown();
                return;
            }

            InitializeComponent();
            _instance = this;
            
            secretDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RasFocusPro");
            Directory.CreateDirectory(secretDir);

            LoadData();
            RefreshRunningApps();
            SetupTrayIcon();
            PopulateComboBoxes();

            _proc = HookCallback;
            _hookID = SetHook(_proc);

            fastTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            fastTimer.Tick += FastLoop_Tick;
            fastTimer.Start();

            slowTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            slowTimer.Tick += SlowLoop_Tick;
            slowTimer.Start();

            syncTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            syncTimer.Tick += SyncLoop_Tick;
            syncTimer.Start();
        }

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
            this.WindowState = WindowState.Normal;
            this.Activate(); 
            this.Topmost = true; 
            this.Topmost = false; 
            this.Focus();
        }

        // ==========================================
        // UI POPULATE & EVENT HANDLERS
        // ==========================================
        private void PopulateComboBoxes()
        {
            string[] popSites = { "Select..", "facebook.com", "youtube.com", "instagram.com", "tiktok.com", "reddit.com" };
            string[] popApps = { "Select..", "chrome.exe", "msedge.exe", "vlc.exe", "telegram.exe", "code.exe" };

            foreach (var site in popSites) { ComboWebBlock.Items.Add(site); ComboWebAllow.Items.Add(site); }
            foreach (var app in popApps) { ComboAppBlock.Items.Add(app); ComboAppAllow.Items.Add(app); }

            ComboWebBlock.SelectedIndex = 0; ComboWebAllow.SelectedIndex = 0;
            ComboAppBlock.SelectedIndex = 0; ComboAppAllow.SelectedIndex = 0;
            
            // Checkbox and Radio logic setup based on loaded data
            RadioAllowList.IsChecked = useAllowMode;
            RadioBlockList.IsChecked = !useAllowMode;
            ChkBlockReels.IsChecked = blockReels;
            ChkBlockShorts.IsChecked = blockShorts;
            ChkAdBlock.IsChecked = isAdblockActive;
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { DragMove(); }
        
        private void CloseButton_Click(object sender, RoutedEventArgs e) 
        { 
            this.Hide(); 
            trayIcon.ShowBalloonTip(2000, "RasFocus Pro", "Running securely in the background...", System.Windows.Forms.ToolTipIcon.Info);
        }

        private void SidebarList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PageFocusMode == null) return;
            int index = SidebarList.SelectedIndex;
            PageFocusMode.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
            PageEyeCure.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
            PagePomodoro.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            userProfileName = TxtProfileName.Text;
            System.Windows.MessageBox.Show("Profile Name Saved!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ==========================================
        // FOCUS & POMODORO START/STOP
        // ==========================================
        private void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            if (isSessionActive) return;
            
            int hr = 0, min = 0;
            int.TryParse(TxtTimeHr.Text, out hr);
            int.TryParse(TxtTimeMin.Text, out min);
            
            focusTimeTotalSeconds = (hr * 3600) + (min * 60);
            currentSessionPass = TxtPass.Text;

            if (focusTimeTotalSeconds > 0 || !string.IsNullOrEmpty(currentSessionPass))
            {
                isPassMode = !string.IsNullOrEmpty(currentSessionPass);
                isTimeMode = focusTimeTotalSeconds > 0;
                useAllowMode = (RadioAllowList.IsChecked == true);
                blockReels = (ChkBlockReels.IsChecked == true);
                blockShorts = (ChkBlockShorts.IsChecked == true);
                
                isSessionActive = true;
                timerTicks = 0;
                
                SaveData();
                UpdateUIStates();
                
                System.Windows.MessageBox.Show("Focus Mode Active. Unauthorized apps and websites are now blocked.", "RasFocus Pro Security", MessageBoxButton.OK, MessageBoxImage.Information);
                this.Hide(); 
            }
            else
            {
                System.Windows.MessageBox.Show("Please set a password or a timer to start focus mode.", "Information Needed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            if (!isSessionActive) return;

            if (isPassMode)
            {
                if (TxtPass.Text != currentSessionPass)
                {
                    System.Windows.MessageBox.Show("Wrong password!", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }
            else if (isTimeMode && focusTimeTotalSeconds > 0)
            {
                System.Windows.MessageBox.Show("Timer is still active. Please wait or use password.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ClearSessionData();
            System.Windows.MessageBox.Show("Session Stopped Successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnPomoStart_Click(object sender, RoutedEventArgs e)
        {
            if (!isSessionActive)
            {
                isPomodoroMode = true;
                isSessionActive = true;
                pomoTicks = 0;
                pomoCurrentSession = 1;
                useAllowMode = true; // Force allow mode for Pomodoro
                
                SaveData();
                UpdateUIStates();
                System.Windows.MessageBox.Show("Pomodoro Started! Only Allow List is active.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnPomoStop_Click(object sender, RoutedEventArgs e)
        {
            if (isPomodoroMode)
            {
                ClearSessionData();
                UpdateUIStates();
                System.Windows.MessageBox.Show("Pomodoro Stopped.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void ClearSessionData()
        {
            isSessionActive = false;
            isPassMode = false;
            isTimeMode = false;
            isPomodoroMode = false;
            currentSessionPass = "";
            focusTimeTotalSeconds = 0;
            timerTicks = 0;
            TxtPass.Clear();
            UpdateUIStates();
        }

        private void UpdateUIStates()
        {
            trayIcon.Text = isSessionActive ? "RasFocus Pro (ACTIVE 🔒)" : "RasFocus Pro (Ready)";
            LblStatus.Text = isSessionActive ? "Focus Active!" : "Ready";
            LblStatus.Foreground = isSessionActive ? new SolidColorBrush(Colors.Green) : new SolidColorBrush(Colors.Red);
            
            // Disable inputs while active
            RadioBlockList.IsEnabled = !isSessionActive;
            RadioAllowList.IsEnabled = !isSessionActive;
            BtnStartFocus.IsEnabled = !isSessionActive;
            BtnStopFocus.IsEnabled = isSessionActive;
        }

        // ==========================================
        // BACKGROUND LOGIC (TIMERS)
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

                // 1. Explicit Keywords Block
                if (blockAdult && explicitKeywords.Any(k => sTitle.Contains(k)))
                {
                    CloseWindowNatively(hWnd);
                    ShowOverlay(GetRandomQuote(1));
                    return;
                }

                // 2. Social Media Block
                if (blockReels && sTitle.Contains("facebook") && sTitle.Contains("reel")) { CloseWindowNatively(hWnd); ShowOverlay(GetRandomQuote(2)); return; }
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
            if (!isSessionActive) return;

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
            if (!isSessionActive) return;

            if (isTimeMode && focusTimeTotalSeconds > 0)
            {
                timerTicks++;
                if (timerTicks >= focusTimeTotalSeconds)
                {
                    ClearSessionData();
                    System.Windows.MessageBox.Show("Focus time is over!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            
            if (isPomodoroMode)
            {
                pomoTicks++;
                int totalMins = isPomodoroBreak ? 5 : pomoLengthMin;
                int left = (totalMins * 60) - pomoTicks;
                if (left < 0) left = 0;

                TimeSpan t = TimeSpan.FromSeconds(left);
                LblStatus.Text = string.Format("Pomo: {0:D2}:{1:D2}", t.Minutes, t.Seconds);

                if (!isPomodoroBreak && pomoTicks >= pomoLengthMin * 60)
                {
                    isPomodoroBreak = true;
                    pomoTicks = 0;
                    ShowOverlay("☕ Time to Rest & Drink Water! Break Started.");
                }
                else if (isPomodoroBreak && pomoTicks >= 5 * 60)
                {
                    isPomodoroBreak = false;
                    pomoTicks = 0;
                    pomoCurrentSession++;
                    if (pomoCurrentSession > pomoTotalSessions)
                    {
                        ClearSessionData();
                        System.Windows.MessageBox.Show("All Pomodoro Sessions Completed!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
            }
            else if (isTimeMode)
            {
                int left = focusTimeTotalSeconds - timerTicks;
                if (left < 0) left = 0;
                TimeSpan t = TimeSpan.FromSeconds(left);
                LblStatus.Text = string.Format("Time: {0:D2}:{1:D2}:{2:D2}", t.Hours, t.Minutes, t.Seconds);
            }
        }

        // ==========================================
        // LIST MANAGEMENT (ADD/REMOVE)
        // ==========================================
        private void AddToList(TextBox input, ListBox list, List<string> dataList, string fileName)
        {
            string val = input.Text.Trim();
            if (!string.IsNullOrEmpty(val) && !dataList.Contains(val))
            {
                dataList.Add(val);
                list.Items.Add(val);
                SaveListToFile(dataList, fileName);
                input.Clear();
            }
        }

        private void RemoveFromList(ListBox list, List<string> dataList, string fileName)
        {
            if (list.SelectedIndex != -1)
            {
                string val = list.SelectedItem.ToString();
                dataList.Remove(val);
                list.Items.RemoveAt(list.SelectedIndex);
                SaveListToFile(dataList, fileName);
            }
        }

        private void BtnAddAppBlock_Click(object sender, RoutedEventArgs e) => AddToList(TxtAppBlock, ListAppBlock, blockedApps, "bl_app.txt");
        private void BtnAddWebBlock_Click(object sender, RoutedEventArgs e) => AddToList(TxtWebBlock, ListWebBlock, blockedWebs, "bl_web.txt");
        private void BtnAddAppAllow_Click(object sender, RoutedEventArgs e) => AddToList(TxtAppAllow, ListAppAllow, allowedApps, "al_app.txt");
        private void BtnAddWebAllow_Click(object sender, RoutedEventArgs e) => AddToList(TxtWebAllow, ListWebAllow, allowedWebs, "al_web.txt");

        private void BtnRemAppBlock_Click(object sender, RoutedEventArgs e) => RemoveFromList(ListAppBlock, blockedApps, "bl_app.txt");
        private void BtnRemWebBlock_Click(object sender, RoutedEventArgs e) => RemoveFromList(ListWebBlock, blockedWebs, "bl_web.txt");
        private void BtnRemAppAllow_Click(object sender, RoutedEventArgs e) => RemoveFromList(ListAppAllow, allowedApps, "al_app.txt");
        private void BtnRemWebAllow_Click(object sender, RoutedEventArgs e) => RemoveFromList(ListWebAllow, allowedWebs, "al_web.txt");

        private void RefreshRunningApps()
        {
            ListRunningApps.Items.Clear();
            var processes = Process.GetProcesses().Select(p => p.ProcessName.ToLower() + ".exe").Distinct().OrderBy(p => p);
            foreach (var p in processes)
            {
                if (p != "svchost.exe" && p != "explorer.exe") 
                    ListRunningApps.Items.Add(p);
            }
        }

        private void BtnAddFromRunning_Click(object sender, RoutedEventArgs e)
        {
            if (ListRunningApps.SelectedIndex != -1)
            {
                string selectedApp = ListRunningApps.SelectedItem.ToString();
                if (RadioBlockList.IsChecked == true)
                {
                    TxtAppBlock.Text = selectedApp;
                    BtnAddAppBlock_Click(null, null);
                }
                else
                {
                    TxtAppAllow.Text = selectedApp;
                    BtnAddAppAllow_Click(null, null);
                }
            }
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
                    Background = new SolidColorBrush(Color.FromArgb(240, 9, 61, 31)),
                    Topmost = true, Width = 800, Height = 300,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen
                };
                TextBlock tb = new TextBlock
                {
                    Text = message, Foreground = Brushes.White, FontSize = 24, FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
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
                            globalKeyBuffer = ""; 
                            IntPtr hActive = GetForegroundWindow();
                            if (hActive != IntPtr.Zero) _instance.CloseWindowNatively(hActive);
                            
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
            SaveListToFile(blockedApps, "bl_app.txt");
            SaveListToFile(blockedWebs, "bl_web.txt");
            SaveListToFile(allowedApps, "al_app.txt");
            SaveListToFile(allowedWebs, "al_web.txt");
        }

        private void SaveListToFile(List<string> data, string filename)
        {
            File.WriteAllLines(Path.Combine(secretDir, filename), data);
        }

        private void LoadData()
        {
            LoadListFromFile(ref blockedApps, ListAppBlock, "bl_app.txt");
            LoadListFromFile(ref blockedWebs, ListWebBlock, "bl_web.txt");
            LoadListFromFile(ref allowedApps, ListAppAllow, "al_app.txt");
            LoadListFromFile(ref allowedWebs, ListWebAllow, "al_web.txt");
        }

        private void LoadListFromFile(ref List<string> dataList, ListBox uiList, string filename)
        {
            string path = Path.Combine(secretDir, filename);
            if (File.Exists(path))
            {
                dataList = File.ReadAllLines(path).ToList();
                uiList.Items.Clear();
                foreach (var item in dataList) uiList.Items.Add(item);
            }
        }
    }
}

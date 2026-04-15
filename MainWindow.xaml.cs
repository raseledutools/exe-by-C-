#pragma warning disable CS8600, CS8602, CS8604, CS8618, CS8622, CS8625, CS0414
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
using System.Windows.Threading;
using System.Windows.Automation;
using Microsoft.Win32;

namespace RasFocusPro
{
    public partial class MainWindow : Window
    {
        #region WINDOWS NATIVE API (P/Invoke)
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

        private const int SW_MINIMIZE = 6;
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const byte VK_CONTROL = 0x11;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const int HWND_BROADCAST = 0xffff;
        private static uint WM_WAKEUP = RegisterWindowMessage("RasFocusPro_Wakeup");
        #endregion

        #region GLOBALS & DATA
        private DispatcherTimer fastTimer;
        private DispatcherTimer slowTimer;
        private DispatcherTimer syncTimer;
        private static readonly HttpClient httpClient = new HttpClient();

        private bool isSessionActive = false, isTimeMode = false, isPassMode = false, useAllowMode = false;
        private bool blockReels = false, blockShorts = false, isAdblockActive = false, blockAdult = true;
        private bool isPomodoroMode = false, isPomodoroBreak = false;
        private bool isLicenseValid = true, isTrialExpired = false;

        private int focusTimeTotalSeconds = 0, timerTicks = 0;
        private int pomoLengthMin = 25, pomoTotalSessions = 4, pomoCurrentSession = 1, pomoTicks = 0;
        private int trialDaysLeft = 174;
        private string currentSessionPass = "", userProfileName = "Rasel Mia";
        private string deviceIdCache = "";

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

        private List<string> adultDomains = new List<string> {
            "pornhub.com", "xvideos.com", "xnxx.com", "xhamster.com", "chaturbate.com",
            "spankbang.com", "redtube.com", "youporn.com", "eporner.com", "hqporner.com",
            "brazzers.com", "onlyfans.com", "rule34.video", "tube8.com", "xhamsterlive.com", "xncx.com", "desi.com"
        };

        private string[] islamicQuotes = { 
            "\"মুমিনদের বলুন, তারা যেন তাদের দৃষ্টি নত রাখে এবং তাদের যৌনাঙ্গর হেফাযত করে।\"", 
            "\"লজ্জাশীলতা কল্যাণ ছাড়া আর কিছুই বয়ে আনে না।\"",
            "\"তোমরা অশ্লীলতার ধারেকাছেও যেও না, নিশ্চয়ই এটা চরম নিকৃষ্ট পথ।\"",
            "\"যে ব্যক্তি নিজের জিহ্বা ও লজ্জাস্থানের হেফাজত করবে, তার জন্য জান্নাতের সুসংবাদ।\"",
            "\"চোখের ব্যভিচার হলো হারাম জিনিসের দিকে দৃষ্টিপাত করা, তাই দৃষ্টিকে সংযত রাখো।\"",
            "\"অশ্লীলতা যে কোনো জিনিসকে কলুষিত করে, আর শালীনতা যে কোনো জিনিসকে সুন্দর করে তোলে।\"",
            "\"লজ্জা ও ঈমান একে অপরের সাথে ওতপ্রোতভাবে জড়িত, একটি বিদায় নিলে অপরটিও বিদায় নেয়।\"",
            "\"যে ব্যক্তি হারাম দৃষ্টি থেকে নিজের চোখকে ফিরিয়ে নেয়, সৃষ্টিকর্তা তার অন্তরে ঈমানের এক অপূর্ব স্বাদ ঢেলে দেন।\"",
            "\"ক্ষণিকের হারাম আনন্দ দীর্ঘস্থায়ী অনুশোচনার কারণ হয়, তাই নফস থেকে নিজেকে বাঁচিয়ে রাখো।\"",
            "\"সবচেয়ে বড় যুদ্ধ হলো নিজের খারাপ প্রবৃত্তির বিরুদ্ধে লড়াই করা।\"",
            "\"নির্জনে করা পাপও গোপন থাকে না, কারণ উপরওয়ালা সব দেখেন এবং সব জানেন।\"",
            "\"যে ব্যক্তি নির্জনে পাপ থেকে বিরত থাকে, তাকে প্রকাশ্যে সম্মানিত করা হয়।\"",
            "\"নিশ্চয়ই সৃষ্টিকর্তা তওবাকারীদের ভালোবাসেন এবং যারা পবিত্র থাকে তাদেরও ভালোবাসেন।\"",
            "\"অন্তর যখন কলুষিত হয়, তখন মানুষের দৃষ্টিও তার পবিত্রতা হারিয়ে ফেলে।\"",
            "\"খারাপ চিন্তা ও দৃষ্টি থেকে নিজেকে দূরে রাখো, কারণ এগুলোই অন্তরের প্রশান্তি নষ্ট করে দেয়।\""
        };

        private string[] timeQuotes = { 
            "\"যারা সময়কে মূল্যায়ন করে না, সময়ও তাদেরকে মূল্যায়ন করে না।\"",
            "\"অতীত চলে গেছে, ভবিষ্যৎ এখনও আসেনি, তোমার কাছে আছে শুধু বর্তমান। তাই এখনই কাজ শুরু করো।\"",
            "\"সময় বিনামূল্যে পাওয়া যায় ঠিকই, কিন্তু এর মূল্য আসলে অমূল্য।\"",
            "\"তুমি সময়কে ধরে রাখতে পারবে না, কিন্তু তুমি একে সঠিকভাবে ব্যবহার করতে পারো।\"",
            "\"যে আজ সময় নষ্ট করছে, কাল সময় তাকে নষ্ট করবে।\"",
            "\"সফলতা আর ব্যর্থতার মধ্যে সবচেয়ে বড় পার্থক্য হলো সময়ের সঠিক ব্যবহার।\"",
            "\"তুমি যদি সময়কে হত্যা করো, তবে সময় তোমার স্বপ্নগুলোকে হত্যা করবে।\"",
            "\"আজকের কাজ কালকের জন্য ফেলে রাখা মানে নিজের সাফল্যের পথকে পিছিয়ে দেওয়া।\"",
            "\"প্রতিটি সেকেন্ড একটি নতুন সুযোগ, একে কাজে লাগাও।\"",
            "\"যে ব্যক্তি জীবনের একটি ঘণ্টাও নষ্ট করতে দ্বিধা করে না, সে জীবনের আসল মূল্যই বোঝেনি।\"",
            "\"সফল মানুষেরা কখনো সময় কাটানোর বাহানা খোঁজে না।\"",
            "\"ঘড়ির কাঁটা কখনো থামে না, তাই তোমার লক্ষ্য পূরণের কাজও থামা উচিত নয়।\"",
            "\"অলসতা হলো সময়ের সবচেয়ে বড় শত্রু, আর ফোকাস হলো সবচেয়ে বড় হাতিয়ার।\"",
            "\"বড় কিছু অর্জন করতে হলে সবচেয়ে আগে নিজের সময়ের নিয়ন্ত্রণ নিতে হবে।\"",
            "\"জীবন একটাই এবং সময় সীমিত। তাই অহেতুক কাজে নিজের এই মূল্যবান সময় নষ্ট কোরো না।\""
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
        #endregion

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
            deviceIdCache = GetDeviceID();
            
            secretDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RasFocusPro");
            Directory.CreateDirectory(secretDir);

            // Setup AutoStart via Registry
            SetupAutoStart(true);

            LoadData();
            RefreshRunningApps();
            SetupTrayIcon();
            PopulateComboBoxes();

            _proc = HookCallback;
            _hookID = SetHook(_proc);

            // Check if started silently via PC Boot
            string[] args = Environment.GetCommandLineArgs();
            if (args.Contains("-autostart"))
            {
                this.WindowState = WindowState.Minimized;
                this.Hide();
            }

            // Init Timers
            fastTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            fastTimer.Tick += FastLoop_Tick;
            fastTimer.Start();

            slowTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            slowTimer.Tick += SlowLoop_Tick;
            slowTimer.Start();

            syncTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            syncTimer.Tick += SyncLoop_Tick;
            syncTimer.Start();

            // Firebase Registration
            _ = RegisterDeviceToFirebase();

            // UI Init
            RadioBlockList.IsChecked = !useAllowMode;
            RadioAllowList.IsChecked = useAllowMode;
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

        #region REGISTRY & SYSTEM MANIPULATION (AutoStart & Adblocker)
        private void SetupAutoStart(bool enable)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true))
                {
                    string appPath = "\"" + System.Reflection.Assembly.GetExecutingAssembly().Location + "\" -autostart";
                    if (enable)
                        key.SetValue("RasFocusPro", appPath);
                    else
                        key.DeleteValue("RasFocusPro", false);
                }
            }
            catch { }
        }

        private void ToggleAdBlockRegistry(bool enable)
        {
            try
            {
                // Silently Force Install uBlock Origin via Registry (Chrome)
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Policies\Google\Chrome\ExtensionInstallForcelist"))
                {
                    if (enable) key.SetValue("1", "cjpalhdlnbpafiamejdnhcphjbkeiagm;https://clients2.google.com/service/update2/crx");
                    else key.DeleteValue("1", false);
                }
                // (Edge)
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Policies\Microsoft\Edge\ExtensionInstallForcelist"))
                {
                    if (enable) key.SetValue("1", "odfafepnkmbhccpbejgmiehpchacaeak;https://edge.microsoft.com/extensionwebstorebase/v1/crx");
                    else key.DeleteValue("1", false);
                }
            }
            catch { }
        }
        #endregion

        #region FIREBASE HTTP CONTROLLER
        private string GetDeviceID()
        {
            string machine = Environment.MachineName;
            string user = Environment.UserName;
            return $"{machine}-{user}".Replace(" ", "_");
        }

        private async Task RegisterDeviceToFirebase()
        {
            try
            {
                string url = $"https://firestore.googleapis.com/v1/projects/mywebtools-f8d53/databases/(default)/documents/subscription_requests/{deviceIdCache}?key=AIzaSyDGd3KAo45UuqmeGFALziz_oKm3htEASHY";
                string jsonBody = $@"{{
                    ""fields"": {{
                        ""deviceID"": {{ ""stringValue"": ""{deviceIdCache}"" }},
                        ""status"": {{ ""stringValue"": ""ACTIVE"" }},
                        ""profileName"": {{ ""stringValue"": ""{userProfileName}"" }}
                    }}
                }}";
                var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                await httpClient.PatchAsync(url, content);
            }
            catch { }
        }

        private async void SyncProfileNameToFirebase(string name)
        {
            try
            {
                string url = $"https://firestore.googleapis.com/v1/projects/mywebtools-f8d53/databases/(default)/documents/subscription_requests/{deviceIdCache}?updateMask.fieldPaths=profileName&key=AIzaSyDGd3KAo45UuqmeGFALziz_oKm3htEASHY";
                string jsonBody = $@"{{ ""fields"": {{ ""profileName"": {{ ""stringValue"": ""{name}"" }} }} }}";
                await httpClient.PatchAsync(url, new StringContent(jsonBody, Encoding.UTF8, "application/json"));
            }
            catch { }
        }

        private async void SyncTogglesToFirebase()
        {
            try
            {
                string url = $"https://firestore.googleapis.com/v1/projects/mywebtools-f8d53/databases/(default)/documents/subscription_requests/{deviceIdCache}?updateMask.fieldPaths=fbReelsBlock&updateMask.fieldPaths=ytShortsBlock&updateMask.fieldPaths=adBlock&key=AIzaSyDGd3KAo45UuqmeGFALziz_oKm3htEASHY";
                string jsonBody = $@"{{
                    ""fields"": {{
                        ""fbReelsBlock"": {{ ""booleanValue"": {blockReels.ToString().ToLower()} }},
                        ""ytShortsBlock"": {{ ""booleanValue"": {blockShorts.ToString().ToLower()} }},
                        ""adBlock"": {{ ""booleanValue"": {isAdblockActive.ToString().ToLower()} }}
                    }}
                }}";
                await httpClient.PatchAsync(url, new StringContent(jsonBody, Encoding.UTF8, "application/json"));
            }
            catch { }
        }
        #endregion

        #region SYSTEM TRAY LOGIC
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
        #endregion

        #region UI EVENT HANDLERS & POPULATION
        private void PopulateComboBoxes()
        {
            string[] popSites = { "Select..", "facebook.com", "youtube.com", "instagram.com", "tiktok.com", "reddit.com" };
            string[] popApps = { "Select..", "chrome.exe", "msedge.exe", "vlc.exe", "telegram.exe", "code.exe" };

            foreach (var site in popSites) { ComboWebBlock.Items.Add(site); ComboWebAllow.Items.Add(site); }
            foreach (var app in popApps) { ComboAppBlock.Items.Add(app); ComboAppAllow.Items.Add(app); }

            ComboWebBlock.SelectedIndex = 0; ComboWebAllow.SelectedIndex = 0;
            ComboAppBlock.SelectedIndex = 0; ComboAppAllow.SelectedIndex = 0;
            
            ChkBlockReels.IsChecked = blockReels;
            ChkBlockShorts.IsChecked = blockShorts;
            ChkAdBlock.IsChecked = isAdblockActive;
        }

        private void RadioList_Checked(object sender, RoutedEventArgs e)
        {
            if (isSessionActive) return;
            useAllowMode = (RadioAllowList.IsChecked == true);
            SaveData();
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { DragMove(); }
        
        private void CloseButton_Click(object sender, RoutedEventArgs e) 
        { 
            this.Hide(); 
            trayIcon.ShowBalloonTip(2000, "RasFocus Pro", "Running securely in the background...", System.Windows.Forms.ToolTipIcon.Info);
        }

        // ============= NAVIGATION BUTTONS =============
        private void BtnNavLiveChat_Click(object sender, RoutedEventArgs e) { SidebarList.SelectedIndex = 4; }
        private void BtnNavUpgrade_Click(object sender, RoutedEventArgs e) { SidebarList.SelectedIndex = 5; }
        private void BtnNavPomodoro_Click(object sender, RoutedEventArgs e) { SidebarList.SelectedIndex = 2; }
        private void BtnNavEyeCure_Click(object sender, RoutedEventArgs e) { SidebarList.SelectedIndex = 1; }

        private void SidebarList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PageFocusMode == null) return;
            int index = SidebarList.SelectedIndex;
            PageFocusMode.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
            PageEyeCure.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
            PagePomodoro.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
            // PageSettings, PageLiveChat, PageActivatePro visibility logic will go here if added to XAML
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            userProfileName = TxtProfileName.Text;
            SyncProfileNameToFirebase(userProfileName);
            System.Windows.MessageBox.Show("Profile Name Saved Successfully to Cloud!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // Fast Action Checkboxes
        private void FastToggles_Click(object sender, RoutedEventArgs e)
        {
            blockReels = ChkBlockReels.IsChecked == true;
            blockShorts = ChkBlockShorts.IsChecked == true;
            if (isAdblockActive != (ChkAdBlock.IsChecked == true))
            {
                isAdblockActive = ChkAdBlock.IsChecked == true;
                ToggleAdBlockRegistry(isAdblockActive); // Apply to Registry silently
            }
            SyncTogglesToFirebase();
        }
        #endregion

        #region FOCUS & POMODORO START/STOP
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
                FastToggles_Click(null, null); // Sync toggles
                
                System.Windows.MessageBox.Show("Focus Mode Active. Unauthorized apps and websites are now blocked.", "Security", MessageBoxButton.OK, MessageBoxImage.Information);
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
                useAllowMode = true; // Pomodoro always runs on Allow List
                RadioAllowList.IsChecked = true;
                
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
            
            RadioBlockList.IsEnabled = !isSessionActive;
            RadioAllowList.IsEnabled = !isSessionActive;
            BtnStartFocus.IsEnabled = !isSessionActive;
            BtnStopFocus.IsEnabled = isSessionActive;
            
            TxtAppBlock.IsEnabled = !isSessionActive;
            TxtWebBlock.IsEnabled = !isSessionActive;
            TxtAppAllow.IsEnabled = !isSessionActive;
            TxtWebAllow.IsEnabled = !isSessionActive;
            BtnAddAppBlock.IsEnabled = !isSessionActive;
            BtnAddWebBlock.IsEnabled = !isSessionActive;
            BtnAddAppAllow.IsEnabled = !isSessionActive;
            BtnAddWebAllow.IsEnabled = !isSessionActive;
            BtnRemAppBlock.IsEnabled = !isSessionActive;
            BtnRemWebBlock.IsEnabled = !isSessionActive;
            BtnRemAppAllow.IsEnabled = !isSessionActive;
            BtnRemWebAllow.IsEnabled = !isSessionActive;
        }
        #endregion

        #region BACKGROUND LOGIC (URL READER & TIMERS)
        private string GetActiveTabUrl(IntPtr hwnd)
        {
            try
            {
                AutomationElement elm = AutomationElement.FromHandle(hwnd);
                AutomationElement elmUrlBar = elm.FindFirst(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));

                if (elmUrlBar != null)
                {
                    AutomationPattern[] patterns = elmUrlBar.GetSupportedPatterns();
                    if (patterns.Length > 0)
                    {
                        ValuePattern val = (ValuePattern)elmUrlBar.GetCurrentPattern(ValuePattern.Pattern);
                        return val.Current.Value.ToLower();
                    }
                }
            }
            catch { }
            return "";
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

                bool isBrowser = sTitle.Contains("chrome") || sTitle.Contains("edge") || 
                                 sTitle.Contains("firefox") || sTitle.Contains("brave") || 
                                 sTitle.Contains("opera");

                string activeUrl = "";
                if (isBrowser)
                {
                    activeUrl = GetActiveTabUrl(hWnd); 
                }

                // 1. Explicit Keywords Block & Adult Domain Block
                if (blockAdult)
                {
                    bool isAdultMatch = explicitKeywords.Any(k => sTitle.Contains(k));
                    if (!isAdultMatch && isBrowser && !string.IsNullOrEmpty(activeUrl))
                    {
                        isAdultMatch = adultDomains.Any(d => activeUrl.Contains(d));
                    }

                    if (isAdultMatch)
                    {
                        CloseWindowNatively(hWnd);
                        ShowOverlay(GetRandomQuote(1));
                        return;
                    }
                }

                // 2. Facebook Reels Block (URL check included)
                if (blockReels && isBrowser)
                {
                    if (activeUrl.Contains("facebook.com/reel") || (sTitle.Contains("facebook") && sTitle.Contains("reel")))
                    {
                        CloseWindowNatively(hWnd);
                        ShowOverlay(GetRandomQuote(2));
                        return;
                    }
                }

                // 3. YouTube Shorts Block (URL check included)
                if (blockShorts && isBrowser)
                {
                    if (activeUrl.Contains("youtube.com/shorts") || (sTitle.Contains("youtube") && sTitle.Contains("shorts")))
                    {
                        CloseWindowNatively(hWnd);
                        ShowOverlay(GetRandomQuote(2));
                        return;
                    }
                }

                // 4. Focus Mode Custom Website Logic
                if (isSessionActive && isBrowser)
                {
                    if (useAllowMode)
                    {
                        if (!allowedWebs.Any(w => sTitle.Contains(w.ToLower()) || activeUrl.Contains(w.ToLower())) && !sTitle.Contains("allowed websites"))
                        {
                            CloseWindowNatively(hWnd);
                            ShowOverlay("Website not in Allow List!");
                        }
                    }
                    else
                    {
                        if (blockedWebs.Any(w => sTitle.Contains(w.ToLower()) || activeUrl.Contains(w.ToLower())))
                        {
                            CloseWindowNatively(hWnd);
                            ShowOverlay("Website Blocked by Focus Mode!");
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
                // Ensure Label update works on UI Thread safely
                Dispatcher.Invoke(() => { 
                    LblStatus.Text = string.Format("Pomo: {0:D2}:{1:D2}", t.Minutes, t.Seconds); 
                });

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
                Dispatcher.Invoke(() => { 
                    LblStatus.Text = string.Format("Time: {0:D2}:{1:D2}:{2:D2}", t.Hours, t.Minutes, t.Seconds); 
                });
            }
        }
        #endregion

        #region LIST MANAGEMENT (ADD/REMOVE)
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
        #endregion

        #region UTILITIES & NATIVE HELPERS
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
        #endregion

        #region REAL-TIME KEYBOARD HOOK (TYPING BLOCKER)
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
        #endregion

        #region DATA SAVE/LOAD
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
        #endregion
    }
}

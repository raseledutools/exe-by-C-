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
        #region C++ CORE DLL IMPORTS (HYBRID BRIDGE)
        private const string CoreDLL = "RasFocusCore.dll";

        public delegate void ViolationCallback();

        [DllImport(CoreDLL, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        private static extern void StartKeyboardFilter(ViolationCallback callback, string badWordsDelimited);

        [DllImport(CoreDLL, CallingConvention = CallingConvention.StdCall)]
        private static extern void StopKeyboardFilter();

        [DllImport(CoreDLL, CallingConvention = CallingConvention.StdCall)]
        private static extern void ForceBlockTab(IntPtr hwnd);

        [DllImport(CoreDLL, CallingConvention = CallingConvention.StdCall)]
        private static extern IntPtr GetActiveWindowHandle();

        [DllImport(CoreDLL, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        private static extern void GetActiveTitle(IntPtr hwnd, StringBuilder buffer, int maxCount);

        [DllImport(CoreDLL, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
        private static extern void KillTargetProcesses(string targetsDelimited, bool isAllowMode, string systemAppsDelimited);
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

        // Eye Cure Windows
        private Window eyeFilterDim = null;
        private Window eyeFilterWarm = null;

        private string secretDir;
        private Window overlayWindow = null;
        private System.Windows.Forms.NotifyIcon trayIcon;

        private static MainWindow _instance; 
        private static Mutex _mutex = null;
        private ViolationCallback myViolationCallback;

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
        #endregion

        #region INITIALIZATION & STARTUP
        public MainWindow()
        {
            bool createdNew;
            _mutex = new Mutex(true, "RasFocusPro_Mutex_Final", out createdNew);
            if (!createdNew)
            {
                Application.Current.Shutdown();
                return;
            }

            InitializeComponent();
            _instance = this;
            deviceIdCache = GetDeviceID();
            
            secretDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RasFocusPro");
            Directory.CreateDirectory(secretDir);

            LoadData();
            RefreshRunningApps();
            SetupTrayIcon();
            PopulateComboBoxes();
            SetupEyeCureOverlays();
            AttachDynamicEvents();

            myViolationCallback = new ViolationCallback(OnAdultWordDetected);
            string badWordsStr = string.Join("|", explicitKeywords);
            StartKeyboardFilter(myViolationCallback, badWordsStr);

            fastTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            fastTimer.Tick += FastLoop_Tick;
            fastTimer.Start();

            slowTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            slowTimer.Tick += SlowLoop_Tick;
            slowTimer.Start();

            syncTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            syncTimer.Tick += SyncLoop_Tick;
            syncTimer.Start();

            _ = RegisterDeviceToFirebase();

            RadioBlockList.IsChecked = !useAllowMode;
            RadioAllowList.IsChecked = useAllowMode;
        }

        private void OnAdultWordDetected()
        {
            Dispatcher.Invoke(() => ShowOverlay(GetRandomQuote(1)));
        }

        protected override void OnClosed(EventArgs e)
        {
            StopKeyboardFilter();
            if (trayIcon != null)
            {
                trayIcon.Visible = false;
                trayIcon.Dispose();
            }
            base.OnClosed(e);
        }
        #endregion

        #region EVENT ATTACHMENTS (DYNAMIC)
        private void AttachDynamicEvents()
        {
            if (ChkAdBlock != null) { ChkAdBlock.Checked += Toggles_StateChanged; ChkAdBlock.Unchecked += Toggles_StateChanged; }
            if (ChkBlockReels != null) { ChkBlockReels.Checked += Toggles_StateChanged; ChkBlockReels.Unchecked += Toggles_StateChanged; }
            if (ChkBlockShorts != null) { ChkBlockShorts.Checked += Toggles_StateChanged; ChkBlockShorts.Unchecked += Toggles_StateChanged; }

            if (RadioBlockList != null) RadioBlockList.Checked += RadioList_Checked;
            if (RadioAllowList != null) RadioAllowList.Checked += RadioList_Checked;

            var sliderBright = this.FindName("SliderBrightness") as Slider;
            if (sliderBright != null) sliderBright.ValueChanged += SliderBrightness_ValueChanged;

            var sliderWarm = this.FindName("SliderWarmth") as Slider;
            if (sliderWarm != null) sliderWarm.ValueChanged += SliderWarmth_ValueChanged;
        }

        private void Toggles_StateChanged(object sender, RoutedEventArgs e)
        {
            blockReels = ChkBlockReels?.IsChecked == true;
            blockShorts = ChkBlockShorts?.IsChecked == true;
            isAdblockActive = ChkAdBlock?.IsChecked == true;
            SyncTogglesToFirebase();
        }
        #endregion

        #region FIREBASE SYNC (HTTP API)
        private string GetDeviceID()
        {
            return $"{Environment.MachineName}-{Environment.UserName}".Replace(" ", "_");
        }

        private async Task RegisterDeviceToFirebase()
        {
            try
            {
                string url = $"https://firestore.googleapis.com/v1/projects/mywebtools-f8d53/databases/(default)/documents/subscription_requests/{deviceIdCache}?key=AIzaSyDGd3KAo45UuqmeGFALziz_oKm3htEASHY";
                string jsonBody = $@"{{ ""fields"": {{ ""deviceID"": {{ ""stringValue"": ""{deviceIdCache}"" }}, ""status"": {{ ""stringValue"": ""ACTIVE"" }}, ""profileName"": {{ ""stringValue"": ""{userProfileName}"" }} }} }}";
                await httpClient.PatchAsync(url, new StringContent(jsonBody, Encoding.UTF8, "application/json"));
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
                string jsonBody = $@"{{ ""fields"": {{ ""fbReelsBlock"": {{ ""booleanValue"": {blockReels.ToString().ToLower()} }}, ""ytShortsBlock"": {{ ""booleanValue"": {blockShorts.ToString().ToLower()} }}, ""adBlock"": {{ ""booleanValue"": {isAdblockActive.ToString().ToLower()} }} }} }}";
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
            if (File.Exists(iconPath)) trayIcon.Icon = new System.Drawing.Icon(iconPath);
            else trayIcon.Icon = System.Drawing.SystemIcons.Shield;

            trayIcon.Text = "RasFocus Pro - Engine Running";
            trayIcon.Visible = true;
            trayIcon.DoubleClick += (s, e) => ShowAppFromTray();

            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add("Open Control Panel", null, (s, e) => ShowAppFromTray());
            menu.Items.Add("Exit Focus Manager", null, (s, e) => 
            {
                if (isSessionActive)
                {
                    System.Windows.MessageBox.Show("Cannot exit while Focus Mode is active! Please stop it first.", "Security Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else
                {
                    StopKeyboardFilter();
                    trayIcon.Visible = false;
                    trayIcon.Dispose();
                    Application.Current.Shutdown();
                }
            });
            trayIcon.ContextMenuStrip = menu;
        }

        private void ShowAppFromTray()
        {
            this.Show();
            this.WindowState = WindowState.Normal;
            this.ShowInTaskbar = true;
            this.Activate(); 
            this.Topmost = true; 
            this.Topmost = false; 
            this.Focus();
        }
        #endregion

        #region UI POPULATION & BUTTON EVENTS
        private void PopulateComboBoxes()
        {
            string[] popSites = { "Select..", "facebook.com", "youtube.com", "instagram.com", "tiktok.com", "reddit.com", "netflix.com" };
            string[] popApps = { "Select..", "chrome.exe", "msedge.exe", "vlc.exe", "telegram.exe", "code.exe", "discord.exe" };

            var comboWBlock = this.FindName("ComboWebBlock") as ComboBox;
            var comboWAllow = this.FindName("ComboWebAllow") as ComboBox;
            var comboABlock = this.FindName("ComboAppBlock") as ComboBox;
            var comboAAllow = this.FindName("ComboAppAllow") as ComboBox;

            if (comboWBlock != null) foreach (var s in popSites) { comboWBlock.Items.Add(s); comboWAllow.Items.Add(s); }
            if (comboABlock != null) foreach (var a in popApps) { comboABlock.Items.Add(a); comboAAllow.Items.Add(a); }

            if(comboWBlock != null) comboWBlock.SelectedIndex = 0;
            if(comboWAllow != null) comboWAllow.SelectedIndex = 0;
            if(comboABlock != null) comboABlock.SelectedIndex = 0;
            if(comboAAllow != null) comboAAllow.SelectedIndex = 0;
        }

        private void RadioList_Checked(object sender, RoutedEventArgs e)
        {
            if (isSessionActive) return;
            useAllowMode = (RadioAllowList?.IsChecked == true);
            SaveData();
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { DragMove(); }
        
        private void CloseButton_Click(object sender, RoutedEventArgs e) 
        { 
            this.Hide(); 
            this.ShowInTaskbar = false;
            if (trayIcon != null)
            {
                trayIcon.Visible = true;
                trayIcon.ShowBalloonTip(2000, "RasFocus Pro", "Running securely in the background...", System.Windows.Forms.ToolTipIcon.Info);
            }
        }

        // === Navigation Shortcut Clicks ===
        private void BtnNavLiveChat_Click(object sender, RoutedEventArgs e) { if (SidebarList != null) SidebarList.SelectedIndex = 4; }
        private void BtnNavUpgrade_Click(object sender, RoutedEventArgs e) { if (SidebarList != null) SidebarList.SelectedIndex = 5; }
        private void BtnNavPomodoro_Click(object sender, RoutedEventArgs e) { if (SidebarList != null) SidebarList.SelectedIndex = 2; }
        private void BtnNavEyeCure_Click(object sender, RoutedEventArgs e) { if (SidebarList != null) SidebarList.SelectedIndex = 1; }

        private void SidebarList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var pageFocus = this.FindName("PageFocusMode") as UIElement;
            var pageEyeCure = this.FindName("PageEyeCure") as UIElement;
            var pagePomodoro = this.FindName("PagePomodoro") as UIElement;
            var pageSettings = this.FindName("PageSettings") as UIElement;
            var pageLiveChat = this.FindName("PageLiveChat") as UIElement;
            var pageActivatePro = this.FindName("PageActivatePro") as UIElement;

            if (pageFocus == null || SidebarList == null) return;

            int i = SidebarList.SelectedIndex;
            if (pageFocus != null) pageFocus.Visibility = i == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (pageEyeCure != null) pageEyeCure.Visibility = i == 1 ? Visibility.Visible : Visibility.Collapsed;
            if (pagePomodoro != null) pagePomodoro.Visibility = i == 2 ? Visibility.Visible : Visibility.Collapsed;
            if (pageSettings != null) pageSettings.Visibility = i == 3 ? Visibility.Visible : Visibility.Collapsed;
            if (pageLiveChat != null) pageLiveChat.Visibility = i == 4 ? Visibility.Visible : Visibility.Collapsed;
            if (pageActivatePro != null) pageActivatePro.Visibility = i == 5 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            var txtProf = this.FindName("TxtProfileName") as TextBox;
            if (txtProf != null)
            {
                userProfileName = txtProf.Text;
                SyncProfileNameToFirebase(userProfileName);
                System.Windows.MessageBox.Show("Profile Name Saved & Synced Successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        #endregion

        #region FOCUS MODE & POMODORO LOGIC
        private void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            if (isSessionActive) return;
            
            var txtHr = this.FindName("TxtTimeHr") as TextBox;
            var txtMin = this.FindName("TxtTimeMin") as TextBox;
            var txtPass = this.FindName("TxtPass") as TextBox;

            int hr = 0, min = 0;
            if (txtHr != null) int.TryParse(txtHr.Text, out hr);
            if (txtMin != null) int.TryParse(txtMin.Text, out min);
            
            focusTimeTotalSeconds = (hr * 3600) + (min * 60);
            currentSessionPass = txtPass != null ? txtPass.Text : "";

            if (focusTimeTotalSeconds > 0 || !string.IsNullOrEmpty(currentSessionPass))
            {
                isPassMode = !string.IsNullOrEmpty(currentSessionPass);
                isTimeMode = focusTimeTotalSeconds > 0;
                useAllowMode = (RadioAllowList?.IsChecked == true);
                
                isSessionActive = true;
                timerTicks = 0;
                
                SaveData();
                UpdateUIStates();
                
                System.Windows.MessageBox.Show("Focus Mode Active. Distractions are now blocked.", "Security", MessageBoxButton.OK, MessageBoxImage.Information);
                this.Hide(); 
                this.ShowInTaskbar = false;
            }
            else
            {
                System.Windows.MessageBox.Show("Please set a password or a timer to start focus mode.", "Information Needed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            if (!isSessionActive) return;

            var txtPass = this.FindName("TxtPass") as TextBox;

            if (isPassMode)
            {
                if (txtPass != null && txtPass.Text != currentSessionPass)
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
                var txtPomoMin = this.FindName("TxtPomoMin") as TextBox;
                var txtPomoSessions = this.FindName("TxtPomoSessions") as TextBox;

                if (txtPomoMin != null) int.TryParse(txtPomoMin.Text, out pomoLengthMin);
                if (txtPomoSessions != null) int.TryParse(txtPomoSessions.Text, out pomoTotalSessions);

                if(pomoLengthMin <= 0) pomoLengthMin = 25;
                if(pomoTotalSessions <= 0) pomoTotalSessions = 4;

                isPomodoroMode = true;
                isSessionActive = true;
                pomoTicks = 0;
                pomoCurrentSession = 1;
                useAllowMode = true; 
                if (RadioAllowList != null) RadioAllowList.IsChecked = true;
                
                SaveData();
                UpdateUIStates();
                System.Windows.MessageBox.Show("Pomodoro Started! Only Allow List apps & websites can be accessed now.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnPomoStop_Click(object sender, RoutedEventArgs e)
        {
            if (isPomodoroMode)
            {
                ClearSessionData();
                UpdateUIStates();
                System.Windows.MessageBox.Show("Pomodoro Stopped manually.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
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
            
            var txtPass = this.FindName("TxtPass") as TextBox;
            if (txtPass != null) txtPass.Clear();
            
            UpdateUIStates();
        }

        private void UpdateUIStates()
        {
            trayIcon.Text = isSessionActive ? "RasFocus Pro (ACTIVE 🔒)" : "RasFocus Pro (Ready)";
            
            var lblStatus = this.FindName("LblStatus") as TextBlock;
            if(lblStatus != null)
            {
                lblStatus.Text = isSessionActive ? "Focus Active!" : "Ready";
                lblStatus.Foreground = isSessionActive ? new SolidColorBrush(Colors.Green) : new SolidColorBrush(Colors.Red);
            }
            
            if (RadioBlockList != null) RadioBlockList.IsEnabled = !isSessionActive;
            if (RadioAllowList != null) RadioAllowList.IsEnabled = !isSessionActive;
            
            var btnStart = this.FindName("BtnStartFocus") as Button;
            var btnStop = this.FindName("BtnStopFocus") as Button;
            if (btnStart != null) btnStart.IsEnabled = !isSessionActive;
            if (btnStop != null) btnStop.IsEnabled = isSessionActive;
            
            var inputs = new[] { "TxtAppBlock", "TxtWebBlock", "TxtAppAllow", "TxtWebAllow", "BtnAddAppBlock", "BtnAddWebBlock", "BtnAddAppAllow", "BtnAddWebAllow", "BtnRemAppBlock", "BtnRemWebBlock", "BtnRemAppAllow", "BtnRemWebAllow" };
            foreach (var name in inputs)
            {
                var el = this.FindName(name) as UIElement;
                if (el != null) el.IsEnabled = !isSessionActive;
            }
        }
        #endregion

        #region BACKGROUND TIMERS (FAST, SLOW, SYNC) -> HYBRID DELEGATION
        private string SafeGetUrl(IntPtr hwnd)
        {
            try
            {
                AutomationElement elm = AutomationElement.FromHandle(hwnd);
                var elmUrlBar = elm.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
                if (elmUrlBar != null)
                {
                    var pattern = elmUrlBar.GetCurrentPattern(ValuePattern.Pattern) as ValuePattern;
                    return pattern?.Current.Value.ToLower() ?? "";
                }
            }
            catch { }
            return "";
        }

        private void FastLoop_Tick(object sender, EventArgs e)
        {
            try
            {
                if (!blockAdult && !blockReels && !blockShorts && !isSessionActive) return;
                if (overlayWindow != null && overlayWindow.IsVisible) return;

                IntPtr hWnd = GetActiveWindowHandle(); // HYBRID DLL CALL
                if (hWnd == IntPtr.Zero) return;

                StringBuilder titleBuilder = new StringBuilder(512);
                GetActiveTitle(hWnd, titleBuilder, 512); // HYBRID DLL CALL
                string title = titleBuilder.ToString().ToLower();

                bool isBrowser = title.Contains("chrome") || title.Contains("edge") || title.Contains("firefox") || title.Contains("brave") || title.Contains("opera");
                string activeUrl = isBrowser ? SafeGetUrl(hWnd) : "";

                // 1. Adult Filter
                if (blockAdult)
                {
                    bool isAdult = explicitKeywords.Any(k => title.Contains(k)) || (isBrowser && adultDomains.Any(d => activeUrl.Contains(d)));
                    if (isAdult) { ForceBlockTab(hWnd); ShowOverlay(GetRandomQuote(1)); return; }
                }

                // 2. Social Media Blockers
                if (blockReels && isBrowser && (activeUrl.Contains("facebook.com/reel") || title.Contains("reels")))
                { ForceBlockTab(hWnd); ShowOverlay(GetRandomQuote(2)); return; }
                
                if (blockShorts && isBrowser && (activeUrl.Contains("youtube.com/shorts") || title.Contains("shorts")))
                { ForceBlockTab(hWnd); ShowOverlay(GetRandomQuote(2)); return; }

                // 3. Focus Mode Custom Lists
                if (isSessionActive && isBrowser)
                {
                    if (useAllowMode)
                    {
                        if (!allowedWebs.Any(w => title.Contains(w.ToLower()) || activeUrl.Contains(w.ToLower())) && !title.Contains("new tab") && !title.Contains("start"))
                        { ForceBlockTab(hWnd); ShowOverlay("Website is not in your Allow List!"); }
                    }
                    else
                    {
                        if (blockedWebs.Any(w => title.Contains(w.ToLower()) || activeUrl.Contains(w.ToLower())))
                        { ForceBlockTab(hWnd); ShowOverlay("Website Blocked by Focus Mode!"); }
                    }
                }
            }
            catch { }
        }

        private void SlowLoop_Tick(object sender, EventArgs e)
        {
            if (!isSessionActive) return;
            string targets = useAllowMode ? string.Join("|", allowedApps) : string.Join("|", blockedApps);
            string sysAppsStr = string.Join("|", systemApps);
            KillTargetProcesses(targets, useAllowMode, sysAppsStr); // HYBRID DLL CALL
        }

        private void SyncLoop_Tick(object sender, EventArgs e)
        {
            if (!isSessionActive) return;
            var lblStatus = this.FindName("LblStatus") as TextBlock;
            var lblBigTime = this.FindName("LblPomoBigTime") as TextBlock;
            var lblBigStatus = this.FindName("LblPomoBigStatus") as TextBlock;

            if (isTimeMode && focusTimeTotalSeconds > 0)
            {
                timerTicks++;
                if (timerTicks >= focusTimeTotalSeconds)
                {
                    ClearSessionData();
                    System.Windows.MessageBox.Show("Focus time is over!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    int left = focusTimeTotalSeconds - timerTicks;
                    TimeSpan t = TimeSpan.FromSeconds(left);
                    Dispatcher.Invoke(() => { if(lblStatus != null) lblStatus.Text = string.Format("Time: {0:D2}:{1:D2}:{2:D2}", t.Hours, t.Minutes, t.Seconds); });
                }
            }
            
            if (isPomodoroMode)
            {
                pomoTicks++;
                int totalMins = isPomodoroBreak ? 5 : pomoLengthMin;
                int left = (totalMins * 60) - pomoTicks;
                if (left < 0) left = 0;

                TimeSpan t = TimeSpan.FromSeconds(left);
                string timeStr = string.Format("{0:D2}:{1:D2}", t.Minutes, t.Seconds);
                
                Dispatcher.Invoke(() => { 
                    if(lblStatus != null) lblStatus.Text = "Pomo: " + timeStr; 
                    if(lblBigTime != null) lblBigTime.Text = timeStr;
                    if(lblBigStatus != null) lblBigStatus.Text = isPomodoroBreak ? $"Break Time | Session {pomoCurrentSession} of {pomoTotalSessions}" : $"Focus Time | Session {pomoCurrentSession} of {pomoTotalSessions}";
                });

                if (!isPomodoroBreak && pomoTicks >= pomoLengthMin * 60)
                {
                    isPomodoroBreak = true;
                    pomoTicks = 0;
                    ShowOverlay("☕ Time to Rest & Drink Water! 5 Minute Break Started.");
                }
                else if (isPomodoroBreak && pomoTicks >= 5 * 60)
                {
                    isPomodoroBreak = false;
                    pomoTicks = 0;
                    pomoCurrentSession++;
                    if (pomoCurrentSession > pomoTotalSessions)
                    {
                        ClearSessionData();
                        System.Windows.MessageBox.Show("All Pomodoro Sessions Completed! Great work.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        ShowOverlay($"🚀 Break Over! Starting Session {pomoCurrentSession}. Get back to work!");
                    }
                }
            }
        }
        #endregion

        #region LIST MANAGEMENT (ADD/REMOVE)
        private void AddToList(TextBox input, ListBox list, List<string> dataList, string fileName)
        {
            if (input == null || list == null) return;
            string val = input.Text.Trim().ToLower();
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
            if (list == null || list.SelectedIndex == -1) return;
            string val = list.SelectedItem.ToString();
            dataList.Remove(val);
            list.Items.RemoveAt(list.SelectedIndex);
            SaveListToFile(dataList, fileName);
        }

        private void BtnAddAppBlock_Click(object sender, RoutedEventArgs e) => AddToList(this.FindName("TxtAppBlock") as TextBox, this.FindName("ListAppBlock") as ListBox, blockedApps, "bl_app.txt");
        private void BtnAddWebBlock_Click(object sender, RoutedEventArgs e) => AddToList(this.FindName("TxtWebBlock") as TextBox, this.FindName("ListWebBlock") as ListBox, blockedWebs, "bl_web.txt");
        private void BtnAddAppAllow_Click(object sender, RoutedEventArgs e) => AddToList(this.FindName("TxtAppAllow") as TextBox, this.FindName("ListAppAllow") as ListBox, allowedApps, "al_app.txt");
        private void BtnAddWebAllow_Click(object sender, RoutedEventArgs e) => AddToList(this.FindName("TxtWebAllow") as TextBox, this.FindName("ListWebAllow") as ListBox, allowedWebs, "al_web.txt");

        private void BtnRemAppBlock_Click(object sender, RoutedEventArgs e) => RemoveFromList(this.FindName("ListAppBlock") as ListBox, blockedApps, "bl_app.txt");
        private void BtnRemWebBlock_Click(object sender, RoutedEventArgs e) => RemoveFromList(this.FindName("ListWebBlock") as ListBox, blockedWebs, "bl_web.txt");
        private void BtnRemAppAllow_Click(object sender, RoutedEventArgs e) => RemoveFromList(this.FindName("ListAppAllow") as ListBox, allowedApps, "al_app.txt");
        private void BtnRemWebAllow_Click(object sender, RoutedEventArgs e) => RemoveFromList(this.FindName("ListWebAllow") as ListBox, allowedWebs, "al_web.txt");

        private void RefreshRunningApps()
        {
            var list = this.FindName("ListRunningApps") as ListBox;
            if (list == null) return;

            list.Items.Clear();
            var processes = Process.GetProcesses().Select(p => p.ProcessName.ToLower() + ".exe").Distinct().OrderBy(p => p);
            foreach (var p in processes)
            {
                if (p != "svchost.exe" && p != "explorer.exe") 
                    list.Items.Add(p);
            }
        }

        private void BtnAddFromRunning_Click(object sender, RoutedEventArgs e)
        {
            var listRun = this.FindName("ListRunningApps") as ListBox;
            var txtB = this.FindName("TxtAppBlock") as TextBox;
            var txtA = this.FindName("TxtAppAllow") as TextBox;

            if (listRun != null && listRun.SelectedIndex != -1)
            {
                string selectedApp = listRun.SelectedItem.ToString();
                if (RadioBlockList != null && RadioBlockList.IsChecked == true)
                {
                    if (txtB != null) txtB.Text = selectedApp;
                    BtnAddAppBlock_Click(null, null);
                }
                else
                {
                    if (txtA != null) txtA.Text = selectedApp;
                    BtnAddAppAllow_Click(null, null);
                }
            }
        }
        #endregion

        #region EYE CURE & OVERLAYS
        private void SetupEyeCureOverlays()
        {
            eyeFilterDim = new Window { WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Black, Topmost = true, ShowInTaskbar = false, IsHitTestVisible = false, WindowState = WindowState.Maximized, Opacity = 0 };
            eyeFilterWarm = new Window { WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.DarkOrange, Topmost = true, ShowInTaskbar = false, IsHitTestVisible = false, WindowState = WindowState.Maximized, Opacity = 0 };
            eyeFilterDim.Show(); 
            eyeFilterWarm.Show();
        }

        private void SliderBrightness_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (eyeFilterDim != null)
            {
                double val = e.NewValue;
                double opacity = (100 - val) / 100.0;
                eyeFilterDim.Opacity = opacity * 0.85;
            }
        }

        private void SliderWarmth_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (eyeFilterWarm != null)
            {
                double val = e.NewValue;
                eyeFilterWarm.Opacity = (val / 100.0) * 0.4;
            }
        }
        #endregion

        #region HELPER UTILITIES
        private string GetRandomQuote(int type)
        {
            Random r = new Random();
            return type == 1 ? islamicQuotes[r.Next(islamicQuotes.Length)] : timeQuotes[r.Next(timeQuotes.Length)];
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
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    ShowInTaskbar = false
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
            LoadListFromFile(ref blockedApps, "ListAppBlock", "bl_app.txt");
            LoadListFromFile(ref blockedWebs, "ListWebBlock", "bl_web.txt");
            LoadListFromFile(ref allowedApps, "ListAppAllow", "al_app.txt");
            LoadListFromFile(ref allowedWebs, "ListWebAllow", "al_web.txt");
        }

        private void LoadListFromFile(ref List<string> dataList, string uiListName, string filename)
        {
            string path = Path.Combine(secretDir, filename);
            var uiList = this.FindName(uiListName) as ListBox;
            if (File.Exists(path))
            {
                dataList = File.ReadAllLines(path).ToList();
                if (uiList != null)
                {
                    uiList.Items.Clear();
                    foreach (var item in dataList) uiList.Items.Add(item);
                }
            }
        }
        #endregion
    }
}

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

        private int focusTimeTotalSeconds = 0, timerTicks = 0;
        private int pomoLengthMin = 25, pomoTotalSessions = 4, pomoCurrentSession = 1, pomoTicks = 0;
        private string currentSessionPass = "", userProfileName = "Rasel Mia", deviceIdCache = "";

        private List<string> blockedApps = new List<string>();
        private List<string> blockedWebs = new List<string>();
        private List<string> allowedApps = new List<string>();
        private List<string> allowedWebs = new List<string>();

        private Window eyeFilterDim = null, eyeFilterWarm = null, overlayWindow = null;
        private System.Windows.Forms.NotifyIcon trayIcon;
        private string secretDir;
        private static Mutex _mutex = null;
        private ViolationCallback myViolationCallback; // Keep reference to prevent Garbage Collection

        private string[] systemApps = { "explorer.exe", "svchost.exe", "taskmgr.exe", "cmd.exe", "conhost.exe", "csrss.exe", "dwm.exe", "lsass.exe", "services.exe", "smss.exe", "wininit.exe", "winlogon.exe", "spoolsv.exe", "fontdrvhost.exe" };
        
        private List<string> explicitKeywords = new List<string> {
            "porn", "xxx", "sex", "nude", "nsfw", "xvideos", "pornhub", "xnxx", "xhamster", "brazzers", "onlyfans", "playboy", "mia khalifa", "bhabi", "chudai", "bangla choti", "magi", "sexy", "ullu"
        };
        private List<string> adultDomains = new List<string> { "pornhub.com", "xvideos.com", "xnxx.com", "xhamster.com", "chaturbate.com", "brazzers.com", "onlyfans.com" };
        #endregion

        #region INITIALIZATION
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
            deviceIdCache = GetDeviceID();
            secretDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RasFocusPro");
            Directory.CreateDirectory(secretDir);

            LoadData();
            RefreshRunningApps();
            SetupTrayIcon();
            PopulateComboBoxes();
            SetupEyeCureOverlays();
            AttachDynamicEvents();

            // Initialize C++ Core Keyboard Hook
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
            // Called by C++ DLL when a bad word is typed!
            Dispatcher.Invoke(() => ShowOverlay("No explicit content allowed! Focused tracking active."));
        }

        protected override void OnClosed(EventArgs e)
        {
            StopKeyboardFilter(); // Stop C++ Core Hook
            if (trayIcon != null) { trayIcon.Visible = false; trayIcon.Dispose(); }
            base.OnClosed(e);
        }
        #endregion

        #region BACKGROUND TIMERS (FAST & SLOW)
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

                IntPtr hWnd = GetActiveWindowHandle(); // From C++ DLL
                if (hWnd == IntPtr.Zero) return;

                StringBuilder titleBuilder = new StringBuilder(512);
                GetActiveTitle(hWnd, titleBuilder, 512); // From C++ DLL
                string title = titleBuilder.ToString().ToLower();

                bool isBrowser = title.Contains("chrome") || title.Contains("edge") || title.Contains("firefox") || title.Contains("brave");
                string activeUrl = isBrowser ? SafeGetUrl(hWnd) : "";

                // 1. Adult Filter (Checking Titles & URLs)
                if (blockAdult && isBrowser && adultDomains.Any(d => activeUrl.Contains(d)))
                {
                    ForceBlockTab(hWnd); // From C++ DLL
                    ShowOverlay("Adult content is blocked!"); 
                    return; 
                }

                // 2. Focus Mode Customs
                if (isSessionActive && isBrowser)
                {
                    if (useAllowMode)
                    {
                        if (!allowedWebs.Any(w => title.Contains(w.ToLower()) || activeUrl.Contains(w.ToLower())) && !title.Contains("new tab"))
                        { ForceBlockTab(hWnd); ShowOverlay("Not in Allow List!"); }
                    }
                    else
                    {
                        if (blockedWebs.Any(w => title.Contains(w.ToLower()) || activeUrl.Contains(w.ToLower())))
                        { ForceBlockTab(hWnd); ShowOverlay("Website Blocked!"); }
                    }
                }
            }
            catch { }
        }

        private void SlowLoop_Tick(object sender, EventArgs e)
        {
            if (!isSessionActive) return;

            // Delegating Process Killing to Native C++ Engine! Much faster & hides from simple task managers.
            string targets = useAllowMode ? string.Join("|", allowedApps) : string.Join("|", blockedApps);
            string sysAppsStr = string.Join("|", systemApps);
            
            KillTargetProcesses(targets, useAllowMode, sysAppsStr); // From C++ DLL
        }
        #endregion

        #region EVENT ATTACHMENTS & TRAY (Dynamic UI)
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

        private void SetupTrayIcon()
        {
            trayIcon = new System.Windows.Forms.NotifyIcon();
            string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icon.ico");
            trayIcon.Icon = File.Exists(iconPath) ? new System.Drawing.Icon(iconPath) : System.Drawing.SystemIcons.Shield;
            trayIcon.Text = "RasFocus Pro Engine";
            trayIcon.Visible = true;
            trayIcon.DoubleClick += (s, e) => { this.Show(); this.WindowState = WindowState.Normal; this.ShowInTaskbar = true; this.Activate(); };

            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add("Exit Focus Engine", null, (s, e) => 
            {
                if (isSessionActive) { System.Windows.MessageBox.Show("Cannot exit while Focus is active!", "Warning"); }
                else { StopKeyboardFilter(); trayIcon.Dispose(); Application.Current.Shutdown(); }
            });
            trayIcon.ContextMenuStrip = menu;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) 
        { 
            this.Hide(); this.ShowInTaskbar = false;
            trayIcon?.ShowBalloonTip(2000, "RasFocus Pro", "Engine running in background...", System.Windows.Forms.ToolTipIcon.Info);
        }
        #endregion

        // ----------------------------------------------------------------------------------
        // Note: The rest of your C# UI functions (PopulateComboBoxes, Window_MouseLeftButtonDown, 
        // SidebarList_SelectionChanged, BtnSave_Click, BtnStart_Click, BtnStop_Click, 
        // SyncLoop_Tick, AddToList, LoadData, SetupEyeCureOverlays, etc.) 
        // remain EXACTLY identical as your previous code. I omitted them here to save space,
        // just copy-paste your existing UI button logic below this line!
        // ----------------------------------------------------------------------------------
        
        #region FIREBASE SYNC (HTTP API)
        private string GetDeviceID() { return $"{Environment.MachineName}-{Environment.UserName}".Replace(" ", "_"); }
        private async Task RegisterDeviceToFirebase() { /* Your Firebase logic here */ }
        private async void SyncProfileNameToFirebase(string name) { /* Your Firebase logic here */ }
        private async void SyncTogglesToFirebase() { /* Your Firebase logic here */ }
        #endregion

        private void Toggles_StateChanged(object sender, RoutedEventArgs e)
        {
            blockReels = ChkBlockReels?.IsChecked == true;
            blockShorts = ChkBlockShorts?.IsChecked == true;
            isAdblockActive = ChkAdBlock?.IsChecked == true;
            SyncTogglesToFirebase();
        }

        private void RadioList_Checked(object sender, RoutedEventArgs e) { if (isSessionActive) return; useAllowMode = (RadioAllowList?.IsChecked == true); SaveData(); }
        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { DragMove(); }

        private void ShowOverlay(string message)
        {
            if (overlayWindow == null)
            {
                overlayWindow = new Window { WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = new SolidColorBrush(Color.FromArgb(240, 9, 61, 31)), Topmost = true, Width = 800, Height = 300, WindowStartupLocation = WindowStartupLocation.CenterScreen, ShowInTaskbar = false };
                overlayWindow.Content = new TextBlock { Text = message, Foreground = Brushes.White, FontSize = 24, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(20) };
            }
            else { ((TextBlock)overlayWindow.Content).Text = message; }
            overlayWindow.Show(); overlayWindow.Topmost = true;
            DispatcherTimer t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
            t.Tick += (s, ev) => { overlayWindow.Hide(); t.Stop(); };
            t.Start();
        }

        // Add remaining Helper/UI functions here (BtnStart_Click, SyncLoop_Tick, LoadData, etc.)
    }
}

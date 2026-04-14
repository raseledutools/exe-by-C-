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

namespace RasFocusPro
{
    public partial class MainWindow : Window
    {
        // ==========================================
        // WINDOWS NATIVE API (P/Invoke)
        // ==========================================
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("kernel32.dll")]
        public static extern IntPtr OpenProcess(int dwDesiredAccess, bool bInheritHandle, int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool QueryFullProcessImageName(IntPtr hProcess, int dwFlags, StringBuilder lpExeName, ref int lpdwSize);

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
        private int trialDaysLeft = 7;
        private bool isLicenseValid = false, isTrialExpired = false;

        private List<string> blockedApps = new List<string>();
        private List<string> blockedWebs = new List<string>();
        private List<string> allowedApps = new List<string>();
        private List<string> allowedWebs = new List<string>();

        private string[] systemApps = { "explorer.exe", "svchost.exe", "taskmgr.exe", "cmd.exe", "conhost.exe", "csrss.exe", "dwm.exe", "lsass.exe", "services.exe", "smss.exe", "wininit.exe", "winlogon.exe", "spoolsv.exe", "fontdrvhost.exe", "searchui.exe", "searchindexer.exe", "sihost.exe", "taskhostw.exe", "ctfmon.exe", "applicationframehost.exe", "system", "registry", "audiodg.exe", "searchapp.exe", "startmenuexperiencehost.exe", "shellexperiencehost.exe", "textinputhost.exe" };
        private string[] explicitKeywords = { "porn", "xxx", "sex", "nude", "nsfw", "xvideos", "pornhub", "xnxx", "xhamster", "brazzers", "onlyfans", "playboy", "mia khalifa", "bhabi", "chudai", "bangla choti", "magi", "sexy" };
        private string[] safeBrowserTitles = { "new tab", "start", "blank page", "allowed websites", "loading", "untitled", "connecting", "pomodoro break" };

        private string secretDir;
        private Window overlayWindow = null;
        private Window eyeFilterDim = null;
        private Window eyeFilterWarm = null;

        // Keyboard Hook
        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private static LowLevelKeyboardProc _proc;
        private static IntPtr _hookID = IntPtr.Zero;
        private static string globalKeyBuffer = "";
        private static MainWindow _instance; // For hook to access non-static members

        public MainWindow()
        {
            InitializeComponent();
            _instance = this;
            
            // Setup Secret Directory
            secretDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RasFocusPro");
            Directory.CreateDirectory(secretDir);
            File.SetAttributes(secretDir, FileAttributes.Hidden | FileAttributes.System);

            // Initialize Lists
            LoadData();
            RefreshRunningApps();

            // Setup Eye Filters
            SetupEyeFilters();

            // Setup Global Keyboard Hook
            _proc = HookCallback;
            _hookID = SetHook(_proc);

            // Setup Fast Timer (200ms) - For Browser Tab/Title & Keyboard monitoring
            fastTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            fastTimer.Tick += FastLoop_Tick;
            fastTimer.Start();

            // Setup Slow Timer (1000ms) - For Process killing & Timer Updates
            slowTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            slowTimer.Tick += SlowLoop_Tick;
            slowTimer.Start();

            // Setup Sync Timer (4000ms) - For Firebase
            syncTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            syncTimer.Tick += SyncLoop_Tick;
            syncTimer.Start();
        }

        protected override void OnClosed(EventArgs e)
        {
            UnhookWindowsHookEx(_hookID);
            base.OnClosed(e);
        }

        // ==========================================
        // UI EVENT HANDLERS
        // ==========================================
        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) { DragMove(); }
        private void CloseButton_Click(object sender, RoutedEventArgs e) { Application.Current.Shutdown(); }

        private void SidebarList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PageFocusMode == null) return;
            int index = SidebarList.SelectedIndex;
            PageEyeCure.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
            PagePomodoro.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
            PageFocusMode.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            userProfileName = TxtProfileName.Text;
            SaveData();
            SyncProfileNameToFirebase(userProfileName);
            MessageBox.Show("Profile Name Saved!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            if (isSessionActive) return;
            
            string pass = TxtPass.Text;
            if (string.IsNullOrEmpty(pass))
            {
                MessageBox.Show("Set a password to lock the device!", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            isPassMode = true;
            currentSessionPass = pass;
            isSessionActive = true;
            
            TxtPass.Clear();
            SaveData();
            UpdateUIStates();
            ManageFocusSound(true);
            SyncPasswordToFirebase(currentSessionPass, true);
            
            MessageBox.Show("Focus Mode Active. Your apps will be blocked.", "RasFocus Pro", MessageBoxButton.OK, MessageBoxImage.Information);
            this.Hide(); // Hide to tray naturally
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            if (!isSessionActive) return;

            if (isPassMode)
            {
                if (TxtPass.Text == currentSessionPass)
                {
                    ClearSessionData();
                    SyncPasswordToFirebase("", false);
                    MessageBox.Show("Session Stopped Successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("Wrong Password!", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
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

        private void BtnAddApp_Click(object sender, RoutedEventArgs e)
        {
            string txt = TxtAppAdd.Text.ToLower().Trim();
            if (!string.IsNullOrEmpty(txt))
            {
                if (!txt.EndsWith(".exe")) txt += ".exe";
                if (!blockedApps.Contains(txt))
                {
                    blockedApps.Add(txt);
                    ListBlockedApps.Items.Add(txt);
                    SaveData();
                }
                TxtAppAdd.Clear();
            }
        }

        private void BtnRemoveApp_Click(object sender, RoutedEventArgs e)
        {
            if (ListBlockedApps.SelectedItem != null)
            {
                string sel = ListBlockedApps.SelectedItem.ToString();
                blockedApps.Remove(sel);
                ListBlockedApps.Items.Remove(sel);
                SaveData();
            }
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e) { RefreshRunningApps(); }

        private void UpdateUIStates()
        {
            LblStatus.Text = isSessionActive ? "🔒 Focus Active" : "Ready";
            TxtPass.IsEnabled = !isSessionActive;
            BtnPomoStart.IsEnabled = !isSessionActive;
        }

        // ==========================================
        // BACKGROUND LOGIC (TIMERS)
        // ==========================================
        private void RefreshRunningApps()
        {
            ListRunningApps.Items.Clear();
            var apps = Process.GetProcesses().Select(p => p.ProcessName.ToLower() + ".exe").Distinct().OrderBy(n => n);
            foreach (var app in apps) ListRunningApps.Items.Add(app);
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

                if (blockAdult && explicitKeywords.Any(k => sTitle.Contains(k)))
                {
                    CloseWindowNatively(hWnd);
                    ShowOverlay(1);
                    return;
                }

                if (isSessionActive)
                {
                    bool isBrowser = sTitle.Contains("chrome") || sTitle.Contains("edge") || sTitle.Contains("firefox") || sTitle.Contains("brave");
                    if (isBrowser)
                    {
                        if (blockedWebs.Any(w => sTitle.Contains(w.ToLower())))
                        {
                            CloseWindowNatively(hWnd);
                            ShowOverlay(2);
                        }
                    }
                }
            }
        }

        private void SlowLoop_Tick(object sender, EventArgs e)
        {
            if (!isSessionActive) return;

            if (isPomodoroMode)
            {
                pomoTicks++;
                int totalMins = isPomodoroBreak ? 2 : pomoLengthMin;
                int left = (totalMins * 60) - pomoTicks;
                if (left < 0) left = 0;

                TimeSpan t = TimeSpan.FromSeconds(left);
                LblPomoTime.Text = t.ToString(@"hh\:mm\:ss");

                if (!isPomodoroBreak && pomoTicks >= pomoLengthMin * 60)
                {
                    isPomodoroBreak = true;
                    pomoTicks = 0;
                    // Show break page logic here
                }
            }

            // Process Killer
            foreach (Process p in Process.GetProcesses())
            {
                try
                {
                    string pName = p.ProcessName.ToLower() + ".exe";
                    if (pName == "taskmgr.exe") { p.Kill(); continue; }

                    if (useAllowMode)
                    {
                        if (!systemApps.Contains(pName) && !allowedApps.Contains(pName)) p.Kill();
                    }
                    else
                    {
                        if (blockedApps.Contains(pName)) p.Kill();
                    }
                }
                catch { }
            }
        }

        private void SyncLoop_Tick(object sender, EventArgs e)
        {
            // Firebase sync logic calls (PowerShell detached)
            SyncLiveTrackerToFirebase();
        }

        // ==========================================
        // UTILITIES & NATIVE HELPERS
        // ==========================================
        private void CloseWindowNatively(IntPtr hWnd)
        {
            System.Windows.Forms.SendKeys.SendWait("^w"); // Ctrl+W
        }

        private void ShowOverlay(int type)
        {
            if (overlayWindow == null)
            {
                overlayWindow = new Window
                {
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = true,
                    Background = new SolidColorBrush(Color.FromArgb(240, 9, 61, 31)),
                    Topmost = true,
                    Width = 800,
                    Height = 300,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen
                };
                TextBlock tb = new TextBlock
                {
                    Text = "Blocked by RasFocus Pro!",
                    Foreground = Brushes.White,
                    FontSize = 30,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                overlayWindow.Content = tb;
            }
            overlayWindow.Show();
            
            // Hide after 5 seconds
            DispatcherTimer t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            t.Tick += (s, e) => { overlayWindow.Hide(); t.Stop(); };
            t.Start();
        }

        private void SetupEyeFilters()
        {
            // Transparent click-through windows overlaying the screen
            eyeFilterDim = new Window
            {
                WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent,
                Topmost = true, ShowInTaskbar = false, IsHitTestVisible = false,
                WindowState = WindowState.Maximized
            };
            eyeFilterWarm = new Window
            {
                WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent,
                Topmost = true, ShowInTaskbar = false, IsHitTestVisible = false,
                WindowState = WindowState.Maximized
            };
            eyeFilterDim.Show();
            eyeFilterWarm.Show();
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
        // KEYBOARD HOOK LOGIC
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
            if (nCode >= 0 && wParam == (IntPtr)WM_KEYDOWN && _instance.blockAdult)
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
                            _instance.ShowOverlay(1);
                            break;
                        }
                    }
                }
            }
            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        // ==========================================
        // FIREBASE SYNC (PowerShell)
        // ==========================================
        private string GetDeviceID()
        {
            return Environment.MachineName; // Simplified for C#
        }

        private void RunPowerShell(string cmd)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-WindowStyle Hidden -Command \"{cmd}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            });
        }

        private void SyncProfileNameToFirebase(string name)
        {
            string url = $"https://firestore.googleapis.com/v1/projects/mywebtools-f8d53/databases/(default)/documents/subscription_requests/{GetDeviceID()}?updateMask.fieldPaths=profileName&key=AIzaSyDGd3KAo45UuqmeGFALziz_oKm3htEASHY";
            string cmd = $"$body = @{{ fields = @{{ profileName = @{{ stringValue = '{name}' }} }} }} | ConvertTo-Json -Depth 5; Invoke-RestMethod -Uri '{url}' -Method Patch -Body $body -ContentType 'application/json'";
            RunPowerShell(cmd);
        }

        private void SyncPasswordToFirebase(string pass, bool isLocking)
        {
            string val = isLocking ? pass : "";
            string url = $"https://firestore.googleapis.com/v1/projects/mywebtools-f8d53/databases/(default)/documents/subscription_requests/{GetDeviceID()}?updateMask.fieldPaths=livePassword&key=AIzaSyDGd3KAo45UuqmeGFALziz_oKm3htEASHY";
            string cmd = $"$body = @{{ fields = @{{ livePassword = @{{ stringValue = '{val}' }} }} }} | ConvertTo-Json -Depth 5; Invoke-RestMethod -Uri '{url}' -Method Patch -Body $body -ContentType 'application/json'";
            RunPowerShell(cmd);
        }

        private void SyncLiveTrackerToFirebase()
        {
            string activeStr = isSessionActive ? "$true" : "$false";
            string url = $"https://firestore.googleapis.com/v1/projects/mywebtools-f8d53/databases/(default)/documents/subscription_requests/{GetDeviceID()}?updateMask.fieldPaths=isSelfControlActive&key=AIzaSyDGd3KAo45UuqmeGFALziz_oKm3htEASHY";
            string cmd = $"$body = @{{ fields = @{{ isSelfControlActive = @{{ booleanValue = {activeStr} }} }} }} | ConvertTo-Json -Depth 5; Invoke-RestMethod -Uri '{url}' -Method Patch -Body $body -ContentType 'application/json'";
            RunPowerShell(cmd);
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
                foreach (var app in blockedApps) ListBlockedApps.Items.Add(app);
            }
        }
    }
}

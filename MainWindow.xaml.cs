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
        static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("winmm.dll")]
        private static extern long mciSendString(string strCommand, StringBuilder strReturn, int iReturnLength, IntPtr hwndCallback);

        // Global Keyboard Hook API
        [DllImport("user32.dll")]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        // Constants
        private const int SW_MINIMIZE = 6;
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
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
        
        private string currentSessionPass = "", userProfileName = "Rasel Mia";
        private int trialDaysLeft = 7;
        private bool isLicenseValid = false, isTrialExpired = false;

        private List<string> blockedApps = new List<string>();
        private List<string> blockedWebs = new List<string>();

        private string[] systemApps = { "explorer.exe", "svchost.exe", "taskmgr.exe", "cmd.exe", "conhost.exe", "csrss.exe", "dwm.exe", "lsass.exe", "services.exe", "smss.exe", "wininit.exe", "winlogon.exe", "spoolsv.exe", "fontdrvhost.exe" };
        private string[] explicitKeywords = { "porn", "xxx", "sex", "nude", "nsfw", "xvideos", "pornhub", "xnxx", "xhamster", "brazzers", "onlyfans", "playboy", "mia khalifa", "bhabi", "chudai", "bangla choti", "magi", "sexy" };
        private string[] safeBrowserTitles = { "new tab", "start", "blank page", "allowed websites", "loading", "untitled", "connecting", "pomodoro break" };

        private string secretDir;
        private Window overlayWindow = null;

        // Keyboard Hook Setup
        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        private static LowLevelKeyboardProc _proc;
        private static IntPtr _hookID = IntPtr.Zero;
        private static string globalKeyBuffer = "";
        private static MainWindow _instance; 

        // Mutex for Single Instance
        private static Mutex _mutex = null;

        public MainWindow()
        {
            // 1. Single Instance Check (Like your C++ Mutex)
            bool createdNew;
            _mutex = new Mutex(true, "RasFocusPro_Mutex_V55", out createdNew);
            if (!createdNew)
            {
                Application.Current.Shutdown();
                return;
            }

            InitializeComponent();
            _instance = this;
            
            // 2. Setup Secret Directory
            secretDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RasFocusPro");
            Directory.CreateDirectory(secretDir);

            // 3. Load Data & UI
            LoadData();
            RefreshRunningApps();

            // 4. Setup Global Keyboard Hook
            _proc = HookCallback;
            _hookID = SetHook(_proc);

            // 5. Setup Background Timers
            fastTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            fastTimer.Tick += FastLoop_Tick;
            fastTimer.Start();

            slowTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            slowTimer.Tick += SlowLoop_Tick;
            slowTimer.Start();

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
        
        private void CloseButton_Click(object sender, RoutedEventArgs e) 
        { 
            // In a real app, you might want to Hide() to tray instead of shutdown
            Application.Current.Shutdown(); 
        }

        private void SidebarList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PageFocusMode == null) return;
            int index = SidebarList.SelectedIndex;
            PageEyeCure.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
            PagePomodoro.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
            PageFocusMode.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
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
            ManageFocusSound(true);
            SyncPasswordToFirebase(currentSessionPass, true);
            
            MessageBox.Show("Focus Mode Active. Your apps will be blocked.", "RasFocus Pro", MessageBoxButton.OK, MessageBoxImage.Information);
            this.Hide(); // Hide to tray
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
                ManageFocusSound(true);
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

                // Adult Content Blocker
                if (blockAdult && explicitKeywords.Any(k => sTitle.Contains(k)))
                {
                    CloseWindowNatively(hWnd);
                    ShowOverlay("⚠️ Blocked by Content Filter!");
                    return;
                }

                // Social Media Blocker
                if (blockReels && sTitle.Contains("facebook") && sTitle.Contains("reels")) { CloseWindowNatively(hWnd); ShowOverlay("Reels Blocked!"); return; }
                if (blockShorts && sTitle.Contains("youtube") && sTitle.Contains("shorts")) { CloseWindowNatively(hWnd); ShowOverlay("Shorts Blocked!"); return; }

                // Focus Mode Browser Blocker
                if (isSessionActive)
                {
                    bool isBrowser = sTitle.Contains("chrome") || sTitle.Contains("edge") || sTitle.Contains("firefox") || sTitle.Contains("brave");
                    if (isBrowser && blockedWebs.Any(w => sTitle.Contains(w.ToLower())))
                    {
                        CloseWindowNatively(hWnd);
                        ShowOverlay("Website Blocked by Focus Mode!");
                    }
                }
            }
        }

        private void SlowLoop_Tick(object sender, EventArgs e)
        {
            if (!isSessionActive) return;

            // Pomodoro Logic
            if (isPomodoroMode)
            {
                pomoTicks++;
                int totalMins = isPomodoroBreak ? 5 : pomoLengthMin; // Using 5 min rest from your UI
                int left = (totalMins * 60) - pomoTicks;
                if (left < 0) left = 0;

                TimeSpan t = TimeSpan.FromSeconds(left);
                LblPomoTime.Text = t.ToString(@"hh\:mm\:ss");

                if (!isPomodoroBreak && pomoTicks >= pomoLengthMin * 60)
                {
                    isPomodoroBreak = true;
                    pomoTicks = 0;
                    ShowOverlay("☕ Time to Rest! Break Started.");
                }
            }

            // Native Process Killer
            foreach (Process p in Process.GetProcesses())
            {
                try
                {
                    string pName = p.ProcessName.ToLower() + ".exe";
                    if (pName == "taskmgr.exe") { p.Kill(); continue; } // Block taskmgr during focus

                    if (useAllowMode)
                    {
                        if (!systemApps.Contains(pName) && !allowedApps.Contains(pName)) p.Kill();
                    }
                    else
                    {
                        if (blockedApps.Contains(pName)) p.Kill();
                    }
                }
                catch { /* Ignore access denied for system processes */ }
            }
        }

        private void SyncLoop_Tick(object sender, EventArgs e)
        {
            SyncLiveTrackerToFirebase();
        }

        // ==========================================
        // UTILITIES & NATIVE HELPERS
        // ==========================================
        private void CloseWindowNatively(IntPtr hWnd)
        {
            // Simulate Ctrl+W natively using user32.dll to close active browser tab
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
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = true,
                    Background = new SolidColorBrush(Color.FromArgb(240, 9, 61, 31)), // Dark Green Overlay
                    Topmost = true,
                    Width = 800,
                    Height = 300,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen
                };
                TextBlock tb = new TextBlock
                {
                    Text = message,
                    Foreground = Brushes.White,
                    FontSize = 32,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                overlayWindow.Content = tb;
            }
            else
            {
                ((TextBlock)overlayWindow.Content).Text = message;
            }
            
            overlayWindow.Show();
            
            DispatcherTimer t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            t.Tick += (s, ev) => { overlayWindow.Hide(); t.Stop(); };
            t.Start();
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
            ManageFocusSound(false);
            
            TxtPass.IsEnabled = true;
            BtnPomoStart.IsEnabled = true;
        }

        // ==========================================
        // KEYBOARD HOOK LOGIC (C++ TO C#)
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
                            _instance.ShowOverlay("⚠️ Content Blocked!");
                            break;
                        }
                    }
                }
            }
            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        // ==========================================
        // FIREBASE SYNC (PowerShell Execution)
        // ==========================================
        private string GetDeviceID()
        {
            return Environment.MachineName; // Replaces C++ GetComputerName
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

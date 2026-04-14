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
        private List<string> allowedApps = new List<string>(); // Fixed variable
        private List<string> allowedWebs = new List<string>(); // Fixed variable

        private string[] systemApps = { "explorer.exe", "svchost.exe", "taskmgr.exe", "cmd.exe", "conhost.exe", "csrss.exe", "dwm.exe", "lsass.exe", "services.exe", "smss.exe", "wininit.exe", "winlogon.exe", "spoolsv.exe", "fontdrvhost.exe" };
        private string[] explicitKeywords = { "porn", "xxx", "sex", "nude", "nsfw", "xvideos", "pornhub", "xnxx", "xhamster", "brazzers", "onlyfans", "playboy", "mia khalifa", "bhabi", "chudai", "bangla choti", "magi", "sexy" };
        
        private string secretDir;
        private Window overlayWindow = null;

        // Keyboard Hook Setup
        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        private static LowLevelKeyboardProc _proc;
        private static IntPtr _hookID = IntPtr.Zero;
        private static string globalKeyBuffer = "";
        private static MainWindow _instance; 

        public MainWindow()
        {
            InitializeComponent();
            _instance = this;
            
            secretDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RasFocusPro");
            Directory.CreateDirectory(secretDir);

            LoadData();
            RefreshRunningApps();

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
            
            MessageBox.Show("Focus Mode Active. Your apps will be blocked.", "RasFocus Pro", MessageBoxButton.OK, MessageBoxImage.Information);
            this.Hide(); 
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            if (!isSessionActive) return;

            if (isPassMode)
            {
                if (TxtPass.Text == currentSessionPass)
                {
                    ClearSessionData();
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
            BtnStart.IsEnabled = !isSessionActive;
            BtnStop.IsEnabled = isSessionActive;
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
                    ShowOverlay("⚠️ Blocked by Content Filter!");
                    return;
                }

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

            if (isPomodoroMode)
            {
                pomoTicks++;
                int totalMins = isPomodoroBreak ? 5 : pomoLengthMin;
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

        private void SyncLoop_Tick(object sender, EventArgs e) { }

        // ==========================================
        // UTILITIES & NATIVE HELPERS
        // ==========================================
        private void CloseWindowNatively(IntPtr hWnd)
        {
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
                    Text = message, Foreground = Brushes.White, FontSize = 32, FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
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

        private void ClearSessionData()
        {
            isSessionActive = false;
            isPassMode = false;
            isPomodoroMode = false;
            currentSessionPass = "";
            SaveData();
            UpdateUIStates();
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

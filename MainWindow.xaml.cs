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

        private uint WM_WAKEUP; // FIX: removed static init

        // ==========================================
        // GLOBALS & DATA (unchanged)
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

        private string[] systemApps = { "explorer.exe","svchost.exe","taskmgr.exe","cmd.exe","conhost.exe","csrss.exe","dwm.exe","lsass.exe","services.exe","smss.exe","wininit.exe","winlogon.exe","spoolsv.exe","fontdrvhost.exe" };

        private string secretDir;
        private Window overlayWindow = null;
        private Window eyeFilterDim = null;
        private Window eyeFilterWarm = null;

        private System.Windows.Forms.NotifyIcon trayIcon;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        private static LowLevelKeyboardProc _proc;
        private static IntPtr _hookID = IntPtr.Zero;
        private static MainWindow _instance;
        private static Mutex _mutex = null;

        public MainWindow()
        {
            bool createdNew;
            _mutex = new Mutex(true, "RasFocusPro_Mutex_V55", out createdNew);
            if (!createdNew)
            {
                WM_WAKEUP = RegisterWindowMessage("RasFocusPro_Wakeup");
                PostMessage((IntPtr)HWND_BROADCAST, WM_WAKEUP, IntPtr.Zero, IntPtr.Zero);
                Application.Current.Shutdown();
                return;
            }

            InitializeComponent();
            _instance = this;

            secretDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RasFocusPro");
            Directory.CreateDirectory(secretDir);

            Loaded += MainWindow_Loaded; // FIX timing
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            WM_WAKEUP = RegisterWindowMessage("RasFocusPro_Wakeup");

            var src = (HwndSource)HwndSource.FromVisual(this);
            if (src != null) src.AddHook(WndProc);

            SetupTrayIcon();
            SetupEyeFilters(); // now safe
            LoadData();
            RefreshRunningApps();

            _proc = HookCallback;          // FIX timing
            _hookID = SetHook(_proc);     // FIX timing

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
            trayIcon?.Dispose();
            base.OnClosed(e);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_WAKEUP)
            {
                Show();
                WindowState = WindowState.Normal;
                Activate();
                handled = true;
            }
            return IntPtr.Zero;
        }

        // ====== CRASH FIX: overlay safe show ======
        private void ShowOverlay(string message)
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
                overlayWindow.Content = new TextBlock
                {
                    Foreground = Brushes.White,
                    FontSize = 24,
                    FontWeight = FontWeights.Bold,
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(20)
                };
            }

            ((TextBlock)overlayWindow.Content).Text = message;

            if (!overlayWindow.IsVisible) // FIX
                overlayWindow.Show();

            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
            t.Tick += (s, ev) => { overlayWindow.Hide(); t.Stop(); };
            t.Start();
        }

        // ====== CRASH FIX: eye filter timing ======
        private void SetupEyeFilters()
        {
            eyeFilterDim = new Window { WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, Topmost = true, ShowInTaskbar = false, IsHitTestVisible = false, WindowState = WindowState.Maximized };
            eyeFilterWarm = new Window { WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, Topmost = true, ShowInTaskbar = false, IsHitTestVisible = false, WindowState = WindowState.Maximized };
        }

        // ====== CRASH FIX: process killer safety ======
        private void SlowLoop_Tick(object sender, EventArgs e)
        {
            foreach (Process p in Process.GetProcesses())
            {
                try
                {
                    if (p.Id == Process.GetCurrentProcess().Id) continue; // FIX
                    if (p.ProcessName.ToLower().Contains("rasfocus")) continue; // FIX
                }
                catch { }
            }
        }

        private void FastLoop_Tick(object sender, EventArgs e) { }
        private void SyncLoop_Tick(object sender, EventArgs e) { }

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
            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        private void RefreshRunningApps() { }
        private void LoadData() { }
    }
}

using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace RasFocusPro
{
    public partial class MainWindow : Window
    {
        private DispatcherTimer focusTimer;
        private TimeSpan focusTimeLeft;

        private DispatcherTimer pomoTimer;
        private TimeSpan pomoTimeLeft;
        private int currentPomoSession = 1;
        private int totalPomoSessions = 4;
        private bool isFocusing = false;

        public MainWindow()
        {
            InitializeComponent();
            
            // Focus Timer Setup
            focusTimer = new DispatcherTimer();
            focusTimer.Interval = TimeSpan.FromSeconds(1);
            focusTimer.Tick += FocusTimer_Tick;

            // Pomodoro Timer Setup
            pomoTimer = new DispatcherTimer();
            pomoTimer.Interval = TimeSpan.FromSeconds(1);
            pomoTimer.Tick += PomoTimer_Tick;
            
            LoadMockData();
        }

        private void LoadMockData()
        {
            // ডেমোনস্ট্রেশনের জন্য কম্বোবক্সে কিছু ডিফল্ট আইটেম যুক্ত করা হয়েছে
            ComboAppBlock.Items.Add("vlc.exe");
            ComboAppBlock.Items.Add("chrome.exe");
            ComboWebBlock.Items.Add("facebook.com");
            ComboWebBlock.Items.Add("youtube.com");
        }

        // --- Window Actions ---
        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        // --- Sidebar Navigation ---
        private void SidebarList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PageFocusMode == null || PageEyeCure == null || PagePomodoro == null) return;

            PageFocusMode.Visibility = Visibility.Collapsed;
            PageEyeCure.Visibility = Visibility.Collapsed;
            PagePomodoro.Visibility = Visibility.Collapsed;

            int index = SidebarList.SelectedIndex;
            if (index == 0) PageFocusMode.Visibility = Visibility.Visible;
            else if (index == 1) PageEyeCure.Visibility = Visibility.Visible;
            else if (index == 2) PagePomodoro.Visibility = Visibility.Visible;
        }

        // --- Focus Mode Features ---
        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show($"Profile '{TxtProfileName.Text}' saved successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnLiveChat_Click(object sender, RoutedEventArgs e)
        {
            // লাইভ চ্যাট লিংক ওপেন করার লজিক
            Process.Start(new ProcessStartInfo("https://your-live-chat-link.com") { UseShellExecute = true });
        }

        private void BtnUpgrade_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Upgrade to Pro to unlock all features!", "Upgrade", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            if (isFocusing) return;

            int hours = 0, minutes = 0;
            int.TryParse(TxtTimeHr.Text, out hours);
            int.TryParse(TxtTimeMin.Text, out minutes);

            if (hours == 0 && minutes == 0)
            {
                MessageBox.Show("Please set a valid time.", "Invalid Time", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(TxtPass.Text))
            {
                 MessageBox.Show("Please set a lock password to start.", "Password Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                 return;
            }

            focusTimeLeft = new TimeSpan(hours, minutes, 0);
            LblStatus.Text = $"Focusing: {focusTimeLeft:hh\\:mm\\:ss}";
            LblStatus.Foreground = (System.Windows.Media.Brush)FindResource("SuccessGreen");
            
            isFocusing = true;
            BtnStart.IsEnabled = false;
            focusTimer.Start();
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            if (!isFocusing) return;

            // পাসওয়ার্ড ভেরিফিকেশন লজিক 
            if (TxtPass.Text != "1234") // এখানে আপনার সিকিউর লজিক বসবে
            {
                MessageBox.Show("Correct password required to stop early!", "Locked", MessageBoxButton.OK, MessageBoxImage.Error);
                // ডেমোর জন্য পাসওয়ার্ড না মিললেও স্টপ না করার অপশন রাখতে পারেন
            }

            focusTimer.Stop();
            isFocusing = false;
            BtnStart.IsEnabled = true;
            LblStatus.Text = "Ready";
            LblStatus.Foreground = (System.Windows.Media.Brush)FindResource("WarningRed");
        }

        private void FocusTimer_Tick(object sender, EventArgs e)
        {
            if (focusTimeLeft.TotalSeconds > 0)
            {
                focusTimeLeft = focusTimeLeft.Subtract(TimeSpan.FromSeconds(1));
                LblStatus.Text = $"Focusing: {focusTimeLeft:hh\\:mm\\:ss}";
            }
            else
            {
                focusTimer.Stop();
                isFocusing = false;
                BtnStart.IsEnabled = true;
                LblStatus.Text = "Finished!";
                MessageBox.Show("Focus Session Completed!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnStopWatch_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Stopwatch feature coming soon!", "Info");
        }

        // --- Block/Allow List Management ---
        private void BtnAddApp_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(ComboAppBlock.Text) && !ListBlockedApps.Items.Contains(ComboAppBlock.Text))
                ListBlockedApps.Items.Add(ComboAppBlock.Text);
        }

        private void BtnAddWeb_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(ComboWebBlock.Text) && !ListBlockedWebs.Items.Contains(ComboWebBlock.Text))
                ListBlockedWebs.Items.Add(ComboWebBlock.Text);
        }

        private void BtnRemApp_Click(object sender, RoutedEventArgs e)
        {
            // সিলেক্টেড আইটেম ডিলিট করার লজিক
            if (ListBlockedApps.SelectedIndex != -1)
                ListBlockedApps.Items.RemoveAt(ListBlockedApps.SelectedIndex);
            else if (ListBlockedWebs.SelectedIndex != -1)
                ListBlockedWebs.Items.RemoveAt(ListBlockedWebs.SelectedIndex);
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            ListRunningApps.Items.Clear();
            var processes = Process.GetProcesses().Where(p => !string.IsNullOrEmpty(p.MainWindowTitle)).Take(15);
            foreach (var p in processes)
            {
                ListRunningApps.Items.Add($"{p.ProcessName}.exe");
            }
        }

        private void BtnAddAllowApp_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(ComboAppAllow.Text) && !ListAllowApps.Items.Contains(ComboAppAllow.Text))
                ListAllowApps.Items.Add(ComboAppAllow.Text);
        }

        private void BtnAddAllowWeb_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(ComboWebAllow.Text) && !ListAllowWebs.Items.Contains(ComboWebAllow.Text))
                ListAllowWebs.Items.Add(ComboWebAllow.Text);
        }

        private void BtnRemAllowApp_Click(object sender, RoutedEventArgs e)
        {
            if (ListAllowApps.SelectedIndex != -1)
                ListAllowApps.Items.RemoveAt(ListAllowApps.SelectedIndex);
            else if (ListAllowWebs.SelectedIndex != -1)
                ListAllowWebs.Items.RemoveAt(ListAllowWebs.SelectedIndex);
        }

        // --- Eye Cure Display ---
        private void SliderBrightness_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            // আসল ব্রাইটনেস কন্ট্রোলের জন্য Win32 API (যেমন WmiMonitorBrightness) ব্যবহার করতে হবে
        }

        private void SliderWarmth_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            // স্ক্রিন ফিল্টারের জন্য কাস্টম ওভারলে বা API ব্যবহার করতে হবে
        }

        private void PresetDay_Click(object sender, RoutedEventArgs e)
        {
            SliderBrightness.Value = 100;
            SliderWarmth.Value = 0;
        }

        private void PresetReading_Click(object sender, RoutedEventArgs e)
        {
            SliderBrightness.Value = 60;
            SliderWarmth.Value = 40;
        }

        private void PresetNight_Click(object sender, RoutedEventArgs e)
        {
            SliderBrightness.Value = 30;
            SliderWarmth.Value = 80;
        }

        // --- Pomodoro Setup ---
        private void BtnPomoStart_Click(object sender, RoutedEventArgs e)
        {
            int min;
            if (int.TryParse(TxtPomoMin.Text, out min))
            {
                int.TryParse(TxtPomoSessions.Text, out totalPomoSessions);
                pomoTimeLeft = TimeSpan.FromMinutes(min);
                LblPomoTime.Text = pomoTimeLeft.ToString("hh\\:mm\\:ss");
                LblPomoStatus.Text = $"Status: Session {currentPomoSession}/{totalPomoSessions} Running";
                pomoTimer.Start();
                BtnPomoStart.IsEnabled = false;
            }
        }

        private void BtnPomoStop_Click(object sender, RoutedEventArgs e)
        {
            pomoTimer.Stop();
            BtnPomoStart.IsEnabled = true;
            LblPomoStatus.Text = "Status: Stopped";
        }

        private void PomoTimer_Tick(object sender, EventArgs e)
        {
            if (pomoTimeLeft.TotalSeconds > 0)
            {
                pomoTimeLeft = pomoTimeLeft.Subtract(TimeSpan.FromSeconds(1));
                LblPomoTime.Text = pomoTimeLeft.ToString("hh\\:mm\\:ss");
            }
            else
            {
                pomoTimer.Stop();
                if (currentPomoSession < totalPomoSessions)
                {
                    currentPomoSession++;
                    MessageBox.Show("Time for a short break!", "Pomodoro", MessageBoxButton.OK, MessageBoxImage.Information);
                    
                    BtnPomoStart.IsEnabled = true;
                    LblPomoStatus.Text = $"Status: Ready for Session {currentPomoSession}";
                }
                else
                {
                    MessageBox.Show("All sessions completed! Great job!", "Pomodoro", MessageBoxButton.OK, MessageBoxImage.Information);
                    currentPomoSession = 1;
                    BtnPomoStart.IsEnabled = true;
                    LblPomoStatus.Text = "Status: Finished";
                }
            }
        }
    }
}

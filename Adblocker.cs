using System;
using Microsoft.Win32;

namespace RasFocusPro
{
    public static class Adblocker
    {
        public static void ToggleAdBlock(bool enable)
        {
            try
            {
                // Chrome Registry Path for Force Install
                string chromePath = @"SOFTWARE\Policies\Google\Chrome\ExtensionInstallForcelist";
                // AdGuard Chrome Extension ID & Update URL
                string adGuardChrome = "bgnkhhnnamicmpeenaelnjfhikgbkllg;https://clients2.google.com/service/update2/crx";

                // Edge Registry Path for Force Install
                string edgePath = @"SOFTWARE\Policies\Microsoft\Edge\ExtensionInstallForcelist";
                // AdGuard Edge Extension ID & Update URL
                string adGuardEdge = "pdffkfellgipmhklpdmokmckkkfcopbh;https://edge.microsoft.com/extensionwebstorebase/v1/crx";

                if (enable)
                {
                    // ক্রোম ব্রাউজারে AdGuard ফোর্স ইনস্টল করা
                    using (RegistryKey chromeKey = Registry.LocalMachine.CreateSubKey(chromePath))
                    {
                        if (chromeKey != null)
                        {
                            chromeKey.SetValue("1", adGuardChrome, RegistryValueKind.String);
                        }
                    }

                    // এজ ব্রাউজারে AdGuard ফোর্স ইনস্টল করা
                    using (RegistryKey edgeKey = Registry.LocalMachine.CreateSubKey(edgePath))
                    {
                        if (edgeKey != null)
                        {
                            edgeKey.SetValue("1", adGuardEdge, RegistryValueKind.String);
                        }
                    }
                }
                else
                {
                    // টিক তুলে নিলে AdGuard ডিলিট করে দেওয়া (Value Delete)
                    using (RegistryKey chromeKey = Registry.LocalMachine.OpenSubKey(chromePath, writable: true))
                    {
                        if (chromeKey != null && chromeKey.GetValue("1") != null)
                        {
                            chromeKey.DeleteValue("1");
                        }
                    }

                    using (RegistryKey edgeKey = Registry.LocalMachine.OpenSubKey(edgePath, writable: true))
                    {
                        if (edgeKey != null && edgeKey.GetValue("1") != null)
                        {
                            edgeKey.DeleteValue("1");
                        }
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Note: Modifying HKEY_LOCAL_MACHINE requires Administrator privileges.
                // If the app is not running as Admin, it will silently fail here (or you can show a MessageBox).
                // System.Windows.MessageBox.Show("Run app as Administrator to enable AdBlocker.", "Permission Denied", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
            catch (Exception)
            {
                // Catch any other unexpected registry errors silently
            }
        }
    }
}

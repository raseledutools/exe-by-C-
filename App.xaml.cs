// =============================================================================
// EnterprisePdfEditor — App.xaml.cs
// =============================================================================

using System.Windows;

namespace EnterprisePdfEditor
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Set up global unhandled exception handler.
            DispatcherUnhandledException += (s, ex) =>
            {
                MessageBox.Show(
                    $"An unexpected error occurred:\n\n{ex.Exception.Message}\n\n" +
                    $"Stack trace:\n{ex.Exception.StackTrace}",
                    "Unhandled Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                ex.Handled = true;
            };

            // Handle non-UI thread exceptions.
            System.AppDomain.CurrentDomain.UnhandledException += (s, ex) =>
            {
                if (ex.ExceptionObject is Exception e2)
                {
                    Dispatcher.BeginInvoke(() =>
                        MessageBox.Show(
                            $"Fatal error:\n\n{e2.Message}",
                            "Fatal Error",
                            MessageBoxButton.OK,
                            MessageBoxImage.Stop));
                }
            };
        }
    }
}

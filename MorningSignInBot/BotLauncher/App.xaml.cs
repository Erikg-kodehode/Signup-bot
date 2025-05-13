using System;
using System.Windows;

namespace BotLauncher
{
    public partial class App : Application
    {
        public App()
        {
            // Set up any global exception handling or application-level configuration here
            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            {
                Exception ex = (Exception)args.ExceptionObject;
                MessageBox.Show(
                    $"An unhandled exception occurred: {ex.Message}\n\n{ex.StackTrace}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            };
        }
    }
}

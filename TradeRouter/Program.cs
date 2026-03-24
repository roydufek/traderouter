using System;
using System.Windows.Forms;

namespace TradeRouter
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetHighDpiMode(HighDpiMode.SystemAware);

            // Global exception handler for UI thread
            Application.ThreadException += (sender, e) =>
            {
                MessageBox.Show(
                    $"An unexpected error occurred:\n\n{e.Exception.Message}",
                    "TradeRouter Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            };

            // Global exception handler for non-UI threads
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                if (e.ExceptionObject is Exception ex)
                {
                    MessageBox.Show(
                        $"A fatal error occurred:\n\n{ex.Message}",
                        "TradeRouter Fatal Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            };

            Application.Run(new MainForm());
        }
    }
}

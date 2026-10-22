using System;
using System.Threading;
using System.Windows.Forms;
using SpaceSleuth.UI;

namespace SpaceSleuth
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => ShowError(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => ShowError(e.ExceptionObject as Exception);
            Application.Run(new MainForm());
        }

        static DateTime lastError = DateTime.MinValue;

        static void ShowError(Exception ex)
        {
            // one dialog at a time, so a repeating problem can't stack up a pile of them
            if ((DateTime.UtcNow - lastError).TotalSeconds < 5) return;
            lastError = DateTime.UtcNow;
            MessageBox.Show("Something went wrong:\n\n" + (ex == null ? "Unknown error" : ex.ToString()),
                "Sleuth", MessageBoxButtons.OK, MessageBoxIcon.Error);
            lastError = DateTime.UtcNow;
        }
    }
}

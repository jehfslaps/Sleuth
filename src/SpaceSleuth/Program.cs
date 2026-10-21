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

        static void ShowError(Exception ex)
        {
            MessageBox.Show("Something went wrong:\n\n" + (ex == null ? "Unknown error" : ex.ToString()),
                "Space Sleuth", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

using System;
using System.Linq;
using System.Net;
using System.Threading;
using System.Windows.Forms;

namespace KlembordTool
{
    internal static class Program
    {
        public const string ShowMessageName = "KlembordTool_ShowWindow_v1";

        [STAThread]
        static void Main(string[] args)
        {
            Func<string, bool> hasArg = name => args.Any(a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
            bool justUpdated = hasArg("/updated");
            bool created;
            using (var mutex = new Mutex(true, "KlembordTool_SingleInstance_v1" + (Environment.GetEnvironmentVariable("KLEMBORDTOOL_DATA") ?? "").Replace('\\', '_').Replace(':', '_'), out created))
            {
                if (!created && justUpdated)
                {
                    // Started by the updater: wait for the old version to finish closing.
                    try { created = mutex.WaitOne(15000); }
                    catch (AbandonedMutexException) { created = true; }
                }
                if (!created)
                {
                    // Already running: ask the existing instance to show its window.
                    Native.AllowSetForegroundWindow(-1);
                    Native.PostMessage(Native.HWND_BROADCAST, Native.RegisterWindowMessage(ShowMessageName), IntPtr.Zero, IntPtr.Zero);
                    return;
                }

                // GitHub requires TLS 1.2, which .NET 4 does not enable by default.
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
                Updater.CleanUp();
                Native.SetProcessDPIAware();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += (s, e) => ReportError(e.Exception);
                Application.Run(new MainForm(hasArg("/tray"), justUpdated));
                GC.KeepAlive(mutex);
            }
        }

        static void ReportError(Exception ex)
        {
            try
            {
                System.IO.Directory.CreateDirectory(AppData.Folder);
                System.IO.File.AppendAllText(System.IO.Path.Combine(AppData.Folder, "fouten.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\r\n" + ex + "\r\n\r\n");
            }
            catch (Exception) { }
            MessageBox.Show("Er ging iets mis: " + ex.Message + "\r\n\r\nDetails staan in fouten.log in de gegevensmap.",
                "Klembord Tool", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}

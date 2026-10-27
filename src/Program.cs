using System;
using System.Linq;
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
            bool created;
            using (var mutex = new Mutex(true, "KlembordTool_SingleInstance_v1", out created))
            {
                if (!created)
                {
                    // Already running: ask the existing instance to show its window.
                    Native.AllowSetForegroundWindow(-1);
                    Native.PostMessage(Native.HWND_BROADCAST, Native.RegisterWindowMessage(ShowMessageName), IntPtr.Zero, IntPtr.Zero);
                    return;
                }

                Native.SetProcessDPIAware();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                bool startHidden = args.Any(a => a.Equals("/tray", StringComparison.OrdinalIgnoreCase));
                Application.Run(new MainForm(startHidden));
                GC.KeepAlive(mutex);
            }
        }
    }
}

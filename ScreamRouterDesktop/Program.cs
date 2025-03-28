using System;
using System.Windows.Forms;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Diagnostics;

namespace ScreamRouterDesktop
{
    static class Program
    {
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool SetForegroundWindow(IntPtr hWnd);

        [STAThread]
        static void Main(string[] args)
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            using (Mutex mutex = new Mutex(true, "ScreamRouterDesktopSingleInstance", out bool createdNew))
            {
                if (createdNew)
                {
                    MainForm mainForm = new MainForm();
                    HandleArguments(args, mainForm);
                    
                    // Initialize update manager and check for updates
                    var updateManager = new UpdateManager();
                    Debug.WriteLine("[Program] Initializing update check");
                    
                    // Handle update check in a way that we can catch errors
                    Task.Run(async () => {
                        try
                        {
                            Debug.WriteLine("[Program] Starting update check");
                            await updateManager.CheckForUpdates();
                            Debug.WriteLine("[Program] Update check completed successfully");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[Program] Error during update check: {ex}");
                            // Don't show error to user - just log it since this is startup
                        }
                    });
                    
                    Application.Run(mainForm);
                }
                else
                {
                    IntPtr hWnd = FindWindow(null, "ScreamRouter Desktop Configuration");
                    if (hWnd != IntPtr.Zero)
                    {
                        SetForegroundWindow(hWnd);
                        if (args.Length > 0)
                        {
                            if (args[0] == "-settings")
                            {
                                SendMessage(hWnd, WM_SHOWSETTINGS, IntPtr.Zero, IntPtr.Zero);
                                return;
                            }
                        }
                        SendMessage(hWnd, WM_SHOWWEBINTERFACE, IntPtr.Zero, IntPtr.Zero);
                    }
                }
            }
        }

        private static void HandleArguments(string[] args, MainForm mainForm)
        {
            if (args.Length > 0)
            {
                if (args[0] == "-openwebinterface")
                {
                    mainForm.ToggleWebInterface();
                }
                else if (args[0] == "-settings")
                {
                    mainForm.ShowSettings();
                }
            }
        }

        [DllImport("user32.dll")]
        static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        const uint WM_SHOWWEBINTERFACE = 0x0400 + 1;
        const uint WM_SHOWSETTINGS = 0x0400 + 2;
    }
}

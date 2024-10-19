using System;
using System.Windows.Forms;
using System.Threading;
using System.Runtime.InteropServices;

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
            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            using (Mutex mutex = new Mutex(true, "ScreamRouterDesktopSingleInstance", out bool createdNew))
            {
                if (createdNew)
                {
                    Form1 mainForm = new Form1();
                    HandleArguments(args, mainForm);
                    Application.Run(mainForm);
                }
                else
                {
                    IntPtr hWnd = FindWindow(null, "ScreamRouter Configuration");
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

        private static void HandleArguments(string[] args, Form1 mainForm)
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
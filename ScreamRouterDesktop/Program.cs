using System;
// using System.Windows.Forms; // No longer needed for WPF startup
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

        // STAThread attribute is typically applied to the Main method of a WPF application
        // if it's defined explicitly, or handled by the framework when using App.xaml.
        // [STAThread] // No longer needed as Main is removed
        // static void Main(string[] args) // Removed Main method - App.xaml is the entry point
        // {
            // WinForms specific initialization - No longer needed for WPF startup via App.xaml
            // Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            // Application.EnableVisualStyles();
            // Application.SetCompatibleTextRenderingDefault(false);

            // Single instance check and application run logic is now handled in App.xaml.cs
            /*
            using (Mutex mutex = new Mutex(true, "ScreamRouterDesktopSingleInstance", out bool createdNew))
            {
                if (createdNew)
                {
                    // Old WinForms startup:
                    // MainForm mainForm = new MainForm();
                    // HandleArguments(args, mainForm); // Argument handling moved to App.xaml.cs
                    // ... (Update check moved to App.xaml.cs) ...
                    // Application.Run(mainForm);

                    // WPF startup is handled by App.xaml / App.xaml.cs
                    // If you need to explicitly start the App object here (less common):
                    // var app = new App();
                    // app.InitializeComponent();
                    // app.Run();
                }
                else
                {
                    // Logic to activate existing instance moved to App.xaml.cs
                    IntPtr hWnd = FindWindow(null, "ScreamRouter Desktop Configuration");
                    if (hWnd != IntPtr.Zero)
                    {
                        SetForegroundWindow(hWnd);
                        // Forwarding arguments via SendMessage might still be needed
                        // if implementing that feature fully in MainWindow.
                        // ...
                    }
                }
            }
            */

             // Minimal Main for WPF: Instantiate and run the App object.
             // This ensures the App.xaml/App.xaml.cs lifecycle starts correctly.
             // var app = new App();
             // app.InitializeComponent(); // Loads App.xaml
             // app.Run(); // Starts the application lifecycle (including Application_Startup)
        // }

        // HandleArguments is now in App.xaml.cs
        /*
        private static void HandleArguments(string[] args, MainForm mainForm)
        {
            // ... old implementation ...
        }
        */

        // P/Invoke methods might still be useful, but are duplicated in App.xaml.cs
        // Consider creating a shared utility class if needed elsewhere.
        [DllImport("user32.dll")]
        static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        const uint WM_SHOWWEBINTERFACE = 0x0400 + 1;
        const uint WM_SHOWSETTINGS = 0x0400 + 2;
    }
}

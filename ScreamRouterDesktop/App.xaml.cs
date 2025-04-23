using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace ScreamRouterDesktop
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        private Mutex? _mutex;
        private const string MutexName = "ScreamRouterDesktopSingleInstance";

        // P/Invoke declarations for single-instance handling
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        // Window messages (ensure these match the ones expected by the target window if needed)
        // Note: MainWindow doesn't currently handle these messages. This needs implementation
        // if we want the exact same argument handling behavior for existing instances.
        const uint WM_SHOWWEBINTERFACE = 0x0400 + 1; // Example message
        const uint WM_SHOWSETTINGS = 0x0400 + 2;     // Example message

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            bool createdNew;
            _mutex = new Mutex(true, MutexName, out createdNew);

            if (!createdNew)
            {
                // Another instance is already running. Find it and bring it to the front.
                // Note: The window title must match exactly. Using the title from MainWindow.xaml.
                IntPtr hWnd = FindWindow(null, "ScreamRouter Desktop Configuration");
                if (hWnd != IntPtr.Zero)
                {
                    SetForegroundWindow(hWnd);

                    // Optional: Handle command-line arguments for the existing instance
                    // This requires the existing MainWindow to handle these messages (e.g., via HwndSource)
                    // if (e.Args.Length > 0)
                    // {
                    //     if (e.Args[0] == "-settings")
                    //     {
                    //         SendMessage(hWnd, WM_SHOWSETTINGS, IntPtr.Zero, IntPtr.Zero);
                    //     }
                    //     else if (e.Args[0] == "-openwebinterface") // Check for web interface arg
                    //     {
                    //          SendMessage(hWnd, WM_SHOWWEBINTERFACE, IntPtr.Zero, IntPtr.Zero);
                    //     }
                    // } else {
                    //     // Default action if no specific arg: show web interface or settings?
                    //     SendMessage(hWnd, WM_SHOWSETTINGS, IntPtr.Zero, IntPtr.Zero); // Example: Show settings by default
                    // }
                 }

                 // Shutdown this new instance
                 System.Windows.Application.Current.Shutdown(); // Fully qualify Application
                 return;
             }

            // --- First instance startup logic ---

            // Initialize and show the main window
            var mainWindow = new MainWindow(); // Our WPF window
            this.MainWindow = mainWindow;
            // mainWindow.Show(); // Show is handled implicitly unless StartupUri is removed and not handled here

            // Handle command-line arguments for the *new* instance
            // We can pass args to MainWindow or handle them here before showing
            HandleArguments(e.Args, mainWindow);

            // Initialize update manager and check for updates (async)
            // Consider moving this inside MainWindow constructor or Loaded event
            var updateManager = new UpdateManager();
            Debug.WriteLine("[App.xaml.cs] Initializing update check");
            Task.Run(async () => {
                try
                {
                    Debug.WriteLine("[App.xaml.cs] Starting update check");
                    await updateManager.CheckForUpdates();
                    Debug.WriteLine("[App.xaml.cs] Update check completed successfully");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[App.xaml.cs] Error during update check: {ex}");
                    // Log error, maybe show non-intrusive notification later
                }
            });

            // The MainWindow will be shown automatically if StartupUri is set,
            // or needs to be shown explicitly here if StartupUri is removed.
            // Since MainWindow starts minimized/hidden, showing it here is fine.
             mainWindow.Show();
        }

        private void HandleArguments(string[] args, MainWindow mainWindow)
        {
            // This logic needs to interact with the already created MainWindow instance
            if (args.Length > 0)
            {
                // Use Dispatcher if calling methods that interact with UI from startup logic
                 mainWindow.Dispatcher.Invoke(() => {
                    if (args[0] == "-openwebinterface")
                    {
                        // mainWindow.ToggleWebInterface(); // Need to implement ToggleWebInterface in MainWindow
                        mainWindow.OpenWebInterface(); // Or just open it directly
                    }
                    else if (args[0] == "-settings")
                    {
                        mainWindow.ShowSettings();
                    }
                 });
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _mutex?.ReleaseMutex();
            _mutex?.Dispose();
            base.OnExit(e);
        }
    }
}

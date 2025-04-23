using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using WinForms = System.Windows.Forms; // Alias for WinForms types

namespace ScreamRouterDesktop
{
    /// <summary>
    /// Provides functionality to help users pin the application's notification area icon
    /// </summary>
    public static class NotificationAreaPinning
    {
        // Shell_NotifyIcon message to modify notification icon
        private const uint NIM_SETVERSION = 0x00000004;

        // Windows message sent when user clicks on notification area icon
        private const int WM_NOTIFYICON = 0x400 + 1001;

        // Flags for Shell_NotifyIcon
        private const int NIF_STATE = 0x00000008;
        private const int NIF_INFO = 0x00000010;
        
        // Icon states
        private const int NIS_HIDDEN = 0x00000001;
        private const int NIS_SHAREDICON = 0x00000002;

        /// <summary>
        /// Opens the Windows notification area settings dialog
        /// </summary>
        public static void OpenNotificationAreaSettings()
        {
            try
            {
                // Windows 10 and 11 notification area settings
                Process.Start(new ProcessStartInfo
                {
                    FileName = "ms-settings:taskbar",
                    UseShellExecute = true
                });
            }
            catch
            {
                try
                {
                    // Fallback for older Windows versions or if ms-settings fails
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "control.exe",
                        Arguments = "/name Microsoft.NotificationAreaIcons",
                        UseShellExecute = true
                    });
                }
                 catch (Exception ex)
                 {
                     WinForms.MessageBox.Show($"Unable to open notification area settings: {ex.Message}\n\n" + // Use alias
                         "Please manually open notification area settings by right-clicking on the taskbar, " +
                         "selecting 'Taskbar settings' and then configuring notification icons.",
                         "Error Opening Settings", WinForms.MessageBoxButtons.OK, WinForms.MessageBoxIcon.Error); // Use alias
                 }
             }
        }

         /// <summary>
         /// Shows a balloon tip instructing the user how to pin the notification area icon
         /// </summary>
         /// <param name="notifyIcon">The NotifyIcon to show instructions for</param>
         public static void ShowPinInstructions(WinForms.NotifyIcon notifyIcon) // Use alias
         {
             if (notifyIcon == null) return;

            notifyIcon.BalloonTipTitle = "Pin ScreamRouter to Notification Area";
            notifyIcon.BalloonTipText = "To keep this icon always visible:\n" +
                 "1. Click the up arrow (^) in the notification area to expand it, the (^) switches to a (v)\n" +
                 "2. Drag the ScreamRouter Desktop icon to the (v) next to the notification area\n" +
                 "3. Or customize notification icons in taskbar settings";

             notifyIcon.BalloonTipIcon = WinForms.ToolTipIcon.Info; // Use alias
             notifyIcon.ShowBalloonTip(15000); // Show for 15 seconds
         }

         /// <summary>
         /// Displays a modal dialog with instructions for pinning the notification icon
         /// </summary>
         public static void ShowPinInstructionsDialog()
         {
             WinForms.DialogResult result = WinForms.MessageBox.Show( // Use alias
                 "Would you like to keep the ScreamRouter icon visible in the notification area?\n\n" +
                 "To pin the icon:\n" +
                 "1. Click the up arrow (^) in the notification area to expand it, the (^) switches to a (v)\n" +
                 "2. Drag the ScreamRouter Desktop icon to the (v) next to the notification area\n" +
                 "- Or -\n" +
                 "Click 'Yes' to open notification area settings where you can customize which icons appear",
                 "Pin to Notification Area",
                 WinForms.MessageBoxButtons.YesNo, // Use alias
                 WinForms.MessageBoxIcon.Question); // Use alias

             if (result == WinForms.DialogResult.Yes) // Use alias
             {
                 OpenNotificationAreaSettings();
            }
        }
    }
}

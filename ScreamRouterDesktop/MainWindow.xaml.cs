using Microsoft.Win32;
using System;
using System.ComponentModel;
using System.Net;
using System.Runtime.InteropServices;
 using Microsoft.Win32;
 using System;
 using System.ComponentModel;
 using System.Net;
 using System.Runtime.InteropServices;
 using System.Windows;
 using System.Windows.Controls; // Explicitly use WPF controls
 using System.Windows.Data; // Required for IValueConverter
 using System.Windows.Media; // Required for Brushes
 using System.Globalization; // Required for IValueConverter CultureInfo
 using System.Drawing; // Required for Icon
 using WinForms = System.Windows.Forms; // Alias for WinForms if needed for interop (like WebInterfaceForm)

namespace ScreamRouterDesktop
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml (formerly MainForm.cs)
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        // Core components
        private ScreamSettings screamSettings;
        private UpdateManager updateManager;
         private WebInterfaceForm? webInterfaceForm; // Assuming WebInterfaceForm is still used
         // private GlobalKeyboardHook? globalKeyboardHook; // TODO: Re-implement later
         private WinForms.NotifyIcon? notifyIcon; // Use WinForms alias

         // Properties for Data Binding (Optional but good practice)
        private string _webInterfaceUrl = "";
        public string WebInterfaceUrl
        {
            get => _webInterfaceUrl;
            set { _webInterfaceUrl = value; OnPropertyChanged(nameof(WebInterfaceUrl)); }
        }
        // Add other properties for settings as needed...

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this; // Set DataContext for potential binding

            // Initialize the application components
             updateManager = new UpdateManager();
             screamSettings = new ScreamSettings();

             // Initialize NotifyIcon
             InitializeNotifyIcon(); // Uncommented

             // Check if start at boot option has been prompted before
             if (!screamSettings.HasStartAtBootBeenPrompted())
            {
                screamSettings.ShowStartAtBootDialog(); // Assuming this shows a standard MessageBox or similar
            }

            // Load configuration
            LoadConfiguration();

            // TODO: Re-implement Global Keyboard Hook later
            // InitializeGlobalKeyboardHook();

            // TODO: Re-implement Jump List creation later
            // SetCurrentProcessExplicitAppUserModelID("ScreamRouterDesktop");
            // CreateJumpList();

            // TODO: Re-implement Pinning Hint later
            // CheckAndShowPinningHint();

            // Start hidden (WPF way) - Handled by WindowState and ShowInTaskbar properties in XAML or code
            // Consider handling this in App.xaml.cs OnStartup if needed globally
        }

        // --- Configuration Loading/Saving ---

        private void LoadConfiguration()
        {
            screamSettings.Load();

            // Load URL
             // Use FindName to get controls if not using Binding
             if (FindName("UrlTextBox") is System.Windows.Controls.TextBox urlTextBox) // Fully qualify or use alias
             {
                 urlTextBox.Text = screamSettings.WebInterfaceUrl;
            }
             // Alternatively, use Binding: WebInterfaceUrl = screamSettings.WebInterfaceUrl;

             // Load standard sender settings
             if (FindName("StandardSenderRadioButton") is System.Windows.Controls.RadioButton standardSenderRadioButton) standardSenderRadioButton.IsChecked = screamSettings.SenderEnabled;
             if (FindName("SenderIpTextBox") is System.Windows.Controls.TextBox senderIpTextBox) senderIpTextBox.Text = screamSettings.SenderIP;
             if (FindName("SenderPortTextBox") is System.Windows.Controls.TextBox senderPortTextBox) senderPortTextBox.Text = screamSettings.SenderPort.ToString(); // Use TextBox now
             if (FindName("MulticastCheckBox") is System.Windows.Controls.CheckBox multicastCheckBox) multicastCheckBox.IsChecked = screamSettings.SenderMulticast;

             // Load per-process sender settings
             bool isCompatible = IsCompatibleWithPerProcessSender();
             if (FindName("PerProcessSenderRadioButton") is System.Windows.Controls.RadioButton perProcessSenderRadioButton)
             {
                 perProcessSenderRadioButton.IsChecked = screamSettings.PerProcessSenderEnabled;
                 perProcessSenderRadioButton.IsEnabled = isCompatible;
             }
             if (FindName("PerProcessSenderIpTextBox") is System.Windows.Controls.TextBox perProcessSenderIpTextBox) perProcessSenderIpTextBox.Text = screamSettings.PerProcessSenderIP;
             if (FindName("PerProcessSenderPortTextBox") is System.Windows.Controls.TextBox perProcessSenderPortTextBox) perProcessSenderPortTextBox.Text = screamSettings.PerProcessSenderPort.ToString(); // Use TextBox now

             // Update compatibility label/UI
             if (FindName("PerProcessSenderCompatibilityLabel") is TextBlock compatibilityLabel) // Assuming you add this TextBlock
             {
                 compatibilityLabel.Visibility = isCompatible ? Visibility.Collapsed : Visibility.Visible;
                 compatibilityLabel.Text = "Requires Windows 10 build 20348+ or Windows 11";
                 compatibilityLabel.Foreground = System.Windows.Media.Brushes.Red; // Fully qualify Brushes
             }

             // Load receiver settings
             if (FindName("ReceiverEnabledCheckBox") is System.Windows.Controls.CheckBox receiverEnabledCheckBox) receiverEnabledCheckBox.IsChecked = screamSettings.ReceiverEnabled;
             if (FindName("ReceiverPortTextBox") is System.Windows.Controls.TextBox receiverPortTextBox) receiverPortTextBox.Text = screamSettings.ReceiverPort.ToString(); // Use TextBox now

             // Load App settings
             if (FindName("UpdateModeComboBox") is System.Windows.Controls.ComboBox updateModeComboBox) updateModeComboBox.SelectedIndex = (int)updateManager.CurrentMode; // Assuming UpdateManager loads its state
              if (FindName("StartAtBootCheckBox") is System.Windows.Controls.CheckBox startAtBootCheckBox) startAtBootCheckBox.IsChecked = screamSettings.StartAtBoot; // Assumes StartAtBoot reflects registry state

             // Update control states based on radio buttons - Now handled by XAML Binding
             // UpdateSenderControlStates();

             // Update audio info display
             UpdateAudioInfo();

            // Start processes if enabled (consider if this should happen on load or after save)
            // screamSettings.StartProcesses(); // Maybe move this to Save or App startup

            // Create the WebInterfaceForm if URL exists (consider lazy loading)
            // string url = screamSettings.WebInterfaceUrl;
            // if (!string.IsNullOrEmpty(url))
            // {
            //     webInterfaceForm = new WebInterfaceForm(url);
            //     // webInterfaceForm.Hide(); // WebInterfaceForm is WinForms, needs careful handling or replacement
            // }
        }

        private void SaveConfiguration()
        {
            // Validate inputs (especially ports)
             bool isValid = true;
             int senderPort = 0, perProcessSenderPort = 0, receiverPort = 0;

             if (FindName("SenderPortTextBox") is System.Windows.Controls.TextBox senderPortTextBox && !int.TryParse(senderPortTextBox.Text, out senderPort) || senderPort < 1 || senderPort > 65535)
             {
                 System.Windows.MessageBox.Show("Invalid Standard Sender Port number.", "Error", MessageBoxButton.OK, MessageBoxImage.Error); // Fully qualify MessageBox
                 isValid = false;
             }
             if (FindName("PerProcessSenderPortTextBox") is System.Windows.Controls.TextBox perProcessSenderPortTextBox && !int.TryParse(perProcessSenderPortTextBox.Text, out perProcessSenderPort) || perProcessSenderPort < 1 || perProcessSenderPort > 65535)
             {
                 System.Windows.MessageBox.Show("Invalid Per-Process Sender Port number.", "Error", MessageBoxButton.OK, MessageBoxImage.Error); // Fully qualify MessageBox
                 isValid = false;
             }
              if (FindName("ReceiverPortTextBox") is System.Windows.Controls.TextBox receiverPortTextBox && !int.TryParse(receiverPortTextBox.Text, out receiverPort) || receiverPort < 1 || receiverPort > 65535)
             {
                 System.Windows.MessageBox.Show("Invalid Receiver Port number.", "Error", MessageBoxButton.OK, MessageBoxImage.Error); // Fully qualify MessageBox
                 isValid = false;
             }

             if (!isValid) return;

             // Save URL
             if (FindName("UrlTextBox") is System.Windows.Controls.TextBox urlTextBox) screamSettings.WebInterfaceUrl = urlTextBox.Text;

             // Save App settings
             if (FindName("UpdateModeComboBox") is System.Windows.Controls.ComboBox updateModeComboBox) updateManager.CurrentMode = (UpdateMode)updateModeComboBox.SelectedIndex; // Assumes UpdateManager saves its state
             if (FindName("StartAtBootCheckBox") is System.Windows.Controls.CheckBox startAtBootCheckBox) screamSettings.StartAtBoot = startAtBootCheckBox.IsChecked ?? false;

             // Save Scream settings
             screamSettings.SenderEnabled = (FindName("StandardSenderRadioButton") as System.Windows.Controls.RadioButton)?.IsChecked ?? false;
             screamSettings.SenderIP = (FindName("SenderIpTextBox") as System.Windows.Controls.TextBox)?.Text ?? "";
             screamSettings.SenderPort = senderPort;
             screamSettings.SenderMulticast = (FindName("MulticastCheckBox") as System.Windows.Controls.CheckBox)?.IsChecked ?? false;

             screamSettings.PerProcessSenderEnabled = (FindName("PerProcessSenderRadioButton") as System.Windows.Controls.RadioButton)?.IsChecked ?? false;
             screamSettings.PerProcessSenderIP = (FindName("PerProcessSenderIpTextBox") as System.Windows.Controls.TextBox)?.Text ?? "";
             screamSettings.PerProcessSenderPort = perProcessSenderPort;

             screamSettings.ReceiverEnabled = (FindName("ReceiverEnabledCheckBox") as System.Windows.Controls.CheckBox)?.IsChecked ?? false;
             screamSettings.ReceiverPort = receiverPort;

             screamSettings.Save();
             screamSettings.RestartProcesses(); // Restart processes after saving

             // Update audio info display
             UpdateAudioInfo();

             System.Windows.MessageBox.Show("Configuration saved successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information); // Fully qualify MessageBox
         }

         // --- Event Handlers ---

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            SaveConfiguration();
        }

        private void OpenWebInterfaceButton_Click(object sender, RoutedEventArgs e)
        {
            OpenWebInterface();
        }

        private void PinToNotificationAreaButton_Click(object sender, RoutedEventArgs e)
        {
            // TODO: Re-implement using NotificationAreaPinning class if it's WPF compatible
             NotificationAreaPinning.ShowPinInstructionsDialog(); // Assuming this is static and shows a dialog
        }

         private void UpdateModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
         {
             // Save happens on SaveButton click, but update manager state if needed immediately
              if (FindName("UpdateModeComboBox") is System.Windows.Controls.ComboBox updateModeComboBox) // Fully qualify
              {
                  updateManager.CurrentMode = (UpdateMode)updateModeComboBox.SelectedIndex;
                  // Consider if updateManager needs immediate saving or action
             }
        }

        private void StartAtBootCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            // Save happens on SaveButton click
             // screamSettings.StartAtBoot = (sender as CheckBox)?.IsChecked ?? false; // Update setting immediately if needed
         }

         // Removed SenderRadioButton_Changed - Handled by XAML Binding
         // private void SenderRadioButton_Changed(object sender, RoutedEventArgs e) { ... }

          // --- UI Logic Helpers ---

          // Removed UpdateSenderControlStates - Handled by XAML Binding
          // private void UpdateSenderControlStates() { ... }

          private void UpdateAudioInfo()
        {
            var audioSettings = screamSettings?.GetCurrentAudioSettings();

            Action<string, string> updateLabel = (labelName, text) =>
            {
                if (FindName(labelName) is TextBlock label)
                {
                    label.Text = text;
                }
            };

            if (audioSettings != null)
            {
                updateLabel("BitDepthLabel", $"{audioSettings.BitDepth} bits");
                updateLabel("SampleRateLabel", $"{audioSettings.SampleRate} Hz");
                updateLabel("ChannelsLabel", $"{audioSettings.Channels}");
                updateLabel("ChannelLayoutLabel", $"{audioSettings.ChannelLayout}");
            }
            else
            {
                updateLabel("BitDepthLabel", "--");
                updateLabel("SampleRateLabel", "--");
                updateLabel("ChannelsLabel", "--");
                updateLabel("ChannelLayoutLabel", "--");
            }
        }

         // --- Core Functionality Methods (Stubs/Placeholders) ---

         public void OpenWebInterface()
         {
              string url = (FindName("UrlTextBox") as System.Windows.Controls.TextBox)?.Text ?? ""; // Fully qualify
             if (!string.IsNullOrEmpty(url))
             {
                 // Handle showing the WebInterfaceForm (WinForms) or a new WPF WebView
                // This needs careful consideration if WebInterfaceForm remains WinForms
                try
                {
                     // Option 1: Keep using WinForms form (requires Microsoft.Windows.Compatibility pack)
                     if (webInterfaceForm == null /*|| webInterfaceForm.IsDisposed*/) // IsDisposed check might not work directly
                     {
                         webInterfaceForm = new WebInterfaceForm(url);
                     }
                     webInterfaceForm.Show();
                     webInterfaceForm.Activate(); // Bring to front

                    // Option 2: Replace with WPF WebView2 (Recommended for pure WPF)
                    // var webViewWindow = new WpfWebViewWindow(url); // Create a new WPF window with WebView2
                    // webViewWindow.Show();
                 }
                 catch (Exception ex)
                 {
                      System.Windows.MessageBox.Show($"Error opening web interface: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error); // Fully qualify MessageBox
                 }
             }
             else
             {
                 System.Windows.MessageBox.Show("Please enter a valid URL for ScreamRouter.", "Error", MessageBoxButton.OK, MessageBoxImage.Error); // Fully qualify MessageBox
             }
         }

        public void ShowSettings()
        {
            UpdateAudioInfo(); // Ensure info is up-to-date when shown
            this.Show();
            this.WindowState = WindowState.Normal;
            this.Activate(); // Bring to front
            this.ShowInTaskbar = true;
        }

        // TODO: Implement ToggleWebInterface if needed for tray icon left-click

        private bool IsCompatibleWithPerProcessSender()
        {
            // Windows 11 check (build 22000+)
            if (Environment.OSVersion.Version.Major >= 10 && Environment.OSVersion.Version.Build >= 22000)
                return true;

            // Windows 10 build 20348+ check
            if (Environment.OSVersion.Version.Major == 10 && Environment.OSVersion.Version.Build >= 20348)
                return true;

            return false;
        }

        // --- Window Event Handlers ---

        protected override void OnClosing(CancelEventArgs e)
        {
            // Implement close-to-tray behavior
            e.Cancel = true; // Prevent window from closing
            this.WindowState = WindowState.Minimized;
            this.ShowInTaskbar = false;
            // Optionally show a notification from the tray icon
            // notifyIcon?.ShowBalloonTip(1000, "ScreamRouter Desktop", "Running in the background.", ToolTipIcon.Info);
            base.OnClosing(e);
        }

        protected override void OnStateChanged(EventArgs e)
        {
             // Hide from taskbar when minimized
            if (WindowState == WindowState.Minimized)
            {
                this.ShowInTaskbar = false;
                // Consider hiding the window completely if using only a tray icon
                // this.Hide();
            }
            else
            {
                this.ShowInTaskbar = true;
            }
            base.OnStateChanged(e);
        }


        // --- TODO: Re-implement later ---
        // InitializeNotifyIcon()
        // NotifyIcon_MouseClick()
        // CreateJumpList() (COM Interop might still work)
        // InitializeGlobalKeyboardHook()
        // GlobalKeyboardHook_MediaKeyPressed()
        // SendMediaCommand() - Depends on WebInterface implementation (WinForms or WPF WebView)
        // WndProc equivalent for messages (HwndSource) if needed
        // CheckAndShowPinningHint()

        // [DllImport("shell32.dll", SetLastError = true)]
         // static extern void SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string AppId);


        // --- Notify Icon Methods (Using WinForms NotifyIcon) ---

        private void InitializeNotifyIcon()
        {
            // Use the application icon (which is embedded in the executable)
            string? exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(exePath))
            {
                 System.Windows.MessageBox.Show("Could not find application executable path for icon.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                 return;
            }

            notifyIcon = new WinForms.NotifyIcon
            {
                Icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath),
                Visible = true,
                Text = "ScreamRouter Desktop"
            };

            notifyIcon.MouseClick += NotifyIcon_MouseClick;

            // Create Context Menu (WinForms)
            WinForms.ContextMenuStrip contextMenu = new WinForms.ContextMenuStrip();
            contextMenu.Items.Add("Open Web Interface", null, (sender, e) => { OpenWebInterface(); });
            contextMenu.Items.Add("Settings", null, (sender, e) => { ShowSettings(); });
            // contextMenu.Items.Add("Play/Pause", null, (sender, e) => { SendMediaCommand("playPause"); }); // TODO: Re-implement SendMediaCommand
            // contextMenu.Items.Add("Next Track", null, (sender, e) => { SendMediaCommand("nextTrack"); }); // TODO: Re-implement SendMediaCommand
            // contextMenu.Items.Add("Previous Track", null, (sender, e) => { SendMediaCommand("previousTrack"); }); // TODO: Re-implement SendMediaCommand
            contextMenu.Items.Add("-"); // Separator
            contextMenu.Items.Add("Pin to Notification Area", null, (sender, e) => { NotificationAreaPinning.ShowPinInstructionsDialog(); });
            contextMenu.Items.Add("-"); // Separator
            contextMenu.Items.Add("Exit", null, (sender, e) => { System.Windows.Application.Current.Shutdown(); }); // Use WPF Application Shutdown

            notifyIcon.ContextMenuStrip = contextMenu;
        }

        private void NotifyIcon_MouseClick(object? sender, WinForms.MouseEventArgs e) // Use WinForms MouseEventArgs
        {
            if (e.Button == WinForms.MouseButtons.Left) // Use WinForms MouseButtons
            {
                ToggleWebInterface();
            }
        }

        public void ToggleWebInterface()
        {
            string url = (FindName("UrlTextBox") as System.Windows.Controls.TextBox)?.Text ?? "";
            if (!string.IsNullOrEmpty(url))
            {
                if (webInterfaceForm == null /*|| webInterfaceForm.IsDisposed*/)
                {
                    try
                    {
                        webInterfaceForm = new WebInterfaceForm(url);
                        // Show and immediately hide to initialize if needed, or just show when toggled
                        webInterfaceForm.Show();
                        webInterfaceForm.Hide();
                    }
                    catch (Exception ex)
                    {
                        System.Windows.MessageBox.Show($"Error creating web interface: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }

                if (webInterfaceForm != null) // Check again after potential creation
                {
                    try
                    {
                        if (webInterfaceForm.Visible)
                            webInterfaceForm.Hide();
                        else
                        {
                            webInterfaceForm.Show();
                            webInterfaceForm.Activate();
                        }
                    }
                    catch (ObjectDisposedException) // Handle if form was disposed unexpectedly
                    {
                         webInterfaceForm = null; // Reset
                         ToggleWebInterface(); // Retry
                    }
                     catch (Exception ex)
                    {
                        System.Windows.MessageBox.Show($"Error toggling web interface: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            else
            {
                System.Windows.MessageBox.Show("Please enter a valid URL for ScreamRouter.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

     } // End of MainWindow class

     // --- Converters ---

    public class BooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool flag = false;
            if (value is bool b)
            {
                flag = b;
            }
            else if (value is bool?)
            {
                bool? nullable = (bool?)value;
                flag = nullable.HasValue ? nullable.Value : false;
            }

            // Invert visibility if parameter is "true" or "invert"
            bool invert = parameter?.ToString().ToLowerInvariant() == "true" || parameter?.ToString().ToLowerInvariant() == "invert";
            if (invert) flag = !flag;

            return flag ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}

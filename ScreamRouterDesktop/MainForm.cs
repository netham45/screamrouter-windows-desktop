using System;
using System.IO;
using System.Windows.Forms;
using System.Net;
using System.Drawing;
using System.Linq;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Net.Sockets;

namespace ScreamRouterDesktop
{
    public partial class MainForm : Form
    {
        private NotifyIcon? notifyIcon;
        private TextBox? urlTextBox;
        private TextBox? ipPortTextBox;
        private Button? saveButton;
        private ScreamSettings screamSettings;
        private Button? openWebInterfaceButton;
        private Button? pinToStartButton;
        private Button? pinToNotificationAreaButton;
        private ComboBox? updateModeComboBox;
        private CheckBox? startAtBootCheckBox;
        private WebInterfaceForm? webInterfaceForm;
        private GlobalKeyboardHook? globalKeyboardHook;
        private UpdateManager updateManager;

        // Controls for Scream settings
        private RadioButton? standardSenderRadioButton;
        private RadioButton? perProcessSenderRadioButton;
        private TextBox? senderIpTextBox;
        private NumericUpDown? senderPortNumeric;
        private CheckBox? multicastCheckBox;
        private TextBox? perProcessSenderIpTextBox;
        private NumericUpDown? perProcessSenderPortNumeric;
        private Label? perProcessSenderCompatibilityLabel;
        private CheckBox? receiverEnabledCheckBox;
        private NumericUpDown? receiverPortNumeric;

        // Controls for Audio Info
        private Label? bitDepthLabel;
        private Label? sampleRateLabel;
        // Method to check if the system meets the minimum requirements for per-process sender
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


        private Label? channelsLabel;
        private Label? channelLayoutLabel;

        [DllImport("shell32.dll", SetLastError = true)]
        static extern void SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string AppID);

        const int WM_SHOWWEBINTERFACE = 0x0400 + 1;
        const int WM_SHOWSETTINGS = 0x0400 + 2;

        public MainForm()
        {
            InitializeComponent();
            updateManager = new UpdateManager();
            screamSettings = new ScreamSettings();
            InitializeCustomComponents();

            // Check if start at boot option has been prompted before
            if (!screamSettings.HasStartAtBootBeenPrompted())
            {
                screamSettings.ShowStartAtBootDialog();
            }

            // Load configuration after the start at boot dialog to ensure UI reflects the latest settings
            LoadConfiguration();


            InitializeGlobalKeyboardHook();
            SetCurrentProcessExplicitAppUserModelID("ScreamRouterDesktop");
            CreateJumpList();

            // Check if this is the first run and notification area pinning hint should be shown
            bool pinningHintShown = Registry.GetValue(@"HKEY_CURRENT_USER\Software\ScreamRouterDesktop", "PinningHintShown", "false")?.ToString() == "true";
            if (!pinningHintShown)
            {
                // Show pinning instructions after a short delay to ensure the icon is visible
                System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
                timer.Interval = 2000; // 2 seconds
                timer.Tick += (s, e) =>
                {
                    if (notifyIcon != null)
                    {
                        NotificationAreaPinning.ShowPinInstructions(notifyIcon);
                        Registry.SetValue(@"HKEY_CURRENT_USER\Software\ScreamRouterDesktop", "PinningHintShown", "true");
                    }
                    timer.Stop();
                    timer.Dispose();
                };
                timer.Start();
            }

            // Start the form hidden
            this.WindowState = FormWindowState.Minimized;
            this.ShowInTaskbar = false;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.Hide();
        }

        private void InitializeCustomComponents()
        {
            this.Text = "ScreamRouter Desktop Configuration";

            // Use DPI-aware sizing
            float scaleFactor = this.DeviceDpi / 96f;
            // If scaleFactor is less than 1 (e.g., lower DPI), clamp it to 1 to avoid making things too small.
            if (scaleFactor < 1.0f) scaleFactor = 1.0f;

            int baseWidth = 525;
            // Increased height slightly to better accommodate content without AutoSize
            int baseHeight = 700;
            int padding = (int)(10 * scaleFactor);
            int controlSpacing = (int)(5 * scaleFactor);
            int sectionSpacing = (int)(15 * scaleFactor);

            // Set AutoScaleMode for proper DPI scaling behavior
            this.AutoScaleMode = AutoScaleMode.Dpi;

            // Set scaled font for the form (controls should inherit)
            this.Font = new Font(this.Font.FontFamily, 8.25f, this.Font.Style); // Base font size, AutoScaleMode handles scaling

            // Disable AutoSize for the form, use fixed size based on scaling
            this.AutoSize = false;
            this.ClientSize = new Size((int)(baseWidth * scaleFactor), (int)(baseHeight * scaleFactor)); // Set fixed initial size


            // Main layout panel
            TableLayoutPanel mainPanel = new TableLayoutPanel
            {
                Padding = new Padding(padding),
                ColumnCount = 1,
                RowCount = 2, // Row for TabControl, Row for Buttons
                AutoSize = false, // Panel should fill the form
                Dock = DockStyle.Fill // Ensure main panel fills the form
            };
            mainPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            // Let TabControl row expand, button row size to content
            mainPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F)); // Tab control row fills available space
            mainPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // Button row sizes itself

            // Tab Control
            TabControl settingsTabControl = new TabControl
            {
                Dock = DockStyle.Fill, // Fill the top row of mainPanel
                Margin = new Padding(0, 0, 0, sectionSpacing),
                AutoSize = false // Do not AutoSize the TabControl itself
            };
            // Removed event handler subscription

            // --- Tab Page 1: ScreamRouter Configuration ---
            TabPage urlTabPage = new TabPage("ScreamRouter");
            settingsTabControl.TabPages.Add(urlTabPage);

            FlowLayoutPanel urlPanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                AutoSize = true, // Let panel size to content
                WrapContents = false,
                Dock = DockStyle.Fill, // Fill the tab page
                Padding = new Padding(padding)
            };

            Label urlLabel = new Label
            {
                Text = "Server URL:",
                AutoSize = true,
                Margin = new Padding(0, 0, 0, controlSpacing)
            };
            urlPanel.Controls.Add(urlLabel);

            urlTextBox = new TextBox
            {
                Width = (int)(450 * scaleFactor), // Keep explicit width scaling
                Margin = new Padding(0, 0, 0, controlSpacing)
            };
            urlPanel.Controls.Add(urlTextBox);

            urlTabPage.Controls.Add(urlPanel);

            // --- Tab Page 2: Scream Audio Settings ---
            TabPage screamTabPage = new TabPage("Audio Transport");
            settingsTabControl.TabPages.Add(screamTabPage);

            FlowLayoutPanel screamPanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                AutoSize = true, // Let panel size to content
                WrapContents = false,
                Dock = DockStyle.Fill, // Fill the tab page
                Padding = new Padding(padding)
            };

            // Sender Settings
            Label senderModeLabel = new Label
            {
                Text = "Sender Mode:",
                AutoSize = true,
                Margin = new Padding(0, 0, 0, controlSpacing)
            };
            screamPanel.Controls.Add(senderModeLabel);

            // Standard Sender Radio Button
            standardSenderRadioButton = new RadioButton
            {
                Name = "standardSenderRadioButton",
                Text = "Standard Sender",
                AutoSize = true,
                Margin = new Padding(0, 0, 0, controlSpacing)
            };
            standardSenderRadioButton.CheckedChanged += StandardSenderRadioButton_CheckedChanged;
            screamPanel.Controls.Add(standardSenderRadioButton);

            // --- Standard Sender IP/Port Panel ---
            FlowLayoutPanel standardSenderDetailsPanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                WrapContents = false,
                Margin = new Padding(padding, 0, 0, controlSpacing) // Indent
            };
            Label senderIpLabel = new Label { Text = "Destination IP:", AutoSize = true, Margin = new Padding(0, controlSpacing / 2, controlSpacing, 0) };
            senderIpTextBox = new TextBox { Name = "senderIpTextBox", Width = (int)(150 * scaleFactor), Margin = new Padding(0, 0, padding, 0) };
            Label senderPortLabel = new Label { Text = "Port:", AutoSize = true, Margin = new Padding(0, controlSpacing / 2, controlSpacing, 0) };
            senderPortNumeric = new NumericUpDown { Name = "senderPortNumeric", Minimum = 1, Maximum = 65535, Value = 16401, Width = (int)(70 * scaleFactor) };
            standardSenderDetailsPanel.Controls.AddRange(new Control[] { senderIpLabel, senderIpTextBox, senderPortLabel, senderPortNumeric });
            screamPanel.Controls.Add(standardSenderDetailsPanel);

            // --- Standard Sender Multicast Checkbox ---
            multicastCheckBox = new CheckBox { Name = "multicastCheckBox", Text = "Use Multicast", AutoSize = true, Margin = new Padding(padding, 0, 0, padding) };
            screamPanel.Controls.Add(multicastCheckBox);


            // --- Per-Process Sender Radio Button ---
            perProcessSenderRadioButton = new RadioButton
            {
                Name = "perProcessSenderRadioButton",
                Text = "Per-Process Sender (Only works with ScreamRouter)",
                AutoSize = true,
                Margin = new Padding(0, 0, 0, controlSpacing)
            };
            perProcessSenderRadioButton.CheckedChanged += PerProcessSenderRadioButton_CheckedChanged;
            screamPanel.Controls.Add(perProcessSenderRadioButton);

            // Check if system is compatible with per-process sender
            bool isCompatible = IsCompatibleWithPerProcessSender();
            perProcessSenderRadioButton.Enabled = isCompatible;

            // --- Per-Process Sender IP/Port Panel ---
            FlowLayoutPanel perProcessSenderDetailsPanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                WrapContents = false,
                Margin = new Padding(padding, 0, 0, controlSpacing) // Indent
            };
            Label perProcessIpLabel = new Label { Text = "Destination IP:", AutoSize = true, Margin = new Padding(0, controlSpacing / 2, controlSpacing, 0) };
            perProcessSenderIpTextBox = new TextBox { Name = "perProcessSenderIpTextBox", Width = (int)(150 * scaleFactor), Margin = new Padding(0, 0, padding, 0), Text = "", Enabled = isCompatible };
            Label perProcessPortLabel = new Label { Text = "Port:", AutoSize = true, Margin = new Padding(0, controlSpacing / 2, controlSpacing, 0) };
            perProcessSenderPortNumeric = new NumericUpDown { Name = "perProcessSenderPortNumeric", Minimum = 1, Maximum = 65535, Value = 16402, Width = (int)(70 * scaleFactor), Enabled = isCompatible };
            perProcessSenderDetailsPanel.Controls.AddRange(new Control[] { perProcessIpLabel, perProcessSenderIpTextBox, perProcessPortLabel, perProcessSenderPortNumeric });
            screamPanel.Controls.Add(perProcessSenderDetailsPanel);

            // Show compatibility warning if needed
            if (!isCompatible)
            {
                perProcessSenderCompatibilityLabel = new Label
                {
                    Text = "Requires Windows 10 build 20348+ or Windows 11",
                    AutoSize = true,
                    ForeColor = Color.Red,
                    Margin = new Padding(padding, 0, 0, padding)
                };
                screamPanel.Controls.Add(perProcessSenderCompatibilityLabel);
            }
            else
            {
                screamPanel.Controls.Add(new Panel { Height = padding, AutoSize = false }); // Spacer
            }


            // --- Receiver Settings ---
            receiverEnabledCheckBox = new CheckBox
            {
                Name = "receiverEnabledCheckBox",
                Text = "Enable Scream Receiver",
                AutoSize = true,
                Margin = new Padding(0, padding, 0, controlSpacing)
            };
            screamPanel.Controls.Add(receiverEnabledCheckBox);

            // --- Receiver Port Panel ---
            FlowLayoutPanel receiverPortPanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                WrapContents = false,
                Margin = new Padding(padding, 0, 0, 0) // Indent
            };
            Label receiverPortLabel = new Label { Text = "Inbound Port:", AutoSize = true, Margin = new Padding(0, controlSpacing / 2, controlSpacing, 0) };
            receiverPortNumeric = new NumericUpDown { Name = "receiverPortNumeric", Minimum = 1, Maximum = 65535, Value = 4010, Width = (int)(70 * scaleFactor) };
            receiverPortPanel.Controls.AddRange(new Control[] { receiverPortLabel, receiverPortNumeric });
            screamPanel.Controls.Add(receiverPortPanel);


            screamTabPage.Controls.Add(screamPanel);

            // --- Tab Page 3: Audio Configuration Info ---
            TabPage audioInfoTabPage = new TabPage("Audio Info");
            settingsTabControl.TabPages.Add(audioInfoTabPage);

            FlowLayoutPanel audioInfoPanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                AutoSize = true, // Let panel size to content
                WrapContents = false,
                Dock = DockStyle.Fill, // Fill the tab page
                Padding = new Padding(padding)
            };

            Label audioInfoLabel = new Label
            {
                Text = "The following audio settings will be advertised via mDNS when the receiver is enabled:",
                AutoSize = true,
                Margin = new Padding(0, 0, 0, controlSpacing)
            };
            audioInfoPanel.Controls.Add(audioInfoLabel);

            // Create labels for each audio setting
            bitDepthLabel = new Label
            {
                Name = "bitDepthLabel",
                Text = "Bit Depth: -- (Windows returns audio in 32-bit regardless of config)",
                AutoSize = true,
                Margin = new Padding(0, 0, 0, controlSpacing)
            };
            audioInfoPanel.Controls.Add(bitDepthLabel);

            sampleRateLabel = new Label
            {
                Name = "sampleRateLabel",
                Text = "Sample Rate: --",
                AutoSize = true,
                Margin = new Padding(0, 0, 0, controlSpacing)
            };
            audioInfoPanel.Controls.Add(sampleRateLabel);

            channelsLabel = new Label
            {
                Name = "channelsLabel",
                Text = "Channels: --",
                AutoSize = true,
                Margin = new Padding(0, 0, 0, controlSpacing)
            };
            audioInfoPanel.Controls.Add(channelsLabel);

            channelLayoutLabel = new Label
            {
                Name = "channelLayoutLabel",
                Text = "Channel Layout: --",
                AutoSize = true,
                Margin = new Padding(0, 0, 0, controlSpacing)
            };
            audioInfoPanel.Controls.Add(channelLayoutLabel);

            audioInfoTabPage.Controls.Add(audioInfoPanel);

            // --- Tab Page 4: Application Settings ---
            TabPage appSettingsTabPage = new TabPage("Application");
            settingsTabControl.TabPages.Add(appSettingsTabPage);

            FlowLayoutPanel appSettingsPanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                AutoSize = true, // Let panel size to content
                WrapContents = false,
                Dock = DockStyle.Fill, // Fill the tab page
                Padding = new Padding(padding)
            };

            Label updateLabel = new Label
            {
                Text = "Update Mode:",
                AutoSize = true,
                Margin = new Padding(0, 0, 0, controlSpacing)
            };
            appSettingsPanel.Controls.Add(updateLabel);

            updateModeComboBox = new ComboBox
            {
                Width = (int)(450 * scaleFactor), // Keep explicit width scaling
                DropDownStyle = ComboBoxStyle.DropDownList,
                Margin = new Padding(0, 0, 0, padding)
            };
            updateModeComboBox.Items.AddRange(new string[] {
                "Do not check for updates",
                "Notify me when updates are available",
                "Automatically install updates"
            });
            updateModeComboBox.SelectedIndex = (int)updateManager.CurrentMode;
            updateModeComboBox.SelectedIndexChanged += UpdateModeComboBox_SelectedIndexChanged;
            appSettingsPanel.Controls.Add(updateModeComboBox);

            // Add start at boot checkbox
            startAtBootCheckBox = new CheckBox
            {
                Text = "Start application when Windows starts",
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 0)
            };
            startAtBootCheckBox.CheckedChanged += StartAtBootCheckBox_CheckedChanged;
            appSettingsPanel.Controls.Add(startAtBootCheckBox);

            appSettingsTabPage.Controls.Add(appSettingsPanel);

            // Add TabControl to the main panel
            mainPanel.Controls.Add(settingsTabControl, 0, 0);

            // Buttons Panel (placed below the TabControl)
            FlowLayoutPanel buttonsPanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true, // Let panel size itself
                WrapContents = false,
                Dock = DockStyle.Bottom, // Dock to bottom
                Padding = new Padding(0, padding, 0, 0)
            };

            // Buttons should scale automatically with AutoScaleMode=Dpi and inherited font
            saveButton = new Button
            {
                Text = "Save Configuration",
                AutoSize = true,
                Margin = new Padding(0, 0, padding, 0)
            };
            saveButton.Click += SaveButton_Click;
            buttonsPanel.Controls.Add(saveButton);

            openWebInterfaceButton = new Button
            {
                Text = "Open Web Interface",
                AutoSize = true,
                Margin = new Padding(0, 0, padding, 0)
            };
            openWebInterfaceButton.Click += OpenWebInterfaceButton_Click;
            buttonsPanel.Controls.Add(openWebInterfaceButton);

            pinToNotificationAreaButton = new Button
            {
                Text = "Pin to Notification Area",
                AutoSize = true
            };
            pinToNotificationAreaButton.Click += PinToNotificationAreaButton_Click;
            buttonsPanel.Controls.Add(pinToNotificationAreaButton);

            mainPanel.Controls.Add(buttonsPanel, 0, 1); // Add buttons to the second row

            this.Controls.Add(mainPanel);
            InitializeNotifyIcon();
        }

        // Removed SettingsTabControl_SelectedIndexChanged handler

        private void UpdateAudioInfo()
        {
            // Get the current audio settings from ZeroconfService
            var audioSettings = screamSettings?.GetCurrentAudioSettings();

            if (audioSettings != null)
            {
                if (bitDepthLabel != null)
                    bitDepthLabel.Text = $"Bit Depth: {audioSettings.BitDepth} bits";

                if (sampleRateLabel != null)
                    sampleRateLabel.Text = $"Sample Rate: {audioSettings.SampleRate} Hz";

                if (channelsLabel != null)
                    channelsLabel.Text = $"Channels: {audioSettings.Channels}";

                if (channelLayoutLabel != null)
                    channelLayoutLabel.Text = $"Channel Layout: {audioSettings.ChannelLayout}";
            }
            else
            {
                // No audio settings available
                if (bitDepthLabel != null)
                    bitDepthLabel.Text = "Bit Depth: --";

                if (sampleRateLabel != null)
                    sampleRateLabel.Text = "Sample Rate: --";

                if (channelsLabel != null)
                    channelsLabel.Text = "Channels: --";

                if (channelLayoutLabel != null)
                    channelLayoutLabel.Text = "Channel Layout: --";
            }
        }

        private void InitializeNotifyIcon()
        {
            // Use the application icon (which is embedded in the executable)
            notifyIcon = new NotifyIcon
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath),
                Visible = true,
                Text = "ScreamRouter Desktop"
            };

            notifyIcon.MouseClick += NotifyIcon_MouseClick;

            ContextMenuStrip contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("Open Web Interface", null, (sender, e) => { OpenWebInterface(); });
            contextMenu.Items.Add("Settings", null, (sender, e) => { ShowSettings(); });
            contextMenu.Items.Add("Play/Pause", null, (sender, e) => { SendMediaCommand("playPause"); });
            contextMenu.Items.Add("Next Track", null, (sender, e) => { SendMediaCommand("nextTrack"); });
            contextMenu.Items.Add("Previous Track", null, (sender, e) => { SendMediaCommand("previousTrack"); });
            contextMenu.Items.Add("-"); // Separator
            contextMenu.Items.Add("Pin to Notification Area", null, (sender, e) => { NotificationAreaPinning.ShowPinInstructionsDialog(); });
            contextMenu.Items.Add("-"); // Separator
            contextMenu.Items.Add("Exit", null, (sender, e) => { Application.Exit(); });

            notifyIcon.ContextMenuStrip = contextMenu;
        }

        private void NotifyIcon_MouseClick(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                ToggleWebInterface();
            }
        }

        private void CreateJumpList()
        {
            ICustomDestinationList jumpList = (ICustomDestinationList)new CustomDestinationList();
            jumpList.SetAppID("ScreamRouterDesktop");

            uint maxSlots;
            IObjectArray removedItems;
            jumpList.BeginList(out maxSlots, typeof(IObjectArray).GUID, out removedItems);

            IObjectCollection tasks = (IObjectCollection)new ObjectCollection();

            // Add "Open Web Interface" task
            IShellLink link = (IShellLink)new ShellLink();
            link.SetPath(Application.ExecutablePath);
            link.SetArguments("-openwebinterface");
            link.SetDescription("Open the ScreamRouter Web Interface");
            ((IPropertyStore)link).SetValue(PKEY_Title, new PropVariant("Open Web Interface"));
            tasks.AddObject(link);

            // Add "Settings" task
            link = (IShellLink)new ShellLink();
            link.SetPath(Application.ExecutablePath);
            link.SetArguments("-settings");
            link.SetDescription("Open ScreamRouter Settings");
            ((IPropertyStore)link).SetValue(PKEY_Title, new PropVariant("Settings"));
            tasks.AddObject(link);

            jumpList.AddUserTasks((IObjectArray)tasks);
            jumpList.CommitList();
        }

        private void SaveButton_Click(object? sender, EventArgs e)
        {
            SaveConfiguration();
            MessageBox.Show("Configuration saved successfully!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void OpenWebInterfaceButton_Click(object? sender, EventArgs e)
        {
            OpenWebInterface();
        }

        public void OpenWebInterface()
        {
            string url = urlTextBox?.Text ?? "";
            if (!string.IsNullOrEmpty(url))
            {
                if (webInterfaceForm == null || webInterfaceForm.IsDisposed)
                {
                    webInterfaceForm = new WebInterfaceForm(url);
                }
                webInterfaceForm.Show();
            }
            else
            {
                MessageBox.Show("Please enter a valid URL for ScreamRouter.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public void ShowSettings()
        {
            UpdateAudioInfo();
            this.Show();
            this.WindowState = FormWindowState.Normal;
            this.ShowInTaskbar = true;
            this.BringToFront();
        }

        private void SendMediaCommand(string command)
        {
            if (webInterfaceForm != null && !webInterfaceForm.IsDisposed)
            {
                webInterfaceForm.InvokeScript(command);
            }
        }

        private void PinToStartButton_Click(object? sender, EventArgs e)
        {
            StartMenuPinning.PinToStartMenu();
        }

        private void PinToNotificationAreaButton_Click(object? sender, EventArgs e)
        {
            NotificationAreaPinning.ShowPinInstructionsDialog();
        }

        public void ToggleWebInterface()
        {
            string url = urlTextBox?.Text ?? "";
            if (!string.IsNullOrEmpty(url))
            {
                if (webInterfaceForm == null || webInterfaceForm.IsDisposed)
                {
                    webInterfaceForm = new WebInterfaceForm(url);
                    webInterfaceForm.Show();
                    webInterfaceForm.Hide();
                }

                if (webInterfaceForm.Visible)
                    webInterfaceForm.Hide();
                else
                    webInterfaceForm.Show();
            }
            else
            {
                MessageBox.Show("Please enter a valid URL for ScreamRouter.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void UpdateModeComboBox_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (updateModeComboBox != null)
            {
                updateManager.CurrentMode = (UpdateMode)updateModeComboBox.SelectedIndex;
            }
        }

        private void StartAtBootCheckBox_CheckedChanged(object? sender, EventArgs e)
        {
            if (startAtBootCheckBox != null)
            {
                screamSettings.StartAtBoot = startAtBootCheckBox.Checked;
            }
        }

        private void SaveConfiguration()
        {
            // Save URL to ScreamSettings instead of directly to registry
            if (urlTextBox != null)
            {
                screamSettings.WebInterfaceUrl = urlTextBox.Text;
            }

            Registry.SetValue(@"HKEY_CURRENT_USER\Software\ScreamRouterDesktop", "IpPort", ipPortTextBox?.Text ?? "");
            if (updateModeComboBox != null)
            {
                updateManager.CurrentMode = (UpdateMode)updateModeComboBox.SelectedIndex;
            }

            // Save Scream settings
            screamSettings.SenderEnabled = standardSenderRadioButton?.Checked ?? false;
            screamSettings.SenderIP = senderIpTextBox?.Text ?? "";
            screamSettings.SenderPort = (int)(senderPortNumeric?.Value ?? 16401);
            screamSettings.SenderMulticast = multicastCheckBox?.Checked ?? false;

            // Save Per-Process Sender settings
            screamSettings.PerProcessSenderEnabled = perProcessSenderRadioButton?.Checked ?? false;
            screamSettings.PerProcessSenderIP = perProcessSenderIpTextBox?.Text ?? "";
            screamSettings.PerProcessSenderPort = (int)(perProcessSenderPortNumeric?.Value ?? 16402);

            screamSettings.ReceiverEnabled = receiverEnabledCheckBox?.Checked ?? false;
            screamSettings.ReceiverPort = (int)(receiverPortNumeric?.Value ?? 4010);
            if (startAtBootCheckBox != null)
            {
                screamSettings.StartAtBoot = startAtBootCheckBox.Checked;
            }

            screamSettings.Save();
            screamSettings.RestartProcesses();

            // Update audio info display
            UpdateAudioInfo();
        }

        private void StandardSenderRadioButton_CheckedChanged(object? sender, EventArgs e)
        {
            if (standardSenderRadioButton?.Checked == true)
            {
                // Enable standard sender controls
                if (senderIpTextBox != null) senderIpTextBox.Enabled = true;
                if (senderPortNumeric != null) senderPortNumeric.Enabled = true;
                if (multicastCheckBox != null) multicastCheckBox.Enabled = true;

                // Disable per-process sender controls
                if (perProcessSenderIpTextBox != null) perProcessSenderIpTextBox.Enabled = false;
                if (perProcessSenderPortNumeric != null) perProcessSenderPortNumeric.Enabled = false;
            }
        }

        private void PerProcessSenderRadioButton_CheckedChanged(object? sender, EventArgs e)
        {
            if (perProcessSenderRadioButton?.Checked == true && IsCompatibleWithPerProcessSender())
            {
                // Disable standard sender controls
                if (senderIpTextBox != null) senderIpTextBox.Enabled = false;
                if (senderPortNumeric != null) senderPortNumeric.Enabled = false;
                if (multicastCheckBox != null) multicastCheckBox.Enabled = false;

                // Enable per-process sender controls
                if (perProcessSenderIpTextBox != null) perProcessSenderIpTextBox.Enabled = true;
                if (perProcessSenderPortNumeric != null) perProcessSenderPortNumeric.Enabled = true;
            }
        }


        private void LoadConfiguration()
        {

            screamSettings.Load();
            // Load URL from ScreamSettings instead of directly from registry
            if (urlTextBox != null)
                urlTextBox.Text = screamSettings.WebInterfaceUrl;

            if (ipPortTextBox != null)
                ipPortTextBox.Text = (string?)Registry.GetValue(@"HKEY_CURRENT_USER\Software\ScreamRouterDesktop", "IpPort", "") ?? "";

            if (string.IsNullOrEmpty(ipPortTextBox?.Text))
                ResolveHostname();

            // Load standard sender settings
            if (standardSenderRadioButton != null)
                standardSenderRadioButton.Checked = screamSettings.SenderEnabled;
            if (senderIpTextBox != null)
            {
                senderIpTextBox.Text = screamSettings.SenderIP;
                senderIpTextBox.Enabled = screamSettings.SenderEnabled;
            }
            if (senderPortNumeric != null)
            {
                senderPortNumeric.Value = screamSettings.SenderPort;
                senderPortNumeric.Enabled = screamSettings.SenderEnabled;
            }
            if (multicastCheckBox != null)
            {
                multicastCheckBox.Checked = screamSettings.SenderMulticast;
                multicastCheckBox.Enabled = screamSettings.SenderEnabled;
            }

            // Load per-process sender settings
            bool isCompatible = IsCompatibleWithPerProcessSender();
            if (perProcessSenderRadioButton != null)
            {
                perProcessSenderRadioButton.Checked = screamSettings.PerProcessSenderEnabled;
                perProcessSenderRadioButton.Enabled = isCompatible;
            }
            if (perProcessSenderIpTextBox != null)
            {
                perProcessSenderIpTextBox.Text = screamSettings.PerProcessSenderIP;
                perProcessSenderIpTextBox.Enabled = screamSettings.PerProcessSenderEnabled && isCompatible;
            }
            if (perProcessSenderPortNumeric != null)
            {
                perProcessSenderPortNumeric.Value = screamSettings.PerProcessSenderPort;
                perProcessSenderPortNumeric.Enabled = screamSettings.PerProcessSenderEnabled && isCompatible;
            }

            // Load receiver settings
            if (receiverEnabledCheckBox != null)
                receiverEnabledCheckBox.Checked = screamSettings.ReceiverEnabled;
            if (receiverPortNumeric != null)
                receiverPortNumeric.Value = screamSettings.ReceiverPort;

            // Update the start at boot checkbox based on the actual Windows startup registry
            if (startAtBootCheckBox != null)
                startAtBootCheckBox.Checked = screamSettings.StartAtBoot;

            // Start processes if enabled
            screamSettings.StartProcesses();

            // Update audio info display
            UpdateAudioInfo();

            // Create the WebInterfaceForm on load
            string url = urlTextBox?.Text ?? "";
            if (!string.IsNullOrEmpty(url))
            {
                webInterfaceForm = new WebInterfaceForm(url);
                // Initially hide it
                webInterfaceForm.Hide();
            }
        }

        private void ResolveHostname()
        {
            try
            {
                string hostName = Dns.GetHostName();
                IPAddress[] addresses = Dns.GetHostAddresses(hostName);
                foreach (IPAddress address in addresses)
                {
                    if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        if (ipPortTextBox != null)
                            ipPortTextBox.Text = $"{address}:0000"; // Replace 0000 with the actual port number if known
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error resolving hostname: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void InitializeGlobalKeyboardHook()
        {
            globalKeyboardHook = new GlobalKeyboardHook();
            globalKeyboardHook.MediaKeyPressed += GlobalKeyboardHook_MediaKeyPressed;
        }


        private void GlobalKeyboardHook_MediaKeyPressed(object? sender, MediaKeyEventArgs e)
        {
            if (webInterfaceForm != null && !webInterfaceForm.IsDisposed)
            {
                string functionName = "";
                switch (e.KeyType)
                {
                    case MediaKeyType.PlayPause:
                        functionName = "playPause";
                        break;
                    case MediaKeyType.NextTrack:
                        functionName = "nextTrack";
                        break;
                    case MediaKeyType.PreviousTrack:
                        functionName = "previousTrack";
                        break;
                }

                if (!string.IsNullOrEmpty(functionName))
                {
                    webInterfaceForm.InvokeScript(functionName);
                }
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_SHOWWEBINTERFACE)
            {
                OpenWebInterface();
            }
            else if (m.Msg == WM_SHOWSETTINGS)
            {
                ShowSettings();
            }
            base.WndProc(ref m);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                this.WindowState = FormWindowState.Minimized;
                this.ShowInTaskbar = false;
                this.Hide();
            }
            else
            {
                // No need to clean up ZeroconfService here as it's managed by ScreamSettings
                base.OnFormClosing(e);
            }
        }

        // COM interfaces and classes for Jump List
        [ComImport, Guid("6332DEBF-87B5-4670-90C0-5E57B408A49E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ICustomDestinationList
        {
            void SetAppID([MarshalAs(UnmanagedType.LPWStr)] string pszAppID);
            [PreserveSig]
            int BeginList(out uint pcMaxSlots, ref Guid riid, out IObjectArray ppv);
            [PreserveSig]
            int AppendCategory([MarshalAs(UnmanagedType.LPWStr)] string pszCategory, IObjectArray poa);
            void AppendKnownCategory(KnownDestinationCategory category);
            [PreserveSig]
            int AddUserTasks(IObjectArray poa);
            void CommitList();
            void GetRemovedDestinations(ref Guid riid, out IObjectArray ppv);
            void DeleteList([MarshalAs(UnmanagedType.LPWStr)] string pszAppID);
            void AbortList();
        }

        [ComImport, Guid("77F10CF0-3DB5-4966-B520-B7C54FD35ED6"), ClassInterface(ClassInterfaceType.None)]
        private class CustomDestinationList { }

        [ComImport, Guid("92CA9DCD-5622-4BBA-A805-5E9F541BD8C9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IObjectArray
        {
            void GetCount(out uint pcObjects);
            void GetAt(uint uiIndex, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);
        }

        [ComImport, Guid("5632B1A4-E38A-400A-928A-D4CD63230295"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IObjectCollection
        {
            // IObjectArray
            [PreserveSig]
            void GetCount(out uint pcObjects);
            [PreserveSig]
            void GetAt(uint uiIndex, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);

            // IObjectCollection
            void AddObject([MarshalAs(UnmanagedType.Interface)] object pvObject);
            void AddFromArray(IObjectArray poaSource);
            void RemoveObject(uint uiIndex);
            void Clear();
        }

        [ComImport, Guid("2D3468C1-36A7-43B6-AC24-D3F02FD9607A"), ClassInterface(ClassInterfaceType.None)]
        private class ObjectCollection { }

        [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellLink
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, out IntPtr pfd, int fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out short pwHotkey);
            void SetHotkey(short wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
            void Resolve(IntPtr hwnd, int fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }

        [ComImport, Guid("00021401-0000-0000-C000-000000000046"), ClassInterface(ClassInterfaceType.None)]
        private class ShellLink { }

        [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertyStore
        {
            [PreserveSig]
            int GetCount([Out] out uint cProps);
            [PreserveSig]
            int GetAt([In] uint iProp, out PropertyKey pkey);
            [PreserveSig]
            int GetValue([In] ref PropertyKey key, [Out] PropVariant pv);
            [PreserveSig]
            int SetValue([In] ref PropertyKey key, [In] PropVariant pv);
            [PreserveSig]
            int Commit();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PropertyKey
        {
            private Guid fmtid;
            private int pid;
            public PropertyKey(Guid guid, int id) { fmtid = guid; pid = id; }
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct PropVariant
        {
            [FieldOffset(0)] ushort vt;
            [FieldOffset(8)] IntPtr pointerVal;
            [FieldOffset(8)] byte byteVal;
            [FieldOffset(8)] long longVal;
            [FieldOffset(8)] short boolVal;

            public PropVariant(string value) : this() { vt = 31; pointerVal = Marshal.StringToCoTaskMemUni(value); }
            public void Clear() { PropVariantClear(ref this); }
            [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant pvar);
        }

        private static PropertyKey PKEY_Title = new PropertyKey(new Guid("{F29F85E0-4FF9-1068-AB91-08002B27B3D9}"), 2);

        private enum KnownDestinationCategory
        {
            Frequent = 1,
            Recent
        }
    }
} 
using System;
using System.IO;
using System.Windows.Forms;
using System.Net;
using System.Drawing;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

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
        private WebInterfaceForm? webInterfaceForm;
        private GlobalKeyboardHook? globalKeyboardHook;
        private UpdateManager updateManager;

        // Controls for Scream settings
        private CheckBox? senderEnabledCheckBox;
        private TextBox? senderIpTextBox;
        private NumericUpDown? senderPortNumeric;
        private CheckBox? multicastCheckBox;
        private CheckBox? receiverEnabledCheckBox;
        private NumericUpDown? receiverPortNumeric;

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
            this.Hide();
        }

        private void InitializeCustomComponents()
        {
            this.Text = "ScreamRouter Desktop Configuration";
            
            // Use DPI-aware sizing
            float scaleFactor = this.DeviceDpi / 96f;
            int baseWidth = 600;
            int baseHeight = 450; // Increased height for better spacing
            int padding = (int)(20 * scaleFactor);
            int sectionSpacing = (int)(30 * scaleFactor);
            
            this.ClientSize = new Size((int)(baseWidth * scaleFactor), (int)(baseHeight * scaleFactor));

            TableLayoutPanel mainPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(padding),
                ColumnCount = 1,
                RowCount = 4, // Reduced to match our sections
                AutoSize = true
            };
            mainPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            
            // Set consistent row heights
            mainPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // URL Section
            GroupBox urlGroupBox = new GroupBox
            {
                Text = "ScreamRouter Configuration",
                Dock = DockStyle.Fill,
                AutoSize = true,
                Padding = new Padding(padding),
                Margin = new Padding(0, 0, 0, sectionSpacing)
            };

            FlowLayoutPanel urlPanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                AutoSize = true,
                WrapContents = false,
                Dock = DockStyle.Fill
            };

            Label urlLabel = new Label
            {
                Text = "Server URL:",
                AutoSize = true,
                Font = new Font(this.Font.FontFamily, 9, FontStyle.Regular),
                Margin = new Padding(0, 0, 0, padding/2)
            };
            urlPanel.Controls.Add(urlLabel);

            urlTextBox = new TextBox
            {
                Width = (int)(450 * scaleFactor),
                Margin = new Padding(0, 0, 0, padding/2)
            };
            urlPanel.Controls.Add(urlTextBox);

            urlGroupBox.Controls.Add(urlPanel);
            mainPanel.Controls.Add(urlGroupBox, 0, 0);

            // Buttons Panel
            FlowLayoutPanel buttonsPanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                Margin = new Padding(0, 0, 0, sectionSpacing),
                WrapContents = false,
                Dock = DockStyle.Fill
            };

            saveButton = new Button
            {
                Text = "Save Configuration",
                Size = new Size((int)(150 * scaleFactor), (int)(30 * scaleFactor)),
                Margin = new Padding(0, 0, padding, 0)
            };
            saveButton.Click += SaveButton_Click;
            buttonsPanel.Controls.Add(saveButton);

            openWebInterfaceButton = new Button
            {
                Text = "Open Web Interface",
                Size = new Size((int)(150 * scaleFactor), (int)(30 * scaleFactor)),
                Margin = new Padding(0, 0, padding, 0)
            };
            openWebInterfaceButton.Click += OpenWebInterfaceButton_Click;
            buttonsPanel.Controls.Add(openWebInterfaceButton);

            pinToNotificationAreaButton = new Button
            {
                Text = "Pin to Notification Area",
                Size = new Size((int)(150 * scaleFactor), (int)(30 * scaleFactor))
            };
            pinToNotificationAreaButton.Click += PinToNotificationAreaButton_Click;
            buttonsPanel.Controls.Add(pinToNotificationAreaButton);

            mainPanel.Controls.Add(buttonsPanel, 0, 1);

            // Scream Settings Section
            GroupBox screamGroupBox = new GroupBox
            {
                Text = "Scream Audio Settings",
                Dock = DockStyle.Fill,
                AutoSize = true,
                Padding = new Padding(padding),
                Margin = new Padding(0, 0, 0, sectionSpacing)
            };

            FlowLayoutPanel screamPanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                AutoSize = true,
                WrapContents = false,
                Dock = DockStyle.Fill
            };

            // Sender Settings
            senderEnabledCheckBox = new CheckBox
            {
                Name = "senderEnabledCheckBox",
                Text = "Enable Scream Sender",
                AutoSize = true,
                Margin = new Padding(0, 0, 0, padding/2)
            };
            senderEnabledCheckBox.CheckedChanged += (s, e) => {
                screamSettings.SenderEnabled = senderEnabledCheckBox.Checked;
                screamSettings.RestartProcesses();
            };
            screamPanel.Controls.Add(senderEnabledCheckBox);

            Label senderIpLabel = new Label { Text = "Sender IP:", AutoSize = true };
            senderIpTextBox = new TextBox 
            { 
                Name = "senderIpTextBox",
                Width = (int)(200 * scaleFactor), 
                Margin = new Padding(0, 0, padding, padding/2) 
            };
            senderIpTextBox.TextChanged += (s, e) => {
                screamSettings.SenderIP = senderIpTextBox.Text;
                if (screamSettings.SenderEnabled)
                    screamSettings.RestartProcesses();
            };
            FlowLayoutPanel senderIpPanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                WrapContents = false
            };
            senderIpPanel.Controls.AddRange(new Control[] { senderIpLabel, senderIpTextBox });
            screamPanel.Controls.Add(senderIpPanel);

            Label senderPortLabel = new Label { Text = "Sender Port:", AutoSize = true };
            senderPortNumeric = new NumericUpDown 
            { 
                Name = "senderPortNumeric",
                Minimum = 1,
                Maximum = 65535,
                Value = 16401,
                Width = (int)(80 * scaleFactor),
                Margin = new Padding(0, 0, padding, padding/2)
            };
            senderPortNumeric.ValueChanged += (s, e) => {
                screamSettings.SenderPort = (int)senderPortNumeric.Value;
                if (screamSettings.SenderEnabled)
                    screamSettings.RestartProcesses();
            };
            FlowLayoutPanel senderPortPanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                WrapContents = false
            };
            senderPortPanel.Controls.AddRange(new Control[] { senderPortLabel, senderPortNumeric });
            screamPanel.Controls.Add(senderPortPanel);

            multicastCheckBox = new CheckBox
            {
                Name = "multicastCheckBox",
                Text = "Use Multicast",
                AutoSize = true,
                Margin = new Padding(0, 0, 0, padding)
            };
            multicastCheckBox.CheckedChanged += (s, e) => {
                screamSettings.SenderMulticast = multicastCheckBox.Checked;
                if (screamSettings.SenderEnabled)
                    screamSettings.RestartProcesses();
            };
            screamPanel.Controls.Add(multicastCheckBox);

            // Receiver Settings
            receiverEnabledCheckBox = new CheckBox
            {
                Name = "receiverEnabledCheckBox",
                Text = "Enable Scream Receiver",
                AutoSize = true,
                Margin = new Padding(0, 0, 0, padding/2)
            };
            receiverEnabledCheckBox.CheckedChanged += (s, e) => {
                screamSettings.ReceiverEnabled = receiverEnabledCheckBox.Checked;
                screamSettings.RestartProcesses();
            };
            screamPanel.Controls.Add(receiverEnabledCheckBox);

            Label receiverPortLabel = new Label { Text = "Receiver Port:", AutoSize = true };
            receiverPortNumeric = new NumericUpDown
            {
                Name = "receiverPortNumeric",
                Minimum = 1,
                Maximum = 65535,
                Value = 4010,
                Width = (int)(80 * scaleFactor),
                Margin = new Padding(0, 0, padding, padding)
            };
            receiverPortNumeric.ValueChanged += (s, e) => {
                screamSettings.ReceiverPort = (int)receiverPortNumeric.Value;
                if (screamSettings.ReceiverEnabled)
                    screamSettings.RestartProcesses();
            };
            FlowLayoutPanel receiverPortPanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                AutoSize = true,
                WrapContents = false
            };
            receiverPortPanel.Controls.AddRange(new Control[] { receiverPortLabel, receiverPortNumeric });
            screamPanel.Controls.Add(receiverPortPanel);

            screamGroupBox.Controls.Add(screamPanel);
            mainPanel.Controls.Add(screamGroupBox, 0, 2);

            // Update Mode Section
            GroupBox updateGroupBox = new GroupBox
            {
                Text = "Update Settings",
                Dock = DockStyle.Fill,
                AutoSize = true,
                Padding = new Padding(padding),
                Margin = new Padding(0)
            };

            FlowLayoutPanel updatePanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                AutoSize = true,
                WrapContents = false,
                Dock = DockStyle.Fill
            };

            Label updateLabel = new Label
            {
                Text = "Update Mode:",
                AutoSize = true,
                Font = new Font(this.Font.FontFamily, 9, FontStyle.Regular),
                Margin = new Padding(0, 0, 0, padding/2)
            };
            updatePanel.Controls.Add(updateLabel);

            updateModeComboBox = new ComboBox
            {
                Width = (int)(450 * scaleFactor),
                DropDownStyle = ComboBoxStyle.DropDownList,
                Margin = new Padding(0, 0, 0, padding/2)
            };
            updateModeComboBox.Items.AddRange(new string[] {
                "Do not check for updates",
                "Notify me when updates are available",
                "Automatically install updates"
            });
            updateModeComboBox.SelectedIndex = (int)updateManager.CurrentMode;
            updateModeComboBox.SelectedIndexChanged += UpdateModeComboBox_SelectedIndexChanged;
            updatePanel.Controls.Add(updateModeComboBox);

            updateGroupBox.Controls.Add(updatePanel);
            mainPanel.Controls.Add(updateGroupBox, 0, 3);

            this.Controls.Add(mainPanel);
            InitializeNotifyIcon();
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

        private void SaveConfiguration()
        {
            Registry.SetValue(@"HKEY_CURRENT_USER\Software\ScreamRouterDesktop", "Url", urlTextBox?.Text ?? "");
            Registry.SetValue(@"HKEY_CURRENT_USER\Software\ScreamRouterDesktop", "IpPort", ipPortTextBox?.Text ?? "");
            if (updateModeComboBox != null)
            {
                updateManager.CurrentMode = (UpdateMode)updateModeComboBox.SelectedIndex;
            }

            // Save Scream settings
            screamSettings.SenderEnabled = senderEnabledCheckBox?.Checked ?? false;
            screamSettings.SenderIP = senderIpTextBox?.Text ?? "127.0.0.1";
            screamSettings.SenderPort = (int)(senderPortNumeric?.Value ?? 16401);
            screamSettings.SenderMulticast = multicastCheckBox?.Checked ?? false;
            screamSettings.ReceiverEnabled = receiverEnabledCheckBox?.Checked ?? false;
            screamSettings.ReceiverPort = (int)(receiverPortNumeric?.Value ?? 4010);
            
            screamSettings.Save();
            screamSettings.RestartProcesses();
        }

        private void LoadConfiguration()
        {
            if (urlTextBox != null)
                urlTextBox.Text = (string?)Registry.GetValue(@"HKEY_CURRENT_USER\Software\ScreamRouterDesktop", "Url", "") ?? "";
            if (ipPortTextBox != null)
                ipPortTextBox.Text = (string?)Registry.GetValue(@"HKEY_CURRENT_USER\Software\ScreamRouterDesktop", "IpPort", "") ?? "";

            if (string.IsNullOrEmpty(ipPortTextBox?.Text))
                ResolveHostname();

            // Load Scream settings
            screamSettings.Load();
            
            if (senderEnabledCheckBox != null)
                senderEnabledCheckBox.Checked = screamSettings.SenderEnabled;
            if (senderIpTextBox != null)
                senderIpTextBox.Text = screamSettings.SenderIP;
            if (senderPortNumeric != null)
                senderPortNumeric.Value = screamSettings.SenderPort;
            if (multicastCheckBox != null)
                multicastCheckBox.Checked = screamSettings.SenderMulticast;
            if (receiverEnabledCheckBox != null)
                receiverEnabledCheckBox.Checked = screamSettings.ReceiverEnabled;
            if (receiverPortNumeric != null)
                receiverPortNumeric.Value = screamSettings.ReceiverPort;

            // Start processes if enabled
            screamSettings.StartProcesses();
            
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

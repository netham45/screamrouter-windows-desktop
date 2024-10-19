using System;
using System.Windows.Forms;
using System.Net;
using System.Drawing;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace ScreamRouterDesktop
{
    public partial class Form1 : Form
    {
        private NotifyIcon? notifyIcon;
        private TextBox? urlTextBox;
        private TextBox? ipPortTextBox;
        private Button? saveButton;
        private Button? openWebInterfaceButton;
        private Button? pinToStartButton;
        private WebInterfaceForm? webInterfaceForm;
        private GlobalKeyboardHook? globalKeyboardHook;

        [DllImport("shell32.dll", SetLastError = true)]
        static extern void SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string AppID);

        const int WM_SHOWWEBINTERFACE = 0x0400 + 1;
        const int WM_SHOWSETTINGS = 0x0400 + 2;

        public Form1()
        {
            InitializeComponent();
            InitializeCustomComponents();
            LoadConfiguration();
            InitializeGlobalKeyboardHook();
            SetCurrentProcessExplicitAppUserModelID("ScreamRouterDesktop");
            CreateJumpList();

            // Start the form hidden
            this.WindowState = FormWindowState.Minimized;
            this.ShowInTaskbar = false;
            this.Hide();
        }

        private void InitializeCustomComponents()
        {
            this.Text = "ScreamRouter Configuration";
            this.Size = new Size(400, 300);

            Label urlLabel = new Label
            {
                Text = "ScreamRouter URL:",
                Location = new Point(20, 20),
                AutoSize = true
            };
            this.Controls.Add(urlLabel);

            urlTextBox = new TextBox
            {
                Location = new Point(20, 40),
                Size = new Size(340, 20)
            };
            this.Controls.Add(urlTextBox);

            /*Label ipPortLabel = new Label
            {
                Text = "ScreamRouter IP:Port:",
                Location = new Point(20, 70),
                AutoSize = true
            };
            this.Controls.Add(ipPortLabel);

            ipPortTextBox = new TextBox
            {
                Location = new Point(20, 90),
                Size = new Size(340, 20)
            };
            this.Controls.Add(ipPortTextBox); */

            saveButton = new Button
            {
                Text = "Save Configuration",
                Location = new Point(20, 130),
                Size = new Size(150, 30)
            };
            saveButton.Click += SaveButton_Click;
            this.Controls.Add(saveButton);

            openWebInterfaceButton = new Button
            {
                Text = "Open Web Interface",
                Location = new Point(180, 130),
                Size = new Size(150, 30)
            };
            openWebInterfaceButton.Click += OpenWebInterfaceButton_Click;
            this.Controls.Add(openWebInterfaceButton);

            InitializeNotifyIcon();
        }

        private void InitializeNotifyIcon()
        {
            notifyIcon = new NotifyIcon
            {
                Icon = SystemIcons.Application,
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
                webInterfaceForm.WindowState = FormWindowState.Maximized;
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

        public void ToggleWebInterface()
        {
            string url = urlTextBox?.Text ?? "";
            if (!string.IsNullOrEmpty(url))
            {
                if (webInterfaceForm == null || webInterfaceForm.IsDisposed)
                {
                    webInterfaceForm = new WebInterfaceForm(url);
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

        private void SaveConfiguration()
        {
            Registry.SetValue(@"HKEY_CURRENT_USER\Software\ScreamRouterDesktop", "Url", urlTextBox?.Text ?? "");
            Registry.SetValue(@"HKEY_CURRENT_USER\Software\ScreamRouterDesktop", "IpPort", ipPortTextBox?.Text ?? "");
        }

        private void LoadConfiguration()
        {
            if (urlTextBox != null)
                urlTextBox.Text = (string?)Registry.GetValue(@"HKEY_CURRENT_USER\Software\ScreamRouterDesktop", "Url", "") ?? "";
            if (ipPortTextBox != null)
                ipPortTextBox.Text = (string?)Registry.GetValue(@"HKEY_CURRENT_USER\Software\ScreamRouterDesktop", "IpPort", "") ?? "";

            if (string.IsNullOrEmpty(ipPortTextBox?.Text))
            {
                ResolveHostname();
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

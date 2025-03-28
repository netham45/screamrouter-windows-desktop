using System;
using System.IO;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Drawing;
using System.Runtime.InteropServices;

namespace ScreamRouterDesktop
{
    public partial class WebInterfaceForm : Form
    {
        private WebView2? webView;
        private bool isWebViewInitialized = false;
        private Panel blurPanel;
        private string url;

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        public WebInterfaceForm(string url)
        {
            this.url = url;
            // Start with opacity 0 to mimic the hide/show behavior that works
            InitializeComponent();
            InitializeWebView();
            PositionFormBottomRight();
            this.Deactivate += WebInterfaceForm_Deactivate;
            this.Shown += WebInterfaceForm_Shown;

            // Workaround for transparency bugging out
            this.Opacity = 0;
            System.Threading.Timer timer = null;
                timer = new System.Threading.Timer((state) => {
                    this.BeginInvoke(new Action(() => {
                        this.Show();
                        System.Threading.Timer timer2 = null;
                        timer2 = new System.Threading.Timer((state) => {
                            this.BeginInvoke(new Action(() => {
                                this.Hide();
                                this.Opacity = 1;
                                timer2?.Dispose();
                            }));
                        }, null, 5, System.Threading.Timeout.Infinite);
                        timer?.Dispose();
                    }));
                }, null, 5, System.Threading.Timeout.Infinite);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00000020; // WS_EX_TRANSPARENT
                return cp;
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Color.Transparent);
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            // 
            // WebInterfaceForm
            // 
            // Scale according to DPI
            float dpiScaling = GetScalingFactor();
            this.ClientSize = new System.Drawing.Size((int)(1010 * dpiScaling), (int)(610 * dpiScaling));
            this.FormBorderStyle = FormBorderStyle.None;
            this.Name = "WebInterfaceForm";
            this.ShowInTaskbar = false;
            this.TopMost = true;
            this.CreateGraphics().Clear(Color.Transparent);
            //this.AutoScaleMode = AutoScaleMode.None;
            this.ResumeLayout(false);
        }

        private async void InitializeWebView()
        {
            webView = new WebView2();
            webView.Dock = DockStyle.Fill;
            this.Controls.Add(webView);
            webView.CreateGraphics().Clear(Color.Transparent);
            webView.BringToFront();

            try
            {
                if (!isWebViewInitialized)
                {
                    // Create environment with no UI and use temporary path for user data
                    var options = new CoreWebView2EnvironmentOptions();
                    // Force the WebView2 to use app mode with no browser UI at all
                    options.AdditionalBrowserArguments = "--app"; 
                    
                    // Create the environment using temp path and initialize the WebView2
                    string userDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScreamRouterDesktop", "WebView2");
                    Directory.CreateDirectory(userDataFolder);
                    CoreWebView2Environment cwv2Environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder, options);
                    await webView.EnsureCoreWebView2Async(cwv2Environment);
                    
                    // Completely disable all browser UI elements
                    /*webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                    webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                    webView.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = false;
                    webView.CoreWebView2.Settings.IsBuiltInErrorPageEnabled = false;
                    webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                    webView.CoreWebView2.Settings.IsZoomControlEnabled = false;*/
                    webView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = true;
                    
                    // Create custom context to block all UI elements
                    webView.CoreWebView2.Profile.PreferredColorScheme = CoreWebView2PreferredColorScheme.Dark;
                    
                    isWebViewInitialized = true;
                }
/*                webView.CoreWebView2.Settings.IsZoomControlEnabled = false;
                webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                webView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = true;
                webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                webView.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = false;
                webView.CoreWebView2.Settings.IsStatusBarEnabled = false;*/
                
                // Create a custom environment to hide the browser UI
                var environment = webView.CoreWebView2.Environment;
                webView.CoreWebView2.Settings.IsBuiltInErrorPageEnabled = false;
                
                // Set the WebView2 size to match the form's client size
                webView.Size = this.ClientSize;
                
                // Set the background of the WebView2 to be transparent
                webView.DefaultBackgroundColor = Color.Transparent;
                
                // Handle new window requests to prevent address bar in popup windows
                webView.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;

                // Navigate directly to the URL in app mode
                webView.CoreWebView2.Navigate(url);
                webView.Visible = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error initializing WebView2: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private float GetScalingFactor()
        {
            Graphics g = CreateGraphics();
            float dpiX = g.DpiX;
            g.Dispose();
            return dpiX / 96f; // 96 DPI is the default (100% scaling)
        }

        private void PositionFormBottomRight()
        {
            Rectangle workingArea = Screen.GetWorkingArea(this);
            this.Location = new System.Drawing.Point(workingArea.Right - Size.Width,
                                           workingArea.Bottom - Size.Height);
        }

        private void WebInterfaceForm_Deactivate(object? sender, EventArgs e)
        {
            this.Hide();
        }
        
        private void WebInterfaceForm_Shown(object? sender, EventArgs e)
        {
            if (webView != null && webView.CoreWebView2 != null)
            {
                InvokeScript("DesktopMenuShow");
            }
            SetForegroundWindow(this.Handle);
            this.Invalidate();
            this.Update();
        }

        public async void InvokeScript(string functionName)
        {
            if (webView != null && webView.CoreWebView2 != null)
            {
                await webView.CoreWebView2.ExecuteScriptAsync($"{functionName}()");
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (webView != null && webView.CoreWebView2 != null)
            {
                InvokeScript("DesktopMenuHide");
            }
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                this.Hide();
            }
            else
            {
                base.OnFormClosing(e);
            }
        }

        public new void Show()
        {
            base.Show();
            SetForegroundWindow(this.Handle);
        }
        
        private void CoreWebView2_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            e.Handled = true;

            var newForm = new Form
            {
                Width = 1920,
                Height = 1080,
                StartPosition = FormStartPosition.CenterScreen,
                AutoScaleMode = AutoScaleMode.None
            };

            var newWebView = new WebView2
            {
                Dock = DockStyle.Fill
            };

            newForm.Controls.Add(newWebView);

            newForm.Load += async (s, args) =>
            {
                // Create options to force app mode with no UI
                var envOptions = new CoreWebView2EnvironmentOptions();
                envOptions.AdditionalBrowserArguments = "--app";
                
                // Use the same user data folder as the main WebView2 to share localStorage
                string userDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScreamRouterDesktop", "WebView2");
                Directory.CreateDirectory(userDataFolder);
                var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder, envOptions);
                await newWebView.EnsureCoreWebView2Async(env);
                
                // Disable all browser UI elements
                /*newWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                newWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                newWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
                newWebView.CoreWebView2.Settings.IsBuiltInErrorPageEnabled = false;
                newWebView.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = false;
                newWebView.CoreWebView2.Settings.IsZoomControlEnabled = false;*/
                newWebView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = true;  // Leave refresh and dev console enabled

                // Add event handler for title changes
                newWebView.CoreWebView2.DocumentTitleChanged += (sender, e) =>
                {
                    newForm.Invoke((MethodInvoker)delegate
                    {
                        newForm.Text = newWebView.CoreWebView2.DocumentTitle;
                    });
                };

                // Navigate to the requested URL
                newWebView.CoreWebView2.Navigate(e.Uri);
            };

            newForm.Show();
        }
    }
}

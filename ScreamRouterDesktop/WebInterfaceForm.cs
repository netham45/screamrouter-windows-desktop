using System;
using System.IO;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;

namespace ScreamRouterDesktop
{
    public partial class WebInterfaceForm : Form
    {
        private WebView2? webView;
        private bool isWebViewInitialized = false;
        private Panel blurPanel;
        private string url;
        private System.Windows.Forms.Timer mousePositionTimer;
        private Point lastMousePosition = Point.Empty;

        private bool mouseDisabled = false;

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        // DwmGetColorizationColor is used to retrieve the Windows desktop accent color
        [DllImport("dwmapi.dll")]
        private static extern int DwmGetColorizationColor(out uint colorization, out bool opaque);

        // Win32 constants
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_LAYERED = 0x80000;
        private const int LWA_ALPHA = 0x2;

        // Win32 API imports
        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        [DllImport("user32.dll")]
        private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte alpha, uint flags);

        private void DisableMouse()
        {

            // Get current window style
            int exStyle = GetWindowLong(this.Handle, GWL_EXSTYLE);

            // Remove layered window style
            SetWindowLong(this.Handle, GWL_EXSTYLE, exStyle | WS_EX_LAYERED);

            // Set the opacity
            SetLayeredWindowAttributes(this.Handle, 0, 255, LWA_ALPHA);

            this.TransparencyKey = Color.Transparent;

            mouseDisabled = true;
        }

        private void EnableMouse()
        {
            // Get current window style
            int exStyle = GetWindowLong(this.Handle, GWL_EXSTYLE);

            // Remove layered window style
            SetWindowLong(this.Handle, GWL_EXSTYLE, (exStyle & WS_EX_LAYERED) == WS_EX_LAYERED ? exStyle - WS_EX_LAYERED : 0);

            // Set the opacity
            SetLayeredWindowAttributes(this.Handle, 0, 255, LWA_ALPHA);

            mouseDisabled = false;
        }

        private void InitializeMousePositionTimer()
        {
            mousePositionTimer = new System.Windows.Forms.Timer();
            mousePositionTimer.Interval = 50; // Poll every 50ms
            mousePositionTimer.Tick += MousePositionTimer_Tick;
            mousePositionTimer.Start();
        }

        private bool IsPointInFormBounds(Point point)
        {
            return point.X >= this.Left && point.X < this.Right &&
                   point.Y >= this.Top && point.Y < this.Bottom;
        }

        private async void MousePositionTimer_Tick(object? sender, EventArgs e)
        {
            if (webView == null || webView.CoreWebView2 == null || !this.Visible)
            {
                return;
            }

            Point mousePos = Control.MousePosition;

            // Skip check if mouse hasn't moved since last check
            if (lastMousePosition == mousePos)
            {
                return;
            }

            // Update last position
            lastMousePosition = mousePos;

            // First check if mouse is in form bounds
            bool isInBounds = IsPointInFormBounds(mousePos);

            if (isInBounds)
            {
                // Convert screen coordinates to client coordinates
                Point clientPoint = this.PointToClient(mousePos);

                // Apply DPI scaling to convert from logical to physical coordinates
                float dpiScaling = GetScalingFactor();
                int scaledX = (int)(clientPoint.X / dpiScaling);
                int scaledY = (int)(clientPoint.Y / dpiScaling);

                // Direct JavaScript check of element at scaled coordinates
                string jsCheck = $"isPointOverBody({scaledX}, {scaledY})";

                // Execute the JavaScript function and get result
                string result = await webView.CoreWebView2.ExecuteScriptAsync(jsCheck);

                // Parse result (true = over body, false = over element)
                bool isOverBody = result.Contains("true");

                // Update mouse state based on element check
                if (isOverBody && !mouseDisabled)
                {
                    DisableMouse();
                }
                else if (!isOverBody && mouseDisabled)
                {
                    EnableMouse();
                }
            }
            // If mouse is outside the form and mouse is disabled, do nothing
            // If outside and mouse is enabled, also do nothing (handled by form's deactivate event)
        }

        public WebInterfaceForm(string url)
        {
            this.url = url;
            InitializeComponent();
            InitializeWebView();
            InitializeMousePositionTimer();
            PositionFormBottomRight();
            this.Deactivate += WebInterfaceForm_Deactivate;
            this.Shown += WebInterfaceForm_Shown;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00000020 | (mouseDisabled ? 0x80000 : 0); // WS_EX_TRANSPARENT
                return cp;
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Color.Transparent);
            // Workaround for transparency bugging out
            this.Opacity = 0;
            System.Threading.Timer timer = null;
            timer = new System.Threading.Timer((state) =>
            {
                this.BeginInvoke(new Action(() =>
                {
                    this.Show();
                    System.Threading.Timer timer2 = null;
                    timer2 = new System.Threading.Timer((state) =>
                    {
                        this.BeginInvoke(new Action(() =>
                        {
                            this.Hide();
                            this.Opacity = 1;
                            timer2?.Dispose();
                        }));
                    }, null, 5, System.Threading.Timeout.Infinite);
                    timer?.Dispose();
                }));
            }, null, 5, System.Threading.Timeout.Infinite);
        }


        private void InitializeComponent()
        {
            this.SuspendLayout();
            // 
            // WebInterfaceForm
            // 
            // Scale according to DPI
            float dpiScaling = GetScalingFactor();
            this.ClientSize = new System.Drawing.Size((int)(1010 * dpiScaling), (int)(950 * dpiScaling));
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

                    // Ignore SSL certificate errors
                    webView.CoreWebView2.ServerCertificateErrorDetected += (sender, args) =>
                    {
                        // NOTE: This is insecure and should only be used in controlled environments
                        // where you trust the source despite certificate errors.
                        args.Action = CoreWebView2ServerCertificateErrorAction.AlwaysAllow;
                    };

                    // Completely disable all browser UI elements
                    webView.CoreWebView2.Settings.AreDevToolsEnabled = true;
                    webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                    webView.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = false;
                    webView.CoreWebView2.Settings.IsBuiltInErrorPageEnabled = false;
                    webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                    webView.CoreWebView2.Settings.IsZoomControlEnabled = false;
                    webView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = true;

                    // Create custom context to block all UI elements
                    webView.CoreWebView2.Profile.PreferredColorScheme = CoreWebView2PreferredColorScheme.Dark;

                    isWebViewInitialized = true;
                }
                webView.CoreWebView2.Settings.IsZoomControlEnabled = false;
                webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                webView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = true;
                webView.CoreWebView2.Settings.AreDevToolsEnabled = true;
                webView.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = false;
                webView.CoreWebView2.Settings.IsStatusBarEnabled = false;

                // Create a custom environment to hide the browser UI
                var environment = webView.CoreWebView2.Environment;
                webView.CoreWebView2.Settings.IsBuiltInErrorPageEnabled = false;

                // Set the WebView2 size to match the form's client size
                webView.Size = this.ClientSize;

                // Set the background of the WebView2 to be transparent
                webView.DefaultBackgroundColor = Color.Transparent;

                // Handle new window requests to prevent address bar in popup windows
                webView.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;

                // Inject JavaScript function to detect if a point is over the body (transparent) or another element
                string jsCode = @"
                    function isPointOverBody(x, y) {
                        // Get the element at the specified point
                        const element = document.elementFromPoint(x, y);                        
                        // If there's no element or it's the body/html, it's over body
                        return (!element || element === document.body || element === document.documentElement | element.id == 'root' || element.parentNode.id == 'root') && element.id != 'chakra-portal' || element.classList.contains('chakra-modal__overlay') || element.classList.contains('chakra-modal__content-container') || element.classList.contains('chakra-modal__body');
                    }
                ";

                await webView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(jsCode);

                // Navigate directly to the URL in app mode
                webView.CoreWebView2.Navigate(url);
                webView.Visible = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error initializing WebView2: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // Direct JavaScript execution is used instead of message passing

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
            // Pause the timer when the form is hidden
            if (mousePositionTimer != null)
            {
                mousePositionTimer.Stop();
            }
        }

        private void WebInterfaceForm_Shown(object? sender, EventArgs e)
        {

        }

        public async void InvokeScript(string functionName)
        {
            Debug.WriteLine("Invoking Script " + functionName);
            if (webView != null && webView.CoreWebView2 != null)
            {
                if (functionName == "DesktopMenuShow")
                {
                    // Check if Windows accent color is enabled
                    var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                    bool accentColorEnabled = key != null && (int)key.GetValue("ColorPrevalence", 0) == 1;

                    if (accentColorEnabled)
                    {
                        // Get the Windows colorization color
                        uint colorizationColor;
                        bool opaque;
                        DwmGetColorizationColor(out colorizationColor, out opaque);

                        // Extract RGB components
                        int a = (int)((colorizationColor >> 24) & 0xFF);
                        int r = (int)((colorizationColor >> 16) & 0xFF);
                        int g = (int)((colorizationColor >> 8) & 0xFF);
                        int b = (int)(colorizationColor & 0xFF);

                        // Pass the color to JavaScript
                        await webView.CoreWebView2.ExecuteScriptAsync($"{functionName}({r}, {g}, {b}, {a})");
                    }
                    else
                    {
                        // Call without color parameters if accent color is disabled
                        await webView.CoreWebView2.ExecuteScriptAsync($"{functionName}(0, 0, 0)");
                    }
                }
                else
                {
                    await webView.CoreWebView2.ExecuteScriptAsync($"{functionName}()");
                }
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Stop and dispose the mouse position timer
            if (mousePositionTimer != null)
            {
                mousePositionTimer.Stop();
                mousePositionTimer.Dispose();
            }

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

        public new void Hide()
        {
            base.Hide();
            InvokeScript("DesktopMenuHide");
        }

        public new void Show()
        {
            base.Show();

            if (webView != null && webView.CoreWebView2 != null)
            {
                InvokeScript("DesktopMenuShow");
            }
            // Resume the timer when the form is shown
            if (mousePositionTimer != null)
            {
                mousePositionTimer.Start();
            }
            SetForegroundWindow(this.Handle);

            // Resume the timer when the form is shown
            Debug.WriteLine("Show 1");

            // If the timer is null or not enabled, create a new one
            if (mousePositionTimer == null || !mousePositionTimer.Enabled)
            {
                Debug.WriteLine("Creating new timer in Show()");
                // The old timer might be stopped or disposed - create a fresh one
                if (mousePositionTimer != null)
                {
                    mousePositionTimer.Dispose();
                }
                InitializeMousePositionTimer();
            }
            else
            {
                Debug.WriteLine("Timer already active");
            }

            SetForegroundWindow(this.Handle);
        }

        private async void CoreWebView2_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            e.Handled = true;
            Form newForm;
            WebView2 newWebView;
            switch (e.Name)
            {
                case "Transcription":
                    TransparentForm transparentNewForm = new TransparentForm()
                    {
                        Width = (int)(Screen.PrimaryScreen.Bounds.Width * .8),
                        Height = 600,
                        StartPosition = FormStartPosition.Manual,
                        Left = (int)(Screen.PrimaryScreen.Bounds.Width * .1),
                        Top = Screen.PrimaryScreen.Bounds.Height - 600 - 100,
                        AutoScaleMode = AutoScaleMode.None,
                        FormBorderStyle = FormBorderStyle.None,
                        BackColor = Color.Transparent,
                        WindowState = FormWindowState.Normal,
                        ShowInTaskbar = false,
                        ShowIcon = false,
                        TopMost = true
                    };
                    newForm = transparentNewForm;

                    newWebView = new WebView2
                    {
                        Dock = DockStyle.Fill
                    };
                    transparentNewForm.webView = newWebView;
                    break;
                case "FullView":
                default:
                    newForm = new Form()
                    {
                        Width = 1920,
                        Height = 1080,
                        StartPosition = FormStartPosition.CenterScreen,
                        AutoScaleMode = AutoScaleMode.None
                    };

                    newWebView = new WebView2
                    {
                        Dock = DockStyle.Fill
                    };
                    break;
            }

            newForm.Controls.Add(newWebView);
            newWebView.DefaultBackgroundColor = Color.Transparent;
            newWebView.CreateGraphics().Clear(Color.Transparent);

            newForm.Load += async (s, args) =>
            {
                // Create options to force app mode with no UI
                var envOptions = new CoreWebView2EnvironmentOptions();
                envOptions.AdditionalBrowserArguments = "--app";

                // Use the same user data folder as the main WebView2 to share localStorage
                string userDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScreamRouterDesktop", "WebView2");
                Directory.CreateDirectory(userDataFolder);
                var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder, envOptions);
                // Set WebView2 background to transparent
                newWebView.DefaultBackgroundColor = Color.Transparent;
                await newWebView.EnsureCoreWebView2Async(env);

                // Configure browser settings
                newWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                newWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                newWebView.CoreWebView2.Settings.AreDevToolsEnabled = true;
                newWebView.CoreWebView2.Settings.IsBuiltInErrorPageEnabled = false;
                newWebView.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = false;
                newWebView.CoreWebView2.Settings.IsZoomControlEnabled = false;
                newWebView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = true;  // Enable all browser keyboard shortcuts including F11

                // Enable F11 fullscreen support
                newWebView.CoreWebView2.ContainsFullScreenElementChanged += (s, args) =>
                {
                    newForm.Invoke((MethodInvoker)delegate
                    {
                        if (newWebView.CoreWebView2.ContainsFullScreenElement)
                        {
                            newForm.FormBorderStyle = FormBorderStyle.None;
                            newForm.WindowState = FormWindowState.Maximized;
                        }
                        else
                        {
                            newForm.FormBorderStyle = FormBorderStyle.Sizable;
                            newForm.WindowState = FormWindowState.Normal;
                        }
                    });
                };

                // Add event handler for title changes
                newWebView.CoreWebView2.DocumentTitleChanged += (sender, e) =>
                {
                    newForm.Invoke((MethodInvoker)delegate
                    {
                        newForm.Text = newWebView.CoreWebView2.DocumentTitle;
                    });
                };

                // Add handler for messages from the new WebView
                newWebView.CoreWebView2.WebMessageReceived += (messageSender, messageArgs) => NewWebView_WebMessageReceived(newForm, messageArgs);

                // Inject JavaScript function to detect if a point is over the body (transparent) or another element
                // Also override window.close to send a message back to the host
                string jsCode = @"
                // Override window.close to post a message instead
                window.close = function() {
                    window.chrome.webview.postMessage({ action: 'close' });
                };

                function isPointOverBody(x, y) {
                    // console.log('check'); // Removed console log for cleaner output
                    // Get the element at the specified point
                    const element = document.elementFromPoint(x, y);                        
                    // If there's no element or it's the body/html, it's over body
                    return (!element || element === document.body || element === document.documentElement | element.id == 'root' || element.parentNode.id == 'root') && element.id != 'chakra-portal' || element.classList.contains('chakra-modal__overlay') || element.classList.contains('chakra-modal__content-container') || element.classList.contains('chakra-modal__body');
                }";

                await newWebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(jsCode);

                // Navigate to the requested URL
                newWebView.CoreWebView2.Navigate(e.Uri);
            };

            newForm.Show();
        }

        // Handles messages received from popup WebView instances
        private void NewWebView_WebMessageReceived(Form windowToClose, CoreWebView2WebMessageReceivedEventArgs args)
        {
            // Attempt to parse the message as JSON
            try
            {
                // Using System.Text.Json for parsing
                using (var jsonDoc = System.Text.Json.JsonDocument.Parse(args.WebMessageAsJson))
                {
                    if (jsonDoc.RootElement.TryGetProperty("action", out var actionProperty) &&
                        actionProperty.ValueKind == System.Text.Json.JsonValueKind.String &&
                        actionProperty.GetString() == "close")
                    {
                        // If the action is 'close', close the associated form
                        windowToClose.Invoke((MethodInvoker)delegate {
                            windowToClose.Close();
                        });
                    }
                }
            }
            catch (System.Text.Json.JsonException ex)
            {
                // Log or handle cases where the message is not valid JSON
                Debug.WriteLine($"Error parsing WebMessageAsJson: {ex.Message}");
            }
            catch (Exception ex)
            {
                // Catch other potential exceptions
                Debug.WriteLine($"Error in NewWebView_WebMessageReceived: {ex.Message}");
            }
        }
    }
}

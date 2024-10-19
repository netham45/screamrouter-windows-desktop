using System;
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
        private Panel blurPanel;
        private string url;

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        public WebInterfaceForm(string url)
        {
            this.url = url;
            InitializeComponent();
            InitializeWebView();
            PositionFormBottomRight();
            this.Deactivate += WebInterfaceForm_Deactivate;
            this.Shown += WebInterfaceForm_Shown;
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
            // Do not paint background
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            // 
            // WebInterfaceForm
            // 
            this.ClientSize = new System.Drawing.Size(1010, 610);
            this.FormBorderStyle = FormBorderStyle.None;
            this.Name = "WebInterfaceForm";
            this.ShowInTaskbar = false;
            this.TopMost = true;
            this.ResumeLayout(false);
        }

        private async void InitializeWebView()
        {
            webView = new WebView2();
            webView.Dock = DockStyle.Fill;
            this.Controls.Add(webView);
            webView.BringToFront();

            try
            {
                await webView.EnsureCoreWebView2Async(null);
                webView.CoreWebView2.Settings.IsZoomControlEnabled = false;
                webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                
                // Set the WebView2 size to match the form's client size
                webView.Size = this.ClientSize;
                
                // Adjust the zoom factor based on the system DPI scaling
                float dpiScaling = GetScalingFactor();
                webView.ZoomFactor = 1 / dpiScaling;

                // Set the background of the WebView2 to be transparent
                webView.DefaultBackgroundColor = Color.Transparent;

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
                webView.CoreWebView2.Reload();
            }
            SetForegroundWindow(this.Handle);
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
    }
}
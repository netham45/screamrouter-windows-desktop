using System;
using System.Windows.Forms;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Diagnostics;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace ScreamRouterDesktop
{
    public class TransparentForm : Form
    {
        public WebView2? webView;
        private System.Windows.Forms.Timer mousePositionTimer;
        private Point lastMousePosition = Point.Empty;
        private bool mouseDisabled = false;

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

        public TransparentForm()
        {
            // Enable transparent background color support
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            InitializeMousePositionTimer();
        }

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
            Debug.WriteLine("tick 1");
            if (webView == null || webView.CoreWebView2 == null || !this.Visible)
            {
                return;
            }
            Debug.WriteLine("tick 2");

            Point mousePos = Control.MousePosition;

            // Skip check if mouse hasn't moved since last check
            if (lastMousePosition == mousePos)
            {
                return;
            }
            Debug.WriteLine("tick 3");

            // Update last position
            lastMousePosition = mousePos;

            // First check if mouse is in form bounds
            bool isInBounds = IsPointInFormBounds(mousePos);

            if (isInBounds)
            {
                Debug.WriteLine("tick 4");
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
                    Debug.WriteLine("tick 5");
                    DisableMouse();
                }
                else if (!isOverBody && mouseDisabled)
                {
                    Debug.WriteLine("tick 6");
                    EnableMouse();
                }
            }
            // If mouse is outside the form and mouse is disabled, do nothing
            // If outside and mouse is enabled, also do nothing (handled by form's deactivate event)
        }

        private float GetScalingFactor()
        {
            Graphics g = CreateGraphics();
            float dpiX = g.DpiX;
            g.Dispose();
            return dpiX / 96f; // 96 DPI is the default (100% scaling)
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Color.Transparent);
            // Workaround for transparency bugging out
            base.OnPaintBackground(e);
            this.Opacity = 0;
            System.Threading.Timer timer = null;
            timer = new System.Threading.Timer((state) =>
            {
                this.BeginInvoke(new Action(() =>
                {
                    this.Hide();
                    System.Threading.Timer timer2 = null;
                    timer2 = new System.Threading.Timer((state) =>
                    {
                        this.BeginInvoke(new Action(() =>
                        {
                            this.Opacity = 1;
                            this.Show();
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
                cp.ExStyle |= 0x00000020 | (mouseDisabled ? 0x80000 : 0); // WS_EX_TRANSPARENT
                return cp;
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

            base.OnFormClosing(e);
        }

        public async new void Show()
        {
            base.Show();

            // Resume the timer when the form is shown
            if (mousePositionTimer != null)
            {
                mousePositionTimer.Start();
            }

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
        }

        public new void Hide()
        {
            base.Hide();

            // Pause the timer when the form is hidden
            if (mousePositionTimer != null)
            {
                mousePositionTimer.Stop();
            }
        }
    }
}
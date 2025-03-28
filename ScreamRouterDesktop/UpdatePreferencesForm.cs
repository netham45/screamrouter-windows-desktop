using System;
using System.Windows.Forms;
using System.Drawing;

namespace ScreamRouterDesktop
{
    public partial class UpdatePreferencesForm : Form
    {
        public UpdateMode SelectedMode { get; private set; }

        public UpdatePreferencesForm()
        {
            InitializeComponents();
        }

        private void InitializeComponents()
        {
            this.Text = "ScreamRouter Desktop Update Settings";
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ShowInTaskbar = false;
            this.AutoScaleMode = AutoScaleMode.Dpi;

            // Use DPI-aware sizing
            float scaleFactor = DeviceDpi / 96f;
            int baseWidth = 600;
            int baseHeight = 350;
            int padding = (int)(20 * scaleFactor);
            int indent = (int)(25 * scaleFactor);
            
            this.ClientSize = new Size((int)(baseWidth * scaleFactor), (int)(baseHeight * scaleFactor));

            TableLayoutPanel mainPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(padding),
                RowCount = 3,
                ColumnCount = 1,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink
            };
            mainPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            mainPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // Header
            Label headerLabel = new Label
            {
                Text = "How would you like ScreamRouter Desktop to handle updates?",
                AutoSize = true,
                Font = new Font(this.Font.FontFamily, 12, FontStyle.Bold),
                Margin = new Padding(0, 0, 0, padding)
            };
            mainPanel.Controls.Add(headerLabel);

            // Options Panel
            FlowLayoutPanel optionsPanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, padding, 0, padding),
                WrapContents = false,
                Dock = DockStyle.Fill
            };

            // Automatic Updates Option
            RadioButton automaticButton = new RadioButton
            {
                Text = "Automatically install updates",
                AutoSize = true,
                Checked = true,
                Font = new Font(this.Font.FontFamily, 10, FontStyle.Regular),
                Margin = new Padding(0, 0, 0, 5)
            };
            optionsPanel.Controls.Add(automaticButton);

            Label automaticDescription = new Label
            {
                Text = "ScreamRouter Desktop  automatically download and install updates when available.",
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Margin = new Padding(indent, 0, 0, padding)
            };
            optionsPanel.Controls.Add(automaticDescription);

            // Notify Option
            RadioButton notifyButton = new RadioButton
            {
                Text = "Notify me when updates are available",
                AutoSize = true,
                Font = new Font(this.Font.FontFamily, 10, FontStyle.Regular),
                Margin = new Padding(0, 0, 0, 5)
            };
            optionsPanel.Controls.Add(notifyButton);

            Label notifyDescription = new Label
            {
                Text = "ScreamRouter Desktop  notify you when updates are available, but won't install them automatically.",
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Margin = new Padding(indent, 0, 0, padding)
            };
            optionsPanel.Controls.Add(notifyDescription);

            // Never Check Option
            RadioButton neverButton = new RadioButton
            {
                Text = "Never check for updates",
                AutoSize = true,
                Font = new Font(this.Font.FontFamily, 10, FontStyle.Regular),
                Margin = new Padding(0, 0, 0, 5)
            };
            optionsPanel.Controls.Add(neverButton);

            Label neverDescription = new Label
            {
                Text = "ScreamRouter Desktop  never check for or install updates automatically.",
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Margin = new Padding(indent, 0, 0, padding)
            };
            optionsPanel.Controls.Add(neverDescription);

            mainPanel.Controls.Add(optionsPanel);

            // Buttons Panel
            FlowLayoutPanel buttonsPanel = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.RightToLeft,
                Dock = DockStyle.Bottom,
                AutoSize = true,
                Margin = new Padding(0, padding, 0, 0)
            };

            Button cancelButton = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Size = new Size((int)(80 * scaleFactor), (int)(30 * scaleFactor)),
            };
            buttonsPanel.Controls.Add(cancelButton);

            Button okButton = new Button
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                Size = new Size((int)(80 * scaleFactor), (int)(30 * scaleFactor)),
            };
            okButton.Click += (s, e) =>
            {
                if (automaticButton.Checked)
                    SelectedMode = UpdateMode.AutomaticUpdate;
                else if (notifyButton.Checked)
                    SelectedMode = UpdateMode.NotifyUser;
                else
                    SelectedMode = UpdateMode.DoNotCheck;
                
                this.DialogResult = DialogResult.OK;
                this.Close();
            };
            buttonsPanel.Controls.Add(okButton);
            mainPanel.Controls.Add(buttonsPanel);

            this.Controls.Add(mainPanel);
            this.AcceptButton = okButton;
            this.CancelButton = cancelButton;

            // Event handler for OK button
        }
    }
}

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace TunnelWatch
{
    internal static class UiTheme
    {
        internal static Color Text { get { return SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(26, 38, 54); } }
        internal static Color Muted { get { return SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(88, 101, 119); } }
        internal static Color Background { get { return SystemInformation.HighContrast ? SystemColors.Window : Color.FromArgb(248, 250, 252); } }
        internal static Color Border { get { return SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(218, 225, 233); } }
        internal static Color Accent { get { return SystemInformation.HighContrast ? SystemColors.Highlight : Color.FromArgb(29, 78, 216); } }
        internal static Color Tone(Health health)
        {
            if (SystemInformation.HighContrast) return SystemColors.WindowText;
            return health == Health.Healthy ? Color.FromArgb(21, 112, 73) : health == Health.Broken ? Color.FromArgb(180, 35, 48)
                : health == Health.Checking ? Color.FromArgb(145, 86, 8) : health == Health.Home ? Accent : Muted;
        }
        internal static Color Surface(Health health)
        {
            if (SystemInformation.HighContrast) return SystemColors.Window;
            return health == Health.Healthy ? Color.FromArgb(236, 248, 241) : health == Health.Broken ? Color.FromArgb(255, 241, 242)
                : health == Health.Checking ? Color.FromArgb(255, 248, 231) : health == Health.Home ? Color.FromArgb(239, 245, 255) : Color.FromArgb(241, 245, 249);
        }
        internal static GraphicsPath Rounded(RectangleF r, float radius)
        {
            var path = new GraphicsPath(); float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            path.AddArc(r.X, r.Y, d, d, 180, 90); path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.X, r.Bottom - d, d, d, 90, 90); path.CloseFigure(); return path;
        }
    }
    // Native button semantics and keyboard input, with a quiet visual treatment.
    internal class UiButton : Button
    {
        internal bool Selected, Quiet;
        private bool hovered;
        internal UiButton()
        {
            FlatStyle = FlatStyle.Flat; UseVisualStyleBackColor = false; Height = 38; MinimumSize = new Size(76, 38);
            Margin = new Padding(4, 0, 0, 0); Padding = new Padding(10, 3, 10, 3);
            SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        }
        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent == null ? UiTheme.Background : Parent.BackColor); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float scale = DeviceDpi / 96f; var rect = new RectangleF(scale, scale, Width - 2 * scale - 1, Height - 2 * scale - 1);
            Color fill = Selected ? UiTheme.Accent : hovered && Enabled ? UiTheme.Surface(Health.Home) : Quiet ? UiTheme.Background : SystemColors.Window;
            Color ink = !Enabled ? SystemColors.GrayText : Selected ? SystemColors.HighlightText : UiTheme.Text;
            using (var shape = UiTheme.Rounded(rect, 7 * scale)) using (var brush = new SolidBrush(fill)) using (var pen = new Pen(Selected ? UiTheme.Accent : UiTheme.Border, scale))
            { e.Graphics.FillPath(brush, shape); if (!Quiet || Selected) e.Graphics.DrawPath(pen, shape); }
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(Padding.Left, Padding.Top, Width - Padding.Horizontal, Height - Padding.Vertical), ink,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | (ShowKeyboardCues ? 0 : TextFormatFlags.HidePrefix));
            if (Focused && ShowFocusCues)
            {
                rect.Inflate(-3 * scale, -3 * scale);
                using (var shape = UiTheme.Rounded(rect, 4 * scale)) using (var pen = new Pen(Selected ? SystemColors.HighlightText : UiTheme.Accent, 2 * scale)) e.Graphics.DrawPath(pen, shape);
            }
        }
        protected override AccessibleObject CreateAccessibilityInstance() { return new SelectionAccessibleObject(this); }
        private sealed class SelectionAccessibleObject : ControlAccessibleObject
        {
            private readonly UiButton owner;
            internal SelectionAccessibleObject(UiButton button) : base(button) { owner = button; }
            public override AccessibleRole Role { get { return AccessibleRole.PushButton; } }
            public override string DefaultAction { get { return L.Get("Ui.Press"); } }
            public override void DoDefaultAction() { owner.PerformClick(); }
            public override AccessibleStates State { get { return base.State | (owner.Selected ? AccessibleStates.Pressed : AccessibleStates.None); } }
        }
    }
    internal sealed class StatusForm : Form
    {
        private readonly Settings settings;
        internal event Action RefreshRequested, DnsRequested;
        internal event Action<string> ActionRequested;
        internal event Action<Control> SettingsRequested;
        internal bool AllowClose;
        private readonly Label heading = new Label(), summary = new Label(), checkedAt = new Label(), actionMessage = new Label(), modeLabel = new Label();
        private readonly DiagnosticsBox details = new DiagnosticsBox();
        private readonly PictureBox symbol = new PictureBox();
        private readonly UiButton refresh = new UiButton(), dns = new UiButton(), expand = new UiButton(), wss = new UiButton(), preferences = new UiButton();
        private readonly Dictionary<string, UiButton> modes = new Dictionary<string, UiButton>();
        private readonly Dictionary<Health, Icon> icons = new Dictionary<Health, Icon>();
        private readonly ToolTip tips = new ToolTip { AutoPopDelay = 12000 };
        private readonly Panel diagnostic = new Panel();
        private readonly TableLayoutPanel layout = new TableLayoutPanel(), title = new TableLayoutPanel();
        private readonly TableLayoutPanel modeRow = new TableLayoutPanel(), operationRow = new TableLayoutPanel();
        private readonly StatusStep transport, vpn, home;
        private readonly Font selectedFont;
        private bool measuring, operating, expanded;
        private string selectedMode, wssAction;
        private int compactHeight;
        internal bool DetailsExpanded { get { return expanded; } }
        internal void RestoreLayout(Rectangle bounds, bool showDetails) { if (showDetails) ToggleDetails(); Bounds = bounds; }
        internal string ActionMessage { set { actionMessage.Text = value; actionMessage.Visible = !string.IsNullOrEmpty(value); } }
        internal StatusForm(Settings config)
        {
            settings = config; Text = "TunnelWatch"; Font = new Font("Segoe UI", 9.5f); selectedFont = new Font(Font, FontStyle.Bold);
            ForeColor = UiTheme.Text; BackColor = UiTheme.Background; AutoScaleMode = AutoScaleMode.Dpi; ClientSize = new Size(480, 432);
            MinimumSize = new Size(496, 471); MaximizeBox = false; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
            foreach (Health health in Enum.GetValues(typeof(Health))) icons.Add(health, TrayContext.MakeIcon(health));
            layout.Dock = DockStyle.Fill; layout.AutoScroll = true; layout.Padding = new Padding(24); layout.ColumnCount = 1; layout.RowCount = 9;
            for (int row = 0; row < 8; row++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            title.Dock = DockStyle.Top; title.AutoSize = true; title.ColumnCount = 2; title.Padding = new Padding(14); title.Margin = new Padding(0, 0, 0, 16);
            title.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 32)); title.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            symbol.Size = new Size(22, 22); symbol.SizeMode = PictureBoxSizeMode.Zoom; symbol.Margin = new Padding(0, 6, 0, 0); symbol.TabStop = false;
            heading.AutoSize = true; heading.Dock = DockStyle.Top; heading.Font = new Font(Font.FontFamily, 19f, FontStyle.Bold); heading.Margin = new Padding(0);
            summary.AutoSize = true; summary.Dock = DockStyle.Top; summary.ForeColor = UiTheme.Muted; summary.Margin = new Padding(0, 5, 0, 0);
            var titleText = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Margin = new Padding(0) };
            titleText.Controls.Add(heading); titleText.Controls.Add(summary); title.Controls.Add(symbol, 0, 0); title.Controls.Add(titleText, 1, 0);
            // Independent observations: no connecting lines implying a verified path.
            var path = new TableLayoutPanel { Dock = DockStyle.Top, Height = 82, ColumnCount = 3, Margin = new Padding(0, 0, 0, 20) };
            for (int col = 0; col < 3; col++) path.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
            transport = new StatusStep("WSS", icons); vpn = new StatusStep("VPN", icons); home = new StatusStep("Homelab", icons);
            transport.Margin = new Padding(0, 0, 6, 0); vpn.Margin = new Padding(3, 0, 3, 0); home.Margin = new Padding(6, 0, 0, 0);
            path.Controls.Add(transport, 0, 0); path.Controls.Add(vpn, 1, 0); path.Controls.Add(home, 2, 0);
            SetTip(transport, L.Get("Tip.Wss")); SetTip(vpn, L.Get("Tip.Vpn")); SetTip(home, L.Get("Tip.Home"));
            modeLabel.Text = L.Get("Ui.VpnMode"); modeLabel.AutoSize = true; modeLabel.Font = selectedFont; modeLabel.Margin = new Padding(0, 0, 0, 8);
            modeRow.Dock = DockStyle.Top; modeRow.AutoSize = true; modeRow.ColumnCount = 3; modeRow.Margin = new Padding(0, 0, 0, 12);
            for (int col = 0; col < 3; col++) modeRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 3));
            foreach (var item in new[] { new[] { L.Get("Common.Off"), "Off" }, new[] { L.Get("Ui.ModeSplit"), "Split" }, new[] { L.Get("Ui.ModeFull"), "Full" } })
            {
                string action = item[1]; var button = new UiButton { Text = item[0], Dock = DockStyle.Fill, Margin = new Padding(action == "Off" ? 0 : 4, 0, action == "Full" ? 0 : 4, 0) };
                button.Click += delegate { SendAction(action); }; modes.Add(action, button); modeRow.Controls.Add(button);
                SetTip(button, action == "Full" ? L.Get("Tip.Full") : action == "Off" ? L.Get("Tip.Off") : L.Get("Tip.Split"));
            }
            modeRow.AccessibleName = L.Get("Ui.VpnMode");
            operationRow.Dock = DockStyle.Top; operationRow.AutoSize = true; operationRow.ColumnCount = 2; operationRow.Margin = new Padding(0, 0, 0, 16);
            operationRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); operationRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var permission = new Label { Text = L.Get("Ui.Uac"), AutoSize = true, Anchor = AnchorStyles.Left, ForeColor = UiTheme.Muted, Margin = new Padding(0), MaximumSize = new Size(250, 0) };
            wss.AutoSize = true; wss.Text = "WSS"; wss.Click += delegate { if (wssAction != null) SendAction(wssAction); };
            operationRow.Controls.Add(permission, 0, 0); operationRow.Controls.Add(wss, 1, 0);
            actionMessage.AutoSize = true; actionMessage.Dock = DockStyle.Top; actionMessage.Margin = new Padding(0, 0, 0, 12); actionMessage.Visible = false;
            var divider = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = UiTheme.Border, Margin = new Padding(0, 0, 0, 12) };
            var footer = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4, Margin = new Padding(0) };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); for (int col = 1; col < 4; col++) footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            checkedAt.AutoSize = true; checkedAt.Anchor = AnchorStyles.Left; checkedAt.ForeColor = UiTheme.Muted; checkedAt.Margin = new Padding(0);
            refresh.AutoSize = true; refresh.Quiet = true; refresh.Text = L.Get("Ui.Refresh"); refresh.Click += delegate { if (RefreshRequested != null) RefreshRequested(); };
            expand.AutoSize = true; expand.Quiet = true; expand.Text = L.Get("Ui.Details"); expand.Click += delegate { ToggleDetails(); };
            preferences.AutoSize = true; preferences.Quiet = true; preferences.Text = L.Get("Ui.Settings"); preferences.AccessibleName = L.Get("Ui.Settings");
            preferences.Click += delegate { if (SettingsRequested != null) SettingsRequested(preferences); };
            footer.Controls.Add(checkedAt, 0, 0); footer.Controls.Add(refresh, 1, 0); footer.Controls.Add(expand, 2, 0); footer.Controls.Add(preferences, 3, 0); SetTip(checkedAt, L.Get("Tip.Checked"));
            diagnostic.Dock = DockStyle.Fill; diagnostic.Visible = false; diagnostic.Margin = new Padding(0, 16, 0, 0);
            var diagnosticContent = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
            diagnosticContent.RowStyles.Add(new RowStyle(SizeType.AutoSize)); diagnosticContent.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            dns.AutoSize = true; dns.Text = L.Get("Ui.Dns"); dns.Margin = new Padding(0, 0, 0, 12); dns.Click += delegate { if (DnsRequested != null) DnsRequested(); };
            details.AccessibleName = L.Get("Ui.Diagnostics"); details.AccessibleDescription = L.Get("Ui.DiagnosticsKeyboard");
            diagnosticContent.Controls.Add(dns); diagnosticContent.Controls.Add(details); diagnostic.Controls.Add(diagnosticContent);
            layout.Controls.Add(title, 0, 0); layout.Controls.Add(path, 0, 1); layout.Controls.Add(modeLabel, 0, 2); layout.Controls.Add(modeRow, 0, 3);
            layout.Controls.Add(operationRow, 0, 4); layout.Controls.Add(actionMessage, 0, 5); layout.Controls.Add(divider, 0, 6); layout.Controls.Add(footer, 0, 7); layout.Controls.Add(diagnostic, 0, 8); Controls.Add(layout);
            modeRow.TabIndex = 0; operationRow.TabIndex = 1; footer.TabIndex = 2; diagnostic.TabIndex = 3;
            modes["Off"].TabIndex = 0; modes["Split"].TabIndex = 1; modes["Full"].TabIndex = 2; refresh.TabIndex = 0; expand.TabIndex = 1; preferences.TabIndex = 2;
            Resize += delegate { WrapText(); }; WrapText();
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (!AllowClose && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
            KeyPreview = true; KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) { Hide(); e.Handled = true; } };
        }
        private void SetTip(Control control, string text)
        {
            tips.SetToolTip(control, text); control.AccessibleDescription = text;
            foreach (Control child in control.Controls) SetTip(child, text);
        }
        private void WrapText()
        {
            int width = Math.Max(220, layout.ClientSize.Width - layout.Padding.Horizontal - 8);
            heading.MaximumSize = summary.MaximumSize = new Size(Math.Max(180, title.ClientSize.Width - title.Padding.Horizontal - (int)title.ColumnStyles[0].Width - 8), 0);
            actionMessage.MaximumSize = new Size(width, 0);
        }
        private void SendAction(string action) { if (!operating && ActionRequested != null) ActionRequested(action); }
        private void ToggleDetails()
        {
            expanded = !expanded; diagnostic.Visible = expanded; expand.Text = expanded ? L.Get("Ui.Overview") : L.Get("Ui.Details"); expand.AccessibleName = L.Get(expanded ? "Ui.HideDetails" : "Ui.ShowDetails");
            // Details is a reading view: retain measured status and footer, reclaim
            // control space, and return to the controls with Overview.
            modeLabel.Visible = modeRow.Visible = operationRow.Visible = !expanded;
            int extra = (int)Math.Round(240 * DeviceDpi / 96.0);
            if (expanded) compactHeight = ClientSize.Height;
            ClientSize = new Size(ClientSize.Width, expanded ? compactHeight + extra : Math.Max(MinimumSize.Height - (Height - ClientSize.Height), compactHeight));
            if (Visible)
            {
                var area = Screen.FromControl(this).WorkingArea; if (Height > area.Height) Height = area.Height;
                Location = new Point(Math.Max(area.Left, Math.Min(Left, area.Right - Width)), Math.Max(area.Top, Math.Min(Top, area.Bottom - Height)));
            }
            WrapText(); if (expanded) details.Focus();
        }
        internal void SetBusy(bool busy) { measuring = busy; UpdateControls(); }
        internal void SetActionBusy(bool busy) { operating = busy; UpdateControls(); }
        private void UpdateControls()
        {
            refresh.Enabled = dns.Enabled = !measuring && !operating; preferences.Enabled = !operating; refresh.Text = measuring ? L.Get("Ui.Checking") : L.Get("Ui.Refresh");
            bool canControl = SettingsRules.CanControl(settings);
            foreach (var item in modes)
            {
                bool selected = item.Key == selectedMode; item.Value.Enabled = !operating && canControl; item.Value.Selected = selected; item.Value.Font = selected ? selectedFont : Font; item.Value.Invalidate();
                item.Value.AccessibleName = selected ? L.Format("Ui.Active", item.Value.Text) : item.Key == "Off" ? L.Get("Ui.DisableVpn") : L.Format("Ui.Enable", item.Value.Text);
            }
            wss.Enabled = !operating && canControl && wssAction != null;
        }
        internal void UpdateStatus(Status status, Observation o, Icon icon)
        {
            heading.Text = status.Title; heading.ForeColor = UiTheme.Tone(status.Health); title.BackColor = UiTheme.Surface(status.Health);
            if (symbol.Image != null) symbol.Image.Dispose(); symbol.Image = icon.ToBitmap();
            var visual = StatusVisual.From(status, o, settings); summary.Text = status.Health == Health.Off ? L.Get("Ui.OffHint") : visual.Summary; summary.Visible = !string.IsNullOrEmpty(summary.Text);
            summary.ForeColor = status.Health == Health.Healthy && visual.Mode == "Full" ? UiTheme.Tone(Health.Checking) : UiTheme.Muted;
            transport.Update(visual.Wss, visual.WssText);
            vpn.Update(visual.Vpn, visual.Vpn == Health.Healthy ? L.Format("Ui.ProfileActive", visual.VpnText) : visual.VpnText);
            home.Update(visual.Home, visual.HomeText, visual.Home == Health.Off);
            selectedMode = visual.Mode; wssAction = o == null || status.Health == Health.Checking || o.ListenerUnknown ? null : o.ListenerVerified ? "WssStop" : "WssStart";
            wss.Text = wssAction == "WssStop" ? L.Get("Ui.WssStop") : L.Get("Ui.WssStart"); SetTip(wss, wssAction == "WssStop" ? L.Get("Tip.WssStop") : L.Get("Tip.WssStart"));
            checkedAt.Text = o == null ? L.Get("Ui.NoMeasurement") : L.Get("Ui.LastChecked") + "\n" + o.CheckedAt.ToString("HH:mm:ss");
            checkedAt.AccessibleName = o == null ? checkedAt.Text : L.Format("Ui.Checked", o.CheckedAt.ToString("HH:mm:ss")); expand.AccessibleName = L.Get(expanded ? "Ui.HideDetails" : "Ui.ShowDetails");
            string detailText = o == null ? status.Reason : L.Format("Ui.DetailsText", status.Profile == "off" ? L.Get("Service.Off") : status.Profile, State(o.Split), State(o.Full),
                o.WssDetail, o.RouterRoute, o.HomeRoute, o.CheckedAt.ToString("T") + ", " + o.CheckedAt.ToString("d"), o.DurationMilliseconds.ToString("0"), status.Reason, L.Get(o.Administrator ? "Ui.Admin" : "Ui.NoAdmin"), o.Architecture)
                + (o.DnsDetail == null ? "" : "\n\n" + o.DnsDetail); details.SetContent(detailText); UpdateControls(); WrapText();
        }
        private static string State(ServiceState state) { return state == ServiceState.Running ? L.Get("Service.Running") : state == ServiceState.Missing ? L.Get("Service.Missing") : state == ServiceState.Stopped ? L.Get("Service.Off") : state == ServiceState.Pending ? L.Get("Service.Switching") : L.Get("Service.Unknown"); }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { tips.Dispose(); selectedFont.Dispose(); if (symbol.Image != null) symbol.Image.Dispose(); foreach (var icon in icons.Values) icon.Dispose(); } base.Dispose(disposing);
        }
    }
    internal sealed class StatusStep : TableLayoutPanel
    {
        private readonly PictureBox symbol = new PictureBox(); private readonly Label value = new Label();
        private readonly string name; private readonly Dictionary<Health, Icon> icons;
        internal StatusStep(string caption, Dictionary<Health, Icon> statusIcons)
        {
            name = caption; icons = statusIcons; Dock = DockStyle.Fill; ColumnCount = 2; RowCount = 2; Padding = new Padding(12);
            ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 22));
            RowStyles.Add(new RowStyle(SizeType.Absolute, 24)); RowStyles.Add(new RowStyle(SizeType.Percent, 100)); BackColor = SystemColors.Window;
            symbol.Size = new Size(18, 18); symbol.SizeMode = PictureBoxSizeMode.Zoom; symbol.Anchor = AnchorStyles.Right; symbol.TabStop = false; symbol.Margin = new Padding(0);
            var label = new Label { Text = caption, Dock = DockStyle.Fill, ForeColor = UiTheme.Muted, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(0) };
            value.Dock = DockStyle.Fill; value.TextAlign = ContentAlignment.MiddleLeft; value.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold); value.Margin = new Padding(0);
            Controls.Add(label, 0, 0); Controls.Add(symbol, 1, 0); Controls.Add(value, 0, 1); SetColumnSpan(value, 2);
        }
        internal void Update(Health health, string text, bool uncertain = false)
        {
            Health tone = uncertain ? Health.Checking : health; BackColor = UiTheme.Surface(tone); value.ForeColor = UiTheme.Tone(tone);
            if (symbol.Image != null) symbol.Image.Dispose(); symbol.Image = icons[tone].ToBitmap(); value.Text = text; AccessibleName = name + ": " + text;
        }
        protected override void Dispose(bool disposing) { if (disposing && symbol.Image != null) symbol.Image.Dispose(); base.Dispose(disposing); }
    }
}

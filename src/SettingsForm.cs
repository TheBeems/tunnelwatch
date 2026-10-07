using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TunnelWatch
{
    internal sealed class SettingsForm : Form
    {
        private readonly Settings original;
        private readonly Func<Settings, Task<RelayDetails>> queryRelay;
        private readonly Dictionary<string, Control> editors = new Dictionary<string, Control>();
        private readonly ComboBox language = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = "NativeName" };
        private readonly CheckBox startup = new CheckBox { AutoSize = true, AutoCheck = false };
        private readonly TextBox relay = ReadOnlyBox(), peers = ReadOnlyBox(), profile = ReadOnlyBox();
        private readonly Label relayNote = new Label(), profileTime = new Label(), error = new Label();
        private readonly UiButton reload = new UiButton();
        private bool initializing, relayBusy, relayQueued;
        private int relayVersion;
        private DateTime lastRelayObservation;
        internal event Action RefreshRequested, OpenWireGuardRequested;
        internal event Action<string> LanguageRequested;
        internal event Action<bool> StartupRequested;
        internal event Action<Settings> SaveRequested;
        internal Settings SavedSettings { get; private set; }
        internal string SaveError { get { return error.Text; } }

        internal SettingsForm(Settings settings, bool startAtLogin)
            : this(settings, startAtLogin, RelayReader.Read) { }
        internal SettingsForm(Settings settings, bool startAtLogin, Func<Settings, Task<RelayDetails>> relayQuery)
        {
            original = SettingsRules.Copy(settings); queryRelay = relayQuery; initializing = true;
            relay.Name = "RelayServer"; peers.Name = "TcpPeers"; profile.Name = "ActiveProfile";
            Text = "TunnelWatch · " + L.Get("Ui.Settings"); Font = new Font("Segoe UI", 10f);
            BackColor = UiTheme.Background; ForeColor = UiTheme.Text; AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96, 96); ClientSize = new Size(740, 610); MinimumSize = new Size(670, 560);
            StartPosition = FormStartPosition.CenterParent; ShowInTaskbar = false; MinimizeBox = false;
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1, RowCount = 3 };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var tabs = new TabControl { Name = "SettingsTabs", Dock = DockStyle.Fill };
            var connection = AddTab(tabs, "Settings.Connection");
            AddRow(connection, "Settings.Relay", relay); peers.Multiline = true; peers.ScrollBars = ScrollBars.Vertical; peers.Height = 58;
            AddRow(connection, "Settings.TcpPeers", peers); AddNote(connection, relayNote);
            AddRow(connection, "Settings.ActiveProfile", profile); AddNote(connection, profileTime);
            var connectionButtons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, WrapContents = true, Margin = new Padding(0, 4, 0, 16) };
            Bind(reload, "Ui.Refresh"); reload.AutoSize = true; reload.Click += delegate { if (RefreshRequested != null) RefreshRequested(); ReadRelay(); };
            var open = new UiButton { AutoSize = true }; Bind(open, "Tray.WireGuard"); open.Click += delegate { if (OpenWireGuardRequested != null) OpenWireGuardRequested(); };
            connectionButtons.Controls.Add(reload); connectionButtons.Controls.Add(open); AddWide(connection, connectionButtons);
            AddNote(connection, Note("Settings.ExternalNote"));
            AddText(connection, "SplitProfile", "Settings.SplitProfile", settings.SplitProfile);
            AddText(connection, "FullProfile", "Settings.FullProfile", settings.FullProfile);
            var executable = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Top, ColumnCount = 2, Margin = new Padding(0) };
            executable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); executable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var path = Editor("WssExecutable", settings.WssExecutable); var browse = new UiButton { AutoSize = true, Quiet = true }; Bind(browse, "Settings.Browse");
            browse.Click += delegate { using (var dialog = new OpenFileDialog { Filter = L.Get("Settings.ExecutableFilter"), CheckFileExists = true }) if (dialog.ShowDialog(this) == DialogResult.OK) path.Text = dialog.FileName; };
            executable.Controls.Add(path, 0, 0); executable.Controls.Add(browse, 1, 0); AddRow(connection, "Settings.WssExecutable", executable);
            AddNumber(connection, "ListenerPort", "Settings.ListenerPort", settings.ListenerPort, 1, 65535);
            AddNote(connection, Note("Settings.ControlNote"));
            var network = AddTab(tabs, "Settings.Monitoring");
            AddNote(network, Note("Settings.MonitoringNote"));
            AddText(network, "Router", "Settings.Router", settings.Router);
            AddText(network, "HomeHost", "Settings.HomeHost", settings.HomeHost);
            AddText(network, "HomeNetwork", "Settings.HomeNetwork", settings.HomeNetwork);
            AddText(network, "DnsServer", "Settings.DnsServer", settings.DnsServer);
            AddText(network, "DnsName", "Settings.DnsName", settings.DnsName);
            AddNumber(network, "IntervalSeconds", "Settings.Interval", settings.IntervalSeconds, 5, 300);
            AddNumber(network, "TimeoutMilliseconds", "Settings.Timeout", settings.TimeoutMilliseconds, 250, 3000);
            var general = AddTab(tabs, "Settings.General");
            foreach (var culture in L.GetLanguages()) language.Items.Add(culture);
            AddRow(general, "Ui.Language", language); startup.Checked = startAtLogin; Bind(startup, "Tray.Startup"); AddWide(general, startup);
            AddNote(general, Note("Settings.ImmediateNote"));
            language.SelectedIndexChanged += delegate {
                if (initializing) return;
                var selected = language.SelectedItem as CultureInfo;
                if (selected != null && LanguageRequested != null) LanguageRequested(selected.Name);
                Localize(); ReadRelay();
            };
            startup.Click += delegate {
                bool desired = !startup.Checked;
                try { if (StartupRequested != null) StartupRequested(desired); startup.Checked = desired; error.Text = ""; }
                catch (Exception e) { ShowError(e); }
            };
            error.AutoSize = true; error.Dock = DockStyle.Top; error.ForeColor = UiTheme.Tone(Health.Broken); error.Margin = new Padding(0, 10, 0, 6); error.AccessibleRole = AccessibleRole.Alert;
            var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top, FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(0, 8, 0, 0) };
            var save = new UiButton { AutoSize = true, Selected = true }; Bind(save, "Settings.Save"); save.Click += delegate { SaveDraft(); };
            var cancel = new UiButton { AutoSize = true, DialogResult = DialogResult.Cancel }; Bind(cancel, "Settings.Cancel"); buttons.Controls.Add(save); buttons.Controls.Add(cancel);
            AcceptButton = save; CancelButton = cancel; root.Controls.Add(tabs, 0, 0); root.Controls.Add(error, 0, 1); root.Controls.Add(buttons, 0, 2); Controls.Add(root);
            Localize(); initializing = false; Shown += delegate { ReadRelay(); };
            Resize += delegate { error.MaximumSize = new Size(Math.Max(250, ClientSize.Width - 40), 0); };
        }
        private static TextBox ReadOnlyBox() { return new TextBox { ReadOnly = true, Dock = DockStyle.Fill, BackColor = SystemColors.Control, BorderStyle = BorderStyle.FixedSingle }; }
        private static void Bind(Control control, string key) { control.Tag = key; control.Text = L.Get(key); control.AccessibleName = control.Text; }
        private static Label Note(string key) { var label = new Label(); Bind(label, key); return label; }
        private static TableLayoutPanel AddTab(TabControl tabs, string key)
        {
            var page = new TabPage { Padding = new Padding(14), BackColor = UiTheme.Background, AutoScroll = true }; Bind(page, key); tabs.TabPages.Add(page);
            var table = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, GrowStyle = TableLayoutPanelGrowStyle.AddRows };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 188)); table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); page.Controls.Add(table); return table;
        }
        private static void AddRow(TableLayoutPanel table, string key, Control value)
        {
            var label = new Label { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 6, 10, 10) }; Bind(label, key);
            value.Dock = DockStyle.Fill; value.Margin = new Padding(0, 3, 0, 10); value.AccessibleName = label.Text;
            int row = table.RowCount++; table.RowStyles.Add(new RowStyle(SizeType.AutoSize)); table.Controls.Add(label, 0, row); table.Controls.Add(value, 1, row);
        }
        private static void AddWide(TableLayoutPanel table, Control value)
        {
            int row = table.RowCount++; table.RowStyles.Add(new RowStyle(SizeType.AutoSize)); table.Controls.Add(value, 0, row); table.SetColumnSpan(value, 2);
        }
        private static void AddNote(TableLayoutPanel table, Label label)
        {
            label.AutoSize = true; label.Dock = DockStyle.Top; label.ForeColor = UiTheme.Muted; label.Margin = new Padding(0, 0, 0, 12);
            AddWide(table, label); table.SizeChanged += delegate { label.MaximumSize = new Size(Math.Max(250, table.ClientSize.Width - 8), 0); };
        }
        private TextBox Editor(string name, string value)
        {
            var box = new TextBox { Name = name, Text = value ?? "", Dock = DockStyle.Fill, MaxLength = name == "WssExecutable" ? 2048 : 253 }; editors.Add(name, box); return box;
        }
        private void AddText(TableLayoutPanel table, string name, string key, string value) { AddRow(table, key, Editor(name, value)); }
        private void AddNumber(TableLayoutPanel table, string name, string key, int value, int min, int max)
        {
            var number = new NumericUpDown { Name = name, Minimum = min, Maximum = max, Value = Math.Max(min, Math.Min(max, value)), ThousandsSeparator = false };
            editors.Add(name, number); AddRow(table, key, number);
        }
        internal Settings ReadDraft()
        {
            var result = SettingsRules.Copy(original); result.Language = L.Language;
            foreach (var item in editors)
            {
                var number = item.Value as NumericUpDown;
                typeof(Settings).GetField(item.Key).SetValue(result, number == null ? (object)item.Value.Text.Trim() : decimal.ToInt32(number.Value));
            }
            return result;
        }
        internal bool SaveDraft()
        {
            try
            {
                var draft = ReadDraft(); SettingsRules.Validate(draft);
                if (SaveRequested == null) throw new InvalidOperationException(L.Get("Settings.SaveUnavailable"));
                SaveRequested(draft); SavedSettings = draft; DialogResult = DialogResult.OK; return true;
            }
            catch (Exception e) { ShowError(e); return false; }
        }
        private void ShowError(Exception e) { error.Text = L.Format("Settings.SaveError", SystemErrors.Describe(e)); error.AccessibleName = error.Text; }
        internal void UpdateConnection(Observation o, bool invalidated)
        {
            profile.Text = ConnectionView.ActiveProfile(o, original, invalidated, DateTime.Now);
            profileTime.Text = o == null || invalidated ? L.Get("Ui.NoMeasurement") : L.Format("Ui.Checked", o.CheckedAt.ToString("T"));
            if (invalidated || o == null || o.Changed || o.ListenerUnknown || !o.ListenerVerified
                || DateTime.Now - o.CheckedAt > TimeSpan.FromSeconds(original.IntervalSeconds + 5)) InvalidateRelay();
            else if (lastRelayObservation != o.CheckedAt) { lastRelayObservation = o.CheckedAt; ReadRelay(); }
        }
        internal void InvalidateRelay()
        {
            relayVersion++; relay.Text = peers.Text = L.Get("Common.Unknown"); relayNote.Text = L.Get("Settings.RefreshHint");
        }
        private async void ReadRelay()
        {
            if (IsDisposed) return;
            InvalidateRelay();
            if (relayBusy) { relayQueued = true; return; }
            relayBusy = true; reload.Enabled = false; int version = relayVersion;
            try
            {
                var info = await queryRelay(original);
                if (IsDisposed || version != relayVersion) return;
                PresentRelay(info);
            }
            catch (Exception) { if (!IsDisposed && version == relayVersion) relayNote.Text = L.Get("Settings.RelayUnreadable"); }
            finally
            {
                relayBusy = false;
                if (!IsDisposed) { reload.Enabled = true; if (relayQueued) { relayQueued = false; ReadRelay(); } }
            }
        }
        internal void PresentRelay(RelayDetails info)
        {
            relay.Text = info.Server; peers.Text = info.Peers;
            relayNote.Text = info.Note + " · " + info.CheckedAt.ToString("T");
        }
        internal void Localize()
        {
            bool wasInitializing = initializing; initializing = true;
            Text = "TunnelWatch · " + L.Get("Ui.Settings"); Translate(Controls);
            string current = L.DisplayedLanguage(L.Language, L.GetLanguages());
            for (int i = 0; i < language.Items.Count; i++) if (((CultureInfo)language.Items[i]).Name == current) language.SelectedIndex = i;
            initializing = wasInitializing;
        }
        private static void Translate(Control.ControlCollection controls)
        {
            foreach (Control control in controls)
            {
                var key = control.Tag as string; if (key != null) Bind(control, key);
                var table = control as TableLayoutPanel;
                if (table != null) for (int row = 0; row < table.RowCount; row++)
                {
                    var label = table.GetControlFromPosition(0, row); var value = table.GetControlFromPosition(1, row);
                    if (label is Label && label.Tag is string && value != null) value.AccessibleName = L.Get((string)label.Tag);
                }
                Translate(control.Controls);
            }
        }
    }
}

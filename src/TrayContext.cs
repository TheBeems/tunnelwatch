using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TunnelWatch
{
    internal sealed class TrayContext : ApplicationContext
    {
        private Settings settings;
        private Collector collector;
        private readonly NotifyIcon tray;
        private readonly Control dispatcher = new Control();
        private readonly Timer timer = new Timer();
        private readonly Dictionary<Health, Icon> icons = new Dictionary<Health, Icon>();
        private StatusForm form;
        private readonly ExplorerWindow explorer;
        private readonly string[] args;
        private bool busy, queued, queuedDns, disposed, suspended;
        private bool actionBusy;
        private ToolStripMenuItem exit, languageMenu;
        private SettingsForm settingsWindow;
        private int generation, roundCount;
        private Observation observation;
        private Status status = new Status { Health = Health.Checking, Title = L.Get("Status.Checking"), Profile = L.Get("Service.Unknown"), Reason = L.Get("Status.CheckingReason") };
        private readonly Stopwatch measurement = Stopwatch.StartNew();
        private TimeSpan startCpu;
        private readonly List<long> working = new List<long>(), privateBytes = new List<long>();
        private readonly List<double> rounds = new List<double>();
        private int measureSeconds;
        private bool measured, warmed, previewSaved;
        private TimeSpan sampleCpu;
        private double sampleElapsed, idleCpuMs, idleElapsedMs, checkCpuMs, checkElapsedMs;
        private int sampleRounds;
        private ToolStripMenuItem startup;

        internal TrayContext(Settings s, string[] arguments)
        {
            settings = s; args = arguments; collector = new Collector(s);
            dispatcher.CreateControl();
            foreach (Health health in Enum.GetValues(typeof(Health))) icons[health] = MakeIcon(health);
            form = CreateStatusForm();
            tray = new NotifyIcon { ContextMenuStrip = CreateTrayMenu(), Icon = icons[Health.Checking], Text = "TunnelWatch · " + L.Get("Status.Checking"), Visible = true };
            tray.MouseClick += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) ShowStatus(); };
            explorer = new ExplorerWindow(delegate { if (!disposed) { tray.Visible = false; tray.Visible = true; } });
            NetworkChange.NetworkAddressChanged += NetworkChanged;
            NetworkChange.NetworkAvailabilityChanged += AvailabilityChanged;
            SystemEvents.PowerModeChanged += PowerChanged;
            int index = Array.IndexOf(args, "--measure");
            if (index >= 0 && index + 1 < args.Length) int.TryParse(args[index + 1], out measureSeconds);
            if (measureSeconds != 0 && (measureSeconds < 20 || measureSeconds > 600)) throw new ArgumentException(L.Get("Error.MeasureRange"));
            timer.Interval = 1000; timer.Tick += Tick; timer.Start();
            Request(false, true);
            if (args.Contains("--show")) ShowStatus();
        }
        private StatusForm CreateStatusForm()
        {
            var window = new StatusForm(settings);
            window.RefreshRequested += delegate { Request(false, false); };
            window.DnsRequested += delegate { Request(true, false); };
            window.ActionRequested += RunAction;
            window.SettingsRequested += ShowSettings;
            return window;
        }
        private ContextMenuStrip CreateTrayMenu()
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add(L.Get("Tray.Show"), null, delegate { ShowStatus(); });
            menu.Items.Add(L.Get("Tray.Refresh"), null, delegate { Request(false, false); });
            menu.Items.Add(L.Get("Tray.WireGuard"), null, delegate { OpenWireGuard(); });
            var preferences = new ToolStripMenuItem(L.Get("Ui.Settings"));
            preferences.DropDownItems.Add(L.Get("Settings.ConnectionSettings"), null, delegate { ShowSettings(form); });
            preferences.DropDownItems.Add(new ToolStripSeparator());
            languageMenu = CreateLanguageMenu(); preferences.DropDownItems.Add(languageMenu); menu.Items.Add(preferences);
            menu.Items.Add(new ToolStripSeparator());
            startup = new ToolStripMenuItem(L.Get("Tray.Startup"));
            startup.Checked = IsStartup(); startup.Click += delegate { ToggleStartup(); }; menu.Items.Add(startup);
            exit = new ToolStripMenuItem(L.Get("Tray.Exit")); exit.Click += delegate { if (!actionBusy) ExitThread(); }; menu.Items.Add(exit);
            return menu;
        }
        private ToolStripMenuItem CreateLanguageMenu()
        {
            var menu = new ToolStripMenuItem(L.Get("Ui.Language")) { Enabled = !actionBusy };
            var available = L.GetLanguages(); string selected = L.DisplayedLanguage(L.Language, available);
            foreach (var culture in available)
            {
                string name = culture.Name;
                var item = new ToolStripMenuItem(culture.NativeName) { Checked = name == selected };
                item.Click += delegate { OnUi(delegate { ChangeLanguage(name); }); };
                menu.DropDownItems.Add(item);
            }
            return menu;
        }
        private void ShowSettings(Control anchor)
        {
            if (disposed || actionBusy) return;
            if (settingsWindow != null) { settingsWindow.Activate(); return; }
            using (var window = new SettingsForm(settings, IsStartup()))
            {
                settingsWindow = window;
                window.UpdateConnection(observation, observation == null);
                window.RefreshRequested += delegate { Request(false, true); };
                window.OpenWireGuardRequested += OpenWireGuard;
                window.LanguageRequested += ChangeLanguage;
                window.StartupRequested += SetStartup;
                window.SaveRequested += SettingsLoader.Save;
                try
                {
                    // The dispatcher remains alive while a language switch replaces
                    // StatusForm. Do not make that replaceable form the modal owner.
                    if (window.ShowDialog(dispatcher) == DialogResult.OK) ApplySettings(window.SavedSettings);
                }
                finally { settingsWindow = null; }
            }
        }
        private void ApplySettings(Settings updated)
        {
            settings = SettingsRules.Copy(updated); collector = new Collector(settings);
            generation++; observation = null;
            status = new Status { Health = Health.Checking, Title = L.Get("Status.Checking"), Profile = L.Get("Service.Unknown"), Reason = L.Get("Status.Recheck") };
            ReplaceStatusForm(); Request(false, false);
        }
        private void ReplaceStatusForm()
        {
            var oldForm = form; bool visible = oldForm.Visible;
            form = CreateStatusForm(); form.RestoreLayout(oldForm.Bounds, oldForm.DetailsExpanded); form.SetBusy(busy);
            oldForm.AllowClose = true; oldForm.Close(); oldForm.Dispose();
            var oldMenu = tray.ContextMenuStrip; tray.ContextMenuStrip = CreateTrayMenu(); oldMenu.Dispose();
            Render(); if (visible) { form.Show(); form.Activate(); }
        }
        private void ChangeLanguage(string language)
        {
            if (disposed || actionBusy) return;
            try { LanguagePreferences.Save(language); }
            catch (Exception e) { MessageBox.Show(form, L.Format("Error.LanguageSave", SystemErrors.Describe(e)), L.Get("Ui.Settings"), MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
            if (language == L.Language) return;
            L.Configure(language); settings.Language = language;
            // In-flight observations captured the previous UI culture. Discard
            // them and remeasure instead of displaying mixed-language diagnostics.
            generation++; observation = null;
            status = new Status { Health = Health.Checking, Title = L.Get("Status.Checking"), Profile = L.Get("Service.Unknown"), Reason = L.Get("Status.CheckingReason") };
            ReplaceStatusForm();
            if (settingsWindow != null) settingsWindow.Localize();
            Request(false, false);
        }
        private async void RunAction(string action)
        {
            if (actionBusy || disposed || settingsWindow != null) return;
            actionBusy = true; exit.Enabled = false; languageMenu.Enabled = false; form.SetActionBusy(true);
            generation++; observation = null; status = new Status { Health = Health.Checking, Title = L.Get("Status.Operating"), Profile = status.Profile, Reason = L.Get("Status.OperatingReason") }; Render();
            form.ActionMessage = L.Get("Action.Wait");
            try { form.ActionMessage = await new ActionRunner(settings).Run(action); }
            catch (System.ComponentModel.Win32Exception e) { form.ActionMessage = e.NativeErrorCode == 1223 ? L.Get("Action.Cancelled") : SystemErrors.Describe(e); }
            catch (Exception e) { form.ActionMessage = L.Format("Error.ActionDetail", SystemErrors.Describe(e)); }
            finally { actionBusy = false; if (!disposed) { exit.Enabled = true; languageMenu.Enabled = true; form.SetActionBusy(false); Request(false, true); } }
        }
        private DateTime next = DateTime.MinValue;
        private void Tick(object sender, EventArgs e)
        {
            if (suspended) return;
            if (!busy && DateTime.UtcNow >= next) { bool extra = queuedDns; queued = queuedDns = false; Request(extra, false); }
            if (observation != null && DateTime.Now - observation.CheckedAt > TimeSpan.FromSeconds(settings.IntervalSeconds + 5) && status.Health != Health.Checking)
            { status = new Status { Health = Health.Checking, Title = L.Get("Status.Stale"), Profile = status.Profile, Reason = L.Get("Status.StaleReason") }; Render(); }
            if (measureSeconds > 0 && !measured)
            {
                if (!warmed)
                {
                    if (measurement.Elapsed.TotalSeconds < 15) return;
                    using (var p = Process.GetCurrentProcess()) startCpu = p.TotalProcessorTime;
                    measurement.Restart(); working.Clear(); privateBytes.Clear(); rounds.Clear(); roundCount = 0; warmed = true;
                    sampleCpu = startCpu; sampleElapsed = 0; sampleRounds = 0;
                }
                using (var p = Process.GetCurrentProcess())
                {
                    p.Refresh(); working.Add(p.WorkingSet64); privateBytes.Add(p.PrivateMemorySize64);
                    var currentCpu = p.TotalProcessorTime; double currentElapsed = measurement.Elapsed.TotalMilliseconds;
                    double cpuDelta = (currentCpu - sampleCpu).TotalMilliseconds, elapsedDelta = currentElapsed - sampleElapsed;
                    if (busy || sampleRounds != roundCount) { checkCpuMs += cpuDelta; checkElapsedMs += elapsedDelta; } else { idleCpuMs += cpuDelta; idleElapsedMs += elapsedDelta; }
                    sampleCpu = currentCpu; sampleElapsed = currentElapsed; sampleRounds = roundCount;
                }
                if (measurement.Elapsed.TotalSeconds >= measureSeconds)
                {
                    measured = true;
                    using (var p = Process.GetCurrentProcess())
                    {
                        double cpu = (p.TotalProcessorTime - startCpu).TotalMilliseconds, elapsed = measurement.Elapsed.TotalMilliseconds;
                        Program.SaveReport(args, new { WarmupSeconds = 15, Seconds = elapsed / 1000, CpuMilliseconds = cpu, CpuPercentOfOneCore = cpu / elapsed * 100, IdleSampleSeconds = idleElapsedMs / 1000, IdleSampleCpuPercentOfOneCore = idleElapsedMs == 0 ? 0 : idleCpuMs / idleElapsedMs * 100, CheckSampleSeconds = checkElapsedMs / 1000, CheckSampleCpuPercentOfOneCore = checkElapsedMs == 0 ? 0 : checkCpuMs / checkElapsedMs * 100, WorkingSetMiBMin = working.Min() / 1048576.0, WorkingSetMiBMax = working.Max() / 1048576.0, PrivateMiBMax = privateBytes.Max() / 1048576.0, Rounds = roundCount, MeanRoundMilliseconds = rounds.Count == 0 ? 0 : rounds.Average(), MaxRoundMilliseconds = rounds.Count == 0 ? 0 : rounds.Max(), Architecture = Native.Architecture(), Administrator = observation != null && observation.Administrator, Status = status, Observation = observation });
                    }
                    if (args.Contains("--exit-after-measure")) ExitThread();
                }
            }
        }
        private void OnUi(Action action) { if (!disposed && dispatcher.IsHandleCreated) { try { dispatcher.BeginInvoke(action); } catch (InvalidOperationException) { } } }
        private void NetworkChanged(object sender, EventArgs e) { OnUi(delegate { Request(false, true); }); }
        private void AvailabilityChanged(object sender, NetworkAvailabilityEventArgs e) { NetworkChanged(sender, e); }
        private void PowerChanged(object sender, PowerModeChangedEventArgs e)
        {
            OnUi(delegate { if (e.Mode == PowerModes.Suspend) { suspended = true; generation++; observation = null; status = new Status { Health = Health.Checking, Title = L.Get("Status.Sleep"), Profile = status.Profile, Reason = L.Get("Status.SleepReason") }; Render(); } else if (e.Mode == PowerModes.Resume) { suspended = false; Request(false, true); } });
        }
        private async void Request(bool dns, bool invalidate)
        {
            if (disposed || suspended) return;
            if (actionBusy) { queued = true; queuedDns |= dns; return; }
            if (invalidate) { generation++; observation = null; status = new Status { Health = Health.Checking, Title = L.Get("Status.Checking"), Profile = status.Profile, Reason = L.Get("Status.Recheck") }; Render(); }
            if (busy) { queued = true; queuedDns |= dns; return; }
            busy = true; form.SetBusy(true); int startedGeneration = generation;
            try
            {
                var result = await collector.Collect(dns);
                if (disposed) return;
                roundCount++; rounds.Add(result.DurationMilliseconds); if (rounds.Count > 100) rounds.RemoveAt(0);
                if (generation != startedGeneration || suspended) { queued = true; return; }
                observation = result; status = Status.Evaluate(result, settings); Render();
                if (result.Changed) queued = true;
            }
            catch (Exception e)
            {
                if (!disposed && generation == startedGeneration) { observation = null; status = new Status { Health = Health.Checking, Title = L.Get("Status.Unavailable"), Profile = L.Get("Service.Unknown"), Reason = SystemErrors.Describe(e) }; Render(); }
                else if (!disposed) queued = true;
            }
            finally
            {
                busy = false;
                if (!disposed) { form.SetBusy(false); next = DateTime.UtcNow.AddSeconds(queued ? 1 : settings.IntervalSeconds); }
            }
        }
        private void Render()
        {
            if (disposed) return;
            tray.Icon = icons[status.Health]; string text = L.Format("Tray.Tooltip", status.Title, status.Profile); tray.Text = text.Length > 63 ? text.Substring(0, 63) : text;
            form.UpdateStatus(status, observation, icons[status.Health]);
            if (settingsWindow != null) settingsWindow.UpdateConnection(observation, observation == null);
            int preview = Array.IndexOf(args, "--preview");
            if (!previewSaved && observation != null && preview >= 0 && preview + 1 < args.Length)
            {
                previewSaved = true;
                // App-owned layout render, not a desktop screenshot or UI interaction check.
                form.PerformLayout(); using (var image = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size)); image.Save(args[preview + 1], System.Drawing.Imaging.ImageFormat.Png); }
            }
        }
        private void ShowStatus()
        {
            Render();
            if (!form.Visible) { var area = Screen.FromPoint(Cursor.Position).WorkingArea; form.Location = new Point(area.Right - form.Width - 12, area.Bottom - form.Height - 12); form.Show(); }
            form.Activate();
        }
        private void OpenWireGuard()
        {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WireGuard", "wireguard.exe");
            try { if (!File.Exists(path)) throw new FileNotFoundException(L.Get("Error.WireGuardMissing")); Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception e) { MessageBox.Show(form, SystemErrors.Describe(e), L.Get("Tray.WireGuard")); }
        }
        private string StartupCommand { get { return "\"" + Application.ExecutablePath + "\""; } }
        private bool IsStartup() { using (var key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run")) return key != null && (key.GetValue("TunnelWatch") as string) == StartupCommand; }
        private void ToggleStartup()
        {
            try
            {
                SetStartup(!IsStartup());
            }
            catch (Exception e) { MessageBox.Show(form, SystemErrors.Describe(e), L.Get("Tray.StartupTitle")); }
        }
        private void SetStartup(bool enabled)
        {
            using (var key = Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run"))
            {
                string existing = key.GetValue("TunnelWatch") as string;
                if (existing != null && existing != StartupCommand) throw new InvalidOperationException(L.Get("Error.StartupConflict"));
                if (enabled) key.SetValue("TunnelWatch", StartupCommand); else key.DeleteValue("TunnelWatch", false);
            }
            startup.Checked = IsStartup();
        }
        internal static Icon MakeIcon(Health health)
        {
            Color color = health == Health.Healthy ? Color.FromArgb(22, 133, 68) : health == Health.Broken ? Color.FromArgb(190, 38, 38) : health == Health.Checking ? Color.FromArgb(191, 112, 0) : health == Health.Home ? Color.FromArgb(29, 78, 216) : Color.FromArgb(93, 102, 112);
            using (var bitmap = new Bitmap(32, 32)) using (var g = Graphics.FromImage(bitmap)) using (var brush = new SolidBrush(color)) using (var pen = new Pen(Color.White, 3))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias; g.FillEllipse(brush, 1, 1, 30, 30);
                if (health == Health.Healthy) g.DrawLines(pen, new[] { new Point(8, 16), new Point(14, 22), new Point(24, 10) });
                else if (health == Health.Broken) { g.DrawLine(pen, 10, 10, 22, 22); g.DrawLine(pen, 22, 10, 10, 22); }
                else if (health == Health.Checking) { g.DrawArc(pen, 8, 8, 16, 16, 35, 280); g.DrawLines(pen, new[] { new Point(22, 6), new Point(24, 12), new Point(18, 12) }); }
                else if (health == Health.Home) { g.DrawLines(pen, new[] { new Point(7, 15), new Point(16, 8), new Point(25, 15) }); g.DrawRectangle(pen, 11, 15, 10, 9); }
                else g.DrawLine(pen, 9, 16, 23, 16);
                IntPtr handle = bitmap.GetHicon(); try { return (Icon)Icon.FromHandle(handle).Clone(); } finally { Native.DestroyIcon(handle); }
            }
        }
        protected override void ExitThreadCore() { Dispose(); base.ExitThreadCore(); }
        protected override void Dispose(bool disposing)
        {
            if (disposed) return; disposed = true;
            if (disposing) { timer.Stop(); timer.Dispose(); NetworkChange.NetworkAddressChanged -= NetworkChanged; NetworkChange.NetworkAvailabilityChanged -= AvailabilityChanged; SystemEvents.PowerModeChanged -= PowerChanged; if (settingsWindow != null) settingsWindow.Close(); tray.Visible = false; tray.ContextMenuStrip.Dispose(); tray.Dispose(); explorer.Dispose(); form.AllowClose = true; form.Close(); form.Dispose(); dispatcher.Dispose(); foreach (var icon in icons.Values) icon.Dispose(); }
            base.Dispose(disposing);
        }
    }
    internal sealed class ExplorerWindow : NativeWindow, IDisposable
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern uint RegisterWindowMessage(string name);
        private readonly uint message = RegisterWindowMessage("TaskbarCreated"); private readonly Action reset;
        internal ExplorerWindow(Action action) { reset = action; CreateHandle(new CreateParams { Caption = "TunnelWatch events" }); }
        protected override void WndProc(ref Message m) { if ((uint)m.Msg == message) reset(); base.WndProc(ref m); }
        public void Dispose() { DestroyHandle(); }
    }
}

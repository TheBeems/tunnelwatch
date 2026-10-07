#if UI_REVIEW
using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace TunnelWatch
{
    // Isolated native review of the production form. No collector, tray, helper,
    // real configuration, network calls or saved user preferences are used.
    internal static class UiReview
    {
        [STAThread]
        private static void Main(string[] args)
        {
            int preview = Array.IndexOf(args, "--settings-preview");
            if (preview >= 0 && preview + 1 < args.Length)
            {
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                SettingsLayout.SavePreviews(args[preview + 1]); return;
            }
            int languageIndex = Array.IndexOf(args, "--language");
            L.Configure(languageIndex >= 0 && languageIndex + 1 < args.Length ? args[languageIndex + 1] : "en");
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            var settings = new Settings { Router = "192.0.2.1", HomeHost = "198.51.100.33", HomeNetwork = "198.51.100.0/24" };
            int scenarioIndex = Array.IndexOf(args, "--scenario");
            string scenario = scenarioIndex >= 0 && scenarioIndex + 1 < args.Length ? args[scenarioIndex + 1] : "off"; StatusForm form = null;
            Action render = delegate
            {
                var o = new Observation { CheckedAt = DateTime.Now, Split = ServiceState.Stopped, Full = ServiceState.Missing,
                    RouterRoute = new Route { Error = "Example route unavailable" }, HomeRoute = new Route { Error = "Example home route unavailable" },
                    WssDetail = "Example listener observation", Architecture = "UI review", DurationMilliseconds = 85 };
                if (scenario == "split" || scenario == "full" || scenario == "broken")
                {
                    bool full = scenario == "full"; o.Split = full ? ServiceState.Stopped : ServiceState.Running; o.Full = full ? ServiceState.Running : ServiceState.Missing;
                    o.ListenerVerified = scenario != "broken"; o.RouterReachable = true;
                    o.RouterRoute = new Route { Adapter = full ? settings.FullProfile : settings.SplitProfile, Description = "WireGuard Tunnel", Up = true,
                        Source = "192.0.2.2", Index = 7, Luid = 7, NextHop = "0.0.0.0", Prefix = "192.0.2.0", PrefixLength = 24 };
                    if (scenario == "broken") o.WssDetail = L.Get("Status.WssMissingReason");
                }
                if (scenario == "home")
                {
                    o.HomeReachable = true; o.HomeRoute = new Route { Adapter = "Example Wi-Fi", Physical = true, Up = true, Source = "198.51.100.7", Prefix = "198.51.100.0", PrefixLength = 24, NextHop = "0.0.0.0" };
                }
                var status = Status.Evaluate(o, settings);
                if (scenario == "checking") { status = new Status { Health = Health.Checking, Title = L.Get("Status.Checking"), Reason = L.Get("Status.CheckingReason") }; o = null; }
                using (var icon = TrayContext.MakeIcon(status.Health)) form.UpdateStatus(status, o, icon);
                form.SetBusy(scenario == "checking");
            };
            Func<StatusForm> create = delegate
            {
                var f = new StatusForm(settings) { AllowClose = true, ShowInTaskbar = true, StartPosition = FormStartPosition.CenterScreen };
                f.Text = "TunnelWatch — UI review (F1–F6, F8 language)";
                f.ActionRequested += delegate(string action) { f.ActionMessage = "UI review: " + action + " (no system change)"; };
                f.RefreshRequested += delegate { render(); }; f.DnsRequested += delegate { f.ActionMessage = "UI review: DNS (no network request)"; };
                f.SettingsRequested += delegate(Control anchor)
                {
                    var menu = new ContextMenuStrip(); menu.Items.Add("UI review: F8 = English / Nederlands"); menu.Show(anchor, new Point(0, anchor.Height));
                    // Dispose after ToolStrip finishes processing its close event.
                    menu.Closed += delegate { f.BeginInvoke(new Action(menu.Dispose)); };
                };
                return f;
            };
            form = create();
            Action<StatusForm> attach = null;
            attach = delegate(StatusForm f)
            {
                f.KeyDown += delegate(object sender, KeyEventArgs e)
                {
                    if (e.KeyCode >= Keys.F1 && e.KeyCode <= Keys.F6) { scenario = new[] { "off", "split", "full", "broken", "home", "checking" }[(int)e.KeyCode - (int)Keys.F1]; render(); e.Handled = true; }
                    if (e.KeyCode == Keys.F8)
                    {
                        Rectangle bounds = f.Bounds; bool expanded = f.DetailsExpanded; L.Configure(L.Language == "en" ? "nl" : "en");
                        form = create(); form.RestoreLayout(bounds, expanded); attach(form); render(); form.Show(); f.Hide();
                        form.FormClosed += delegate { f.Close(); }; e.Handled = true;
                    }
                };
            };
            attach(form); render(); if (Array.IndexOf(args, "--details") >= 0) form.RestoreLayout(form.Bounds, true); Application.Run(form);
        }
    }
}
#endif

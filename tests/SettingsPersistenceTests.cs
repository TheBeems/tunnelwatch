#if !CORE_TESTS
using System;
using System.IO;
using System.Web.Script.Serialization;
using System.Collections.Generic;
using System.Windows.Forms;

namespace TunnelWatch
{
    internal static class SettingsPersistenceTests
    {
        internal static int Run()
        {
            int passed = 0;
            string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder); string path = Path.Combine(folder, "config.local.json");
            var settings = SettingsTests.Example();
            File.WriteAllText(path, "{\"Custom\":{\"Keep\":true}}");
            SettingsLoader.Save(path, settings);
            var restored = SettingsLoader.Load(path);
            if (restored.Router != settings.Router || restored.DnsName != settings.DnsName || restored.WssExecutable != settings.WssExecutable) throw new Exception("Edited settings do not survive reload."); passed++;
            var document = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(path));
            if (!document.ContainsKey("Custom") || !((Dictionary<string, object>)document["Custom"]).ContainsKey("Keep")) throw new Exception("Unknown configuration removed by Save."); passed++;
            string saved = File.ReadAllText(path); var invalid = SettingsRules.Copy(settings); invalid.Router = "invalid"; bool failed = false;
            try { SettingsLoader.Save(path, invalid); } catch (InvalidOperationException) { failed = true; }
            if (!failed || saved != File.ReadAllText(path)) throw new Exception("Invalid Save damages configuration."); passed++;
            failed = false;
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                try { SettingsLoader.Save(path, settings); } catch (IOException) { failed = true; }
            if (!failed || saved != File.ReadAllText(path) || Directory.GetFiles(folder, "*.tmp").Length != 0) throw new Exception("Failed Save damages configuration or leaks temporary files."); passed++;
            settings.IntervalSeconds = 25; settings.SplitProfile = "example-split"; SettingsLoader.Save(path, settings);
            if (SettingsLoader.Load(path).IntervalSeconds != 25 || SettingsLoader.Load(path).SplitProfile != "example-split" || Directory.GetFiles(folder).Length != 1) throw new Exception("Atomic replacement does not persist all edits."); passed++;
            File.WriteAllText(path, "null"); failed = false;
            try { SettingsLoader.Load(path); } catch (InvalidOperationException) { failed = true; }
            if (!failed) throw new Exception("Null configuration accepted."); passed++;
            using (var form = new SettingsForm(settings, false))
            {
                Find(form, "Router").Text = "203.0.113.1";
                if (settings.Router == form.ReadDraft().Router || settings.Router != "192.0.2.1") throw new Exception("Typing edits live settings."); passed++;
                L.Configure("nl"); form.Localize();
                if (form.ReadDraft().Router != "203.0.113.1" || !form.Text.Contains("Instellingen")) throw new Exception("Language switch loses unsaved settings."); passed++;
                L.Configure("en"); form.Localize();
                bool called = false; form.SaveRequested += delegate { called = true; throw new IOException("Synthetic storage failure"); };
                if (form.SaveDraft() || !called || form.SavedSettings != null || form.DialogResult == DialogResult.OK || !form.SaveError.Contains("Synthetic storage failure")) throw new Exception("Failed Save closes the editor or claims success."); passed++;
            }
            using (var form = new SettingsForm(settings, false))
            {
                Find(form, "Router").Text = "invalid"; bool called = false; form.SaveRequested += delegate { called = true; };
                if (form.SaveDraft() || called || form.SaveError.Length == 0) throw new Exception("UI Save bypasses validation."); passed++;
            }
            using (var form = new SettingsForm(settings, false))
            {
                Find(form, "Router").Text = "203.0.113.9"; Settings captured = null; form.SaveRequested += delegate(Settings candidate) { captured = SettingsRules.Copy(candidate); };
                if (!form.SaveDraft() || captured == null || captured.Router != "203.0.113.9" || form.DialogResult != DialogResult.OK) throw new Exception("Successful Save does not apply draft."); passed++;
            }
            using (var form = new SettingsForm(settings, false))
            {
                form.PresentRelay(new RelayDetails { Server = "wss://relay.example.test", Peers = "192.0.2.80:443", Note = "Synthetic", CheckedAt = DateTime.Now });
                form.UpdateConnection(new Observation { CheckedAt = DateTime.Now, Split = ServiceState.Running, Full = ServiceState.Stopped, ListenerVerified = false }, false);
                if (Find(form, "RelayServer").Text != L.Get("Common.Unknown") || Find(form, "TcpPeers").Text != L.Get("Common.Unknown")) throw new Exception("Stopped WSS keeps an old connected peer."); passed++;
            }
            return passed;
        }
        private static Control Find(Control root, string name)
        {
            var matches = root.Controls.Find(name, true);
            if (matches.Length != 1) throw new Exception("Editor missing: " + name);
            return matches[0];
        }
    }
}
#endif

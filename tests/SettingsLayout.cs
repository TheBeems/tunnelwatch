#if UI_REVIEW
using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System.Threading.Tasks;

namespace TunnelWatch
{
    internal static class SettingsLayout
    {
        internal static void SavePreviews(string directory)
        {
            Directory.CreateDirectory(directory);
            foreach (string culture in new[] { "en", "nl" })
            {
                L.Configure(culture);
                var settings = SettingsTests.Example();
                var sample = new RelayDetails { Server = "wss://relay.example.test", Peers = "192.0.2.80:443\r\n[2001:db8::80]:443", Note = L.Get("Settings.RelayMeasured"), CheckedAt = DateTime.Now };
                using (var form = new SettingsForm(settings, false, delegate { return Task.FromResult(sample); }))
                {
                    form.UpdateConnection(new Observation { CheckedAt = DateTime.Now, Split = ServiceState.Running, Full = ServiceState.Stopped }, false);
                    // Exercise our own controls with fictional data off-screen.
                    // DrawToBitmap remains a layout render, not a desktop capture.
                    form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-10000, -10000); form.Show();
                    var tabs = (TabControl)form.Controls.Find("SettingsTabs", true)[0];
                    for (int tab = 0; tab < tabs.TabPages.Count; tab++)
                    {
                        tabs.SelectedIndex = tab; form.PerformLayout(); Application.DoEvents();
                        using (var bitmap = new Bitmap(form.Width, form.Height))
                        {
                            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                            bitmap.Save(Path.Combine(directory, "settings-" + culture + "-" + tab + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                        }
                    }
                }
            }
        }
    }
}
#endif

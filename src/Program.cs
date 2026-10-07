using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using System.Web.Script.Serialization;

namespace TunnelWatch
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                L.Configure("en");
                if (args.Contains("--self-test")) { int passed = Tests.Run(); if (args.Contains("--report")) SaveReport(args, new { Passed = passed }); return 0; }
                Settings settings = SettingsLoader.Load();
                if (args.Contains("--observe"))
                {
                    var o = new Collector(settings).Collect(args.Contains("--dns")).GetAwaiter().GetResult();
                    var relay = args.Contains("--relay") ? RelayReader.Read(settings).GetAwaiter().GetResult() : null;
                    SaveReport(args, new { Observation = o, Status = Status.Evaluate(o, settings), Relay = relay }); return 0;
                }
                bool created;
                using (var mutex = new Mutex(true, "Local\\TunnelWatch-" + Environment.UserName, out created))
                {
                    if (!created) { MessageBox.Show(L.Get("Error.AlreadyRunning"), "TunnelWatch"); return 0; }
                    Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                    using (var context = new TrayContext(settings, args)) Application.Run(context);
                    mutex.ReleaseMutex();
                }
                return 0;
            }
            catch (Exception e)
            {
                if (args.Contains("--observe") || args.Contains("--self-test")) { Console.Error.WriteLine(e.ToString()); return 1; }
                MessageBox.Show(e.Message, L.Get("Error.Startup"), MessageBoxButtons.OK, MessageBoxIcon.Error); return 1;
            }
        }
        internal static void SaveReport(string[] args, object report)
        {
            string json = new JavaScriptSerializer().Serialize(report);
            int index = Array.IndexOf(args, "--report");
            if (index >= 0 && index + 1 < args.Length) File.WriteAllText(args[index + 1], json);
            else Console.WriteLine(json);
        }
    }
}

using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace TunnelWatch
{
    internal sealed class ActionResult
    {
        public string OperationId { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; }
    }
    internal sealed class ActionRunner
    {
        private readonly Settings settings;
        internal ActionRunner(Settings s) { settings = s; }
        private static string Quote(string text) { if (text.IndexOf('"') >= 0) throw new ArgumentException(L.Get("Error.Path")); return "\"" + text + "\""; }
        internal async Task<string> Run(string action)
        {
            if (action != "WssStart" && action != "WssStop" && action != "Split" && action != "Full" && action != "Off") throw new ArgumentException(L.Get("Error.Action"));
            if (!SettingsRules.CanControl(settings))
                throw new InvalidOperationException(L.Get("Error.Installation"));
            string script = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Control-TunnelWatch.ps1");
            if (!File.Exists(script)) throw new FileNotFoundException(L.Get("Error.HelperMissing"));
            string id = Guid.NewGuid().ToString("N"), sid;
            using (var identity = WindowsIdentity.GetCurrent()) sid = identity.User.Value;
            string resultPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "action-result.local.json");
            var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell\\v1.0\\powershell.exe")) {
                Arguments = "-NoProfile -ExecutionPolicy Bypass -File " + Quote(script) + " -Action " + action + " -UserSid " + sid + " -OperationId " + id + " -ResultPath " + Quote(resultPath) + " -Language " + Quote(L.Language),
                UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden
            };
            return await Task.Run(delegate
            {
                using (var process = Process.Start(start))
                {
                    process.WaitForExit(); // Never kill a helper halfway through route/DNS cleanup.
                    if (!File.Exists(resultPath)) throw new InvalidOperationException(L.Get("Error.NoActionResult"));
                    var result = new JavaScriptSerializer().Deserialize<ActionResult>(File.ReadAllText(resultPath));
                    if (result.OperationId != id) throw new InvalidOperationException(L.Get("Error.StaleActionResult"));
                    if (!result.Success || process.ExitCode != 0) throw new InvalidOperationException(result.Message ?? L.Get("Error.ActionFailed"));
                    return result.Message;
                }
            });
        }
    }
}

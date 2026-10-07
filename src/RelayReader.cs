using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management;
using System.Threading.Tasks;

namespace TunnelWatch
{
    internal sealed class RelayDetails
    {
        public string Server, Peers, Note;
        public DateTime CheckedAt;
    }
    internal static class RelayReader
    {
        internal static Task<RelayDetails> Read(Settings settings)
        {
            var copy = SettingsRules.Copy(settings);
            return Task.Run(delegate { return Collect(copy); });
        }
        private static RelayDetails Collect(Settings settings)
        {
            var result = new RelayDetails { Server = L.Get("Common.Unknown"), Peers = L.Get("Common.Unknown") };
            try
            {
                var owners = Native.ListenerOwners(settings.ListenerPort);
                if (owners.Count == 0) { result.Note = L.Get("Settings.NoTransport"); return result; }
                if (owners.Count != 1) { result.Note = L.Get("Wss.MultipleOwners"); return result; }
                int pid = owners[0];
                string expected = Path.GetFullPath(Environment.ExpandEnvironmentVariables(settings.WssExecutable));
                if (!string.Equals(Native.ProcessPath(pid), expected, StringComparison.OrdinalIgnoreCase)) { result.Note = L.Get("Wss.WrongOwner"); return result; }
                using (var process = Process.GetProcessById(pid))
                {
                    long started = process.StartTime.ToUniversalTime().Ticks;
                    string server = null, note = null;
                    try
                    {
                        var options = new EnumerationOptions { ReturnImmediately = false, Timeout = TimeSpan.FromSeconds(2) };
                        using (var query = new ManagementObjectSearcher("root\\cimv2", "SELECT CommandLine FROM Win32_Process WHERE ProcessId = " + pid.ToString(CultureInfo.InvariantCulture), options))
                        using (var rows = query.Get())
                            foreach (ManagementObject row in rows)
                                using (row) { server = RelayAddress.FromCommandLine(row["CommandLine"] as string); }
                        if (server == null) note = L.Get("Settings.RelayUnknown");
                    }
                    catch (Exception) { note = L.Get("Settings.RelayUnreadable"); } // Never put command lines in errors.
                    var peers = Native.TcpPeers(pid);
                    var after = Native.ListenerOwners(settings.ListenerPort);
                    using (var current = Process.GetProcessById(pid))
                        if (process.HasExited || after.Count != 1 || after[0] != pid || current.StartTime.ToUniversalTime().Ticks != started
                            || !string.Equals(Native.ProcessPath(pid), expected, StringComparison.OrdinalIgnoreCase))
                        { result.Note = L.Get("Settings.TransportChanged"); return result; }
                    result.Server = server ?? L.Get("Common.Unknown");
                    result.Peers = peers.Count == 0 ? L.Get("Settings.NoTcp") : string.Join(Environment.NewLine, peers.ToArray());
                    result.Note = note ?? L.Get("Settings.RelayMeasured");
                }
            }
            catch (Exception) { result.Note = L.Get("Settings.RelayUnreadable"); }
            finally { result.CheckedAt = DateTime.Now; }
            return result;
        }
    }
}

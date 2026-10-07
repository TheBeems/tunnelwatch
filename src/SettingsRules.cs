using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace TunnelWatch
{
    internal static class SettingsRules
    {
        internal static Settings Copy(Settings source)
        {
            return new Settings { Language = source.Language, SplitProfile = source.SplitProfile, FullProfile = source.FullProfile,
                WssExecutable = source.WssExecutable, ListenerPort = source.ListenerPort, Router = source.Router,
                HomeHost = source.HomeHost, HomeNetwork = source.HomeNetwork, DnsServer = source.DnsServer,
                DnsName = source.DnsName, IntervalSeconds = source.IntervalSeconds, TimeoutMilliseconds = source.TimeoutMilliseconds };
        }
        internal static void Validate(Settings s)
        {
            if (s == null) throw new InvalidOperationException(L.Get("Config.Empty"));
            try { CultureInfo.GetCultureInfo(s.Language ?? "en"); }
            catch (CultureNotFoundException) { throw new InvalidOperationException(L.Format("Config.LanguageInvalid", s.Language)); }
            foreach (string address in new[] { s.Router, s.HomeHost, s.DnsServer })
            {
                IPAddress ip;
                if (!IPAddress.TryParse(address, out ip) || ip.AddressFamily != AddressFamily.InterNetwork)
                    throw new InvalidOperationException(L.Get("Config.Addresses"));
            }
            string[] subnet = (s.HomeNetwork ?? "").Split('/');
            IPAddress network; int prefix;
            if (subnet.Length != 2 || !IPAddress.TryParse(subnet[0], out network) || network.AddressFamily != AddressFamily.InterNetwork
                || !int.TryParse(subnet[1], out prefix) || prefix < 8 || prefix > 30)
                throw new InvalidOperationException(L.Get("Config.Subnet"));
            if (s.ListenerPort < 1 || s.ListenerPort > 65535 || s.IntervalSeconds < 5 || s.IntervalSeconds > 300 || s.TimeoutMilliseconds < 250 || s.TimeoutMilliseconds > 3000)
                throw new InvalidOperationException(L.Get("Config.Ranges"));
            if (!ProfileValid(s.SplitProfile) || !ProfileValid(s.FullProfile) || string.Equals(s.SplitProfile, s.FullProfile, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(s.WssExecutable))
                throw new InvalidOperationException(L.Get("Config.Profiles"));
            try
            {
                string expanded = Environment.ExpandEnvironmentVariables(s.WssExecutable);
                bool drivePath = expanded.Length >= 3 && expanded[1] == ':' && (expanded[2] == '\\' || expanded[2] == '/');
                if ((!drivePath && !expanded.StartsWith("\\\\", StringComparison.Ordinal)) || expanded.IndexOf('"') >= 0) throw new ArgumentException();
                Path.GetFullPath(expanded);
            }
            catch (Exception e)
            {
                if (!(e is ArgumentException) && !(e is NotSupportedException) && !(e is PathTooLongException)) throw;
                throw new InvalidOperationException(L.Get("Config.Executable"));
            }
            string dns = (s.DnsName ?? "").TrimEnd('.');
            if (dns.Length == 0 || dns.Length > 253 || Uri.CheckHostName(dns) != UriHostNameType.Dns)
                throw new InvalidOperationException(L.Get("Dns.InvalidName"));
            foreach (string label in dns.Split('.'))
            {
                if (label.Length == 0 || label.Length > 63 || label[0] == '-' || label[label.Length - 1] == '-') throw new InvalidOperationException(L.Get("Dns.InvalidName"));
                foreach (char c in label) if (!(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z') && !(c >= '0' && c <= '9') && c != '-') throw new InvalidOperationException(L.Get("Dns.InvalidName"));
            }
        }
        private static bool ProfileValid(string name)
        {
            return !string.IsNullOrWhiteSpace(name) && name.Length <= 128 && name == name.Trim()
                && name.IndexOfAny(new[] { '/', '\\', '"', '\r', '\n', '\0' }) < 0;
        }
        internal static bool CanControl(Settings s)
        {
            string runtime = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TunnelWatchWss", "wstunnel.exe");
            try { return s.SplitProfile == "tunnelwatch-wss-split" && s.FullProfile == "tunnelwatch-wss-full" && s.ListenerPort == 39075
                && string.Equals(Path.GetFullPath(Environment.ExpandEnvironmentVariables(s.WssExecutable)), runtime, StringComparison.OrdinalIgnoreCase); }
            catch (ArgumentException) { return false; }
            catch (NotSupportedException) { return false; }
        }
    }
}

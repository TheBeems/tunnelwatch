using System;
using System.Reflection;

namespace TunnelWatch
{
    internal static class SettingsTests
    {
        internal static int Run()
        {
            // Reflection keeps this regression executable on the unchanged app:
            // the missing presentation behavior is a test failure, not a build error.
            var type = typeof(Settings).Assembly.GetType("TunnelWatch.RelayAddress");
            if (type == null) throw new Exception("Settings cannot present the WSS relay server safely.");
            var parse = type.GetMethod("FromCommandLine", BindingFlags.Static | BindingFlags.NonPublic);
            if (parse == null) throw new Exception("Settings cannot extract the WSS relay server.");
            Func<string, string> address = delegate(string command) { return (string)parse.Invoke(null, new object[] { command }); };
            int passed = 0;
            if (address("wstunnel.exe client -L udp://127.0.0.1:39075:192.0.2.1:51820 wss://relay.example.test/secret?token=private") != "wss://relay.example.test") throw new Exception("Relay path or query disclosed."); passed++;
            if (address("\"C:\\Example App\\wstunnel.exe\" client \"wss://user:password@relay.example.test:8443/private\"") != "wss://relay.example.test:8443") throw new Exception("Relay credentials disclosed or quoted URL lost."); passed++;
            string ipv6Relay = address("wstunnel.exe client wss://[2001:db8::1]:8443/private");
            if (ipv6Relay != "wss://[2001:db8::1]:8443") throw new Exception("IPv6 relay not preserved: " + ipv6Relay); passed++;
            if (address(null) != null || address("wstunnel.exe client ws://relay.example.test") != null || address("wstunnel.exe client --header wss://secret.example.test -L udp://localhost") != null) throw new Exception("Unknown relay presented as a connection."); passed++;
            if (address("wstunnel.exe client --http-headers wss://secret.example.test") != null || address("wstunnel.exe client wss://relay.example.test --http-headers wss://secret.example.test") != null) throw new Exception("Option value presented as relay."); passed++;
            var valid = Example(); SettingsRules.Validate(valid); passed++;
            Reject(delegate(Settings s) { s.Router = "2001:db8::1"; }); passed++;
            Reject(delegate(Settings s) { s.HomeNetwork = "198.51.100.0/32"; }); passed++;
            Reject(delegate(Settings s) { s.FullProfile = s.SplitProfile.ToUpperInvariant(); }); passed++;
            Reject(delegate(Settings s) { s.WssExecutable = "relative\\wstunnel.exe"; }); passed++;
            Reject(delegate(Settings s) { s.WssExecutable = "C:wstunnel.exe"; }); passed++;
            Reject(delegate(Settings s) { s.WssExecutable = "\\wstunnel.exe"; }); passed++;
            Reject(delegate(Settings s) { s.ListenerPort = 0; }); passed++;
            Reject(delegate(Settings s) { s.IntervalSeconds = 4; }); passed++;
            Reject(delegate(Settings s) { s.IntervalSeconds = 301; }); passed++;
            Reject(delegate(Settings s) { s.TimeoutMilliseconds = 249; }); passed++;
            Reject(delegate(Settings s) { s.TimeoutMilliseconds = 3001; }); passed++;
            Reject(delegate(Settings s) { s.DnsName = ""; }); passed++;
            Reject(delegate(Settings s) { s.DnsName = "tést.example.test"; }); passed++;
            var copy = SettingsRules.Copy(valid); copy.Router = "203.0.113.1";
            if (valid.Router == copy.Router) throw new Exception("Draft modifies active settings before Save."); passed++;
            valid.WssExecutable = "%LOCALAPPDATA%\\TunnelWatchWss\\wstunnel.exe";
            if (!SettingsRules.CanControl(valid)) throw new Exception("Existing installation controls disabled."); passed++;
            copy = SettingsRules.Copy(valid); copy.SplitProfile = "other-split";
            if (SettingsRules.CanControl(copy)) throw new Exception("Custom profile can operate installation-bound helper."); passed++;
            copy = SettingsRules.Copy(valid); copy.ListenerPort++;
            if (SettingsRules.CanControl(copy)) throw new Exception("Custom listener can operate installation-bound helper."); passed++;
            copy = SettingsRules.Copy(valid); copy.WssExecutable = "C:\\Example\\other.exe";
            if (SettingsRules.CanControl(copy)) throw new Exception("Custom executable can operate installation-bound helper."); passed++;
            DateTime now = DateTime.Now;
            var observation = new Observation { CheckedAt = now, Split = ServiceState.Running, Full = ServiceState.Stopped };
            if (ConnectionView.ActiveProfile(observation, valid, false, now) != valid.SplitProfile) throw new Exception("Actual split profile missing."); passed++;
            observation.Full = ServiceState.Running; observation.Split = ServiceState.Stopped;
            if (ConnectionView.ActiveProfile(observation, valid, false, now) != valid.FullProfile) throw new Exception("Actual full profile missing."); passed++;
            observation.Split = ServiceState.Running;
            string conflict = ConnectionView.ActiveProfile(observation, valid, false, now);
            if (!conflict.Contains(valid.SplitProfile) || !conflict.Contains(valid.FullProfile) || !conflict.Contains(L.Get("Common.TwoProfiles"))) throw new Exception("Two profiles presented as one connection."); passed++;
            observation.Split = ServiceState.Pending;
            if (ConnectionView.ActiveProfile(observation, valid, false, now) != L.Get("Service.Switching")) throw new Exception("Switching profile overstated."); passed++;
            observation.Split = ServiceState.Unknown;
            if (ConnectionView.ActiveProfile(observation, valid, false, now) != L.Get("Common.Unknown")) throw new Exception("Unreadable profile overstated."); passed++;
            observation.Split = ServiceState.Stopped; observation.Full = ServiceState.Missing;
            if (ConnectionView.ActiveProfile(observation, valid, false, now) != L.Get("Settings.NoProfile")) throw new Exception("Disabled profile presented as active."); passed++;
            observation.Split = ServiceState.Running;
            if (ConnectionView.ActiveProfile(observation, valid, true, now) != L.Get("Common.Unknown") || ConnectionView.ActiveProfile(observation, valid, false, now.AddMinutes(10)) != L.Get("Common.Unknown")) throw new Exception("Stale profile retained."); passed++;
            observation.Changed = true;
            if (ConnectionView.ActiveProfile(observation, valid, false, now) != L.Get("Common.Unknown") || ConnectionView.ActiveProfile(null, valid, false, now) != L.Get("Common.Unknown")) throw new Exception("Changed or absent measurement overstated."); passed++;
            var table = new byte[4 + 3 * 24]; Write(table, 0, 3);
            for (int i = 0; i < 3; i++)
            {
                int p = 4 + i * 24; Write(table, p, i == 2 ? 2 : 5); Write(table, p + 20, i == 1 ? 202 : 101);
                Array.Copy(System.Net.IPAddress.Parse("192.0.2.7").GetAddressBytes(), 0, table, p + 12, 4); table[p + 16] = 1; table[p + 17] = 187;
            }
            var tcp = TcpPeerCodec.Read(table, 2, 101);
            if (tcp.Count != 1 || tcp[0] != "192.0.2.7:443") throw new Exception("TCP state or owner filtering incorrect."); passed++;
            var ipv6 = new byte[60]; Write(ipv6, 0, 1); Write(ipv6, 52, 5); Write(ipv6, 56, 101);
            Array.Copy(System.Net.IPAddress.Parse("2001:db8::7").GetAddressBytes(), 0, ipv6, 28, 16); ipv6[48] = 32; ipv6[49] = 251;
            tcp = TcpPeerCodec.Read(ipv6, 23, 101);
            if (tcp.Count != 1 || tcp[0] != "[2001:db8::7]:8443") throw new Exception("IPv6 TCP endpoint decoded incorrectly."); passed++;
            ipv6[47] = 3; tcp = TcpPeerCodec.Read(ipv6, 23, 101);
            if (tcp[0] != "[2001:db8::7%3]:8443") throw new Exception("IPv6 scope lost or wrong byte order."); passed++;
            Write(table, 0, 1000); bool malformed = false;
            try { TcpPeerCodec.Read(table, 2, 101); } catch (InvalidOperationException) { malformed = true; }
            if (!malformed) throw new Exception("Truncated TCP table accepted."); passed++;
            return passed;
        }
        internal static Settings Example()
        {
            return new Settings { Language = "en", Router = "192.0.2.1", HomeHost = "198.51.100.33", HomeNetwork = "198.51.100.0/24",
                DnsServer = "198.51.100.2", DnsName = "nas.example.test", WssExecutable = "C:\\Example\\wstunnel.exe" };
        }
        private static void Reject(Action<Settings> change)
        {
            var s = Example(); change(s); bool rejected = false;
            try { SettingsRules.Validate(s); } catch (InvalidOperationException) { rejected = true; }
            if (!rejected) throw new Exception("Invalid monitoring setting accepted.");
        }
        private static void Write(byte[] bytes, int offset, int value) { Array.Copy(BitConverter.GetBytes(value), 0, bytes, offset, 4); }
    }
}

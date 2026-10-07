using System;
using System.Net;

namespace TunnelWatch
{
    public enum Health { Off, Checking, Healthy, Broken, Home }
    public enum ServiceState { Missing, Stopped, Running, Pending, Unknown }
    public sealed class Settings
    {
        public string Language = "en";
        public string SplitProfile = "tunnelwatch-wss-split";
        public string FullProfile = "tunnelwatch-wss-full";
        public string WssExecutable = "%LOCALAPPDATA%\\TunnelWatchWss\\wstunnel.exe";
        public int ListenerPort = 39075;
        public string Router;
        public string HomeHost;
        public string HomeNetwork;
        public string DnsServer;
        public string DnsName;
        public int IntervalSeconds = 10;
        public int TimeoutMilliseconds = 1200;

    }
    internal static class PortCodec
    {
        internal static int Decode(byte[] bytes, int offset) { return bytes[offset] * 256 + bytes[offset + 1]; }
    }
    public sealed class Route
    {
        public uint Index;
        public ulong Luid;
        public string Source, NextHop, Prefix, Adapter, Description;
        public int PrefixLength;
        public bool Up, Physical;
        public string Error;
        public bool Same(Route other)
        {
            return other != null && Error == null && other.Error == null && Index == other.Index && Luid == other.Luid && Source == other.Source && NextHop == other.NextHop && Prefix == other.Prefix && PrefixLength == other.PrefixLength && Adapter == other.Adapter && Up == other.Up;
        }
        public bool IsTunnel(string profile)
        {
            return Error == null && Up && Adapter == profile && (Description ?? "").IndexOf("WireGuard", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        public bool IsHome(string subnet, string destination)
        {
            if (Error != null || !Up || !Physical || NextHop != "0.0.0.0") return false;
            string[] parts = subnet.Split('/'); int prefix = int.Parse(parts[1]);
            return PrefixLength >= prefix && InSubnet(Source, parts[0], prefix) && InSubnet(destination, parts[0], prefix);
        }
        public static bool InSubnet(string address, string network, int prefix)
        {
            byte[] a = IPAddress.Parse(address).GetAddressBytes(), b = IPAddress.Parse(network).GetAddressBytes();
            for (int i = 0; i < 4; i++) { int bits = Math.Min(8, Math.Max(0, prefix - i * 8)); int mask = (255 << (8 - bits)) & 255; if ((a[i] & mask) != (b[i] & mask)) return false; }
            return true;
        }
        public override string ToString() { return Error ?? L.Format("Route.Description", Adapter, Index, Source, Prefix, PrefixLength, NextHop); }
    }
    public sealed class Observation
    {
        public DateTime CheckedAt;
        public double DurationMilliseconds;
        public ServiceState Split, Full;
        public bool ListenerVerified, ListenerUnknown, Tcp443;
        public int ListenerPid;
        public string WssDetail, ServiceError, ProbeError;
        public Route RouterRoute, HomeRoute;
        public bool RouterReachable, HomeReachable, Changed;
        public string DnsDetail;
        public bool Administrator;
        public string Architecture;
    }
    // A conservative view of the latest measurement. Never reuse green component
    // indicators when the overall measurement has been invalidated.
    internal sealed class StatusVisual
    {
        internal Health Wss, Vpn, Home;
        internal string WssText, VpnText, HomeText, Summary, Mode;
        internal static StatusVisual From(Status status, Observation o, Settings settings)
        {
            var v = new StatusVisual { Wss = Health.Checking, Vpn = Health.Checking, Home = Health.Checking,
                WssText = L.Get("Common.Unknown"), VpnText = L.Get("Common.Unknown"), HomeText = L.Get("Common.Unknown"), Summary = status.Reason };
            if (o == null || status.Health == Health.Checking) return v;
            bool active = o.Split == ServiceState.Running || o.Full == ServiceState.Running;
            bool both = o.Split == ServiceState.Running && o.Full == ServiceState.Running;
            bool direct = o.HomeReachable && o.HomeRoute != null && o.HomeRoute.IsHome(settings.HomeNetwork, settings.HomeHost);
            string profile = o.Full == ServiceState.Running ? settings.FullProfile : settings.SplitProfile;
            bool tunnel = active && !both && o.RouterReachable && o.RouterRoute != null && o.RouterRoute.IsTunnel(profile);
            v.Mode = both ? null : o.Full == ServiceState.Running ? "Full" : o.Split == ServiceState.Running ? "Split" : "Off";
            v.Wss = o.ListenerUnknown ? Health.Checking : o.ListenerVerified ? Health.Healthy : active ? Health.Broken : Health.Off;
            v.WssText = o.ListenerUnknown ? L.Get("Common.Unknown") : o.ListenerVerified ? L.Get("Common.Verified") : L.Get("Common.Missing");
            v.Vpn = both ? Health.Broken : active ? Health.Healthy : Health.Off;
            v.VpnText = both ? L.Get("Common.TwoProfiles") : active ? L.Get(v.Mode == "Full" ? "Ui.ModeFull" : "Ui.ModeSplit") : L.Get("Common.Off");
            v.Home = tunnel ? Health.Healthy : direct ? Health.Home : active ? Health.Broken : Health.Off;
            v.HomeText = tunnel ? L.Get("Common.ViaTunnel") : direct ? L.Get("Common.DirectHome") : L.Get("Common.Unconfirmed");
            if (status.Health == Health.Healthy) v.Summary = v.Mode == "Full" ? L.Get("Status.FullCaveat") : "";
            else if (status.Health == Health.Home || status.Health == Health.Off) v.Summary = "";
            return v;
        }
    }
    public sealed class Status
    {
        public Health Health;
        public string Title, Reason, Profile;
        public static Status Evaluate(Observation o, Settings settings)
        {
            var s = new Status { Profile = o.Split == ServiceState.Running && o.Full == ServiceState.Running ? "split + full" : o.Full == ServiceState.Running ? "full" : o.Split == ServiceState.Running ? "split" : "off" };
            string profile = s.Profile == "full" ? settings.FullProfile : settings.SplitProfile;
            bool active = o.Split == ServiceState.Running || o.Full == ServiceState.Running;
            bool home = o.HomeReachable && o.HomeRoute != null && o.HomeRoute.IsHome(settings.HomeNetwork, settings.HomeHost);
            if (o.Changed) return Finish(s, Health.Checking, L.Get("Status.NetworkChanged"), L.Get("Status.NetworkChangedReason"));
            if (o.Split == ServiceState.Unknown || o.Full == ServiceState.Unknown) return Finish(s, Health.Checking, L.Get("Status.ServiceUnknown"), o.ServiceError ?? L.Get("Status.ServiceUnknownReason"));
            if (o.Split == ServiceState.Pending || o.Full == ServiceState.Pending) return Finish(s, Health.Checking, L.Get("Status.Switching"), L.Get("Status.SwitchingReason"));
            if (o.Split == ServiceState.Running && o.Full == ServiceState.Running) return Finish(s, Health.Broken, L.Get("Status.BothActive"), L.Get("Status.BothActiveReason"));
            if (active)
            {
                if (o.ListenerUnknown) return Finish(s, Health.Checking, L.Get("Status.WssUnknown"), o.WssDetail ?? L.Get("Status.WssUnknownReason"));
                if (!o.ListenerVerified) return Finish(s, Health.Broken, L.Get("Status.WssMissing"), o.WssDetail ?? L.Get("Status.WssMissingReason"));
                if (o.RouterRoute == null || o.RouterRoute.Error != null) return Finish(s, Health.Broken, L.Get("Status.RouteMissing"), o.RouterRoute == null ? L.Get("Status.NoRouterRoute") : o.RouterRoute.Error);
                if (!o.RouterRoute.IsTunnel(profile)) return Finish(s, Health.Broken, L.Get("Status.WrongRoute"), L.Format(home ? "Status.WrongRouteHomeReason" : "Status.WrongRouteReason", o.RouterRoute.Adapter));
                if (!o.RouterReachable) return Finish(s, Health.Broken, L.Get("Status.RouterUnreachable"), o.ProbeError ?? L.Get("Status.RouterUnreachableReason"));
                return Finish(s, Health.Healthy, L.Get("Status.Healthy"), L.Format("Status.HealthyReason", s.Profile));
            }
            if (home) return Finish(s, Health.Home, L.Get("Status.Home"), L.Get("Status.HomeReason"));
            return Finish(s, Health.Off, L.Get("Status.Off"), L.Get("Status.OffReason"));
        }
        private static Status Finish(Status s, Health h, string title, string reason) { s.Health = h; s.Title = title; s.Reason = reason; return s; }
    }
}

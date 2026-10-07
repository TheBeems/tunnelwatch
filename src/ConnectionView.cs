using System;

namespace TunnelWatch
{
    internal static class ConnectionView
    {
        internal static string ActiveProfile(Observation o, Settings s, bool invalidated, DateTime now)
        {
            if (o == null || invalidated || o.Changed || now - o.CheckedAt > TimeSpan.FromSeconds(s.IntervalSeconds + 5)
                || o.Split == ServiceState.Unknown || o.Full == ServiceState.Unknown) return L.Get("Common.Unknown");
            if (o.Split == ServiceState.Pending || o.Full == ServiceState.Pending) return L.Get("Service.Switching");
            if (o.Split == ServiceState.Running && o.Full == ServiceState.Running) return s.SplitProfile + " + " + s.FullProfile + " (" + L.Get("Common.TwoProfiles") + ")";
            if (o.Split == ServiceState.Running) return s.SplitProfile;
            if (o.Full == ServiceState.Running) return s.FullProfile;
            return L.Get("Settings.NoProfile");
        }
    }
}

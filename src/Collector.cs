using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using System.Threading.Tasks;

namespace TunnelWatch
{
    internal sealed class Collector
    {
        private readonly Settings settings;
        internal Collector(Settings s) { settings = s; }
        internal static ServiceState Service(string name, out string error)
        {
            error = null;
            try
            {
                using (var service = new ServiceController("WireGuardTunnel$" + name))
                {
                    var state = service.Status;
                    if (state == ServiceControllerStatus.Running) return ServiceState.Running;
                    if (state == ServiceControllerStatus.Stopped) return ServiceState.Stopped;
                    return ServiceState.Pending;
                }
            }
            catch (InvalidOperationException e)
            {
                var native = e.InnerException as Win32Exception;
                if (native != null && native.NativeErrorCode == 1060) return ServiceState.Missing;
                error = L.Format("Error.Service", name, SystemErrors.Describe((Exception)native ?? e)); return ServiceState.Unknown;
            }
            catch (Exception e) { error = L.Format("Error.Service", name, SystemErrors.Describe(e)); return ServiceState.Unknown; }
        }
        private void Wss(Observation o)
        {
            try
            {
                var owners = Native.ListenerOwners(settings.ListenerPort);
                if (owners.Count == 0) { o.WssDetail = L.Format("Wss.NoListener", settings.ListenerPort); return; }
                string expected = Path.GetFullPath(Environment.ExpandEnvironmentVariables(settings.WssExecutable));
                if (owners.Count != 1) { o.WssDetail = L.Get("Wss.MultipleOwners"); return; }
                o.ListenerPid = owners[0];
                string actual = Native.ProcessPath(owners[0]);
                o.ListenerVerified = string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
                if (!o.ListenerVerified) { o.WssDetail = L.Get("Wss.WrongOwner"); return; }
                try { o.Tcp443 = Native.HasTcp443(owners[0]); o.WssDetail = L.Get(o.Tcp443 ? "Wss.TcpEstablished" : "Wss.TcpWaiting"); }
                catch (Exception e) { o.WssDetail = L.Format("Wss.TcpUnreadable", SystemErrors.Describe(e)); }
            }
            catch (Exception e) { o.ListenerUnknown = true; o.WssDetail = L.Format("Wss.OwnerUnreadable", SystemErrors.Describe(e)); }
        }
        internal async Task<Observation> Collect(bool dns)
        {
            return await Task.Run(async delegate
            {
                var watch = Stopwatch.StartNew();
                string splitError, fullError;
                var o = new Observation { Split = Service(settings.SplitProfile, out splitError), Full = Service(settings.FullProfile, out fullError), Architecture = Native.Architecture() };
                o.ServiceError = splitError ?? fullError;
                using (var identity = WindowsIdentity.GetCurrent()) o.Administrator = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
                Wss(o);
                o.RouterRoute = Native.BestRoute(settings.Router); o.HomeRoute = Native.BestRoute(settings.HomeHost);
                // Bind ICMP to the source chosen by Windows; reject a result if the route or service changes.
                bool active = o.Split == ServiceState.Running || o.Full == ServiceState.Running;
                var routerProbe = Task.Run(delegate { return active ? Native.Ping(settings.Router, o.RouterRoute, settings.TimeoutMilliseconds) : L.Get("Probe.RouterSkipped"); });
                var homeProbe = Task.Run(delegate { return o.HomeRoute.IsHome(settings.HomeNetwork, settings.HomeHost) ? Native.Ping(settings.HomeHost, o.HomeRoute, settings.TimeoutMilliseconds) : L.Get("Probe.NoHomeRoute"); });
                await Task.WhenAll(routerProbe, homeProbe);
                o.ProbeError = routerProbe.Result; o.RouterReachable = routerProbe.Result == null; o.HomeReachable = homeProbe.Result == null;
                if (dns) o.DnsDetail = DnsProbe();
                var after = new Observation(); Wss(after);
                string ignored;
                o.Changed = RouteChanged(o.RouterRoute, Native.BestRoute(settings.Router)) || RouteChanged(o.HomeRoute, Native.BestRoute(settings.HomeHost)) || o.Split != Service(settings.SplitProfile, out ignored) || o.Full != Service(settings.FullProfile, out ignored) || o.ListenerVerified != after.ListenerVerified || o.ListenerUnknown != after.ListenerUnknown || o.ListenerPid != after.ListenerPid;
                o.CheckedAt = DateTime.Now; o.DurationMilliseconds = watch.Elapsed.TotalMilliseconds;
                return o;
            });
        }
        private static bool RouteChanged(Route before, Route after) { return before.Error != null && after.Error != null ? before.Error != after.Error : !before.Same(after); }
        private string DnsProbe()
        {
            try
            {
                Route route = Native.BestRoute(settings.DnsServer);
                if (route.Error != null) return "DNS: " + route.Error;
                string[] labels = (settings.DnsName ?? "").TrimEnd('.').Split('.');
                if (labels.Any(x => x.Length == 0 || x.Length > 63) || settings.DnsName.Length > 253) return L.Get("Dns.InvalidName");
                ushort id = (ushort)new Random().Next(1, 65535);
                var packet = new System.Collections.Generic.List<byte> { (byte)(id >> 8), (byte)id, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0 };
                foreach (var label in labels) { packet.Add((byte)label.Length); packet.AddRange(Encoding.ASCII.GetBytes(label)); }
                packet.AddRange(new byte[] { 0, 0, 1, 0, 1 });
                using (var udp = new UdpClient(new IPEndPoint(IPAddress.Parse(route.Source), 0)))
                {
                    udp.Client.ReceiveTimeout = settings.TimeoutMilliseconds; udp.Connect(settings.DnsServer, 53);
                    byte[] request = packet.ToArray(); udp.Send(request, request.Length);
                    var endpoint = new IPEndPoint(IPAddress.Any, 0); byte[] reply = udp.Receive(ref endpoint);
                    if (!route.Same(Native.BestRoute(settings.DnsServer))) return L.Get("Dns.RouteChanged");
                    if (reply.Length < 12 || reply[0] != request[0] || reply[1] != request[1] || (reply[2] & 128) == 0) return L.Get("Dns.InvalidReply");
                    int answers = reply[6] * 256 + reply[7], rcode = reply[3] & 15;
                    return rcode == 0 && answers > 0 && (reply[2] & 2) == 0 ? L.Format("Dns.Success", settings.DnsName, route.Adapter) : L.Format("Dns.Partial", settings.DnsName, route.Adapter, rcode, answers);
                }
            }
            catch (Exception e) { return L.Format("Dns.Error", SystemErrors.Describe(e)); }
        }
    }
}

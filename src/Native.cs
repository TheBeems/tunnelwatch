using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;

namespace TunnelWatch
{
    internal static class Native
    {
        [DllImport("iphlpapi.dll")] static extern uint GetBestRoute2(IntPtr luid, uint index, IntPtr source, byte[] destination, uint options, [Out] byte[] route, [Out] byte[] bestSource);
        [DllImport("iphlpapi.dll")] static extern uint GetIfEntry2([In, Out] byte[] row);
        [DllImport("iphlpapi.dll")] static extern uint GetExtendedUdpTable(IntPtr table, ref int size, bool order, int family, int tableClass, uint reserved);
        [DllImport("iphlpapi.dll")] static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int family, int tableClass, uint reserved);
        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, int id);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder path, ref int size);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool IsWow64Process2(IntPtr process, out ushort processMachine, out ushort nativeMachine);
        [DllImport("iphlpapi.dll", SetLastError = true)] static extern IntPtr IcmpCreateFile();
        [DllImport("iphlpapi.dll")] static extern bool IcmpCloseHandle(IntPtr handle);
        [DllImport("iphlpapi.dll", SetLastError = true)] static extern uint IcmpSendEcho2Ex(IntPtr handle, IntPtr eventHandle, IntPtr apcRoutine, IntPtr apcContext, uint source, uint destination, byte[] data, ushort dataSize, IntPtr options, IntPtr reply, uint replySize, uint timeout);
        [DllImport("user32.dll")] internal static extern bool DestroyIcon(IntPtr icon);

        internal static string Architecture()
        {
            ushort process, native;
            if (!IsWow64Process2(System.Diagnostics.Process.GetCurrentProcess().Handle, out process, out native)) return L.Get("Service.Unknown");
            return (native == 0xaa64 ? "ARM64" : native == 0x8664 ? "x64" : "0x" + native.ToString("x")) + (process == 0 ? L.Get("Architecture.Native") : L.Format("Architecture.Emulated", process.ToString("x")));
        }
        internal static Route BestRoute(string destination)
        {
            try
            {
                var dst = new byte[28]; dst[0] = 2; Array.Copy(IPAddress.Parse(destination).GetAddressBytes(), 0, dst, 4, 4);
                var row = new byte[104]; var source = new byte[28];
                uint code = GetBestRoute2(IntPtr.Zero, 0, IntPtr.Zero, dst, 0, row, source);
                if (code != 0) return new Route { Error = "Route: " + SystemErrors.Win32((int)code) };
                var r = new Route { Luid = BitConverter.ToUInt64(row, 0), Index = BitConverter.ToUInt32(row, 8), Source = Address(source, 4), Prefix = Address(row, 16), PrefixLength = row[40], NextHop = Address(row, 48) };
                // MIB_IF_ROW2: fixed WCHAR[257] names and hardware/status flags.
                // Buffer includes ample space for the architecture-dependent tail.
                var nic = new byte[2048]; Array.Copy(BitConverter.GetBytes(r.Luid), nic, 8);
                code = GetIfEntry2(nic);
                if (code != 0) { r.Error = "Interface: " + SystemErrors.Win32((int)code); return r; }
                r.Adapter = Encoding.Unicode.GetString(nic, 28, 514).TrimEnd('\0');
                r.Description = Encoding.Unicode.GetString(nic, 542, 514).TrimEnd('\0');
                r.Up = BitConverter.ToUInt32(nic, 1156) == 1;
                uint type = BitConverter.ToUInt32(nic, 1128);
                r.Physical = (nic[1152] & 1) != 0 && (type == 6 || type == 71);
                return r;
            }
            catch (Exception e) { return new Route { Error = L.Format("Error.RouteUnreadable", SystemErrors.Describe(e)) }; }
        }
        private static string Address(byte[] bytes, int offset) { var b = new byte[4]; Array.Copy(bytes, offset, b, 0, 4); return new IPAddress(b).ToString(); }
        internal static List<int> ListenerOwners(int port)
        {
            var owners = new List<int>();
            ReadTable(false, 2, delegate(byte[] b) {
                int count = BitConverter.ToInt32(b, 0);
                if (count < 0 || 4L + count * 12L > b.Length) throw new InvalidOperationException(L.Get("Error.UdpTable"));
                for (int i = 0; i < count; i++) { int p = 4 + i * 12; if (Address(b, p) == "127.0.0.1" && Port(b, p + 4) == port) owners.Add(BitConverter.ToInt32(b, p + 8)); }
            });
            return owners;
        }
        internal static bool HasTcp443(int process)
        {
            return TcpPeers(process).Exists(peer => peer.EndsWith(":443", StringComparison.Ordinal));
        }
        internal static List<string> TcpPeers(int process)
        {
            var peers = new List<string>();
            foreach (int family in new[] { 2, 23 })
                ReadTable(true, family, delegate(byte[] b) {
                    foreach (string peer in TcpPeerCodec.Read(b, family, process)) if (!peers.Contains(peer)) peers.Add(peer);
                });
            return peers;
        }
        internal static int Port(byte[] b, int offset) { return PortCodec.Decode(b, offset); }
        private static void ReadTable(bool tcp, int family, Action<byte[]> action)
        {
            int size = 0;
            uint code = tcp ? GetExtendedTcpTable(IntPtr.Zero, ref size, false, family, 5, 0) : GetExtendedUdpTable(IntPtr.Zero, ref size, false, family, 1, 0);
            if (code != 122 && code != 0) throw new Win32Exception((int)code);
            for (int retry = 0; retry < 3; retry++)
            {
                if (size < 4 || size > 32 * 1024 * 1024) throw new InvalidOperationException(L.Get("Error.SocketSize"));
                IntPtr buffer = Marshal.AllocHGlobal(size);
                try
                {
                    int allocated = size;
                    code = tcp ? GetExtendedTcpTable(buffer, ref size, false, family, 5, 0) : GetExtendedUdpTable(buffer, ref size, false, family, 1, 0);
                    if (code == 122) continue;
                    if (code != 0) throw new Win32Exception((int)code);
                    var bytes = new byte[allocated]; Marshal.Copy(buffer, bytes, 0, allocated); action(bytes); return;
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
            throw new InvalidOperationException(L.Get("Error.SocketChanging"));
        }
        internal static string ProcessPath(int id)
        {
            IntPtr process = OpenProcess(0x1000, false, id);
            if (process == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            try { int size = 32768; var path = new StringBuilder(size); if (!QueryFullProcessImageName(process, 0, path, ref size)) throw new Win32Exception(Marshal.GetLastWin32Error()); return path.ToString(); }
            finally { CloseHandle(process); }
        }
        internal static string Ping(string destination, Route route, int timeout)
        {
            if (route == null || route.Error != null) return L.Get("Probe.NoRoute");
            IntPtr handle = IcmpCreateFile();
            if (handle == new IntPtr(-1)) return "ICMP: " + SystemErrors.Win32(Marshal.GetLastWin32Error());
            IntPtr reply = Marshal.AllocHGlobal(512);
            try
            {
                byte[] payload = Encoding.ASCII.GetBytes("TunnelWatch");
                uint count = IcmpSendEcho2Ex(handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, BitConverter.ToUInt32(IPAddress.Parse(route.Source).GetAddressBytes(), 0), BitConverter.ToUInt32(IPAddress.Parse(destination).GetAddressBytes(), 0), payload, (ushort)payload.Length, IntPtr.Zero, reply, 512, (uint)timeout);
                int status = count == 0 ? Marshal.GetLastWin32Error() : Marshal.ReadInt32(reply, 4);
                if (status == 0 && count > 0 && (uint)Marshal.ReadInt32(reply, 0) == BitConverter.ToUInt32(IPAddress.Parse(destination).GetAddressBytes(), 0)) return null;
                return status == 11010 ? L.Format("Probe.Timeout", destination, route.Source) : L.Format("Probe.Status", destination, status, route.Source);
            }
            finally { Marshal.FreeHGlobal(reply); IcmpCloseHandle(handle); }
        }
    }
}

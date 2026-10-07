using System;
using System.Collections.Generic;
using System.Net;

namespace TunnelWatch
{
    internal static class TcpPeerCodec
    {
        internal static List<string> Read(byte[] bytes, int family, int process)
        {
            if ((family != 2 && family != 23) || bytes == null || bytes.Length < 4) throw new InvalidOperationException(L.Get("Error.TcpTable"));
            int count = BitConverter.ToInt32(bytes, 0), size = family == 2 ? 24 : 56;
            if (count < 0 || 4L + count * (long)size > bytes.Length) throw new InvalidOperationException(L.Get("Error.TcpTable"));
            var peers = new List<string>();
            for (int i = 0; i < count; i++)
            {
                int p = 4 + i * size;
                if (BitConverter.ToInt32(bytes, p + (family == 2 ? 20 : 52)) != process || BitConverter.ToInt32(bytes, p + (family == 2 ? 0 : 48)) != 5) continue;
                var address = new byte[family == 2 ? 4 : 16]; Array.Copy(bytes, p + (family == 2 ? 12 : 24), address, 0, address.Length);
                uint scope = family == 2 ? 0 : ((uint)bytes[p + 40] << 24) | ((uint)bytes[p + 41] << 16) | ((uint)bytes[p + 42] << 8) | bytes[p + 43];
                string ip = family == 2 ? new IPAddress(address).ToString() : new IPAddress(address, scope).ToString();
                string peer = (family == 2 ? ip : "[" + ip + "]") + ":" + PortCodec.Decode(bytes, p + (family == 2 ? 16 : 44));
                if (!peers.Contains(peer)) peers.Add(peer);
            }
            return peers;
        }
    }
}

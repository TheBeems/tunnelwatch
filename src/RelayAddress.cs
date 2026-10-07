using System;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace TunnelWatch
{
    internal static class RelayAddress
    {
        internal static string FromCommandLine(string command)
        {
            if (string.IsNullOrWhiteSpace(command) || command.Length > 32768) return null;
            // Accept only a final standalone server argument, never a URL in a header.
            var match = Regex.Match(command, "(?:^|\\s)(?:\"(wss://[^\"]+)\"|(wss://[^\\s\"]+))\\s*$", RegexOptions.IgnoreCase);
            if (!match.Success) return null;
            if (Regex.Matches(command, "(?:^|\\s)(?:\"wss://[^\"]+\"|wss://[^\\s\"]+)(?=\\s|$)", RegexOptions.IgnoreCase).Count != 1) return null;
            var previous = Regex.Match(command.Substring(0, match.Index), "(?:^|\\s)(\"[^\"]*\"|[^\\s\"]+)\\s*$");
            string preceding = previous.Success ? previous.Groups[1].Value.Trim('"') : "";
            if (preceding.StartsWith("-", StringComparison.Ordinal) && preceding != "--") return null;
            string argument = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
            Uri uri;
            // Framework and modern .NET register wss differently, especially for
            // IPv6. HTTPS has the same authority/default-port rules on both.
            if (!Uri.TryCreate("https://" + argument.Substring(6), UriKind.Absolute, out uri) || string.IsNullOrEmpty(uri.Host)) return null;
            // Framework's Uri authority expands IPv6 in uppercase. IPAddress
            // provides one readable spelling on both runtimes. Build only the
            // host and port, never any credentials, path, query or fragment.
            string host = uri.DnsSafeHost; IPAddress ip;
            if (IPAddress.TryParse(host, out ip)) host = ip.AddressFamily == AddressFamily.InterNetworkV6 ? "[" + ip + "]" : ip.ToString();
            else host = uri.Host.ToLowerInvariant();
            return "wss://" + host + (uri.Port == 443 ? "" : ":" + uri.Port.ToString(CultureInfo.InvariantCulture));
        }
    }
}

using System.Net;
using System.Net.Sockets;

namespace Aholo3D.Provider;

/// <summary>Transport policy for service-returned stream hosts. DNS is pinned on the actual socket connect.</summary>
public static class RealtimeNetwork
{
    public static string ValidateHost(string host)
    {
        if (string.IsNullOrEmpty(host) || host.Length > 253 || !host.Contains('.') || host.EndsWith('.') ||
            host.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '-')) ||
            Uri.CheckHostName(host) != UriHostNameType.Dns || IPAddress.TryParse(host, out _) ||
            host.Split('.').Any(label => label.Length is 0 or > 63 || label.StartsWith('-') || label.EndsWith('-')) ||
            host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Realtime stream host must be a public DNS hostname with no URL, path, port or credentials.");
        return host.ToLowerInvariant();
    }

    public static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) return IsPublicAddress(address.MapToIPv4());
        byte[] b = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
            return b[0] != 0 && b[0] != 10 && b[0] != 127 &&
                !(b[0] == 100 && b[1] is >= 64 and <= 127) && !(b[0] == 169 && b[1] == 254) &&
                !(b[0] == 172 && b[1] is >= 16 and <= 31) && !(b[0] == 192 && b[1] == 168) &&
                !(b[0] == 192 && b[1] == 0 && b[2] is 0 or 2) && !(b[0] == 192 && b[1] == 88 && b[2] == 99) &&
                !(b[0] == 198 && b[1] is 18 or 19) && !(b[0] == 198 && b[1] == 51 && b[2] == 100) &&
                !(b[0] == 203 && b[1] == 0 && b[2] == 113) && b[0] < 224;
        if (address.AddressFamily != AddressFamily.InterNetworkV6 || address.ScopeId != 0 || (b[0] & 0xe0) != 0x20) return false;
        return !(b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x0d && b[3] == 0xb8) &&
            !(b[0] == 0x3f && b[1] == 0xff && (b[2] & 0xf0) == 0) &&
            !(b[0] == 0x20 && b[1] == 0x01 && b[2] < 2) && !(b[0] == 0x20 && b[1] == 0x02);
    }

    internal static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        ValidateHost(context.DnsEndPoint.Host);
        if (context.DnsEndPoint.Port != 443) throw new HttpRequestException("Unexpected stream port.");
        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct).ConfigureAwait(false);
        if (addresses.Length == 0 || addresses.Any(a => !IsPublicAddress(a))) throw new HttpRequestException("Stream host did not resolve exclusively to public addresses.");
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, 443), ct).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (OperationCanceledException) { socket.Dispose(); throw; }
            catch (SocketException) { socket.Dispose(); }
        }
        throw new HttpRequestException("Stream host could not be reached.");
    }
}

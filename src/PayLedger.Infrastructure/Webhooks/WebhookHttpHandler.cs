using System.Net;
using System.Net.Sockets;

namespace PayLedger.Infrastructure.Webhooks;

/// <summary>An HTTP handler that refuses to connect to private addresses and never follows redirects.</summary>
public static class WebhookHttpHandler
{
    public static SocketsHttpHandler Create(bool allowPrivateNetworks) => new()
    {
        AllowAutoRedirect = false,
        UseProxy = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        ConnectCallback = async (context, cancellationToken) =>
        {
            var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
            var allowed = allowPrivateNetworks ? addresses : addresses.Where(NetworkRules.IsPublic).ToArray();
            if (allowed.Length == 0 || allowed.Length != addresses.Length)
            {
                // Refuse if any address is private: picking only the public ones could still be raced by DNS.
                throw new HttpRequestException($"Webhook host '{context.DnsEndPoint.Host}' resolves to a non-public address.");
            }

            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(allowed, context.DnsEndPoint.Port, cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        },
    };
}

using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace SyncStreamAPI.Net;

/// <summary>
///     The client to use for any request whose address came from a user.
/// </summary>
/// <remarks>
///     Several features hand a client-supplied URL to the server and have it fetched: reading a page title when a
///     video is added, downloading media, probing a stream. Each was a plain <c>HttpClient</c>, so each was a way
///     to make the server issue a request from inside its own network, and the title of the page came back into
///     the room, which made it a way to read the answer too.
///     <para>
///         The check is in <see cref="SocketsHttpHandler.ConnectCallback" /> rather than in front of the call,
///         which is what makes it hold. Validating the URL first and then handing it to a client leaves two
///         holes: a redirect to an internal address, and a name that resolves to something public on the first
///         lookup and something private on the second. Here every connection the handler opens, including the
///         ones behind redirects, resolves the name and refuses the address it actually got.
///     </para>
/// </remarks>
public static class OutboundHttp
{
    /// <summary>How long to wait on a fetch of somebody else's URL.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    /// <summary>
    ///     Shared, because a client per call exhausts sockets under any load and because the handler holds the
    ///     connection pool that makes this cheap.
    /// </summary>
    public static readonly HttpClient Client = Build();

    private static HttpClient Build()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = true,

            // Enough to follow the usual shortener chains, few enough that a redirect loop ends.
            MaxAutomaticRedirections = 5,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            ConnectCallback = ConnectToPublicAddressOnly
        };

        var client = new HttpClient(handler) { Timeout = Timeout };

        // Some sites answer a request without one with a challenge page or a redirect loop, which then reads as
        // "no title" rather than as a refusal.
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) " +
            "Chrome/120.0.0.0 Safari/537.36");

        return client;
    }

    private static async ValueTask<System.IO.Stream> ConnectToPublicAddressOnly(
        SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host;

        var resolved = IPAddress.TryParse(host, out var literal)
            ? new[] { literal }
            : await Dns.GetHostAddressesAsync(host, cancellationToken);

        var allowed = PrivateAddresses.PublicOnly(resolved);
        if (allowed.Length == 0)
            throw new HttpRequestException(
                $"Refusing to connect to {host}: it resolves only to addresses inside this network.");

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            // Connected to the addresses that were just checked, not to the name, so nothing can resolve
            // differently between the check and the connection.
            await socket.ConnectAsync(allowed, context.DnsEndPoint.Port, cancellationToken);
            return new NetworkStream(socket, true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    /// <summary>
    ///     Whether a URL is worth handing to anything at all.
    /// </summary>
    /// <remarks>
    ///     A first sieve for callers that cannot route through <see cref="Client" />, such as the headless
    ///     browser, which opens its own connections. On its own it does not survive a redirect, so it is a
    ///     narrowing rather than a guarantee: prefer <see cref="Client" /> wherever the work is an HTTP fetch.
    /// </remarks>
    public static async Task<bool> LooksReachableAsync(string? url, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;

        try
        {
            var resolved = IPAddress.TryParse(uri.Host, out var literal)
                ? new[] { literal }
                : await Dns.GetHostAddressesAsync(uri.Host, cancellationToken);

            return PrivateAddresses.PublicOnly(resolved).Length > 0;
        }
        catch (SocketException)
        {
            // A name that does not resolve is not reachable either.
            return false;
        }
    }
}

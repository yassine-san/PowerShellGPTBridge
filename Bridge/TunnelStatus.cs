using System.Net.Http;
using System.Net.Sockets;

namespace PersistentBridge;

internal static class TunnelStatus
{
    // GET /readyz over the client's private filesystem socket. No TCP fallback.
    public static async Task Watch(Window window, CancellationToken stop)
    {
        var path = Environment.GetEnvironmentVariable("PSBRIDGE_HEALTH_SOCKET");
        if (string.IsNullOrEmpty(path)) return;
        using var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            ConnectCallback = async (_, token) =>
            {
                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                try
                {
                    await socket.ConnectAsync(new UnixDomainSocketEndPoint(path), token);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch { socket.Dispose(); throw; }
            }
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(2) };
        while (!stop.IsCancellationRequested)
        {
            bool ready;
            try
            {
                using var response = await client.GetAsync("http://localhost/readyz", stop);
                ready = response.IsSuccessStatusCode;
            }
            catch (Exception) when (!stop.IsCancellationRequested) { ready = false; }
            if (stop.IsCancellationRequested) return;
            window.Transport.ForeColor = ready ? Color.LightGreen : Color.Khaki;
            window.Transport.Text = ready ? "Tunnel client reports ready" : "Tunnel client starting or unavailable";
            await Task.Delay(TimeSpan.FromSeconds(3), stop);
        }
    }
}

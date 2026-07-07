using System.Net.WebSockets;

namespace MarqueeManager.Setup.Detection;

/// <summary>
/// Checks whether APIExpose answers on its WebSocket base URL. Used by the welcome
/// checks and the "Tester la connexion" button — a failure here explains why the
/// surfaces show no content, before the user blames his screen setup.
/// </summary>
public static class ApiExposeProbe
{
    public static async Task<bool> IsAliveAsync(string baseUrl, CancellationToken ct = default)
    {
        try
        {
            var url = baseUrl.TrimEnd('/');
            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                url = "ws://" + url["http://".Length..];
            }

            using var socket = new ClientWebSocket();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            await socket.ConnectAsync(new Uri(url + "/ws/marquee"), timeout.Token).ConfigureAwait(false);
            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "probe", CancellationToken.None)
                .ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

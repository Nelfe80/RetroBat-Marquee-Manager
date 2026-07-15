using System.IO;
using System.Net.WebSockets;
using System.Text.Json;

namespace MarqueeManager.Setup.Data;

/// <summary>A signal seen live on the ws/ingame stream.</summary>
public sealed record IngameEvent(DateTime At, string Action, string Family);

/// <summary>
/// Live tap on APIExpose's ws/ingame stream: shows the signals firing while a
/// game is being played, so the user discovers what his game emits by simply
/// playing it (RetroCreator's event monitor pattern). Extraction mirrors the
/// runtime's HandleIngame: Payload.actionType, then signal.Name; family beside.
/// Auto-reconnects until disposed.
/// </summary>
public sealed class IngameMonitor : IDisposable
{
    private readonly Uri _uri;
    private readonly CancellationTokenSource _cts = new();

    /// <summary>Raised on a worker thread — marshal to the dispatcher.</summary>
    public event Action<IngameEvent>? EventReceived;

    /// <summary>Connection state changes (true = listening).</summary>
    public event Action<bool>? ConnectedChanged;

    public IngameMonitor(string baseUrl)
    {
        var url = baseUrl.TrimEnd('/');
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            url = "ws://" + url["http://".Length..];
        }
        _uri = new Uri(url + "/ws/ingame");
        _ = ListenAsync();
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }

    private async Task ListenAsync()
    {
        var buffer = new byte[64 * 1024];
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                using var socket = new ClientWebSocket();
                await socket.ConnectAsync(_uri, _cts.Token).ConfigureAwait(false);
                ConnectedChanged?.Invoke(true);

                while (socket.State == WebSocketState.Open && !_cts.IsCancellationRequested)
                {
                    using var message = new MemoryStream();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await socket.ReceiveAsync(buffer, _cts.Token).ConfigureAwait(false);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            throw new WebSocketException("closed");
                        }
                        message.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        Parse(message.ToArray());
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                ConnectedChanged?.Invoke(false);
                try
                {
                    await Task.Delay(3000, _cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        ConnectedChanged?.Invoke(false);
    }

    private void Parse(byte[] json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var payload = Child(root, "Payload", "payload");
            if (payload.ValueKind != JsonValueKind.Object)
            {
                payload = root;
            }

            var action = Text(root, "Action", "action");
            if (action.Length == 0) action = Text(payload, "ActionType", "actionType", "Action", "action");
            if (action.Length == 0)
            {
                var signal = Child(payload, "Signal", "signal");
                if (signal.ValueKind == JsonValueKind.Object) action = Text(signal, "Name", "name");
            }
            if (action.Length == 0)
            {
                return;
            }

            var family = Text(root, "Family", "family");
            if (family.Length == 0) family = Text(payload, "Family", "family");
            EventReceived?.Invoke(new IngameEvent(DateTime.Now, action, family));
        }
        catch
        {
            // malformed frame: skipped
        }
    }

    private static JsonElement Child(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var child))
            {
                return child;
            }
        }
        return default;
    }

    private static string Text(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty(name, out var value)
                && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString() ?? "";
            }
        }
        return "";
    }
}

using System;
using System.Text.Json;

namespace RetroBatMarqueeManager.Application.Services
{
    public class ApiExposeEvent
    {
        public string Type { get; set; } = "";
        public string Stream { get; set; } = "";
        public int? Player { get; set; }
        public string? Target { get; set; }
        public string? Action { get; set; }
        public string? Value { get; set; }
        public string? Text { get; set; }
        public string? System { get; set; }
        public string? Rom { get; set; }
        public JsonElement Root { get; set; }

        public static ApiExposeEvent? FromElement(JsonElement root, string defaultStream)
        {
            try
            {
                JsonElement payload = root;
                if (root.TryGetProperty("payload", out var p) || root.TryGetProperty("Payload", out p))
                {
                    payload = p;
                }

                JsonElement signal = default;
                if (payload.ValueKind == JsonValueKind.Object && (payload.TryGetProperty("signal", out var s) || payload.TryGetProperty("Signal", out s)))
                {
                    signal = s;
                }

                var type = ReadString(root, "type") ?? ReadString(root, "Type") ?? "";
                var stream = ReadString(root, "stream") ?? ReadString(root, "Stream") ?? defaultStream;
                var target = ReadString(root, "target") ?? ReadString(root, "Target");
                var action = ReadString(root, "action") ?? ReadString(root, "Action");
                var value = ReadString(root, "value") ?? ReadString(root, "Value") ?? ReadString(root, "score");
                var text = ReadString(root, "text") ?? ReadString(root, "Text");
                var system = ReadString(root, "system") ?? ReadString(root, "System") ?? ReadString(payload, "SystemId") ?? ReadString(payload, "systemId");
                var rom = ReadString(root, "rom") ?? ReadString(root, "Rom") ?? ReadString(root, "game") ?? ReadString(payload, "Rom") ?? ReadString(payload, "rom") ?? ReadString(payload, "game");

                // Fallbacks from payload/signal
                if (payload.ValueKind == JsonValueKind.Object)
                {
                    action ??= ReadString(payload, "actionType") ?? ReadString(payload, "ActionType");
                    value ??= ReadString(payload, "Value") ?? ReadString(payload, "value");
                }

                if (signal.ValueKind == JsonValueKind.Object)
                {
                    action ??= ReadString(signal, "Name") ?? ReadString(signal, "name");
                    value ??= ReadString(signal, "Value") ?? ReadString(signal, "value");
                }

                if (string.IsNullOrEmpty(type))
                {
                    type = action ?? "";
                }

                return new ApiExposeEvent
                {
                    Type = type,
                    Stream = stream,
                    Target = target,
                    Action = action,
                    Value = value,
                    Text = text,
                    System = system,
                    Rom = rom,
                    Root = root
                };
            }
            catch
            {
                return null;
            }
        }

        private static string? ReadString(JsonElement element, string property)
        {
            if (element.ValueKind != JsonValueKind.Object) return null;
            if (!element.TryGetProperty(property, out var value)) return null;

            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.ToString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => null
            };
        }
    }
}

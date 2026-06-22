using System.Text.Json;
using System.Text.Json.Serialization;

namespace RetroBatMarqueeManager.Infrastructure.Api
{
    /// <summary>
    /// EN: Custom JSON converter for RetroAchievements API dates (handles Unix timestamps, ISO dates, and empty strings)
    /// FR: Convertisseur JSON personnalisé pour les dates de l'API RetroAchievements (gère timestamps Unix, dates ISO et chaînes vides)
    /// </summary>
    public class FlexibleDateTimeConverter : JsonConverter<DateTime?>
    {
        public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.Null:
                    return null;
                    
                case JsonTokenType.String:
                    var stringValue = reader.GetString();
                    
                    // EN: Handle empty strings / FR: Gérer les chaînes vides
                    if (string.IsNullOrWhiteSpace(stringValue))
                    {
                        return null;
                    }
                    
                    // EN: Try ISO 8601 format first / FR: Essayer format ISO 8601 d'abord
                    if (DateTime.TryParse(stringValue, out var parsedDate))
                    {
                        return parsedDate;
                    }
                    
                    // EN: Try Unix timestamp as string / FR: Essayer timestamp Unix en chaîne
                    if (long.TryParse(stringValue, out var unixTimestamp))
                    {
                        return DateTimeOffset.FromUnixTimeSeconds(unixTimestamp).DateTime;
                    }
                    
                    return null;
                    
                case JsonTokenType.Number:
                    // EN: Unix timestamp as number / FR: Timestamp Unix en nombre
                    if (reader.TryGetInt64(out var timestamp))
                    {
                        // EN: Handle 0 as null (unearned achievement) / FR: Gérer 0 comme null (succès non obtenu)
                        if (timestamp == 0)
                        {
                            return null;
                        }
                        return DateTimeOffset.FromUnixTimeSeconds(timestamp).DateTime;
                    }
                    return null;
                    
                default:
                    return null;
            }
        }

        public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
        {
            if (value.HasValue)
            {
                writer.WriteStringValue(value.Value.ToString("O")); // ISO 8601 format
            }
            else
            {
                writer.WriteNullValue();
            }
        }
    }
    
    /// <summary>
    /// EN: Custom JSON converter for non-nullable DateTime (for fields like MemberSince)
    /// FR: Convertisseur JSON personnalisé pour DateTime non-nullable (pour champs comme MemberSince)
    /// </summary>
    public class FlexibleDateTimeNonNullableConverter : JsonConverter<DateTime>
    {
        public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.String:
                    var stringValue = reader.GetString();
                    
                    if (string.IsNullOrWhiteSpace(stringValue))
                    {
                        return DateTime.MinValue;
                    }
                    
                    if (DateTime.TryParse(stringValue, out var parsedDate))
                    {
                        return parsedDate;
                    }
                    
                    if (long.TryParse(stringValue, out var unixTimestamp))
                    {
                        return DateTimeOffset.FromUnixTimeSeconds(unixTimestamp).DateTime;
                    }
                    
                    return DateTime.MinValue;
                    
                case JsonTokenType.Number:
                    if (reader.TryGetInt64(out var timestamp))
                    {
                        if (timestamp == 0) return DateTime.MinValue;
                        return DateTimeOffset.FromUnixTimeSeconds(timestamp).DateTime;
                    }
                    return DateTime.MinValue;
                    
                default:
                    return DateTime.MinValue;
            }
        }

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString("O"));
        }
    }
}

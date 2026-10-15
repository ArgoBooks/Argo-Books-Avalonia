using System.Globalization;

namespace ArgoBooks.Core.Models.Portal;

/// <summary>
/// Reads dates from portal responses, accepting MySQL's "yyyy-MM-dd HH:mm:ss"
/// alongside ISO 8601.
/// </summary>
/// <remarks>
/// Without this, one unrecognised date throws and the whole response is
/// discarded, so a stray format on a single field reads as "portal offline"
/// rather than as one missing value.
/// </remarks>
public class PortalDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => Parse(ref reader) ?? default;

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
        => writer.WriteStringValue(value);

    internal static DateTime? Parse(ref Utf8JsonReader reader)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        if (reader.TokenType != JsonTokenType.String)
            return null;

        if (reader.TryGetDateTime(out var iso))
            return iso;

        var raw = reader.GetString();
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        return DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;
    }
}

/// <summary>
/// Nullable counterpart to <see cref="PortalDateTimeConverter"/>.
/// </summary>
public class PortalNullableDateTimeConverter : JsonConverter<DateTime?>
{
    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => PortalDateTimeConverter.Parse(ref reader);

    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
            writer.WriteStringValue(value.Value);
        else
            writer.WriteNullValue();
    }
}

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace My.Shared.Serialization;

/// <summary>
/// Expense line dates are calendar days, not instants. Round-trip as yyyy-MM-dd so
/// a UTC offset cannot move the day.
/// </summary>
public sealed class CalendarDateJsonConverter : JsonConverter<DateTime>
{
    private const string Format = "yyyy-MM-dd";

    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var text = reader.GetString() ?? "";
            if (text.Length >= 10
                && DateOnly.TryParseExact(
                    text.AsSpan(0, 10),
                    Format,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var date))
            {
                return date.ToDateTime(TimeOnly.MinValue);
            }
        }

        if (reader.TryGetDateTime(out var value))
            return DateTime.SpecifyKind(value.Date, DateTimeKind.Unspecified);

        throw new JsonException("Invalid calendar date.");
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
        writer.WriteStringValue(
            DateTime.SpecifyKind(value.Date, DateTimeKind.Unspecified)
                .ToString(Format, CultureInfo.InvariantCulture));
}

/// <summary>Same calendar-day wire format for a date the caller may leave empty.</summary>
public sealed class NullableCalendarDateJsonConverter : JsonConverter<DateTime?>
{
    private readonly CalendarDateJsonConverter inner = new();

    public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;
        return inner.Read(ref reader, typeof(DateTime), options);
    }

    public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }

        inner.Write(writer, value.Value, options);
    }
}

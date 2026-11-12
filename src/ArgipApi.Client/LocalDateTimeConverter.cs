using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ArgipApi.Client;

/// <summary>
/// Writes a DateTime as <c>yyyy-MM-ddTHH:mm:ss</c> with no offset. The default serializer adds "+01:00" to a Local DateTime
/// (for example <c>DateTime.Today</c>), and the server then converts it to its own time zone, which can shift the date.
/// </summary>
internal sealed class LocalDateTimeConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => reader.GetDateTime();

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture));
}

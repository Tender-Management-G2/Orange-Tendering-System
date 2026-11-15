using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orange_Tendering_System.Services.Reports;

public static class ReportCanonicalJson
{
    private static readonly JsonSerializerOptions Options = CreateOptions();
    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(); options.Converters.Add(new DecimalValueConverter()); return options;
    }
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
    private sealed class DecimalValueConverter : JsonConverter<decimal>
    {
        public override decimal Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) => reader.GetDecimal();
        public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) =>
            writer.WriteRawValue(value.ToString("0.############################", CultureInfo.InvariantCulture));
    }
}

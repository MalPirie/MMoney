using System.Text.Json;
using System.Text.Json.Serialization;

namespace MMoney.Core;

/// <summary>
/// Serializes <see cref="MonthOnly"/> as a JSON object as <c>{"Year":2026,"Month":4}</c>.
/// Required because System.Text.Json cannot use the primary constructor of a readonly 
/// record struct by default, resulting in the zero-valued default being deserialized.
/// </summary>
internal sealed class MonthOnlyJsonConverter : JsonConverter<MonthOnly>
{
    public override MonthOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected start of object for MonthOnly.");
        }

        int year = 0, month = 0;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return new MonthOnly(year, month);
            }

            if (reader.TokenType == JsonTokenType.PropertyName)
            {
                var name = reader.GetString();
                reader.Read();
                if (name == "Year")
                {
                    year = reader.GetInt32();
                }
                else if (name == "Month")
                {
                    month = reader.GetInt32();
                }
            }
        }

        throw new JsonException("Unexpected end of JSON while reading MonthOnly.");
    }

    public override void Write(Utf8JsonWriter writer, MonthOnly value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("Year", value.Year);
        writer.WriteNumber("Month", value.Month);
        writer.WriteEndObject();
    }
}

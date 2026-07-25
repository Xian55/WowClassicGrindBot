using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharedLib.Converters;

public sealed class Vector2Converter : JsonConverter<Vector2>
{
    private readonly bool upperCase;

    public Vector2Converter(bool upperCase = false)
    {
        this.upperCase = upperCase;
    }

    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert == typeof(Vector2);
    }

    [SkipLocalsInit]
    public override Vector2 Read(ref Utf8JsonReader reader,
        Type typeToConvert, JsonSerializerOptions options)
    {
        float x = 0;
        float y = 0;

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
                continue;

            if (reader.ValueTextEquals("X"u8) || reader.ValueTextEquals("x"u8))
            {
                reader.Read();
                x = reader.GetSingle();
            }
            else if (reader.ValueTextEquals("Y"u8) || reader.ValueTextEquals("y"u8))
            {
                reader.Read();
                y = reader.GetSingle();
            }
        }

        return new Vector2(x, y);
    }

    public override void Write(Utf8JsonWriter writer,
        Vector2 value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        if (upperCase)
        {
            writer.WriteNumber("X"u8, value.X);
            writer.WriteNumber("Y"u8, value.Y);
        }
        else
        {
            writer.WriteNumber("x"u8, value.X);
            writer.WriteNumber("y"u8, value.Y);
        }

        writer.WriteEndObject();
    }
}

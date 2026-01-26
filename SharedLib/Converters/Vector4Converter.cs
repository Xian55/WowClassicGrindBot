using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SharedLib.Converters;

public sealed class Vector4Converter : JsonConverter<Vector4>
{
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert == typeof(Vector4);
    }

    [SkipLocalsInit]
    public override Vector4 Read(ref Utf8JsonReader reader,
        Type typeToConvert, JsonSerializerOptions options)
    {
        float x = 0;
        float y = 0;
        float z = 0;
        float w = 0;

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
                continue;

            if (reader.ValueTextEquals("x"u8))
            {
                reader.Read();
                x = reader.GetSingle();
            }
            else if (reader.ValueTextEquals("y"u8))
            {
                reader.Read();
                y = reader.GetSingle();
            }
            else if (reader.ValueTextEquals("z"u8))
            {
                reader.Read();
                z = reader.GetSingle();
            }
            else if (reader.ValueTextEquals("w"u8))
            {
                reader.Read();
                w = reader.GetSingle();
            }
        }


        return new Vector4(x, y, z, w);
    }

    public override void Write(Utf8JsonWriter writer,
        Vector4 value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("x"u8, value.X);
        writer.WriteNumber("y"u8, value.Y);
        writer.WriteNumber("z"u8, value.Z);
        writer.WriteNumber("w"u8, value.W);
        writer.WriteEndObject();
    }
}

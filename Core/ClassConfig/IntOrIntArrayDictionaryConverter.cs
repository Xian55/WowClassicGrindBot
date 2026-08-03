using Newtonsoft.Json;

using System;
using System.Collections.Generic;

namespace Core;

/// <summary>
/// Deserializes IntVariables where each value can be either a scalar int or an int[].
/// Scalars are normalized to single-element arrays for uniform handling.
/// </summary>
public sealed class IntOrIntArrayDictionaryConverter : JsonConverter<Dictionary<string, int[]>>
{
    public override Dictionary<string, int[]>? ReadJson(
        JsonReader reader, Type objectType,
        Dictionary<string, int[]>? existingValue, bool hasExistingValue,
        JsonSerializer serializer)
    {
        Dictionary<string, int[]> result = existingValue ?? [];

        if (reader.TokenType == JsonToken.Null)
            return result;

        if (reader.TokenType != JsonToken.StartObject)
            throw new JsonSerializationException(
                $"Expected StartObject for IntVariables, got {reader.TokenType}.");

        while (ReadSkippingComments(reader))
        {
            if (reader.TokenType == JsonToken.EndObject)
                return result;

            if (reader.TokenType != JsonToken.PropertyName)
                throw new JsonSerializationException(
                    $"Expected PropertyName, got {reader.TokenType}.");

            string key = (string)reader.Value!;
            ReadSkippingComments(reader);

            int[] values = reader.TokenType switch
            {
                JsonToken.Integer => [(int)(long)reader.Value!],
                JsonToken.StartArray => ReadIntArray(reader),
                _ => throw new JsonSerializationException(
                    $"IntVariables['{key}']: expected integer or array, got {reader.TokenType}.")
            };

            result[key] = values;
        }

        return result;
    }

    /// <summary>
    /// Advances past any comment tokens. Class profiles are commented heavily, and a
    /// hand-written converter sees those tokens where the default deserializer would
    /// have hidden them - a single // line inside IntVariables otherwise fails the
    /// whole profile load with "Expected PropertyName, got Comment".
    /// </summary>
    private static bool ReadSkippingComments(JsonReader reader)
    {
        while (reader.Read())
        {
            if (reader.TokenType != JsonToken.Comment)
                return true;
        }

        return false;
    }

    private static int[] ReadIntArray(JsonReader reader)
    {
        List<int> list = [];
        while (ReadSkippingComments(reader))
        {
            if (reader.TokenType == JsonToken.EndArray)
                return [.. list];

            if (reader.TokenType != JsonToken.Integer)
                throw new JsonSerializationException(
                    $"IntVariables array element: expected integer, got {reader.TokenType}.");

            list.Add((int)(long)reader.Value!);
        }

        throw new JsonSerializationException("Unexpected end of JSON in IntVariables array.");
    }

    public override void WriteJson(
        JsonWriter writer, Dictionary<string, int[]>? value, JsonSerializer serializer)
    {
        writer.WriteStartObject();
        if (value != null)
        {
            foreach ((string key, int[] values) in value)
            {
                writer.WritePropertyName(key);
                if (values.Length == 1)
                {
                    writer.WriteValue(values[0]);
                }
                else
                {
                    writer.WriteStartArray();
                    for (int i = 0; i < values.Length; i++)
                        writer.WriteValue(values[i]);
                    writer.WriteEndArray();
                }
            }
        }
        writer.WriteEndObject();
    }
}

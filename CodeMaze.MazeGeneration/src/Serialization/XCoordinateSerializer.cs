using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeMaze.MazeGeneration.Serialization;

/// <summary>Reads and writes an XCoordinate as a bare JSON number.</summary>
public sealed class XCoordinateSerializer : JsonConverter<XCoordinate>
{
    public override XCoordinate Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.Number)
            throw new JsonException("An x coordinate must be a number.");

        return new XCoordinate(reader.GetInt32());
    }

    public override void Write(Utf8JsonWriter writer, XCoordinate value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value.Value);
    }
}
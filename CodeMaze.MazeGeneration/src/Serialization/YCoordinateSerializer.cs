using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeMaze.MazeGeneration.Serialization;

/// <summary>Reads and writes a YCoordinate as a bare JSON number.</summary>
public sealed class YCoordinateSerializer : JsonConverter<YCoordinate>
{
    public override YCoordinate Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.Number)
            throw new JsonException("A y coordinate must be a number.");

        return new YCoordinate(reader.GetInt32());
    }

    public override void Write(Utf8JsonWriter writer, YCoordinate value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value.Value);
    }
}
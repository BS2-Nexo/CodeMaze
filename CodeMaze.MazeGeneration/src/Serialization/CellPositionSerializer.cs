using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeMaze.MazeGeneration.Serialization;

/// <summary>
/// Reads and writes a CellPosition as { "y": 0, "x": 1 }, the same shape the maze JSON uses.
/// The property names are fixed (lowercase on write, case-insensitive on read), so the result
/// does not depend on the naming policy of the JsonSerializerOptions in use.
/// </summary>
public sealed class CellPositionSerializer : JsonConverter<CellPosition>
{
    public override CellPosition Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("A position must be an object with y and x.");

        int? y = null;
        int? x = null;

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException("Unexpected token in position.");

            var name = reader.GetString();
            reader.Read();

            if (string.Equals(name, "y", StringComparison.OrdinalIgnoreCase))
                y = reader.GetInt32();
            else if (string.Equals(name, "x", StringComparison.OrdinalIgnoreCase))
                x = reader.GetInt32();
            else
                reader.Skip();
        }

        if (y is null || x is null)
            throw new JsonException("A position needs both y and x.");

        return new CellPosition(new YCoordinate(y.Value), new XCoordinate(x.Value));
    }

    public override void Write(Utf8JsonWriter writer, CellPosition value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("y", value.Y.Value);
        writer.WriteNumber("x", value.X.Value);
        writer.WriteEndObject();
    }
}
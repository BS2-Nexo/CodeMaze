using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeMaze.MazeGeneration.Serialization;

/// <summary>
/// JSON format and convenience API for Maze in one class.
///
/// As a JsonConverter it defines the JSON shape:
///
///   {
///     "width": 3, "height": 2,
///     "entrance": { "y": 0, "x": 1 },
///     "exit":     { "y": 1, "x": 2 },
///     "cells": [
///       { "y": 0, "x": 0, "walls": ["top", "left"] },
///       { "y": 0, "x": 1, "walls": ["top"] },
///       { "y": 1, "x": 1, "walls": [] }
///     ],
///     "shortestPath": [ { "y": 0, "x": 1 }, { "y": 1, "x": 1 }, { "y": 1, "x": 2 } ]
///   }
///
/// "shortestPath" is optional. Positions (entrance, exit, path items) are written and
/// read through CellPositionSerializer, which is picked up via the attribute on CellPosition.
///
/// The static members (ToJson, FromJson, Save, ...) are the char/string/file
/// based entry points and go through the converter via the attribute on Maze.
/// </summary>
public sealed class MazeSerializer : JsonConverter<Maze>
{
    private static readonly JsonSerializerOptions Compact = new();
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    private static JsonSerializerOptions Pick(bool indented) => indented ? Indented : Compact;


    /// <summary>Serialize to a string.</summary>
    public static string ToJson(Maze maze, bool indented = false)
    {
        return JsonSerializer.Serialize(maze, Pick(indented));
    }

    /// <summary>
    /// Serialize into a caller-provided char buffer. Returns false when the
    /// destination is too small.
    /// </summary>
    public static bool TryWriteJson(
        Maze maze,
        Span<char> destination,
        out int charsWritten,
        bool indented = false)
    {
        var utf8 = JsonSerializer.SerializeToUtf8Bytes(maze, Pick(indented));
        return Encoding.UTF8.TryGetChars(utf8, destination, out charsWritten);
    }

    /// <summary>Serialize to any TextWriter.</summary>
    public static void WriteJson(Maze maze, TextWriter writer, bool indented = false)
    {
        writer.Write(ToJson(maze, indented));
    }


    /// <summary>Deserialize from any in-memory char sequence (a string converts implicitly).</summary>
    public static Maze FromJson(ReadOnlySpan<char> json)
    {
        return JsonSerializer.Deserialize<Maze>(json, Compact)
            ?? throw new JsonException("Empty maze document.");
    }

    /// <summary>Deserialize from a possibly multi-segment char sequence.</summary>
    public static Maze FromJson(ReadOnlySequence<char> json)
    {
        return json.IsSingleSegment
            ? FromJson(json.FirstSpan)
            : FromJson(json.ToString());
    }

    /// <summary>Deserialize from any TextReader.</summary>
    public static Maze FromJson(TextReader reader)
    {
        return FromJson(reader.ReadToEnd());
    }


    public static void Save(Maze maze, string path, bool indented = true)
    {
        File.WriteAllText(path, ToJson(maze, indented));
    }

    public static Maze Load(string path)
    {
        return FromJson(File.ReadAllText(path));
    }


    public override void Write(Utf8JsonWriter writer, Maze maze, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("width", maze.Width);
        writer.WriteNumber("height", maze.Height);

        writer.WritePropertyName("entrance");
        JsonSerializer.Serialize(writer, maze.Entrance, options);
        writer.WritePropertyName("exit");
        JsonSerializer.Serialize(writer, maze.Exit, options);

        writer.WriteStartArray("cells");
        var cells = maze.Cells;
        for (var y = 0; y < maze.Height; y++)
        {
            for (var x = 0; x < maze.Width; x++)
            {
                WriteCell(writer, y, x, cells[y * maze.Width + x]);
            }
        }
        writer.WriteEndArray();

        if (maze.ShortestPath is { } path)
        {
            writer.WritePropertyName("shortestPath");
            JsonSerializer.Serialize(writer, path, options);
        }

        writer.WriteEndObject();
    }

    public override Maze Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("A maze must be a JSON object.");

        int? width = null, height = null;
        CellPosition? entrance = null, exit = null;
        List<(int Y, int X, Walls Walls)>? rawCells = null;
        List<CellPosition>? path = null;

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException("Unexpected token in maze.");

            var name = reader.GetString();
            reader.Read();

            if (Is(name, "width")) width = reader.GetInt32();
            else if (Is(name, "height")) height = reader.GetInt32();
            else if (Is(name, "entrance")) entrance = JsonSerializer.Deserialize<CellPosition>(ref reader, options);
            else if (Is(name, "exit")) exit = JsonSerializer.Deserialize<CellPosition>(ref reader, options);
            else if (Is(name, "cells")) rawCells = ReadCells(ref reader);
            else if (Is(name, "shortestPath")) path = JsonSerializer.Deserialize<List<CellPosition>>(ref reader, options);
            else reader.Skip();
        }

        if (width is null || height is null)
            throw new JsonException("Missing width or height.");
        if (width <= 0 || height <= 0)
            throw new JsonException("Width and height must be positive.");
        if (entrance is null || exit is null || rawCells is null)
            throw new JsonException("Missing entrance, exit or cells.");

        var cellCount = checked(width.Value * height.Value);
        if (rawCells.Count != cellCount)
            throw new JsonException($"Expected {cellCount} cells but found {rawCells.Count}.");

        var cells = new Walls[cellCount];
        var seen = new bool[cellCount];

        foreach (var (y, x, walls) in rawCells)
        {
            if (y < 0 || y >= height || x < 0 || x >= width)
                throw new JsonException($"Cell ({y}, {x}) lies outside the maze.");

            var i = y * width.Value + x;
            if (seen[i])
                throw new JsonException($"Cell ({y}, {x}) is listed more than once.");

            seen[i] = true;
            cells[i] = walls;
        }

        var maze = new Maze(height.Value, width.Value, cells, entrance.Value, exit.Value);

        if (!maze.Contains(entrance.Value)) throw new JsonException("The entrance lies outside the maze.");
        if (!maze.Contains(exit.Value)) throw new JsonException("The exit lies outside the maze.");

        if (path is not null)
        {
            foreach (var position in path)
            {
                if (!maze.Contains(position))
                    throw new JsonException($"Shortest path position {position} lies outside the maze.");
            }

            maze.ShortestPath = path;
        }

        return maze;
    }

    private static bool Is(string? name, string expected)
        => string.Equals(name, expected, StringComparison.OrdinalIgnoreCase);

    private static void WriteCell(Utf8JsonWriter writer, int y, int x, Walls walls)
    {
        writer.WriteStartObject();
        writer.WriteNumber("y", y);
        writer.WriteNumber("x", x);
        writer.WriteStartArray("walls");
        if (walls.HasFlag(Walls.Top)) writer.WriteStringValue("top");
        if (walls.HasFlag(Walls.Right)) writer.WriteStringValue("right");
        if (walls.HasFlag(Walls.Bottom)) writer.WriteStringValue("bottom");
        if (walls.HasFlag(Walls.Left)) writer.WriteStringValue("left");
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static List<(int Y, int X, Walls Walls)> ReadCells(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("cells must be an array.");

        var result = new List<(int, int, Walls)>();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            result.Add(ReadCell(ref reader));
        }

        return result;
    }

    private static (int Y, int X, Walls Walls) ReadCell(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("A cell must be an object.");

        int? y = null, x = null;
        var walls = Walls.None;

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException("Unexpected token in cell.");

            var name = reader.GetString();
            reader.Read();

            if (Is(name, "y")) y = reader.GetInt32();
            else if (Is(name, "x")) x = reader.GetInt32();
            else if (Is(name, "walls")) walls = ReadWalls(ref reader);
            else reader.Skip();
        }

        if (y is null || x is null)
            throw new JsonException("A cell needs both y and x.");

        return (y.Value, x.Value, walls);
    }

    private static Walls ReadWalls(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
            throw new JsonException("walls must be an array.");

        var walls = Walls.None;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            var name = reader.GetString();
            walls |= name?.ToLowerInvariant() switch
            {
                "top" => Walls.Top,
                "right" => Walls.Right,
                "bottom" => Walls.Bottom,
                "left" => Walls.Left,
                _ => throw new JsonException($"Unknown wall '{name}'."),
            };
        }

        return walls;
    }
}
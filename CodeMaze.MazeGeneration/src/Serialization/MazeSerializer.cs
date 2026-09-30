using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeMaze.MazeGeneration.Serialization;

/// <summary>
/// JSON format and convenience API for Labyrinth in one class.
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
///     ]
///   }
///
/// The static members (ToJson, FromJson, Save, ...) are the char/string/file
/// based entry points and go through the converter via the attribute on Labyrinth.
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


    internal sealed record PositionDto(int Y, int X);
    internal sealed record CellDto(int Y, int X, string[] Walls);
    internal sealed record LabyrinthDto(
        int Width,
        int Height,
        PositionDto Entrance,
        PositionDto Exit,
        List<CellDto> Cells);

    private static readonly JsonSerializerOptions DtoOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public override void Write(Utf8JsonWriter writer, Maze maze, JsonSerializerOptions options)
    {
        var cells = new List<CellDto>(maze.Width * maze.Height);
        for (var y = 0; y < maze.Height; y++)
        {
            for (var x = 0; x < maze.Width; x++)
            {
                cells.Add(new CellDto(y, x, ToNames(maze.Cells[y * maze.Width + x])));
            }
        }

        var dto = new LabyrinthDto(
            maze.Width,
            maze.Height,
            new PositionDto(maze.Entrance.Y.Value, maze.Entrance.X.Value),
            new PositionDto(maze.Exit.Y.Value, maze.Exit.X.Value),
            cells);

        JsonSerializer.Serialize(writer, dto, DtoOptions);
    }

    public override Maze Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var dto = JsonSerializer.Deserialize<LabyrinthDto>(ref reader, DtoOptions)
            ?? throw new JsonException("Empty maze document.");

        if (dto.Width <= 0 || dto.Height <= 0)
        {
            throw new JsonException("Width and height must be positive.");
        }

        if (dto.Entrance is null || dto.Exit is null || dto.Cells is null)
        {
            throw new JsonException("Missing entrance, exit or cells.");
        }

        var cellCount = dto.Width * dto.Height;
        if (dto.Cells.Count != cellCount)
        {
            throw new JsonException($"Expected {cellCount} cells but found {dto.Cells.Count}.");
        }

        var cells = new Walls[cellCount];
        var seen = new bool[cellCount];

        foreach (var cell in dto.Cells)
        {
            if (cell is null || cell.Y < 0 || cell.Y >= dto.Height || cell.X < 0 || cell.X >= dto.Width)
            {
                throw new JsonException($"Cell ({cell?.Y}, {cell?.X}) lies outside the maze.");
            }

            var i = cell.Y * dto.Width + cell.X;
            if (seen[i])
            {
                throw new JsonException($"Cell ({cell.Y}, {cell.X}) is listed more than once.");
            }

            seen[i] = true;
            cells[i] = FromNames(cell.Walls, cell.Y, cell.X);
        }

        return new Maze(
            dto.Height,
            dto.Width,
            cells,
            ToPosition(dto.Entrance, dto, "entrance"),
            ToPosition(dto.Exit, dto, "exit"));
    }

    private static string[] ToNames(Walls walls)
    {
        var names = new List<string>(4);
        if (walls.HasFlag(Walls.Top)) names.Add("top");
        if (walls.HasFlag(Walls.Right)) names.Add("right");
        if (walls.HasFlag(Walls.Bottom)) names.Add("bottom");
        if (walls.HasFlag(Walls.Left)) names.Add("left");
        return names.ToArray();
    }

    private static Walls FromNames(string[]? names, int y, int x)
    {
        var walls = Walls.None;
        foreach (var name in names ?? [])
        {
            walls |= name?.ToLowerInvariant() switch
            {
                "top" => Walls.Top,
                "right" => Walls.Right,
                "bottom" => Walls.Bottom,
                "left" => Walls.Left,
                _ => throw new JsonException($"Cell ({y}, {x}): unknown wall '{name}'."),
            };
        }

        return walls;
    }

    private static CellPosition ToPosition(PositionDto p, LabyrinthDto dto, string name)
    {
        if (p.Y < 0 || p.Y >= dto.Height || p.X < 0 || p.X >= dto.Width)
        {
            throw new JsonException($"The {name} lies outside the maze.");
        }

        return new CellPosition(new YCoordinate(p.Y), new XCoordinate(p.X));
    }
}
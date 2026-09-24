using System;

namespace CodeMaze.MazeGeneration.Maze;

public class Maze
{
    private const int DEFAULT_MAZE_WIDTH = 10;
    private const int DEFAULT_MAZE_HEIGHT = 10;
    public int Width { get; init; }
    public int Height { get; init; }
    public int[,] Cells { get; init; } = new int[DEFAULT_MAZE_HEIGHT, DEFAULT_MAZE_WIDTH];
}

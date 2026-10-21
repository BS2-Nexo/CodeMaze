using CodeMaze.MazeLang.Core;
using CodeMaze.MazeGeneration;
namespace CodeMaze.MazeLang.Interpreter;

/// <summary>Read-only description of a maze. Implement this to plug in your own maze model.</summary>
public interface IMazeLayout
{
    CellPosition Entrance { get; }
    CellPosition Exit { get; }
    /// <summary>True if the cell exists (is part of the maze).</summary>
    bool Contains(CellPosition cell);
    /// <summary>True if there is a wall on the given side (Up/Down/Left/Right) of the cell.</summary>
    bool HasWall(CellPosition cell, Direction side);
}
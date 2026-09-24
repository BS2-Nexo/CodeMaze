namespace CodeMaze.MazeGeneration.Maze.Draft;

public sealed class DraftCell(Walls walls)
{
    public Walls Walls { get; private set; } = walls;
}
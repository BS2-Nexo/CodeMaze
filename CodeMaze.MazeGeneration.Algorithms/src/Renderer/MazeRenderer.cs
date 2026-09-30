using System.Text;
using CodeMaze.MazeGeneration.Maze;
using CodeMaze.MazeGeneration.Maze.Draft;
namespace CodeMaze.MazeGeneration.Algorithms.Renderer;

public static class MazeRenderer
{
    public static void Print(DraftMaze maze)
    {
        Console.WriteLine(Render(maze));
    }

    public static string Render(DraftMaze maze)
    {
        var sb = new StringBuilder();

        for (var y = 0; y < maze.Height; y++)
        {
            for (var x = 0; x < maze.Width; x++)
            {
                sb.Append('+');
                sb.Append(Has(maze, y, x, Walls.Top) ? "---" : "   ");
            }
            sb.AppendLine("+");

            for (var x = 0; x < maze.Width; x++)
            {
                sb.Append(Has(maze, y, x, Walls.Left) ? '|' : ' ');
                sb.Append("   ");
            }
            sb.AppendLine(Has(maze, y, maze.Width - 1, Walls.Right) ? "|" : " ");
        }

        for (var x = 0; x < maze.Width; x++)
        {
            sb.Append('+');
            sb.Append(Has(maze, maze.Height - 1, x, Walls.Bottom) ? "---" : "   ");
        }
        sb.Append('+');

        return sb.ToString();
    }

    private static bool Has(DraftMaze maze, int y, int x, Walls wall)
    {
        return maze[new YCoordinate(y), new XCoordinate(x)].HasFlag(wall);
    }
}
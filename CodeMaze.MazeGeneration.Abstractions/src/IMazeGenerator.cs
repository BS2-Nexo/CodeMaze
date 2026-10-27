namespace CodeMaze.MazeGeneration.Abstractions;

public interface IMazeGenerator
{
    DraftMaze Generate(int height, int width);
    DraftMaze Generate(int height, int width, Difficulty difficulty);
}
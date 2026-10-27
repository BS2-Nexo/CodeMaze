namespace CodeMaze.MazeGeneration.Abstractions;

public interface IGeneratorAlgorithm
{
    DraftMaze Generate(int height, int width, Difficulty difficulty);
    DraftMaze Generate(DraftMaze draft);
}
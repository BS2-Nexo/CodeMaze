namespace CodeMaze.MazeGeneration.Abstractions;

public interface IGeneratorAlgorithm
{
    DraftMaze Generate(int height, int width);
    DraftMaze Generate(DraftMaze draft);
}
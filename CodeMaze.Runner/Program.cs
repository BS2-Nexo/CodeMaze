using CodeMaze.MazeGeneration;
using CodeMaze.MazeGeneration.Algorithms.RecursiveBacktracker;
using CodeMaze.MazeGeneration.Algorithms.Renderer;

var maze = new DraftMaze(10, 10, Walls.All);
var rand = new Random();
var algo = new RecursiveBacktracker(rand);
algo.Generate(maze);
MazeRenderer.Print(maze);
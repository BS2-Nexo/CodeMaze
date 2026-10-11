using System.Text.Json;
using CodeMaze.MazeGeneration;
using CodeMaze.MazeGeneration.Algorithms.RecursiveBacktracker;
using CodeMaze.MazeGeneration.Algorithms.Renderer;
using CodeMaze.MazeGeneration.Serialization;

var maze = new DraftMaze(10, 10, Walls.All);
var rand = new Random(123);
var algo = new RecursiveBacktracker(rand);
algo.Generate(maze);
var final = maze.Finalize();
MazeRenderer.Print(maze);
MazeSerializer.Save(final, "maze.json");   // indented, written straight to disk
Console.WriteLine(Path.GetFullPath("maze.json"));
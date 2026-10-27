using CodeMaze.MazeGeneration;

namespace CodeMaze.MazeLang.Interpreter;
/// <summary>Summary of a finished run. See <see cref="MazeInterpreter.GetRunStatistics"/>.</summary>

public sealed record RunStatistics(
    RunOutcome Outcome,
    string? Message,
    bool ReachedExit,
    CellPosition FinalPosition,
    long Steps,               // successful moves (as counted by MazeState)
    int DistinctCellsVisited, // including the start cell
    int RevisitedMoves,       // moves that ended on an already visited cell
    int BlockedMoves,         // attempted moves into a wall / with no history
    long InstructionsExecuted);
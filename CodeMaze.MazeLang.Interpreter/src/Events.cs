using System.Text.Json.Serialization;
using CodeMaze.MazeLang.Core;

namespace CodeMaze.MazeLang.Interpreter;

/// <summary>What happens when a move runs into a wall (or "back" has no history).</summary>
public enum CollisionBehavior
{
    /// <summary>The run ends with <see cref="RunOutcome.Crashed"/>.</summary>
    Crash,
    /// <summary>The move is a no-op: a <see cref="MoveBlocked"/> event is emitted and execution continues.</summary>
    Ignore,
}

public enum BlockReason { Wall, NoHistory }

public enum RunOutcome
{
    ExitReached,
    ProgramEnded,
    Crashed,
    Cancelled,
    /// <summary>Instruction budget or call depth exceeded.</summary>
    LimitExceeded,
    /// <summary>Malformed IL (compiler bug). The message has the details.</summary>
    Fault,
}

/// <summary>
/// Everything the interpreter reports. Plain immutable records, so they can be consumed
/// in-process or serialized (JSON polymorphism attributes are included) across a process boundary.
/// The last event of every run is always exactly one <see cref="Finished"/>.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(Started), "started")]
[JsonDerivedType(typeof(Moved), "moved")]
[JsonDerivedType(typeof(MoveBlocked), "moveBlocked")]
[JsonDerivedType(typeof(ExitReached), "exitReached")]
[JsonDerivedType(typeof(Finished), "finished")]
public abstract record InterpreterEvent;

public sealed record Started(CellPosition Position, CellPosition Exit) : InterpreterEvent;

/// <param name="Direction">The requested direction; <see cref="Direction.Back"/> for a step back through the history.</param>
/// <param name="Step">Running number of successful moves.</param>
public sealed record Moved(CellPosition From, CellPosition To, Direction Direction, int Step) : InterpreterEvent;

/// <summary>A move was not possible. In <see cref="CollisionBehavior.Crash"/> mode a Finished(Crashed) follows.</summary>
public sealed record MoveBlocked(CellPosition Position, Direction Direction, BlockReason Reason) : InterpreterEvent;

public sealed record ExitReached(CellPosition Position, int Steps) : InterpreterEvent;

public sealed record Finished(
    RunOutcome Outcome, string? Message, CellPosition Position, int Steps, long Instructions) : InterpreterEvent;
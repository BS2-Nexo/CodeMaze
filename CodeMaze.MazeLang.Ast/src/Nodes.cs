using CodeMaze.MazeLang.Core;

namespace CodeMaze.MazeLang.Ast;

// The AST is the common interface of all front ends.
// It knows nothing about the keywords or spelling rules (e.g. umlaut folding, letter case) of a
// concrete syntax; that is the job of the respective front end.

/// <summary>Position in the source text (1-based), used for error messages only.</summary>
public readonly record struct SourceLocation(int Line, int Column)
{
    public override string ToString() => $"{Line}:{Column}";
}

/// <summary>Base of all nodes. <see cref="Location"/> is optional.</summary>
public abstract record Node
{
    private protected Node() { }
    public SourceLocation? Location { get; init; }
}

// ------------------------------------------------------------ Program

/// <param name="Functions">User-defined functions (order does not matter, recursion is allowed).</param>
/// <param name="Body">Main program.</param>
public sealed record MazeProgram(
    IReadOnlyList<FunctionDecl> Functions,
    IReadOnlyList<Stmt> Body) : Node;

/// <summary>
/// A function. Parameters are always directions (never <see cref="Direction.Back"/>).
/// If any <see cref="Return"/> returns a value, the function is a sensor
/// (usable in conditions), otherwise it is an action.
/// </summary>
public sealed record FunctionDecl(
    string Name,
    IReadOnlyList<string> Parameters,
    IReadOnlyList<Stmt> Body) : Node;

// ------------------------------------------------------- Direction values

/// <summary>Expression that yields a direction.</summary>
public abstract record DirectionExpr : Node
{
    private protected DirectionExpr() { }
}

public sealed record DirectionLiteral(Direction Value) : DirectionExpr;

public sealed record ParameterRef(string Name) : DirectionExpr;

// ----------------------------------------------------------- Expressions

/// <summary>Expression that yields true/false.</summary>
public abstract record Expr : Node
{
    private protected Expr() { }
}

public sealed record BoolLiteral(bool Value) : Expr;

public sealed record Not(Expr Operand) : Expr;

/// <summary>Short-circuit: the right operand is evaluated only if needed.</summary>
public sealed record And(Expr Left, Expr Right) : Expr;

/// <summary>Short-circuit: the right operand is evaluated only if needed.</summary>
public sealed record Or(Expr Left, Expr Right) : Expr;

public enum SenseKind
{
    /// <summary>No wall in that direction (and not the maze border).</summary>
    Free,
    /// <summary>The cell in that direction has already been visited.</summary>
    Visited,
}

public sealed record Sense(SenseKind Kind, DirectionExpr Direction) : Expr;

/// <summary>Call of a user-defined function as an expression (the function must return a value).</summary>
public sealed record CallExpr(string Function, IReadOnlyList<DirectionExpr> Arguments) : Expr;

// ------------------------------------------------------------ Statements

public abstract record Stmt : Node
{
    private protected Stmt() { }
}

/// <summary>Move one cell in the direction (or go back with <see cref="Direction.Back"/>).</summary>
public sealed record Move(DirectionExpr Direction) : Stmt;

/// <summary>Call of a user-defined function as a statement; a result is discarded.</summary>
public sealed record CallStmt(string Function, IReadOnlyList<DirectionExpr> Arguments) : Stmt;

public sealed record Repeat(int Count, IReadOnlyList<Stmt> Body) : Stmt;

/// <summary>Infinite loop (ends by reaching the goal, crashing, or Return).</summary>
public sealed record Forever(IReadOnlyList<Stmt> Body) : Stmt;

/// <summary>An "else if" is an If inside the Else branch. An empty Else means no else.</summary>
public sealed record If(
    Expr Condition,
    IReadOnlyList<Stmt> Then,
    IReadOnlyList<Stmt> Else) : Stmt;

/// <summary>Ends the function, optionally with a value. In the main program only without a value.</summary>
public sealed record Return(Expr? Value) : Stmt;

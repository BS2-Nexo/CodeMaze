using CodeMaze.MazeLang.Ast;

namespace CodeMaze.MazeLang.Compiler.AST;

public sealed record Diagnostic(string Message, SourceLocation? Location)
{
    public override string ToString() =>
        Location is { } l ? $"({l}) {Message}" : Message;
}

/// <summary>Thrown when the AST is semantically invalid. Contains all errors found.</summary>
public sealed class CompileException : Exception
{
    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    public CompileException(IReadOnlyList<Diagnostic> diagnostics)
        : base(string.Join(Environment.NewLine, diagnostics))
    {
        Diagnostics = diagnostics;
    }
}

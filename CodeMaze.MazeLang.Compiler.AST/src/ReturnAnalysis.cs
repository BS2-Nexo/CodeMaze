using CodeMaze.MazeLang.Ast;

namespace CodeMaze.MazeLang.Compiler.AST;


/// <param name="WithValue">The body contains a Return with a value.</param>
/// <param name="Bare">The body contains a Return without a value.</param>
/// <param name="CanFallOff">Execution can reach the end of the body without hitting a Return.</param>
internal readonly record struct ReturnInfo(bool WithValue, bool Bare, bool CanFallOff)
{
    /// <summary>
    /// A function that returns a value anywhere must return one on every path. Otherwise the
    /// implicit "PushUnit; Return" at the end hands a unit to a caller that expects a bool
    /// (the interpreter would end the run with a Fault: "expected Bool, found Unit").
    /// </summary>
    public bool Inconsistent => WithValue && (Bare || CanFallOff);
}

internal static class ReturnAnalysis
{
    public static ReturnInfo Analyze(IReadOnlyList<Stmt> body)
    {
        var (withValue, bare) = Scan(body);
        return new ReturnInfo(withValue, bare, CanFallOff: !NeverFallsThrough(body));
    }

    /// <summary>Which kinds of Return occur anywhere in the body?</summary>
    private static (bool WithValue, bool Bare) Scan(IReadOnlyList<Stmt> body)
    {
        bool withValue = false, bare = false;
        foreach (var stmt in body)
        {
            (bool WithValue, bool Bare) r = stmt switch
            {
                Return { Value: null } => (false, true),
                Return => (true, false),
                If i => Merge(Scan(i.Then), Scan(i.Else)),
                Repeat rep => Scan(rep.Body),
                Forever f => Scan(f.Body),
                _ => (false, false),
            };
            withValue |= r.WithValue;
            bare |= r.Bare;
        }
        return (withValue, bare);
    }

    private static (bool WithValue, bool Bare) Merge(
        (bool WithValue, bool Bare) a, (bool WithValue, bool Bare) b) =>
        (a.WithValue || b.WithValue, a.Bare || b.Bare);

    /// <summary>True if control can never reach the end of this block.</summary>
    private static bool NeverFallsThrough(IReadOnlyList<Stmt> block)
    {
        foreach (var stmt in block)
            if (EndsControlFlow(stmt))
                return true;
        return false;
    }

    private static bool EndsControlFlow(Stmt stmt) => stmt switch
    {
        Return => true,
        // There is no 'break', so a Forever loop is only ever left through a Return.
        // (Revisit this if the language ever gets a way to leave a loop.)
        Forever => true,
        // Only if BOTH branches end; an 'if' without 'else' can always be skipped.
        If i => i.Else.Count > 0 && NeverFallsThrough(i.Then) && NeverFallsThrough(i.Else),
        // Repeat may run zero times, so it can always fall through.
        _ => false,
    };
}
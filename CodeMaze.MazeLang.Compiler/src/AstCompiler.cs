using System.Diagnostics.CodeAnalysis;
using CodeMaze.MazeLang.Ast;
using CodeMaze.MazeLang.Core;
using CodeMaze.MazeLang.IL;

namespace CodeMaze.MazeLang.Compiler;

/// <summary>
/// Translates an AST into IL. Along the way it checks everything the interpreter
/// should not have to check: unknown functions/parameters, argument count, Back in
/// the wrong place, action functions used in conditions, and more. All errors are
/// collected and reported as a <see cref="CompileException"/>.
/// </summary>
public static class AstCompiler
{
    public static ILProgram Compile(MazeProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);
        return new Compilation(program).Run();
    }
}

internal sealed record FunctionInfo(FunctionDecl Decl, int Index, bool ReturnsValue);

internal sealed class Compilation
{
    private const string EntryName = "<entry>";

    private readonly MazeProgram _program;
    private readonly List<Diagnostic> _diagnostics = new();
    private readonly Dictionary<string, FunctionInfo> _byName = new(StringComparer.Ordinal);
    private readonly List<FunctionInfo> _ordered = new();

    public Compilation(MazeProgram program) => _program = program;

    public void Error(string message, SourceLocation? at) =>
        _diagnostics.Add(new Diagnostic(message, at));

    public bool TryGetFunction(string name, [NotNullWhen(true)] out FunctionInfo? info) =>
        _byName.TryGetValue(name, out info);

    public ILProgram Run()
    {
        // Phase 1: register functions (index 0 is the entry point).
        // Afterwards every function may call every other one, including itself.
        foreach (var decl in _program.Functions)
        {
            if (_byName.ContainsKey(decl.Name))
            {
                Error($"The function '{decl.Name}' is defined more than once.", decl.Location);
                continue;
            }
            var (withValue, bare) = ScanReturns(decl.Body);
            if (withValue && bare)
                Error($"The function '{decl.Name}' sometimes returns a value and sometimes does not.", decl.Location);

            var info = new FunctionInfo(decl, _ordered.Count + 1, withValue);
            _byName[decl.Name] = info;
            _ordered.Add(info);
        }

        // Phase 2: generate code.
        var functions = new List<ILFunction>
        {
            new FunctionCompiler(this, EntryName, [], _program.Body, isEntry: true).Compile()
        };
        foreach (var info in _ordered)
            functions.Add(new FunctionCompiler(this, info.Decl.Name, info.Decl.Parameters, info.Decl.Body,
                isEntry: false).Compile());

        if (_diagnostics.Count > 0)
            throw new CompileException(_diagnostics);
        return new ILProgram(functions, EntryPoint: 0);
    }

    /// <summary>Which kinds of Return occur in the body?</summary>
    private static (bool WithValue, bool Bare) ScanReturns(IReadOnlyList<Stmt> body)
    {
        bool withValue = false, bare = false;
        foreach (var stmt in body)
        {
            (bool WithValue, bool Bare) r = stmt switch
            {
                Return { Value: null } => (false, true),
                Return => (true, false),
                If i => Merge(ScanReturns(i.Then), ScanReturns(i.Else)),
                Repeat rep => ScanReturns(rep.Body),
                Forever f => ScanReturns(f.Body),
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
}

/// <summary>Generates the code for exactly one function (or the main program).</summary>
internal sealed class FunctionCompiler
{
    private readonly Compilation _owner;
    private readonly string _name;
    private readonly IReadOnlyList<Stmt> _body;
    private readonly bool _isEntry;
    private readonly int _parameterCount;
    private readonly Dictionary<string, int> _slots = new(StringComparer.Ordinal);
    private readonly CodeBuilder _b = new();
    private int _nextLocal;

    public FunctionCompiler(Compilation owner, string name, IReadOnlyList<string> parameters,
        IReadOnlyList<Stmt> body, bool isEntry)
    {
        _owner = owner;
        _name = name;
        _body = body;
        _isEntry = isEntry;
        _parameterCount = parameters.Count;
        for (int i = 0; i < parameters.Count; i++)
            if (!_slots.TryAdd(parameters[i], i))
                owner.Error($"The parameter '{parameters[i]}' appears more than once in '{name}'.", null);
        _nextLocal = parameters.Count;
    }

    public ILFunction Compile()
    {
        EmitBlock(_body);
        // If execution falls off the end: no result.
        _b.Emit(OpCode.PushUnit);
        _b.Emit(OpCode.Return);
        return new ILFunction(_name, _parameterCount, _nextLocal, _b.Build());
    }

    // ------------------------------------------------------- Statements

    private void EmitBlock(IReadOnlyList<Stmt> block)
    {
        foreach (var stmt in block)
            EmitStmt(stmt);
    }

    private void EmitStmt(Stmt stmt)
    {
        switch (stmt)
        {
            case Move m:
                EmitDirection(m.Direction, allowBack: true);
                _b.Emit(OpCode.Move);
                break;

            case CallStmt c:
                EmitCall(c.Function, c.Arguments, c.Location, needsValue: false);
                _b.Emit(OpCode.Pop);               // discard the result
                break;

            case Repeat rep:
                {
                    if (rep.Count < 0)
                        _owner.Error("The repeat count must not be negative.", rep.Location);
                    int counter = _nextLocal++;        // separate slot per loop
                    var start = _b.NewLabel();
                    var end = _b.NewLabel();
                    _b.Emit(OpCode.PushInt, rep.Count);
                    _b.Emit(OpCode.StoreLocal, counter);
                    _b.Mark(start);
                    _b.Emit(OpCode.LoadLocal, counter);
                    _b.EmitJump(OpCode.JumpIfZero, end);
                    EmitBlock(rep.Body);
                    _b.Emit(OpCode.DecLocal, counter);
                    _b.EmitJump(OpCode.Jump, start);
                    _b.Mark(end);
                    break;
                }

            case Forever f:
                {
                    var start = _b.NewLabel();
                    _b.Mark(start);
                    EmitBlock(f.Body);
                    _b.EmitJump(OpCode.Jump, start);
                    break;
                }

            case If i:
                {
                    var elseLabel = _b.NewLabel();
                    EmitExpr(i.Condition);
                    _b.EmitJump(OpCode.JumpIfFalse, elseLabel);
                    EmitBlock(i.Then);
                    if (i.Else.Count == 0)
                    {
                        _b.Mark(elseLabel);
                    }
                    else
                    {
                        var end = _b.NewLabel();
                        _b.EmitJump(OpCode.Jump, end);
                        _b.Mark(elseLabel);
                        EmitBlock(i.Else);
                        _b.Mark(end);
                    }
                    break;
                }

            case Return ret:
                if (ret.Value is null)
                {
                    _b.Emit(OpCode.PushUnit);
                }
                else
                {
                    if (_isEntry)
                        _owner.Error("The main program cannot return a value.", ret.Location);
                    EmitExpr(ret.Value);
                }
                _b.Emit(OpCode.Return);
                break;

            default:
                throw new NotSupportedException($"Unknown statement type {stmt.GetType().Name}.");
        }
    }

    // --------------------------------------------------------- Expressions

    private void EmitExpr(Expr expr)
    {
        switch (expr)
        {
            case BoolLiteral b:
                _b.Emit(OpCode.PushBool, b.Value ? 1 : 0);
                break;

            case Not n:
                EmitExpr(n.Operand);
                _b.Emit(OpCode.Not);
                break;

            case And a:
                {
                    // left; if false -> false, otherwise right
                    var isFalse = _b.NewLabel();
                    var end = _b.NewLabel();
                    EmitExpr(a.Left);
                    _b.EmitJump(OpCode.JumpIfFalse, isFalse);
                    EmitExpr(a.Right);
                    _b.EmitJump(OpCode.Jump, end);
                    _b.Mark(isFalse);
                    _b.Emit(OpCode.PushBool, 0);
                    _b.Mark(end);
                    break;
                }

            case Or o:
                {
                    // left; if false -> right, otherwise true
                    var tryRight = _b.NewLabel();
                    var end = _b.NewLabel();
                    EmitExpr(o.Left);
                    _b.EmitJump(OpCode.JumpIfFalse, tryRight);
                    _b.Emit(OpCode.PushBool, 1);
                    _b.EmitJump(OpCode.Jump, end);
                    _b.Mark(tryRight);
                    EmitExpr(o.Right);
                    _b.Mark(end);
                    break;
                }

            case Sense s:
                EmitDirection(s.Direction, allowBack: false);
                _b.Emit(s.Kind == SenseKind.Free ? OpCode.SenseFree : OpCode.SenseVisited);
                break;

            case CallExpr c:
                EmitCall(c.Function, c.Arguments, c.Location, needsValue: true);
                break;

            default:
                throw new NotSupportedException($"Unknown expression type {expr.GetType().Name}.");
        }
    }

    private void EmitDirection(DirectionExpr dir, bool allowBack)
    {
        switch (dir)
        {
            case DirectionLiteral lit:
                if (lit.Value == Direction.Back && !allowBack)
                    _owner.Error("'Back' is only allowed as the target of a move.", lit.Location);
                _b.Emit(OpCode.PushDirection, (int)lit.Value);
                break;

            case ParameterRef p:
                if (_slots.TryGetValue(p.Name, out int slot))
                    _b.Emit(OpCode.LoadLocal, slot);
                else
                    _owner.Error($"The parameter '{p.Name}' is not known here.", p.Location);
                break;

            default:
                throw new NotSupportedException($"Unknown direction type {dir.GetType().Name}.");
        }
    }

    private void EmitCall(string name, IReadOnlyList<DirectionExpr> args, SourceLocation? at, bool needsValue)
    {
        foreach (var arg in args)
            EmitDirection(arg, allowBack: false);

        if (!_owner.TryGetFunction(name, out var info))
        {
            _owner.Error($"The function '{name}' is not defined.", at);
            return;
        }
        if (args.Count != info.Decl.Parameters.Count)
            _owner.Error($"'{name}' expects {info.Decl.Parameters.Count} argument(s) " +
                         $"but got {args.Count}.", at);
        if (needsValue && !info.ReturnsValue)
            _owner.Error($"'{name}' does not return a value and cannot be used as a condition here.", at);

        _b.Emit(OpCode.Call, info.Index);
    }
}

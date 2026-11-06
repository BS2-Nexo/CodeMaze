using System.Diagnostics.CodeAnalysis;
using CodeMaze.MazeLang.Ast;
using CodeMaze.MazeLang.Core;
using CodeMaze.MazeLang.IL;

namespace CodeMaze.MazeLang.Compiler.AST;


/// <summary>
/// Translates an AST into IL. Along the way it checks everything the interpreter
/// should not have to check: unknown functions/parameters, argument count, Back in
/// the wrong place, direction/number mix-ups, action functions used in conditions,
/// and more. All errors are collected and reported as a <see cref="CompileException"/>.
/// </summary>
public static class AstCompiler
{
    public static ILProgram Compile(MazeProgram program)
    {
        ArgumentNullException.ThrowIfNull(program);
        return new Compilation(program).Run();
    }
}

/// <summary>What a parameter holds. Inferred from how the parameter is used.</summary>
internal enum ValueKind { Unknown, Direction, Number }

internal sealed record FunctionInfo(FunctionDecl Decl, int Index, bool ReturnsValue)
{
    /// <summary>Inferred kind per parameter; Unknown = never used in a way that decides it.</summary>
    public ValueKind[] ParamKinds { get; } = new ValueKind[Decl.Parameters.Count];
}

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

            var ret = ReturnAnalysis.Analyze(decl.Body);
            if (ret.Inconsistent)
                Error($"The function '{decl.Name}' returns a value on some paths but can also end without one.", decl.Location);

            var info = new FunctionInfo(decl, _ordered.Count + 1, ret.WithValue);
            _byName[decl.Name] = info;
            _ordered.Add(info);
        }

        // Phase 1.5: infer parameter kinds (direction / number) from their uses.
        InferParameterKinds();

        // Phase 2: generate code.
        var functions = new List<ILFunction>
        {
            new FunctionCompiler(this, EntryName, [], [], _program.Body, isEntry: true).Compile()
        };
        foreach (var info in _ordered)
            functions.Add(new FunctionCompiler(this, info.Decl.Name, info.Decl.Parameters, info.ParamKinds,
                info.Decl.Body, isEntry: false).Compile());

        if (_diagnostics.Count > 0)
            throw new CompileException(_diagnostics);
        return new ILProgram(functions, EntryPoint: 0);
    }

    // ------------------------------------------------ parameter kind inference

    /// <summary>
    /// The first use that decides a parameter's kind wins. Kinds propagate through calls in both
    /// directions until nothing changes. Conflicting uses are reported later, during emission.
    /// </summary>
    private void InferParameterKinds()
    {
        bool changed;
        do
        {
            changed = false;
            foreach (var info in _ordered)
                changed |= InferBlock(info, info.Decl.Body);
        } while (changed);
    }

    private bool InferBlock(FunctionInfo f, IReadOnlyList<Stmt> block)
    {
        bool changed = false;
        foreach (var s in block)
            changed |= InferStmt(f, s);
        return changed;
    }

    private bool InferStmt(FunctionInfo f, Stmt s) => s switch
    {
        Move m => Constrain(f, m.Direction, ValueKind.Direction),
        CallStmt c => InferCall(f, c.Function, c.Arguments),
        Repeat r => Constrain(f, r.Count, ValueKind.Number) | InferBlock(f, r.Body),
        Forever fv => InferBlock(f, fv.Body),
        If i => InferExpr(f, i.Condition) | InferBlock(f, i.Then) | InferBlock(f, i.Else),
        Return { Value: { } v } => InferExpr(f, v),
        _ => false,
    };

    private bool InferExpr(FunctionInfo f, Expr e) => e switch
    {
        Not n => InferExpr(f, n.Operand),
        And a => InferExpr(f, a.Left) | InferExpr(f, a.Right),
        Or o => InferExpr(f, o.Left) | InferExpr(f, o.Right),
        Sense s => Constrain(f, s.Direction, ValueKind.Direction),
        CallExpr c => InferCall(f, c.Function, c.Arguments),
        _ => false,
    };

    private bool InferCall(FunctionInfo f, string name, IReadOnlyList<ValueExpr> args)
    {
        if (!_byName.TryGetValue(name, out var callee)) return false;
        bool changed = false;
        int n = Math.Min(args.Count, callee.ParamKinds.Length);
        for (int i = 0; i < n; i++)
        {
            var argKind = KindOf(f, args[i]);
            if (argKind == ValueKind.Unknown)
            {
                changed |= Constrain(f, args[i], callee.ParamKinds[i]);      // callee -> caller
            }
            else if (callee.ParamKinds[i] == ValueKind.Unknown)
            {
                callee.ParamKinds[i] = argKind;                              // caller -> callee
                changed = true;
            }
        }
        return changed;
    }

    private static ValueKind KindOf(FunctionInfo f, ValueExpr v)
    {
        switch (v)
        {
            case DirectionLiteral: return ValueKind.Direction;
            case NumberLiteral: return ValueKind.Number;
            case ParameterRef p:
                int idx = ParamIndex(f, p.Name);
                return idx >= 0 ? f.ParamKinds[idx] : ValueKind.Unknown;
            default: return ValueKind.Unknown;
        }
    }

    private static bool Constrain(FunctionInfo f, ValueExpr v, ValueKind kind)
    {
        if (kind == ValueKind.Unknown || v is not ParameterRef p) return false;
        int i = ParamIndex(f, p.Name);
        if (i < 0 || f.ParamKinds[i] != ValueKind.Unknown) return false;
        f.ParamKinds[i] = kind;
        return true;
    }

    private static int ParamIndex(FunctionInfo f, string name)
    {
        for (int i = 0; i < f.Decl.Parameters.Count; i++)
            if (f.Decl.Parameters[i] == name) return i;   // first one wins, like _slots
        return -1;
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
    private readonly ValueKind[] _kinds;
    private readonly IReadOnlyList<Stmt> _body;
    private readonly bool _isEntry;
    private readonly int _parameterCount;
    private readonly Dictionary<string, int> _slots = new(StringComparer.Ordinal);
    private readonly CodeBuilder _b = new();
    private int _nextLocal;

    public FunctionCompiler(Compilation owner, string name, IReadOnlyList<string> parameters,
        ValueKind[] kinds, IReadOnlyList<Stmt> body, bool isEntry)
    {
        _owner = owner;
        _name = name;
        _kinds = kinds;
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
                EmitValue(m.Direction, ValueKind.Direction, allowBack: true);
                _b.Emit(OpCode.Move);
                break;

            case CallStmt c:
                EmitCall(c.Function, c.Arguments, c.Location, needsValue: false);
                _b.Emit(OpCode.Pop);               // discard the result
                break;

            case Repeat rep:
                {
                    int counter = _nextLocal++;        // separate slot per loop
                    var start = _b.NewLabel();
                    var end = _b.NewLabel();
                    EmitValue(rep.Count, ValueKind.Number, allowBack: false);   // literal or parameter
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
                EmitValue(s.Direction, ValueKind.Direction, allowBack: false);
                _b.Emit(s.Kind == SenseKind.Free ? OpCode.SenseFree : OpCode.SenseVisited);
                break;

            case CallExpr c:
                EmitCall(c.Function, c.Arguments, c.Location, needsValue: true);
                break;

            default:
                throw new NotSupportedException($"Unknown expression type {expr.GetType().Name}.");
        }
    }

    // ------------------------------------------------------------ Values

    private void EmitValue(ValueExpr v, ValueKind expected, bool allowBack)
    {
        switch (v)
        {
            case DirectionLiteral lit:
                if (expected == ValueKind.Number)
                    _owner.Error("Expected a number but found a direction.", lit.Location);
                else if (lit.Value == Direction.Back && !allowBack)
                    _owner.Error("'Back' is only allowed as the target of a move.", lit.Location);
                _b.Emit(OpCode.PushDirection, (int)lit.Value);
                break;

            case NumberLiteral num:
                if (expected == ValueKind.Direction)
                    _owner.Error("Expected a direction but found a number.", num.Location);
                _b.Emit(OpCode.PushInt, num.Value);
                break;

            case ParameterRef p:
                if (_slots.TryGetValue(p.Name, out int slot))
                {
                    var actual = _kinds[slot];
                    if (actual != ValueKind.Unknown && expected != ValueKind.Unknown && actual != expected)
                        _owner.Error($"The parameter '{p.Name}' is used as a {actual.ToString().ToLowerInvariant()} " +
                                     $"and as a {expected.ToString().ToLowerInvariant()}.", p.Location);
                    _b.Emit(OpCode.LoadLocal, slot);
                }
                else
                {
                    _owner.Error($"The parameter '{p.Name}' is not known here.", p.Location);
                }
                break;

            default:
                throw new NotSupportedException($"Unknown value type {v.GetType().Name}.");
        }
    }

    private void EmitCall(string name, IReadOnlyList<ValueExpr> args, SourceLocation? at, bool needsValue)
    {
        if (!_owner.TryGetFunction(name, out var info))
        {
            foreach (var arg in args)                      // still report errors inside the arguments
                EmitValue(arg, ValueKind.Unknown, allowBack: false);
            _owner.Error($"The function '{name}' is not defined.", at);
            return;
        }

        for (int i = 0; i < args.Count; i++)
        {
            var expected = i < info.ParamKinds.Length ? info.ParamKinds[i] : ValueKind.Unknown;
            EmitValue(args[i], expected, allowBack: false);
        }

        if (args.Count != info.Decl.Parameters.Count)
            _owner.Error($"'{name}' expects {info.Decl.Parameters.Count} argument(s) but got {args.Count}.", at);
        if (needsValue && !info.ReturnsValue)
            _owner.Error($"'{name}' does not return a value and cannot be used as a condition here.", at);

        _b.Emit(OpCode.Call, info.Index);
    }
}
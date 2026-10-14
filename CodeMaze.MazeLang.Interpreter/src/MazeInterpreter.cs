using System.Runtime.CompilerServices;
using CodeMaze.MazeLang.Core;
using CodeMaze.MazeLang.IL;

namespace CodeMaze.MazeLang.Interpreter;

/// <summary>
/// Stack-machine interpreter for <see cref="ILProgram"/>.
///
/// Usage styles (one run per instance):
///  - pull:   foreach (var ev in interpreter.Run(ct)) ...         (lazy; the consumer sets the pace)
///  - async:  await foreach (var ev in interpreter.RunAsync(...)) (optional delay after each move)
///  - push:   interpreter.RunToCompletion(ev => ..., ct)
/// The interpreter has no console/UI/process dependencies; a host can wrap it in a separate
/// process by serializing the events (see <see cref="InterpreterEvent"/>).
/// </summary>
public sealed class MazeInterpreter
{
    private enum Kind : byte { Unit, Bool, Int, Direction }

    private readonly record struct Value(Kind Kind, int Raw)
    {
        public static readonly Value Unit = new(Kind.Unit, 0);
        public static Value Bool(bool b) => new(Kind.Bool, b ? 1 : 0);
        public static Value Int(int i) => new(Kind.Int, i);
        public static Value Dir(Direction d) => new(Kind.Direction, (int)d);

        public bool AsBool() => Expect(Kind.Bool) != 0;
        public int AsInt() => Expect(Kind.Int);
        public Direction AsDirection() => (Direction)Expect(Kind.Direction);

        private int Expect(Kind k) => Kind == k
            ? Raw
            : throw new InvalidOperationException($"Type error in IL: expected {k}, found {Kind}.");
    }

    private sealed class Frame(ILFunction fn, Value[] locals)
    {
        public ILFunction Fn { get; } = fn;
        public Value[] Locals { get; } = locals;
        public int Pc;
    }

    /// <summary>Internal: terminates the run with a given outcome.</summary>
    private sealed class StopRun(RunOutcome outcome, string? detail) : Exception(detail)
    {
        public RunOutcome Outcome { get; } = outcome;
        /// <summary>Unlike Exception.Message this stays null when no message was given.</summary>
        public string? Detail { get; } = detail;
    }

    private readonly ILProgram _program;
    private readonly MazeState _maze;
    private readonly InterpreterOptions _options;
    private readonly Stack<Value> _stack = new();
    private readonly Stack<Frame> _frames = new();
    private Frame _frame = null!;
    private long _instructions;
    private bool _exitReported;
    private bool _used;

    public MazeInterpreter(ILProgram program, IMazeLayout layout, InterpreterOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(layout);
        _program = program;
        _options = options ?? new InterpreterOptions();
        _maze = new MazeState(layout);
    }

    /// <summary>Live state, e.g. for a UI that wants to inspect visited cells.</summary>
    public MazeState State => _maze;
    public long InstructionsExecuted => _instructions;

    // ------------------------------------------------------------ entry points

    /// <summary>
    /// Runs lazily: code executes only while the consumer enumerates. Cancellation never throws;
    /// it ends the stream with Finished(Cancelled). Stopping enumeration early simply pauses/abandons the run.
    /// </summary>
    public IEnumerable<InterpreterEvent> Run(CancellationToken cancellation = default)
    {
        if (_used) throw new InvalidOperationException("A MazeInterpreter instance can only run once.");
        _used = true;
        return Execute(cancellation);
    }

    /// <summary>Async stream. <paramref name="stepDelay"/> (animation) is awaited after each Moved event.</summary>
    public async IAsyncEnumerable<InterpreterEvent> RunAsync(
        TimeSpan stepDelay = default,
        [EnumeratorCancellation] CancellationToken cancellation = default)
    {
        foreach (var ev in Run(cancellation))
        {
            yield return ev;
            if (stepDelay > TimeSpan.Zero && ev is Moved)
            {
                try { await Task.Delay(stepDelay, cancellation).ConfigureAwait(false); }
                catch (OperationCanceledException) { /* next loop turn emits Finished(Cancelled) */ }
            }
        }
    }

    /// <summary>Push style: runs synchronously and calls <paramref name="onEvent"/> for each event. Returns the final event.</summary>
    public Finished RunToCompletion(Action<InterpreterEvent>? onEvent = null, CancellationToken cancellation = default)
    {
        Finished? last = null;
        foreach (var ev in Run(cancellation))
        {
            onEvent?.Invoke(ev);
            if (ev is Finished f) last = f;
        }
        return last!;
    }

    // --------------------------------------------------------------- the loop

    private IEnumerable<InterpreterEvent> Execute(CancellationToken ct)
    {
        yield return new Started(_maze.Position, _maze.Exit);

        var entry = _program.Functions[_program.EntryPoint];
        _frame = new Frame(entry, NewLocals(entry));
        _frames.Push(_frame);

        var pending = new List<InterpreterEvent>(2);
        Finished? end = null;

        while (end is null)
        {
            if (ct.IsCancellationRequested)
            {
                end = Finish(RunOutcome.Cancelled, "Cancelled.");
                break;
            }
            if (_instructions >= _options.MaxInstructions)
            {
                end = Finish(RunOutcome.LimitExceeded, "Too many instructions. Is the program running in circles?");
                break;
            }
            _instructions++;

            // yield is not allowed inside try/catch, so step inside the try and yield afterwards.
            try
            {
                if (!Step(pending))
                    end = Finish(RunOutcome.ProgramEnded, null);
            }
            catch (StopRun s)
            {
                end = Finish(s.Outcome, s.Detail);
            }
            catch (Exception e) when (e is InvalidOperationException or ArgumentOutOfRangeException
                                          or IndexOutOfRangeException or NotSupportedException)
            {
                end = Finish(RunOutcome.Fault, $"Invalid IL in '{_frame.Fn.Name}' at {_frame.Pc - 1}: {e.Message}");
            }

            if (pending.Count > 0)
            {
                foreach (var ev in pending) yield return ev;
                pending.Clear();
            }
        }

        yield return end;
    }

    private Finished Finish(RunOutcome outcome, string? message) =>
        new(outcome, message, _maze.Position, _maze.Steps, _instructions);

    private static Value[] NewLocals(ILFunction fn)
    {
        var locals = new Value[fn.LocalCount];
        Array.Fill(locals, Value.Unit);
        return locals;
    }

    /// <summary>Executes one instruction. Returns false when the program has ended.</summary>
    private bool Step(List<InterpreterEvent> output)
    {
        var code = _frame.Fn.Code;
        if (_frame.Pc >= code.Count)
            return DoReturn(Value.Unit);   // fell off the end: same as "return nothing"

        var ins = code[_frame.Pc++];
        switch (ins.Op)
        {
            case OpCode.PushBool: _stack.Push(Value.Bool(ins.Operand != 0)); break;
            case OpCode.PushInt: _stack.Push(Value.Int(ins.Operand)); break;
            case OpCode.PushDirection: _stack.Push(Value.Dir((Direction)ins.Operand)); break;
            case OpCode.PushUnit: _stack.Push(Value.Unit); break;

            case OpCode.LoadLocal: _stack.Push(_frame.Locals[ins.Operand]); break;
            case OpCode.StoreLocal: _frame.Locals[ins.Operand] = _stack.Pop(); break;
            case OpCode.DecLocal:
                _frame.Locals[ins.Operand] = Value.Int(_frame.Locals[ins.Operand].AsInt() - 1);
                break;

            case OpCode.Pop: _stack.Pop(); break;
            case OpCode.Not: _stack.Push(Value.Bool(!_stack.Pop().AsBool())); break;

            case OpCode.SenseFree: _stack.Push(Value.Bool(_maze.IsFree(_stack.Pop().AsDirection()))); break;
            case OpCode.SenseVisited: _stack.Push(Value.Bool(_maze.IsVisited(_stack.Pop().AsDirection()))); break;
            case OpCode.Move: DoMove(_stack.Pop().AsDirection(), output); break;

            case OpCode.Jump: _frame.Pc = ins.Operand; break;
            case OpCode.JumpIfFalse: if (!_stack.Pop().AsBool()) _frame.Pc = ins.Operand; break;
            case OpCode.JumpIfZero: if (_stack.Pop().AsInt() == 0) _frame.Pc = ins.Operand; break;

            case OpCode.Call: DoCall(ins.Operand); break;
            case OpCode.Return: return DoReturn(_stack.Pop());

            default: throw new NotSupportedException($"Unknown opcode {ins.Op}.");
        }
        return true;
    }

    private void DoCall(int functionIndex)
    {
        var fn = _program.Functions[functionIndex];
        if (_frames.Count - 1 >= _options.MaxCallDepth)
            throw new StopRun(RunOutcome.LimitExceeded, $"The function '{fn.Name}' calls itself too deeply.");

        var locals = NewLocals(fn);
        for (int i = fn.ParameterCount - 1; i >= 0; i--)   // arg0 was pushed first
            locals[i] = _stack.Pop();

        _frame = new Frame(fn, locals);
        _frames.Push(_frame);
    }

    private bool DoReturn(Value result)
    {
        _frames.Pop();
        if (_frames.Count == 0)
            return false;                 // return from the entry function = end of program
        _frame = _frames.Peek();
        _stack.Push(result);
        return true;
    }

    private void DoMove(Direction dir, List<InterpreterEvent> output)
    {
        if (!_maze.TryMove(dir, out var from, out var to))
        {
            var reason = dir == Direction.Back ? BlockReason.NoHistory : BlockReason.Wall;
            output.Add(new MoveBlocked(from, dir, reason));
            if (_options.OnCollision == CollisionBehavior.Crash)
                throw new StopRun(RunOutcome.Crashed, reason == BlockReason.Wall
                    ? $"Crash! There is a wall ({dir})."
                    : "Crash! There is no step to go back to.");
            return;                       // Ignore: no-op, continue
        }

        output.Add(new Moved(from, to, dir, _maze.Steps));

        if (_maze.AtExit && !_exitReported)
        {
            _exitReported = true;
            output.Add(new ExitReached(to, _maze.Steps));
            if (_options.StopAtExit)
                throw new StopRun(RunOutcome.ExitReached, null);
        }
    }
}
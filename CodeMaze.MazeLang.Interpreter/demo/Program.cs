// Demo / self-test suite for the IL interpreter.
//
// Runs 13 hand-assembled IL programs on small ASCII mazes. Every event is written to stdout as one
// JSON line; lines starting with '#' are comments (scenario headers and PASS/FAIL results), so
// the event stream can be filtered with: grep -v '^#'
//
//   dotnet run                     all scenarios
//   dotnet run -- --quiet          only headers and results
//   dotnet run -- --only dfs-solver
//   dotnet run -- --list
//
// Exit code 0 = all scenarios passed.
//
// What is covered (opcode / feature):
//   PushBool/PushInt/PushDirection/PushUnit, LoadLocal/StoreLocal/DecLocal, Pop, Not,
//   SenseFree, SenseVisited, Move (incl. Back), Jump/JumpIfFalse/JumpIfZero, Call/Return,
//   recursion, 2 parameters (argument order), bool-returning functions used as conditions,
//   short-circuit and/or, if / else-if / else, nested repeat, forever, return from inside a loop,
//   both collision modes (wall + "back" without history), StopAtExit on/off,
//   instruction limit, call-depth limit, external cancellation, malformed IL (Fault),
//   plus a generic event-stream consistency check applied to every run.

using System.Text.Json;
using System.Text.Json.Serialization;
using CodeMaze.MazeLang.Core;
using CodeMaze.MazeLang.IL;
using CodeMaze.MazeLang.Interpreter;
using CodeMaze.MazeGeneration;

bool quiet = args.Contains("--quiet");
string? only = null;
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--only" && i + 1 < args.Length) only = args[++i];
    else if (args[i] != "--quiet" && args[i] != "--list")
    {
        Console.Error.WriteLine("Usage: [--quiet] [--only <name>] [--list]");
        return 2;
    }
}

var scenarios = Scenarios.All();
if (args.Contains("--list"))
{
    foreach (var s in scenarios) Console.WriteLine($"{s.Name,-24} {s.Description}");
    return 0;
}

using var appCts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; appCts.Cancel(); };

var json = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
};

int passed = 0, total = 0;
foreach (var sc in scenarios)
{
    if (only is not null && sc.Name != only) continue;
    total++;
    Console.WriteLine($"# === {sc.Name}: {sc.Description}");

    var run = new Run();
    var interpreter = new MazeInterpreter(sc.Program, sc.Maze, sc.Options);
    using var cts = CancellationTokenSource.CreateLinkedTokenSource(appCts.Token);

    await foreach (var ev in interpreter.RunAsync(cancellation: cts.Token))
    {
        run.Events.Add(ev);
        if (!quiet)
            Console.WriteLine(JsonSerializer.Serialize<InterpreterEvent>(ev, json)); // base type => "type" discriminator
        sc.OnEvent?.Invoke(ev, cts);
    }

    var asserts = new Asserts();
    Checks.Consistency(asserts, run, sc.Maze);
    sc.Check(asserts, run);

    if (asserts.Errors.Count == 0) { passed++; Console.WriteLine("# PASS"); }
    else
    {
        Console.WriteLine("# FAIL");
        foreach (var e in asserts.Errors) Console.WriteLine($"#   - expected: {e}");
    }
}

Console.WriteLine($"# {passed}/{total} scenarios passed");
return passed == total ? 0 : 1;


// =============================================================================================
// Scenarios
// =============================================================================================

static class Scenarios
{
    static readonly InterpreterOptions Crash = new();
    static readonly InterpreterOptions Ignore = new() { OnCollision = CollisionBehavior.Ignore };

    // 4x4 maze with a loop and dead ends. Only the exit E at top right is interesting:
    // DFS order (up, right, down, left) goes S -> (0,1) -> (0,2) -> (1,2) -> (1,1) -> (2,1) -> (3,1)
    // -> (3,2) -> (2,2) [dead end, one Back] -> (3,3) -> (2,3) -> (1,3) -> E (0,3) = 13 steps.
    static readonly AsciiMaze Dfs4 = AsciiMaze.Parse(
        "+---+---+---+---+",
        "| S         | E |",
        "+   +---+   +   +",
        "|   |       |   |",
        "+   +   +---+   +",
        "|       |   |   |",
        "+---+   +   +   +",
        "|               |",
        "+---+---+---+---+");

    static readonly AsciiMaze Corridor = AsciiMaze.Parse(
        "+---+---+---+---+---+---+",
        "| S                   E |",
        "+---+---+---+---+---+---+");

    // Open 2x2 room, start top left, exit bottom left.
    static readonly AsciiMaze Room = AsciiMaze.Parse(
        "+---+---+",
        "| S     |",
        "+   +   +",
        "| E     |",
        "+---+---+");

    const Direction Up = Direction.Up, Down = Direction.Down, Left = Direction.Left, Right = Direction.Right;

    public static List<Scenario> All()
    {
        int blockedForCancel = 0;
        return new List<Scenario>
        {
            new("dfs-solver",
                "recursive DFS: calls, args, bool result, and/not, frei/besucht, Back",
                DfsProgram(), Dfs4, Crash, (a, r) =>
                {
                    Checks.Outcome(a, r, RunOutcome.ExitReached);
                    a.That(r.Final?.Steps == 13, "13 steps");
                    a.That(r.Moves.Count(m => m.Direction == Direction.Back) == 1, "exactly one Back move");
                    a.That(!r.Blocked.Any(), "no blocked moves");
                    a.That(r.Final?.Position == Dfs4.Exit, "ends on the exit");
                    a.That(r.Exits.Count() == 1, "one ExitReached event");
                    Checks.NoRevisits(a, r, Dfs4.Entrance);
                }),

            new("dfs-continue-after-exit",
                "same DFS with StopAtExit=false: explores everything and walks back to the start",
                DfsProgram(), Dfs4, new InterpreterOptions { StopAtExit = false }, (a, r) =>
                {
                    Checks.Outcome(a, r, RunOutcome.ProgramEnded);
                    a.That(r.Exits.Count() == 1 && r.Exits.First().Steps == 13, "ExitReached once, at step 13");
                    a.That(r.Final?.Steps == 30, "30 steps (15 forward + 15 back)");
                    a.That(r.Moves.Count(m => m.Direction != Direction.Back) == 15, "15 forward moves = 16 cells visited");
                    a.That(r.Final?.Position == Dfs4.Entrance, "back at the entrance");
                    Checks.NoRevisits(a, r, Dfs4.Entrance);
                }),

            new("nested-loops",
                "repeat in repeat (own counter slots), repeat 0 is a no-op",
                NestedLoopsProgram(), Corridor, Crash, (a, r) =>
                {
                    Checks.Outcome(a, r, RunOutcome.ProgramEnded);
                    a.That(r.Final?.Steps == 6, "6 steps");
                    a.That(r.Final?.Position == new CellPosition(new YCoordinate(0),new XCoordinate(2)), "ends at x=2");
                    a.That(!r.Blocked.Any(), "no blocked moves");
                }),

            new("if-else-chain",
                "if / else-if / else, or (both operands), and, not, literal, if without else",
                IfElseProgram(), Room, Crash, (a, r) =>
                {
                    Checks.Outcome(a, r, RunOutcome.ExitReached);
                    a.That(r.Moves.Select(m => m.Direction).SequenceEqual(new[] { Right, Down, Left }),
                        "moves: right, down, left");
                }),

            new("return-from-forever",
                "bool function returns from inside forever+if; call used as condition",
                BisWandProgram(), Dfs4, Crash, (a, r) =>
                {
                    Checks.Outcome(a, r, RunOutcome.ProgramEnded);
                    a.That(r.Moves.Select(m => m.Direction).SequenceEqual(new[] { Right, Right, Down }),
                        "moves: right, right, down");
                    a.That(r.Final?.Position == new CellPosition(new YCoordinate(1), new XCoordinate(2)), "ends at (1,2)");
                }),

            new("two-parameters",
                "function with 2 parameters: argument order and local slots",
                TwoParamsProgram(), Dfs4, Crash, (a, r) =>
                {
                    Checks.Outcome(a, r, RunOutcome.ProgramEnded);
                    a.That(r.Moves.Select(m => m.Direction).SequenceEqual(new[] { Down, Down, Right, Left }),
                        "moves: down, down, right, left");
                    a.That(r.Final?.Position == new CellPosition(new YCoordinate(2), new XCoordinate(0)), "ends at (2,0)");
                }),

            new("collision-crash",
                "move into a wall with CollisionBehavior.Crash",
                CollisionProgram(), Dfs4, Crash, (a, r) =>
                {
                    Checks.Outcome(a, r, RunOutcome.Crashed);
                    a.That(r.Blocked.Select(b => b.Reason).SequenceEqual(new[] { BlockReason.Wall }), "one MoveBlocked(Wall)");
                    a.That(r.Final?.Steps == 0, "0 steps");
                    a.That(r.Final?.Message?.Contains("wall") == true, "message mentions the wall");
                }),

            new("collision-ignore",
                "same program with Ignore: wall and back-without-history are no-ops",
                CollisionProgram(), Dfs4, Ignore, (a, r) =>
                {
                    Checks.Outcome(a, r, RunOutcome.ProgramEnded);
                    a.That(r.Blocked.Select(b => b.Reason).SequenceEqual(new[] { BlockReason.Wall, BlockReason.NoHistory }),
                        "MoveBlocked(Wall) then MoveBlocked(NoHistory)");
                    a.That(r.Moves.Select(m => m.Direction).SequenceEqual(new[] { Right, Direction.Back }),
                        "moves: right, back");
                    a.That(r.Final?.Steps == 2 && r.Final?.Position == Dfs4.Entrance, "2 steps, back at the entrance");
                }),

            new("back-without-history",
                "'back' with an empty history crashes in Crash mode",
                BackTwiceProgram(), Dfs4, Crash, (a, r) =>
                {
                    Checks.Outcome(a, r, RunOutcome.Crashed);
                    a.That(r.Blocked.Select(b => b.Reason).SequenceEqual(new[] { BlockReason.NoHistory }), "one MoveBlocked(NoHistory)");
                    a.That(r.Final?.Steps == 2, "2 successful steps before the crash");
                    a.That(r.Final?.Message?.Contains("go back") == true, "message mentions going back");
                }),

            new("limit-instructions",
                "endless loop of ignored moves ends at MaxInstructions=40",
                ForeverUpProgram(), Dfs4, new InterpreterOptions { OnCollision = CollisionBehavior.Ignore, MaxInstructions = 40 }, (a, r) =>
                {
                    Checks.Outcome(a, r, RunOutcome.LimitExceeded);
                    a.That(r.Final?.Instructions == 40, "exactly 40 instructions executed");
                    a.That(r.Blocked.Count() == 13, "13 blocked moves (a Move every 3rd instruction)");
                    a.That(r.Final?.Steps == 0, "0 steps");
                }),

            new("limit-recursion",
                "unbounded recursion ends at MaxCallDepth",
                EndlessRecursionProgram(), Dfs4, Crash, (a, r) =>
                {
                    Checks.Outcome(a, r, RunOutcome.LimitExceeded);
                    a.That(r.Final?.Message?.Contains("endlos") == true, "message names the function");
                    a.That(!r.Moves.Any(), "no moves");
                }),

            new("cancel",
                "consumer cancels after the 5th blocked move of an endless loop",
                ForeverUpProgram(), Dfs4, Ignore, (a, r) =>
                {
                    Checks.Outcome(a, r, RunOutcome.Cancelled);
                    a.That(r.Blocked.Count() == 5, "stream stops right after the 5th event");
                },
                OnEvent: (ev, cts) => { if (ev is MoveBlocked && ++blockedForCancel == 5) cts.Cancel(); }),

            new("fault",
                "malformed IL (int used as direction) ends with Fault instead of throwing",
                FaultProgram(), Dfs4, Crash, (a, r) =>
                {
                    Checks.Outcome(a, r, RunOutcome.Fault);
                    a.That(r.Final?.Message?.Contains("Type error") == true, "message describes the type error");
                }),
        };
    }

    // ------------------------------------------------------------------ IL programs

    static ILProgram Prog(Asm entry, params (string Name, int Params, Asm Body)[] functions)
    {
        var list = new List<ILFunction> { entry.Build("<entry>", 0) };
        foreach (var f in functions) list.Add(f.Body.Build(f.Name, f.Params));
        return new ILProgram(list, EntryPoint: 0);
    }

    // funktion begehbar(d)  ergebnis frei d und nicht besucht d  ende
    // funktion versuche(d)
    //     wenn begehbar d
    //         gehe d
    //         versuche hoch / rechts / runter / links
    //         gehe rueckwaerts
    //     ende
    // ende
    // versuche hoch / rechts / runter / links
    static ILProgram DfsProgram()
    {
        const int Begehbar = 1, Versuche = 2;
        var dirs = new[] { Up, Right, Down, Left };

        var entry = new Asm(0);
        foreach (var d in dirs) entry.CallStmt(Versuche, d);
        entry.End();

        var begehbar = new Asm(1);
        begehbar.And(() => begehbar.FreeLocal(0),
                     () => begehbar.Not(() => begehbar.VisitedLocal(0)))
                .Op(OpCode.Return);
        begehbar.End();

        var versuche = new Asm(1);
        versuche.If(() => versuche.Load(0).Call(Begehbar),
                    () =>
                    {
                        versuche.GoLocal(0);
                        foreach (var d in dirs) versuche.CallStmt(Versuche, d);
                        versuche.Back();
                    });
        versuche.End();

        return Prog(entry, ("begehbar", 1, begehbar), ("versuche", 1, versuche));
    }

    // wiederhole 2 mal
    //     wiederhole 2 mal  gehe rechts  ende
    //     gehe links
    // ende
    // wiederhole 0 mal  gehe rechts  ende
    static ILProgram NestedLoopsProgram()
    {
        var e = new Asm(0);
        e.Repeat(2, () =>
        {
            e.Repeat(2, () => e.Go(Right));
            e.Go(Left);
        });
        e.Repeat(0, () => e.Go(Right));
        e.End();
        return Prog(e);
    }

    // wenn frei hoch oder frei links
    //     gehe hoch
    // sonst wenn nicht frei rechts und wahr
    //     gehe links
    // sonst
    //     gehe rechts
    // ende
    // wenn frei hoch oder frei runter  gehe runter  ende
    // gehe links
    static ILProgram IfElseProgram()
    {
        var e = new Asm(0);
        e.If(() => e.Or(() => e.Free(Up), () => e.Free(Left)),
             () => e.Go(Up),
             () => e.If(() => e.And(() => e.Not(() => e.Free(Right)), () => e.Bool(true)),
                        () => e.Go(Left),
                        () => e.Go(Right)));
        e.If(() => e.Or(() => e.Free(Up), () => e.Free(Down)),
             () => e.Go(Down));
        e.Go(Left);
        e.End();
        return Prog(e);
    }

    // funktion bisWand(d)
    //     immer
    //         wenn nicht frei d  ergebnis wahr  ende
    //         gehe d
    //     ende
    // ende
    // wenn bisWand rechts  gehe runter  ende
    static ILProgram BisWandProgram()
    {
        var e = new Asm(0);
        e.If(() => e.Dir(Right).Call(1), () => e.Go(Down));
        e.End();

        var f = new Asm(1);
        f.Forever(() =>
        {
            f.If(() => f.Not(() => f.FreeLocal(0)),
                 () => f.Bool(true).Op(OpCode.Return));
            f.GoLocal(0);
        });
        f.End();

        return Prog(e, ("bisWand", 1, f));
    }

    // funktion zwei(a, b)  gehe a  gehe b  ende
    // zwei runter runter
    // zwei rechts links
    static ILProgram TwoParamsProgram()
    {
        var e = new Asm(0);
        e.CallStmt(1, Down, Down);
        e.CallStmt(1, Right, Left);
        e.End();

        var f = new Asm(2);
        f.GoLocal(0).GoLocal(1);
        f.End();

        return Prog(e, ("zwei", 2, f));
    }

    // gehe hoch / gehe rechts / gehe rueckwaerts / gehe rueckwaerts
    static ILProgram CollisionProgram()
    {
        var e = new Asm(0);
        e.Go(Up).Go(Right).Back().Back();
        e.End();
        return Prog(e);
    }

    // gehe rechts / gehe rueckwaerts / gehe rueckwaerts
    static ILProgram BackTwiceProgram()
    {
        var e = new Asm(0);
        e.Go(Right).Back().Back();
        e.End();
        return Prog(e);
    }

    // immer  gehe hoch  ende
    static ILProgram ForeverUpProgram()
    {
        var e = new Asm(0);
        e.Forever(() => e.Go(Up));
        e.End();
        return Prog(e);
    }

    // funktion endlos(d)  endlos d  ende
    // endlos rechts
    static ILProgram EndlessRecursionProgram()
    {
        var e = new Asm(0);
        e.CallStmt(1, Right);
        e.End();

        var f = new Asm(1);
        f.Load(0).Call(1).Op(OpCode.Pop);
        f.End();

        return Prog(e, ("endlos", 1, f));
    }

    // Not producible by the compiler: pushes an int and uses it as a direction.
    static ILProgram FaultProgram()
    {
        var e = new Asm(0);
        e.Op(OpCode.PushInt, 1).Op(OpCode.Move);
        e.End();
        return Prog(e);
    }
}

// =============================================================================================
// Checks
// =============================================================================================

sealed class Asserts
{
    public List<string> Errors { get; } = new();
    public void That(bool ok, string expectation) { if (!ok) Errors.Add(expectation); }
}

sealed class Run
{
    public List<InterpreterEvent> Events { get; } = new();
    public Finished? Final => Events.OfType<Finished>().LastOrDefault();
    public IEnumerable<Moved> Moves => Events.OfType<Moved>();
    public IEnumerable<MoveBlocked> Blocked => Events.OfType<MoveBlocked>();
    public IEnumerable<ExitReached> Exits => Events.OfType<ExitReached>();
}

sealed record Scenario(
    string Name,
    string Description,
    ILProgram Program,
    IMazeLayout Maze,
    InterpreterOptions Options,
    Action<Asserts, Run> Check,
    Action<InterpreterEvent, CancellationTokenSource>? OnEvent = null);

static class Checks
{
    public static void Outcome(Asserts a, Run r, RunOutcome expected)
    {
        a.That(r.Final?.Outcome == expected, $"outcome {expected} (was {r.Final?.Outcome.ToString() ?? "none"})");
        if (expected is RunOutcome.ExitReached or RunOutcome.ProgramEnded)
            a.That(r.Final?.Message is null, $"no message on {expected} (was \"{r.Final?.Message}\")");
    }

    /// <summary>The first-moved-cell-only rule of a DFS: a non-Back move never enters a visited cell.</summary>
    public static void NoRevisits(Asserts a, Run r, CellPosition start)
    {
        var seen = new HashSet<CellPosition> { start };
        bool ok = true;
        foreach (var m in r.Moves.Where(m => m.Direction != Direction.Back))
            ok &= seen.Add(m.To);
        a.That(ok, "no forward move enters an already visited cell");
    }

    /// <summary>Invariants of every run, independent of the program.</summary>
    public static void Consistency(Asserts a, Run r, IMazeLayout maze)
    {
        var ev = r.Events;
        a.That(ev.Count >= 2 && ev[0] is Started s && s.Position == maze.Entrance, "first event is Started at the entrance");
        a.That(ev.OfType<Finished>().Count() == 1 && ev[^1] is Finished, "exactly one Finished, and it is the last event");

        var pos = maze.Entrance;
        int steps = 0;
        bool ok = true;
        foreach (var m in r.Moves)
        {
            if (m.From != pos || m.Step != ++steps) ok = false;
            if (m.Direction != Direction.Back)
            {
                var (dy, dx) = m.Direction.Delta();
                if (m.To != new CellPosition(m.From.Y + dy, m.From.X + dx) || maze.HasWall(m.From, m.Direction))
                    ok = false;
            }
            pos = m.To;
        }
        a.That(ok, "every Moved starts where the last one ended, steps count 1..n, no walking through walls");
        if (r.Final is { } f)
            a.That(f.Position == pos && f.Steps == steps, "Finished reports the final position and step count");
    }
}

// =============================================================================================
// Test helpers: ASCII maze and a tiny IL assembler (mirrors the code patterns of AstCompiler)
// =============================================================================================

/// <summary>
/// Maze from ASCII art: 4 chars per cell horizontally, 2 lines per cell vertically,
/// 'S' = entrance, 'E' = exit, any non-space char on a border position is a wall.
/// </summary>
sealed class AsciiMaze : IMazeLayout
{
    private readonly string[] _l;
    public int Width { get; }
    public int Height { get; }
    public CellPosition Entrance { get; }
    public CellPosition Exit { get; }

    private AsciiMaze(string[] lines)
    {
        _l = lines;
        Height = (lines.Length - 1) / 2;
        Width = (lines[0].Length - 1) / 4;
        CellPosition? s = null, e = null;
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                char c = lines[2 * y + 1][4 * x + 2];
                if (c == 'S') s = new CellPosition(new YCoordinate(y), new XCoordinate(x));
                if (c == 'E') e = new CellPosition(new YCoordinate(y), new XCoordinate(x));
            }
        Entrance = s ?? throw new ArgumentException("maze has no 'S'");
        Exit = e ?? throw new ArgumentException("maze has no 'E'");
    }

    public static AsciiMaze Parse(params string[] lines)
    {
        if (lines.Length < 3 || lines.Length % 2 == 0)
            throw new ArgumentException("maze needs 2*height+1 lines");
        int len = lines[0].Length;
        if ((len - 1) % 4 != 0)
            throw new ArgumentException($"line width {len} is not 4*width+1");
        for (int i = 0; i < lines.Length; i++)
            if (lines[i].Length != len)
                throw new ArgumentException($"maze line {i} has length {lines[i].Length}, expected {len}");
        return new AsciiMaze(lines);
    }

    public bool Contains(CellPosition c) => c.Y >= 0 && c.Y < Height && c.X >= 0 && c.X < Width;

    public bool HasWall(CellPosition c, Direction side) => side switch
    {
        Direction.Up => _l[2 * c.Y][4 * c.X + 2] != ' ',
        Direction.Down => _l[2 * c.Y + 2][4 * c.X + 2] != ' ',
        Direction.Left => _l[2 * c.Y + 1][4 * c.X] != ' ',
        Direction.Right => _l[2 * c.Y + 1][4 * c.X + 4] != ' ',
        _ => throw new InvalidOperationException($"'{side}' is not a physical direction."),
    };
}

/// <summary>Fluent mini assembler with labels and the same statement/expression patterns the compiler emits.</summary>
sealed class Asm
{
    public sealed class Label { public int Pos = -1; }

    private readonly List<Instruction> _code = new();
    private readonly List<(int At, Label Target)> _fixups = new();
    private int _locals;

    public Asm(int parameterCount) => _locals = parameterCount;

    // --- raw
    public Label NewLabel() => new();
    public int NewLocal() => _locals++;
    public Asm Mark(Label l) { l.Pos = _code.Count; return this; }
    public Asm Op(OpCode op, int operand = 0) { _code.Add(new Instruction(op, operand)); return this; }
    public Asm Jump(OpCode op, Label target)
    {
        _fixups.Add((_code.Count, target));
        _code.Add(new Instruction(op, -1));
        return this;
    }

    public ILFunction Build(string name, int parameterCount)
    {
        var code = _code.ToArray();
        foreach (var (at, target) in _fixups)
        {
            if (target.Pos < 0) throw new InvalidOperationException("label never marked");
            code[at] = code[at] with { Operand = target.Pos };
        }
        return new ILFunction(name, parameterCount, _locals, code);
    }

    // --- values
    public Asm Dir(Direction d) => Op(OpCode.PushDirection, (int)d);
    public Asm Bool(bool b) => Op(OpCode.PushBool, b ? 1 : 0);
    public Asm Load(int slot) => Op(OpCode.LoadLocal, slot);
    public Asm Call(int fn) => Op(OpCode.Call, fn);

    // --- conditions
    public Asm Free(Direction d) => Dir(d).Op(OpCode.SenseFree);
    public Asm FreeLocal(int slot) => Load(slot).Op(OpCode.SenseFree);
    public Asm VisitedLocal(int slot) => Load(slot).Op(OpCode.SenseVisited);
    public Asm Not(Action operand) { operand(); return Op(OpCode.Not); }

    public Asm And(Action left, Action right)
    {
        var isFalse = NewLabel(); var end = NewLabel();
        left();
        Jump(OpCode.JumpIfFalse, isFalse);
        right();
        Jump(OpCode.Jump, end);
        Mark(isFalse).Bool(false);
        return Mark(end);
    }

    public Asm Or(Action left, Action right)
    {
        var tryRight = NewLabel(); var end = NewLabel();
        left();
        Jump(OpCode.JumpIfFalse, tryRight);
        Bool(true);
        Jump(OpCode.Jump, end);
        Mark(tryRight);
        right();
        return Mark(end);
    }

    // --- statements
    public Asm Go(Direction d) => Dir(d).Op(OpCode.Move);
    public Asm GoLocal(int slot) => Load(slot).Op(OpCode.Move);
    public Asm Back() => Go(Direction.Back);
    public Asm CallStmt(int fn, params Direction[] args)
    {
        foreach (var a in args) Dir(a);
        return Call(fn).Op(OpCode.Pop);
    }
    /// <summary>Falling off the end: no result.</summary>
    public Asm End() => Op(OpCode.PushUnit).Op(OpCode.Return);

    public Asm If(Action cond, Action then, Action? @else = null)
    {
        var elseL = NewLabel();
        cond();
        Jump(OpCode.JumpIfFalse, elseL);
        then();
        if (@else is null) return Mark(elseL);
        var end = NewLabel();
        Jump(OpCode.Jump, end);
        Mark(elseL);
        @else();
        return Mark(end);
    }

    public Asm Repeat(int count, Action body)
    {
        int counter = NewLocal();
        var start = NewLabel(); var end = NewLabel();
        Op(OpCode.PushInt, count).Op(OpCode.StoreLocal, counter);
        Mark(start).Op(OpCode.LoadLocal, counter).Jump(OpCode.JumpIfZero, end);
        body();
        Op(OpCode.DecLocal, counter).Jump(OpCode.Jump, start);
        return Mark(end);
    }

    public Asm Forever(Action body)
    {
        var start = NewLabel();
        Mark(start);
        body();
        return Jump(OpCode.Jump, start);
    }
}
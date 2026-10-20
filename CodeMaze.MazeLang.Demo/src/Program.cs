using CodeMaze.MazeLang.Ast;
using CodeMaze.MazeLang.Compiler.AST;
using CodeMaze.MazeLang.Core;
using CodeMaze.MazeLang.IL;

// Helpers to keep the AST construction readable
static DirectionLiteral D(Direction d) => new(d);
static ParameterRef P(string n) => new(n);

// function fresh(r)          return free(r) and not visited(r)
var fresh = new FunctionDecl("fresh", ["r"], [
    new Return(new And(
        new Sense(SenseKind.Free, P("r")),
        new Not(new Sense(SenseKind.Visited, P("r")))))
]);

// function any_fresh(a, b)   return fresh(a) or fresh(b)
var anyFresh = new FunctionDecl("any_fresh", ["a", "b"], [
    new Return(new Or(
        new CallExpr("fresh", [P("a")]),
        new CallExpr("fresh", [P("b")])))
]);

// function explore(r)        move r
var explore = new FunctionDecl("explore", ["r"], [new Move(P("r"))]);

// repeat 2: move up
// forever:
//   if fresh(up):            explore(up)
//   else if fresh(right):    explore(right)
//   else if any_fresh(down, left):
//                            if fresh(down): explore(down) else: explore(left)
//   else:                    move back
var program = new MazeProgram([fresh, anyFresh, explore], [
    new Repeat(2, [new Move(D(Direction.Up))]),
    new Forever([
        new If(new CallExpr("fresh", [D(Direction.Up)]),
            [new CallStmt("explore", [D(Direction.Up)])],
            [new If(new CallExpr("fresh", [D(Direction.Right)]),
                [new CallStmt("explore", [D(Direction.Right)])],
                [new If(new CallExpr("any_fresh", [D(Direction.Down), D(Direction.Left)]),
                    [new If(new CallExpr("fresh", [D(Direction.Down)]),
                        [new CallStmt("explore", [D(Direction.Down)])],
                        [new CallStmt("explore", [D(Direction.Left)])])],
                    [new Move(D(Direction.Back))])])])
    ])
]);

Console.WriteLine(ILPrinter.Print(AstCompiler.Compile(program)));

// Error case: all problems are collected
var broken = new MazeProgram([explore], [
    new If(new CallExpr("explore", [D(Direction.Up)]), [], []),          // action used as a condition
    new CallStmt("does_not_exist", [D(Direction.Up)]),                   // unknown function
    new CallStmt("explore", [D(Direction.Up), D(Direction.Down)]),       // wrong argument count
    new If(new Sense(SenseKind.Free, D(Direction.Back)), [], []),        // Back used with a sensor
]);

try { AstCompiler.Compile(broken); }
catch (CompileException e)
{
    Console.WriteLine("Errors:");
    foreach (var d in e.Diagnostics) Console.WriteLine("  " + d);
}

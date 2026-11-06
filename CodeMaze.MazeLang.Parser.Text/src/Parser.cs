using CodeMaze.MazeLang.Ast;
using CodeMaze.MazeLang.Compiler.AST;
using CodeMaze.MazeLang.Core;

namespace CodeMaze.MazeLang.Parser.Text;

public sealed class ParseException : Exception
{
    public IReadOnlyList<Diagnostic> Diagnostics { get; }
    public ParseException(IReadOnlyList<Diagnostic> diagnostics)
        : base(string.Join(Environment.NewLine, diagnostics)) => Diagnostics = diagnostics;
}

/// <summary>Text → AST for any language described by a <see cref="LanguageDefinition"/>.</summary>
public sealed class MazeParser
{
    private readonly LanguageTable _lang;

    public MazeParser(LanguageDefinition language) => _lang = new LanguageTable(language);

    public MazeProgram Parse(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var diags = new List<Diagnostic>();
        var tokens = Lexer.Lex(source, _lang, diags);
        var program = new Run(_lang, tokens, diags).ParseProgram();
        if (diags.Count > 0)
            throw new ParseException(diags
                .OrderBy(d => d.Location?.Line ?? 0)
                .ThenBy(d => d.Location?.Column ?? 0)
                .ToList());
        return program;
    }

    private sealed class ParseError(Diagnostic d) : Exception(d.Message)
    {
        public Diagnostic Diagnostic { get; } = d;
    }

    private sealed class Run
    {
        private readonly LanguageTable _lang;
        private readonly List<Token> _t;
        private readonly List<Diagnostic> _diags;
        private readonly Dictionary<string, int> _arity = new(StringComparer.Ordinal);
        private int _i;

        public Run(LanguageTable lang, List<Token> tokens, List<Diagnostic> diags)
        {
            _lang = lang; _t = tokens; _diags = diags;
            PreScanFunctions();
        }

        // ---------------------------------------------------------- helpers

        private Token Peek => _t[_i];

        private Token Next()
        {
            var t = _t[_i];
            if (t.Kind != TokenKind.Eof) _i++;
            return t;
        }

        private static bool Is(Token t, Keyword k) => t.Kind == TokenKind.Word && t.Keyword == k;

        private bool Accept(Keyword k)
        {
            if (!Is(Peek, k)) return false;
            Next();
            return true;
        }

        private static string Describe(Token t) => t.Kind == TokenKind.Eof ? "end of program" : $"'{t.Text}'";

        private static ParseError Err(string msg, Token at) => new(new Diagnostic(msg, at.Location));

        private void Expect(Keyword k)
        {
            if (!Accept(k))
                throw Err($"Expected '{_lang.Display(k)}' but found {Describe(Peek)}.", Peek);
        }

        private void Expect(TokenKind kind, string what)
        {
            if (Peek.Kind != kind)
                throw Err($"Expected {what} but found {Describe(Peek)}.", Peek);
            Next();
        }

        private static bool IsIdentifier(Token t) => t.Kind == TokenKind.Word && t.Keyword is null;

        private static bool IsDirectionKeyword(Keyword? k) =>
            k is Keyword.Up or Keyword.Down or Keyword.Left or Keyword.Right or Keyword.Back;

        private static bool IsDirectionStart(Token t) =>
            t.Kind == TokenKind.Word && (t.Keyword is null || IsDirectionKeyword(t.Keyword));

        private static bool StartsExpr(Token t) =>
            t.Kind == TokenKind.Word &&
            (t.Keyword is null or Keyword.Not or Keyword.Free or Keyword.Visited or Keyword.True or Keyword.False);

        /// <summary>Error recovery: run a parse step, record the error, skip the rest of the offending line.</summary>
        private void Guarded(Action step)
        {
            int start = _i;
            try { step(); }
            catch (ParseError e)
            {
                _diags.Add(e.Diagnostic);
                if (_i == start) { Next(); return; }               // guarantee progress
                int line = _t[_i - 1].Line;
                while (Peek.Kind != TokenKind.Eof && Peek.Line == line) Next();
            }
        }

        // ------------------------------------------------------ pre-scan

        private void PreScanFunctions()
        {
            for (int i = 0; i + 2 < _t.Count; i++)
            {
                if (!Is(_t[i], Keyword.Function) || !IsIdentifier(_t[i + 1]) || _t[i + 2].Kind != TokenKind.LParen)
                    continue;
                int count = 0, j = i + 3;
                while (j < _t.Count && _t[j].Kind is not (TokenKind.RParen or TokenKind.Eof))
                {
                    if (IsIdentifier(_t[j])) count++;
                    j++;
                }
                _arity.TryAdd(_t[i + 1].Text, count);               // first declaration wins; duplicates are the compiler's job
            }
        }

        // -------------------------------------------------------- program

        public MazeProgram ParseProgram()
        {
            var functions = new List<FunctionDecl>();
            var body = new List<Stmt>();

            while (Peek.Kind != TokenKind.Eof)
            {
                Guarded(() =>
                {
                    var t = Peek;
                    if (Is(t, Keyword.Function)) functions.Add(ParseFunction());
                    else if (Is(t, Keyword.End) || Is(t, Keyword.Else))
                        throw Err($"Unexpected '{t.Text}'.", t);
                    else body.Add(ParseStatement());
                });
            }
            return new MazeProgram(functions, body) { Location = new SourceLocation(1, 1) };
        }

        private FunctionDecl ParseFunction()
        {
            var kw = Next();
            var nameTok = Peek;
            if (!IsIdentifier(nameTok))
                throw Err($"Expected a function name but found {Describe(nameTok)}.", nameTok);
            Next();

            Expect(TokenKind.LParen, "'('");
            var parameters = new List<string>();
            if (Peek.Kind != TokenKind.RParen)
            {
                while (true)
                {
                    var p = Peek;
                    if (!IsIdentifier(p))
                        throw Err($"Expected a parameter name but found {Describe(p)}.", p);
                    Next();
                    parameters.Add(p.Text);
                    if (Peek.Kind == TokenKind.Comma) { Next(); continue; }
                    break;
                }
            }
            Expect(TokenKind.RParen, "')'");

            var body = ParseBlock();
            Expect(Keyword.End);
            return new FunctionDecl(nameTok.Text, parameters, body) { Location = kw.Location };
        }

        // ------------------------------------------------------ statements

        /// <summary>Statements until 'end', 'else' or EOF (the terminator is not consumed).</summary>
        private List<Stmt> ParseBlock()
        {
            var list = new List<Stmt>();
            while (true)
            {
                var t = Peek;
                if (t.Kind == TokenKind.Eof || Is(t, Keyword.End) || Is(t, Keyword.Else)) break;
                Guarded(() => list.Add(ParseStatement()));
            }
            return list;
        }

        private Stmt ParseStatement()
        {
            var t = Peek;

            switch (t.Keyword)
            {
                case Keyword.Go:
                    Next();
                    return new Move(ParseDirection()) { Location = t.Location };


                case Keyword.Repeat:
                    {
                        Next();
                        var n = Peek;
                        ValueExpr count;
                        if (n.Kind == TokenKind.Number)
                        {
                            Next();
                            count = new NumberLiteral(n.Number) { Location = n.Location };
                            Accept(Keyword.Times);
                        }
                        else if (IsIdentifier(n) && n.Line == t.Line)
                        {
                            Next();
                            count = new ParameterRef(n.Text) { Location = n.Location };
                            Accept(Keyword.Times);
                        }
                        else
                        {
                            _diags.Add(new Diagnostic($"Expected a number but found {Describe(n)}.", n.Location));
                            while (Peek.Kind != TokenKind.Eof && Peek.Line == t.Line) Next();
                            count = new NumberLiteral(0) { Location = n.Location };
                        }
                        var body = ParseBlock();
                        Expect(Keyword.End);
                        return new Repeat(count, body) { Location = t.Location };
                    }

                case Keyword.Forever:
                    {
                        Next();
                        var body = ParseBlock();
                        Expect(Keyword.End);
                        return new Forever(body) { Location = t.Location };
                    }

                case Keyword.If:
                    {
                        Next();
                        var node = ParseIfChain(t);
                        Expect(Keyword.End);                             // one 'end' closes the whole chain
                        return node;
                    }

                case Keyword.Result:
                    {
                        Next();
                        Expr? value = null;
                        if (Peek.Line == t.Line && StartsExpr(Peek))
                            value = ParseExpr();
                        return new Return(value) { Location = t.Location };
                    }

                case Keyword.Function:
                    throw Err("Functions can only be declared at the top level.", t);

                case null when t.Kind == TokenKind.Word:
                    Next();
                    return new CallStmt(t.Text, ParseArguments(t)) { Location = t.Location };
            }

            throw Err($"Unexpected {Describe(t)}.", t);
        }

        /// <summary>The 'if' token is already consumed. Handles 'else if' chains without consuming 'end'.</summary>
        private If ParseIfChain(Token ifTok)
        {
            var cond = ParseExpr();
            var then = ParseBlock();
            IReadOnlyList<Stmt> otherwise = [];

            var elseTok = Peek;
            if (Accept(Keyword.Else))
            {
                // 'else if' only when both words are on the same line
                if (Is(Peek, Keyword.If) && Peek.Line == elseTok.Line)
                {
                    var nestedIf = Next();
                    otherwise = [ParseIfChain(nestedIf)];
                }
                else
                {
                    otherwise = ParseBlock();
                }
            }
            return new If(cond, then, otherwise) { Location = ifTok.Location };
        }

        // ----------------------------------------------------- expressions

        private Expr ParseExpr()
        {
            var left = ParseAnd();
            while (Is(Peek, Keyword.Or))
            {
                var op = Next();
                left = new Or(left, ParseAnd()) { Location = op.Location };
            }
            return left;
        }

        private Expr ParseAnd()
        {
            var left = ParseNot();
            while (Is(Peek, Keyword.And))
            {
                var op = Next();
                left = new And(left, ParseNot()) { Location = op.Location };
            }
            return left;
        }

        private Expr ParseNot()
        {
            if (Is(Peek, Keyword.Not))
            {
                var op = Next();
                return new Not(ParseNot()) { Location = op.Location };
            }
            return ParsePrimary();
        }

        private Expr ParsePrimary()
        {
            var t = Peek;
            switch (t.Keyword)
            {
                case Keyword.Free:
                    Next();
                    return new Sense(SenseKind.Free, ParseDirection()) { Location = t.Location };
                case Keyword.Visited:
                    Next();
                    return new Sense(SenseKind.Visited, ParseDirection()) { Location = t.Location };
                case Keyword.True:
                    Next();
                    return new BoolLiteral(true) { Location = t.Location };
                case Keyword.False:
                    Next();
                    return new BoolLiteral(false) { Location = t.Location };
                case null when t.Kind == TokenKind.Word:
                    Next();
                    return new CallExpr(t.Text, ParseArguments(t)) { Location = t.Location };
            }
            throw Err($"Expected a condition but found {Describe(t)}.", t);
        }

        // ------------------------------------------- directions & arguments

        private ValueExpr ParseDirection()
        {
            var t = Peek;
            if (t.Kind == TokenKind.Word)
            {
                Direction? d = t.Keyword switch
                {
                    Keyword.Up => Direction.Up,
                    Keyword.Down => Direction.Down,
                    Keyword.Left => Direction.Left,
                    Keyword.Right => Direction.Right,
                    Keyword.Back => Direction.Back,
                    _ => null,
                };
                if (d is { } dir)
                {
                    Next();
                    return new DirectionLiteral(dir) { Location = t.Location };
                }
                if (t.Keyword is null)
                {
                    Next();
                    return new ParameterRef(t.Text) { Location = t.Location };
                }
            }
            throw Err($"Expected a direction but found {Describe(t)}.", t);
        }
        private static bool IsArgumentStart(Token t) =>
            t.Kind == TokenKind.Number || IsDirectionStart(t);
        private ValueExpr ParseArgument()
        {
            var t = Peek;
            if (t.Kind == TokenKind.Number)
            {
                Next();
                return new NumberLiteral(t.Number) { Location = t.Location };
            }
            return ParseDirection();
        }

        private List<ValueExpr> ParseArguments(Token nameTok)
        {
            int max = _arity.TryGetValue(nameTok.Text, out var n) ? n : int.MaxValue;
            var args = new List<ValueExpr>();
            while (args.Count < max && Peek.Line == nameTok.Line && IsArgumentStart(Peek))
                args.Add(ParseArgument());
            return args;
        }
    }
}
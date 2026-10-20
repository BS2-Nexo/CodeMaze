using System.Diagnostics.CodeAnalysis;
using CodeMaze.MazeLang.Ast;
using CodeMaze.MazeLang.Compiler.AST;
using CodeMaze.MazeLang.Core;
using CodeMaze.MazeLang.IL;
using CodeMaze.MazeLang.Parser.Text;

namespace CodeMaze.MazeLang.Compiler;

/// <summary>Both stages of a compilation: what the parser produced and what the compiler made of it.</summary>
public sealed record CompilationResult(MazeProgram Ast, ILProgram Il);

/// <summary>
/// The whole front-to-back compiler: source text -> AST -> IL, in the language given per call.
/// Holds no state, so one instance can be shared across requests.
/// </summary>
public sealed class MazeCompiler
{
    /// <summary>Throws <see cref="ParseException"/> or <see cref="CompileException"/> for invalid source.</summary>
    public CompilationResult Compile(string source, LanguageDefinition language)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(language);

        // MazeParser builds its LanguageTable in the constructor, so a parser per call is
        // the price of a language per call (cheap compared to everything else in a request).
        var ast = new MazeParser(language).Parse(source);
        var il = AstCompiler.Compile(ast);

        return new CompilationResult(ast, il);
    }

    /// <summary>Non-throwing variant: on failure returns every diagnostic of the stage that failed.</summary>
    public bool TryCompile(
        string source,
        LanguageDefinition language,
        [NotNullWhen(true)] out CompilationResult? result,
        out IReadOnlyList<Diagnostic> diagnostics)
    {
        try
        {
            result = Compile(source, language);
            diagnostics = [];
            return true;
        }
        catch (ParseException ex)
        {
            result = null;
            diagnostics = ex.Diagnostics;
            return false;
        }
        catch (CompileException ex)
        {
            result = null;
            diagnostics = ex.Diagnostics;   // assumes CompileException exposes the list it was built with
            return false;
        }
    }
}
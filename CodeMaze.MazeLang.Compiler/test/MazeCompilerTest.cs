using CodeMaze.MazeLang.Compiler;
using CodeMaze.MazeLang.Compiler.AST;
using CodeMaze.MazeLang.Parser.Text;
using Xunit;

namespace CodeMaze.MazeLang.Compiler.Tests;

public class MazeCompilerTests
{
    // ---- Adjust these two things to your project -------------------------------------------

    // Any LanguageDefinition whose keywords match the snippets below.
    private static LanguageDefinition Language => BuiltinLanguages.English;

    private const string Valid = "go up";
    private const string ValidWithFunction = "function step(d)\n  go d\nend\nstep up";
    private const string DuplicateFunction = "function f()\n  go up\nend\nfunction f()\n  go up\nend";
    private const string ParseError = "go";   // a direction is missing

    // Identifiers are not keywords, so this is invalid in every language.
    private const string UnknownFunction = "doesNotExist";

    // -----------------------------------------------------------------------------------------

    private readonly MazeCompiler _compiler = new();

    [Fact]
    public void Compile_ValidProgram_ReturnsAstAndIl()
    {
        var result = _compiler.Compile(Valid, Language);

        Assert.Single(result.Ast.Body);
        Assert.Empty(result.Ast.Functions);
        Assert.NotNull(result.Il);
        Assert.Equal(0, result.Il.EntryPoint);
    }

    [Fact]
    public void Compile_FunctionDeclaration_IsParsedAndCompiled()
    {
        var result = _compiler.Compile(ValidWithFunction, Language);

        Assert.Single(result.Ast.Functions);
        Assert.Single(result.Ast.Body);
    }

    [Fact]
    public void Compile_SyntaxError_ThrowsParseExceptionWithDiagnostics()
    {
        var ex = Assert.Throws<ParseException>(() => _compiler.Compile(ParseError, Language));

        Assert.NotEmpty(ex.Diagnostics);
    }

    [Fact]
    public void Compile_UnknownFunction_ThrowsCompileException()
    {
        var ex = Assert.Throws<CompileException>(() => _compiler.Compile(UnknownFunction, Language));

        Assert.NotEmpty(ex.Diagnostics);
    }

    [Fact]
    public void Compile_DuplicateFunction_ThrowsCompileException()
    {
        Assert.Throws<CompileException>(() => _compiler.Compile(DuplicateFunction, Language));
    }

    [Fact]
    public void Compile_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => _compiler.Compile(null!, Language));
        Assert.Throws<ArgumentNullException>(() => _compiler.Compile(Valid, null!));
    }

    [Fact]
    public void TryCompile_ValidProgram_ReturnsTrue()
    {
        var ok = _compiler.TryCompile(Valid, Language, out var result, out var diagnostics);

        Assert.True(ok);
        Assert.NotNull(result);
        Assert.Empty(diagnostics);
    }

    [Theory]
    [InlineData(ParseError)]
    [InlineData(UnknownFunction)]
    public void TryCompile_InvalidProgram_ReturnsFalseWithDiagnostics(string source)
    {
        var ok = _compiler.TryCompile(source, Language, out var result, out var diagnostics);

        Assert.False(ok);
        Assert.Null(result);
        Assert.NotEmpty(diagnostics);
    }
}
using System.Text;
using CodeMaze.MazeLang.Compiler.AST;
using CodeMaze.MazeLang.Core;
using CodeMaze.MazeLang.IL;
using CodeMaze.MazeLang.Parser.Text;

// ---------------------------------------------------------------- languages
// Built-in: no loading needed.
var german = BuiltinLanguages.German;
var english = BuiltinLanguages.English;

// Custom: TOML text -> LanguageDefinition. In real use the string comes from HTTP, a file, ...
const string frenchToml = """
    name           = "fr"
    display_name   = "Français"
    comment_prefix = "#"

    [keywords]
    go        = ["va", "aller"]
    repeat    = "répète"
    times     = "fois"
    end       = "fin"
    forever   = "toujours"
    if        = "si"
    else      = "sinon"
    up        = "haut"
    down      = "bas"
    left      = "gauche"
    right     = "droite"
    back      = ["arrière", "recule"]
    free      = "libre"
    visited   = "visité"
    not       = "non"
    and       = "et"
    or        = "ou"
    function  = "fonction"
    result    = "résultat"

    [replacements]
    "é" = "e"
    "è" = "e"
    "ê" = "e"
    "à" = "a"
    "ç" = "c"
    """;

LanguageDefinition french;
try
{
  french = LanguageToml.Parse(frenchToml, "inline-french");
}
catch (LanguageFormatException e)
{
  Console.WriteLine("Could not load the French language:");
  foreach (var err in e.Errors) Console.WriteLine($"  {err}");
  return;
}

// ----------------------------------------------------------------- programs
const string germanSource = """
    # Rechte-Hand-Regel light: immer nach oben oder rechts, sonst zurück
    funktion neu(r)
      ergebnis frei r und nicht besucht r
    ende

    immer
      wenn neu hoch
        gehe hoch
      sonst wenn neu rechts
        gehe rechts
      sonst
        gehe rückwärts
      ende
    ende
    """;

const string englishSource = """
    function fresh(d)
      result free d and not visited d
    end

    repeat 3 times
      go right
    end

    forever
      if fresh up
        go up
      else
        go back
      end
    end
    """;

const string frenchSource = """
    fonction nouveau(d)
      résultat libre d et non visité d
    fin

    toujours
      si nouveau haut
        va haut
      sinon
        va arrière
      fin
    fin
    """;

Show(german, "German", germanSource);
Show(english, "English", englishSource);
Show(french, "French (custom TOML)", frenchSource);

// ------------------------------------------------------------------- errors
Show(english, "Parse error demo", """
    go sideways
    repeat many times
      go up
    end
    """);

Show(english, "Compile error demo", """
    go up
    explore left        # not defined
    forever
      go back
    end
    """);

// ----------------------------------------------------------------- pipeline
static void Show(LanguageDefinition language, string title, string source)
{
  Console.WriteLine();
  Console.WriteLine($"=== {title} ===");
  try
  {
    var parser = new MazeParser(language);     // text -> AST
    var ast = parser.Parse(source);
    var il = AstCompiler.Compile(ast);     // AST  -> IL
    Console.WriteLine(Disassemble(il));
  }
  catch (ParseException e)
  {
    Console.WriteLine("Parse errors:");
    foreach (var d in e.Diagnostics) Console.WriteLine($"  {d}");
  }
  catch (CompileException e)
  {
    Console.WriteLine("Compile errors:");
    foreach (var d in e.Diagnostics) Console.WriteLine($"  {d}");
  }
}

// ------------------------------------------------------------ disassembler
static string Disassemble(ILProgram program)
{
  var sb = new StringBuilder();
  for (int f = 0; f < program.Functions.Count; f++)
  {
    var fn = program.Functions[f];
    var entry = f == program.EntryPoint ? "  <- entry" : "";
    sb.AppendLine($"[{f}] {fn.Name}  (params: {fn.ParameterCount}, locals: {fn.LocalCount}){entry}");

    // instructions that some jump lands on get a '>' marker
    var targets = fn.Code
        .Where(i => i.Op is OpCode.Jump or OpCode.JumpIfFalse or OpCode.JumpIfZero)
        .Select(i => i.Operand)
        .ToHashSet();

    for (int i = 0; i < fn.Code.Count; i++)
    {
      var ins = fn.Code[i];
      var marker = targets.Contains(i) ? ">" : " ";
      sb.AppendLine($"  {marker}{i,4}  {ins.Op,-14} {Operand(program, ins)}".TrimEnd());
    }
    if (targets.Contains(fn.Code.Count))
      sb.AppendLine($"  >{fn.Code.Count,4}  (end)");
    sb.AppendLine();
  }
  return sb.ToString().TrimEnd();
}

static string Operand(ILProgram program, Instruction ins) => ins.Op switch
{
  OpCode.PushBool => ins.Operand != 0 ? "true" : "false",
  OpCode.PushInt => ins.Operand.ToString(),
  OpCode.PushDirection => ((Direction)ins.Operand).ToString(),
  OpCode.LoadLocal or OpCode.StoreLocal
      or OpCode.DecLocal => $"slot {ins.Operand}",
  OpCode.Jump or OpCode.JumpIfFalse
      or OpCode.JumpIfZero => $"-> {ins.Operand}",
  OpCode.Call => $"[{ins.Operand}] {program.Functions[ins.Operand].Name}",
  _ => "",
};
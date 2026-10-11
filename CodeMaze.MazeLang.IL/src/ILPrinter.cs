using System.Text;
using CodeMaze.MazeLang.Core;

namespace CodeMaze.MazeLang.IL;

/// <summary>Readable output (disassembler) for debugging and tests.</summary>
public static class ILPrinter
{
    public static string Print(ILProgram program)
    {
        var sb = new StringBuilder();
        for (int f = 0; f < program.Functions.Count; f++)
        {
            var fn = program.Functions[f];
            string kind = f == program.EntryPoint ? "entry" : "func";
            sb.AppendLine($"{kind} #{f} {fn.Name}  params={fn.ParameterCount} locals={fn.LocalCount}");
            for (int i = 0; i < fn.Code.Count; i++)
                sb.AppendLine($"  {i,4}  {fn.Code[i].Op,-14}{Operand(program, fn.Code[i])}".TrimEnd());
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string Operand(ILProgram p, Instruction i) => i.Op switch
    {
        OpCode.PushBool => i.Operand != 0 ? "true" : "false",
        OpCode.PushInt => i.Operand.ToString(),
        OpCode.PushDirection => ((Direction)i.Operand).ToString(),
        OpCode.LoadLocal or OpCode.StoreLocal or OpCode.DecLocal => $"${i.Operand}",
        OpCode.Jump or OpCode.JumpIfFalse or OpCode.JumpIfZero => $"-> {i.Operand}",
        OpCode.Call => $"#{i.Operand} {p.Functions[i.Operand].Name}",
        _ => "",
    };
}

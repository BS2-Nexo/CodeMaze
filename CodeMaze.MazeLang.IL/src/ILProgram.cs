namespace CodeMaze.MazeLang.IL;

/// <summary>One IL instruction with its operand (meaning: see <see cref="OpCode"/>).</summary>
public readonly record struct Instruction(OpCode Op, int Operand = 0);

/// <param name="Name">For debugging and printing only; execution never uses it.</param>
/// <param name="ParameterCount">Number of arguments a Call takes from the stack.</param>
/// <param name="LocalCount">Slots in the frame (parameters + helper variables).</param>
public sealed record ILFunction(
    string Name,
    int ParameterCount,
    int LocalCount,
    IReadOnlyList<Instruction> Code);

/// <summary>
/// The complete program for the interpreter. Contains nothing from the source text:
/// no names to resolve, only indices.
/// </summary>
/// <param name="EntryPoint">Index of the function where execution starts (0 parameters).</param>
public sealed record ILProgram(IReadOnlyList<ILFunction> Functions, int EntryPoint = 0);

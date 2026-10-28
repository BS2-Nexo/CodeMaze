using CodeMaze.MazeLang.IL;

namespace CodeMaze.MazeLang.Compiler;

internal sealed class Label
{
    public int Position { get; set; } = -1;
}

/// <summary>Collects instructions and resolves jump labels at the end.</summary>
internal sealed class CodeBuilder
{
    private readonly List<Instruction> _code = new();
    private readonly List<(int At, Label Target)> _fixups = new();

    public Label NewLabel() => new();

    public void Mark(Label label) => label.Position = _code.Count;

    public void Emit(OpCode op, int operand = 0) => _code.Add(new Instruction(op, operand));

    public void EmitJump(OpCode op, Label target)
    {
        _fixups.Add((_code.Count, target));
        _code.Add(new Instruction(op, -1));
    }

    public IReadOnlyList<Instruction> Build()
    {
        foreach (var (at, target) in _fixups)
        {
            if (target.Position < 0)
                throw new InvalidOperationException("A label was never marked (compiler bug).");
            _code[at] = _code[at] with { Operand = target.Position };
        }
        return _code.ToArray();
    }
}

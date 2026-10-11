namespace CodeMaze.MazeLang.IL;

/// <summary>
/// Instruction set of the stack machine. Stack values are bool, int, direction or unit
/// ("no result"). Every instruction has at most one operand (an int).
///
/// Conventions:
///  - Every function has a frame with LocalCount slots; parameters occupy slots 0..n-1.
///  - A Call ALWAYS pushes exactly one value (unit if the function returns nothing).
///  - Jump targets are absolute indices into the code of the current function.
///  - At statement level the stack of a function is empty.
/// </summary>
public enum OpCode : byte
{
    // --- Constants -------------------------------------------------------
    /// <summary>Operand 0/1. Stack: → bool</summary>
    PushBool,
    /// <summary>Operand = number. Stack: → int</summary>
    PushInt,
    /// <summary>Operand = (int)Direction. Stack: → direction</summary>
    PushDirection,
    /// <summary>Stack: → unit</summary>
    PushUnit,

    // --- Local variables -------------------------------------------------
    /// <summary>Operand = slot. Stack: → value</summary>
    LoadLocal,
    /// <summary>Operand = slot. Stack: value →</summary>
    StoreLocal,
    /// <summary>Operand = slot. Decrements the int in this slot by 1. Stack unchanged.</summary>
    DecLocal,

    // --- Stack -----------------------------------------------------------
    /// <summary>Stack: value →</summary>
    Pop,

    // --- Logic -----------------------------------------------------------
    /// <summary>Stack: bool → bool</summary>
    Not,

    // --- Maze ------------------------------------------------------------
    /// <summary>No wall in that direction. Stack: direction → bool</summary>
    SenseFree,
    /// <summary>Cell in that direction already visited. Stack: direction → bool</summary>
    SenseVisited,
    /// <summary>Move one cell; Direction.Back = back through the history. Stack: direction →</summary>
    Move,

    // --- Control flow ----------------------------------------------------
    /// <summary>Operand = target.</summary>
    Jump,
    /// <summary>Operand = target. Jumps if false. Stack: bool →</summary>
    JumpIfFalse,
    /// <summary>Operand = target. Jumps if 0. Stack: int →</summary>
    JumpIfZero,
    /// <summary>Operand = function index. Stack: arg0..argN-1 → result</summary>
    Call,
    /// <summary>Ends the function. Stack: result → (caller sees: result). In the entry function: end of program.</summary>
    Return,
}

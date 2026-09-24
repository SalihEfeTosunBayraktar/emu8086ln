namespace Emu8086.Core.Assembler;

/// <summary>
/// Error identifiers. The user-facing text lives in the UI language files
/// (key "asm.&lt;code&gt;"), so the assembler itself never embeds messages.
/// </summary>
public static class AsmErrorCode
{
    public const string UnterminatedString = "UnterminatedString";
    public const string InvalidNumber = "InvalidNumber";
    public const string UnknownInstruction = "UnknownInstruction";
    public const string UndefinedSymbol = "UndefinedSymbol";
    public const string DuplicateSymbol = "DuplicateSymbol";
    public const string SyntaxError = "SyntaxError";
    public const string ExpectedExpression = "ExpectedExpression";
    public const string InvalidOperands = "InvalidOperands";
    public const string OperandCount = "OperandCount";
    public const string SizeMismatch = "SizeMismatch";
    public const string SizeUnknown = "SizeUnknown";
    public const string ValueOutOfRange = "ValueOutOfRange";
    public const string JumpOutOfRange = "JumpOutOfRange";
    public const string InvalidAddressing = "InvalidAddressing";
    public const string DivisionByZero = "DivisionByZero";
    public const string IncludeNotFound = "IncludeNotFound";
    public const string MacroRecursion = "MacroRecursion";
    public const string MissingEndm = "MissingEndm";
    public const string MissingEnds = "MissingEnds";
    public const string MissingEndp = "MissingEndp";
    public const string UnexpectedDirective = "UnexpectedDirective";
    public const string NotConstant = "NotConstant";
    public const string ImmediateDestination = "ImmediateDestination";
    public const string MemoryToMemory = "MemoryToMemory";
    public const string SegmentImmediate = "SegmentImmediate";
    public const string PopCs = "PopCs";
    public const string NoCode = "NoCode";
    public const string TooManyPasses = "TooManyPasses";
    public const string ConditionalMismatch = "ConditionalMismatch";
}

public enum DiagnosticSeverity { Error, Warning }

public sealed record AsmDiagnostic(
    DiagnosticSeverity Severity,
    string Code,
    string[] Args,
    string File,
    int Line,
    string SourceText);

public sealed class AsmException(string code, params string[] args) : Exception(code)
{
    public string Code { get; } = code;
    public string[] Args { get; } = args;
}

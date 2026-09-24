namespace Emu8086.Core.Assembler;

public static class Registers
{
    public static readonly string[] Reg8 = ["AL", "CL", "DL", "BL", "AH", "CH", "DH", "BH"];
    public static readonly string[] Reg16 = ["AX", "CX", "DX", "BX", "SP", "BP", "SI", "DI"];
    public static readonly string[] Seg = ["ES", "CS", "SS", "DS"];

    public static int IndexOf(string[] set, string name) =>
        Array.FindIndex(set, r => r.Equals(name, StringComparison.OrdinalIgnoreCase));

    public static bool IsRegister(string name) =>
        IndexOf(Reg8, name) >= 0 || IndexOf(Reg16, name) >= 0 || IndexOf(Seg, name) >= 0;
}

public enum OperandKind { Reg8, Reg16, Seg, Imm, Mem }

public sealed class Operand
{
    public OperandKind Kind { get; init; }
    public int Reg { get; init; }
    public ExprValue Value;

    public int Size => Kind switch
    {
        OperandKind.Reg8 => 1,
        OperandKind.Reg16 or OperandKind.Seg => 2,
        OperandKind.Mem => Value.Size,
        _ => 0,
    };

    public bool IsReg => Kind is OperandKind.Reg8 or OperandKind.Reg16;
    public bool IsRm => Kind is OperandKind.Reg8 or OperandKind.Reg16 or OperandKind.Mem;
    public bool IsAcc => IsReg && Reg == 0;

    public static Operand Parse(List<Token> tokens, IExprContext ctx)
    {
        if (tokens.Count == 0) throw new AsmException(AsmErrorCode.ExpectedExpression);
        if (tokens.Count == 1 && tokens[0].Kind == TokenKind.Identifier)
        {
            string name = tokens[0].Text;
            int r;
            if ((r = Registers.IndexOf(Registers.Reg8, name)) >= 0) return new Operand { Kind = OperandKind.Reg8, Reg = r };
            if ((r = Registers.IndexOf(Registers.Reg16, name)) >= 0) return new Operand { Kind = OperandKind.Reg16, Reg = r };
            if ((r = Registers.IndexOf(Registers.Seg, name)) >= 0) return new Operand { Kind = OperandKind.Seg, Reg = r };
        }

        var parser = new ExpressionParser(tokens, 0, ctx);
        var value = parser.Parse();
        if (parser.Position < tokens.Count) throw new AsmException(AsmErrorCode.SyntaxError, tokens[parser.Position].Text);
        return new Operand
        {
            Kind = value.Memory || value.HasRegisters ? OperandKind.Mem : OperandKind.Imm,
            Value = value,
        };
    }

    /// <summary>Splits an operand list on top-level commas.</summary>
    public static List<List<Token>> Split(List<Token> tokens, int start)
    {
        var result = new List<List<Token>>();
        if (start >= tokens.Count) return result;
        var current = new List<Token>();
        int depth = 0;
        for (int i = start; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (t.Kind == TokenKind.Punct)
            {
                if (t.Text is "(" or "[") depth++;
                else if (t.Text is ")" or "]") depth--;
                else if (t.Text == "," && depth == 0)
                {
                    result.Add(current);
                    current = new List<Token>();
                    continue;
                }
            }
            current.Add(t);
        }
        result.Add(current);
        return result;
    }
}

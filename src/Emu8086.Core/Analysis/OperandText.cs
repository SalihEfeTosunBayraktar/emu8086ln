namespace Emu8086.Core.Analysis;

internal static class OperandText
{
    /// <summary>Splits disassembled operands at top-level commas ("[BX+SI], AX" gives two parts).</summary>
    public static List<string> Split(string operands)
    {
        var result = new List<string>();
        int depth = 0, start = 0;
        for (int i = 0; i < operands.Length; i++)
        {
            if (operands[i] == '[') depth++;
            else if (operands[i] == ']') depth--;
            else if (operands[i] == ',' && depth == 0)
            {
                result.Add(operands[start..i].Trim());
                start = i + 1;
            }
        }
        if (operands.Trim().Length > 0) result.Add(operands[start..].Trim());
        return result;
    }
}

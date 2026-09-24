namespace Emu8086.Core.Assembler;

/// <summary>Evaluates a constant assembler expression (0FFh, 1010b, 'A', +, *, AND, SHL ...) outside a program.</summary>
public static class ExpressionCalculator
{
    private sealed class NoSymbols : IExprContext
    {
        public Symbol? Lookup(string name) => throw new AsmException(AsmErrorCode.UndefinedSymbol, name);
        public int CurrentSegment => 0;
        public long CurrentLocation => 0;
        public int SegmentIndex(string name) => -1;
    }

    /// <exception cref="AsmException">The text is not a valid constant expression.</exception>
    public static long Evaluate(string text)
    {
        var tokens = Lexer.Tokenize(text);
        if (tokens.Count == 0) throw new AsmException(AsmErrorCode.ExpectedExpression);
        var parser = new ExpressionParser(tokens, 0, new NoSymbols());
        var value = parser.Parse();
        if (parser.Position < tokens.Count) throw new AsmException(AsmErrorCode.SyntaxError, tokens[parser.Position].Text);
        if (value.HasRegisters || value.Memory) throw new AsmException(AsmErrorCode.NotConstant);
        return value.Num;
    }
}

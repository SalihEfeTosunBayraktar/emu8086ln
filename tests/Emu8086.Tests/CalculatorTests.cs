using Emu8086.Core.Assembler;

namespace Emu8086.Tests;

public class CalculatorTests
{
    [Theory]
    [InlineData("0FFh", 255)]
    [InlineData("1010b", 10)]
    [InlineData("17o", 15)]
    [InlineData("'A'", 65)]
    [InlineData("0FFh + 1010b * 2", 275)]
    [InlineData("'a' XOR 20h", 65)]
    [InlineData("1 SHL 4", 16)]
    [InlineData("-5", -5)]
    [InlineData("(10 + 6) / 4 MOD 3", 1)]
    public void Evaluates(string text, long expected) => Assert.Equal(expected, ExpressionCalculator.Evaluate(text));

    [Theory]
    [InlineData("")]
    [InlineData("foo + 1")]
    [InlineData("[bx]")]
    [InlineData("12 34")]
    public void RejectsInvalid(string text) => Assert.Throws<AsmException>(() => ExpressionCalculator.Evaluate(text));
}

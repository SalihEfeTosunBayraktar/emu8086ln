using Emu8086.Core.Assembler;
using Emu8086.Core.Machine;

namespace Emu8086.Tests;

internal static class TestHost
{
    public static AssemblyResult Assemble(string source, Func<string, string?>? includes = null)
    {
        var result = new Assembler8086().Assemble(source, "test.asm", includes ?? (_ => null));
        return result;
    }

    public static AssemblyResult AssembleOk(string source, Func<string, string?>? includes = null)
    {
        var result = Assemble(source, includes);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics.Select(d => $"{d.Line}: {d.Code} {string.Join(",", d.Args)} [{d.SourceText}]")));
        return result;
    }

    /// <summary>Bytes of a COM program (after ORG 100h).</summary>
    public static byte[] Bytes(string body) => ProgramImage.Link(AssembleOk("org 100h\n" + body)).Bytes;

    public static Machine Run(string source, string keys = "", int maxSteps = 2_000_000, Func<string, string?>? includes = null)
    {
        var result = AssembleOk(source, includes);
        var machine = new Machine(Path.Combine(Path.GetTempPath(), "emu8086ln-tests", Guid.NewGuid().ToString("N")));
        machine.Load(ProgramImage.Link(result));
        machine.Keyboard.PushText(keys);
        for (int i = 0; i < maxSteps && !machine.IsStopped; i++)
        {
            if (machine.Step() == Emu8086.Core.Cpu.StepResult.Waiting && !machine.Keyboard.HasKey)
                break;
        }
        return machine;
    }
}

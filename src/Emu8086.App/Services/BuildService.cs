using System.IO;
using Emu8086.Core.Assembler;
using Emu8086.Core.Machine;

namespace Emu8086.App.Services;

public sealed record BuildOutput(AssemblyResult Result, ProgramImage? Image, string SourcePath);

/// <summary>Assembles source text, resolving INCLUDE files next to the source, then in the library folder.</summary>
public static class BuildService
{
    public static BuildOutput Build(string source, string sourcePath)
    {
        string? directory = Path.GetDirectoryName(sourcePath);
        string? Resolve(string name)
        {
            foreach (var dir in new[] { directory, AppPaths.LibraryDirectory })
            {
                if (string.IsNullOrEmpty(dir)) continue;
                string candidate = Path.Combine(dir, name);
                if (File.Exists(candidate)) return File.ReadAllText(candidate);
            }
            return null;
        }

        var result = new Assembler8086().Assemble(source, Path.GetFileName(sourcePath), Resolve);
        var image = result.Success ? ProgramImage.Link(result) : null;
        return new BuildOutput(result, image, sourcePath);
    }

    /// <summary>Localized, single-line description of a diagnostic.</summary>
    public static string Describe(AsmDiagnostic d) =>
        Loc.Instance.Format("asm." + d.Code, d.Args.Cast<object?>().ToArray());
}

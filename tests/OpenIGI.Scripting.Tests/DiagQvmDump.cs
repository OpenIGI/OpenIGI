using System;
using System.IO;
using OpenIGI.Scripting.Qvm;
using OpenIGI.TestSupport;
using Xunit;
using Xunit.Abstractions;

namespace OpenIGI.Scripting.Tests;

/// <summary>
/// Dumps the mission-4 per-soldier AI scripts so their authored patrol-path ids and action calls
/// can be read. Not an assertion test; run with a filter and read the test output.
/// </summary>
public sealed class DiagQvmDump
{
    private readonly ITestOutputHelper _output;

    public DiagQvmDump(ITestOutputHelper output) => _output = output;

    [RequiresGameDataFact]
    public void DumpMission4AiScripts()
    {
        string root = GameData.RequireRoot();
        string aiDir = Path.Combine(root, "MISSIONS", "location0", "level4", "ai");
        foreach (string path in Directory.GetFiles(aiDir, "*.qvm").OrderBy(p => Path.GetFileNameWithoutExtension(p), StringComparer.Ordinal))
        {
            string id = Path.GetFileNameWithoutExtension(path)!;
            _output.WriteLine($"=== {id}.qvm ===");
            QvmProgram program = QvmProgram.Read(File.ReadAllBytes(path));
            foreach (string line in QvmDisassembler.Disassemble(program))
            {
                _output.WriteLine(line);
            }
        }
    }
}

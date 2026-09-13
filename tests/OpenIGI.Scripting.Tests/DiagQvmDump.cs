using System;
using System.IO;
using OpenIGI.Scripting.Qvm;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Scripting.Tests;

/// <summary>
/// Dumps the mission-4 per-soldier AI scripts so their authored patrol-path ids and action calls
/// can be read. Not an assertion test; run with a filter and read the console output.
/// </summary>
public sealed class DiagQvmDump
{
    [RequiresGameDataFact]
    public void DumpMission4AiScripts()
    {
        string root = GameData.RequireRoot();
        string aiDir = Path.Combine(root, "MISSIONS", "location0", "level4", "ai");
        foreach (string path in Directory.GetFiles(aiDir, "*.qvm").OrderBy(p => Path.GetFileNameWithoutExtension(p), StringComparer.Ordinal))
        {
            string id = Path.GetFileNameWithoutExtension(path)!;
            Console.WriteLine($"=== {id}.qvm ===");
            QvmProgram program = QvmProgram.Read(File.ReadAllBytes(path));
            foreach (string line in QvmDisassembler.Disassemble(program))
            {
                Console.WriteLine(line);
            }
        }
    }
}

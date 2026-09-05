using System;
using System.IO;
using OpenIGI.Scripting.Qvm;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Scripting.Tests;

public sealed class DiagObjectsDump
{
    [RequiresGameDataFact]
    public void DumpMission4Objects()
    {
        string root = GameData.RequireRoot();
        string path = Path.Combine(root, "MISSIONS", "location0", "level4", "objects.qvm");
        QvmProgram program = QvmProgram.Read(File.ReadAllBytes(path));
        var lines = QvmDisassembler.Disassemble(program).ToList();
        // Print every string plus the line numbers, then a windowed view around key conditions.
        for (int i = 0; i < lines.Count; i++)
        {
            string line = lines[i];
            if (line.Contains("; string \"") || line.Contains("; symbol"))
            {
                Console.WriteLine($"{i:D6}: {line}");
            }
        }
    }
}

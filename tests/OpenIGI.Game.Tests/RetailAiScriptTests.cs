using OpenIGI.Game.Ai;
using OpenIGI.Scripting.Qvm;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Checks the AI vocabulary against every AI script an installation ships — the nine archetypes, the
/// settings table and the per-task overrides under each level's <c>ai/</c>.
/// </summary>
/// <remarks>
/// This is the test that makes <see cref="AiScriptApi"/> worth transcribing separately from the AI runtime:
/// if a name or an arity in the table is wrong, a shipped script disagrees with it here, long before any
/// behaviour is implemented.
/// </remarks>
public sealed class RetailAiScriptTests
{
    /// <summary>
    /// Every <c>AIAction_*</c> or <c>AIFunction_*</c> name any shipped AI script mentions is one of the
    /// fifty-five, and every <c>AI*</c> constant it mentions is one the module registers.
    /// </summary>
    [RequiresGameDataFact]
    public void EveryAiNameAScriptMentionsIsInTheTable()
    {
        List<string> unknown = [];
        HashSet<string> nativesSeen = new(StringComparer.Ordinal);
        HashSet<string> constantsSeen = new(StringComparer.Ordinal);
        int scripts = 0;

        foreach (string path in AiScripts())
        {
            QvmProgram program = QvmProgram.Read(File.ReadAllBytes(path));
            scripts++;

            foreach (string? symbol in program.SymbolNames)
            {
                if (symbol is null)
                {
                    continue;
                }

                if (symbol.StartsWith("AIAction_", StringComparison.Ordinal) ||
                    symbol.StartsWith("AIFunction_", StringComparison.Ordinal))
                {
                    if (AiScriptApi.TryGetNative(symbol, out _))
                    {
                        nativesSeen.Add(symbol);
                    }
                    else
                    {
                        unknown.Add($"{Name(path)}: native {symbol}");
                    }
                }
                else if (symbol.StartsWith("AIEVENT_", StringComparison.Ordinal) ||
                         symbol.StartsWith("AITYPE_", StringComparison.Ordinal) ||
                         symbol.StartsWith("AIACTIONFLAG_", StringComparison.Ordinal) ||
                         symbol.StartsWith("AIALARMACCESS_", StringComparison.Ordinal) ||
                         symbol.StartsWith("HUMANAI_", StringComparison.Ordinal))
                {
                    if (AiScriptApi.TryGetConstant(symbol, out _))
                    {
                        constantsSeen.Add(symbol);
                    }
                    else
                    {
                        unknown.Add($"{Name(path)}: constant {symbol}");
                    }
                }
            }
        }

        Assert.Empty(unknown);
        Assert.True(scripts > 800, $"only found {scripts} AI scripts");

        // The shipped scripts exercise most of the API but not all of it, which is worth knowing rather
        // than asserting away: an unused native is a lead, not a mistake.
        Assert.NotEmpty(nativesSeen);
        Assert.NotEmpty(constantsSeen);
    }

    /// <summary>
    /// A call site's argument count matches what the registrar declares, for every AI native call in every
    /// shipped script.
    /// </summary>
    /// <remarks>
    /// The callee is not an operand of <c>CALL</c> — the interpreter <b>pops it off the stack</b> — so the
    /// name comes from the instruction before, which is a <c>PUSHII</c>/<c>PUSHIIB</c>/<c>PUSHIIW</c> carrying
    /// a <em>symbol</em> index. Note the family: <c>PUSHSI*</c> is the near-identical <em>string</em>-index
    /// push, and using it resolves no call sites at all rather than failing loudly.
    /// </remarks>
    [RequiresGameDataFact]
    public void EveryCallSiteMatchesTheDeclaredArity()
    {
        List<string> mismatches = [];
        Dictionary<string, int> callCounts = new(StringComparer.Ordinal);

        foreach (string path in AiScripts())
        {
            QvmProgram program = QvmProgram.Read(File.ReadAllBytes(path));
            QvmInstruction? previous = null;

            foreach (QvmInstruction instruction in QvmDisassembler.Decode(program))
            {
                if (instruction.Opcode == QvmOpcode.Call &&
                    previous is not null &&
                    previous.Opcode is QvmOpcode.PushII or QvmOpcode.PushIIB or QvmOpcode.PushIIW &&
                    program.TryGetSymbolName(previous.Integer, out string callee) &&
                    AiScriptApi.TryGetNative(callee, out AiNative native))
                {
                    callCounts[callee] = callCounts.GetValueOrDefault(callee) + 1;
                    if (instruction.ArgumentOffsets.Count != native.ArgumentCount)
                    {
                        mismatches.Add(
                            $"{Name(path)}+0x{instruction.Offset:X}: {callee} passes " +
                            $"{instruction.ArgumentOffsets.Count}, declared {native.ArgumentCount}");
                    }
                }

                previous = instruction;
            }
        }

        Assert.Empty(mismatches);

        // Enough call sites resolved that the arity check means something.
        Assert.True(callCounts.Count >= 20, $"only resolved {callCounts.Count} distinct AI natives at call sites");
        Assert.True(callCounts.Values.Sum() > 1000, $"only {callCounts.Values.Sum()} AI call sites");
    }

    /// <summary>
    /// The nine archetype scripts the engine names all exist, and <c>observer</c> and <c>radioguard</c> ship
    /// beside them without the engine or any shipped script ever naming either.
    /// </summary>
    [RequiresGameDataFact]
    public void TwoArchetypeScriptsShipDead()
    {
        string aiDirectory = Path.Combine(GameData.RequireRoot(), "common", "ai");

        foreach (AiArchetype archetype in Enum.GetValues<AiArchetype>())
        {
            string file = AiScriptApi.ScriptPath(archetype)["LOCAL:common/ai/".Length..].Replace(".qsc", ".qvm");
            Assert.True(File.Exists(Path.Combine(aiDirectory, file)), file);
        }

        Assert.True(File.Exists(Path.Combine(aiDirectory, "observer.qvm")));
        Assert.True(File.Exists(Path.Combine(aiDirectory, "radioguard.qvm")));

        // Nothing anywhere in the shipped data names either one.
        List<string> mentions = [];
        foreach (string path in GameData.EnumerateFiles("*.qvm"))
        {
            QvmProgram program = QvmProgram.Read(File.ReadAllBytes(path));
            foreach (string? text in program.Strings)
            {
                if (text is not null &&
                    (text.Contains("observer", StringComparison.OrdinalIgnoreCase) ||
                     text.Contains("radioguard", StringComparison.OrdinalIgnoreCase)))
                {
                    mentions.Add($"{Name(path)}: {text}");
                }
            }
        }

        Assert.Empty(mentions);
    }

    /// <summary>
    /// A per-task AI override is named by the owning task's id, so a level's <c>ai/</c> holds files whose
    /// numbers are task ids — the same rule the nav graph's file names follow.
    /// </summary>
    [RequiresGameDataFact]
    public void ThePerTaskOverridesAreNamedByTaskId()
    {
        string level1 = Path.Combine(GameData.RequireRoot(), "missions", "location0", "level1", "ai");
        string[] files = Directory.GetFiles(level1, "*.qvm");

        Assert.NotEmpty(files);
        foreach (string file in files)
        {
            string stem = Path.GetFileNameWithoutExtension(file);
            Assert.True(
                int.TryParse(stem, System.Globalization.CultureInfo.InvariantCulture, out int id) && id > 0,
                $"'{stem}' is not a task id");
            Assert.Equal($"MISSION:AI/{id}.qsc", AiScriptApi.MissionScriptPath(id));
        }
    }

    private static IEnumerable<string> AiScripts()
    {
        string root = GameData.RequireRoot();
        foreach (string path in GameData.EnumerateFiles("*.qvm"))
        {
            string relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            if (relative.Contains("/ai/", StringComparison.OrdinalIgnoreCase) ||
                relative.StartsWith("ai/", StringComparison.OrdinalIgnoreCase))
            {
                yield return path;
            }
        }
    }

    private static string Name(string path) => Path.GetFileName(path);
}

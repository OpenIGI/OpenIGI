using OpenIGI.Engine.Tasks;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Engine.Tests.Tasks;

/// <summary>
/// Runs every <c>AnimData</c> programme in the retail levels through the interpreter.
/// </summary>
public sealed class RetailAnimDataTests
{
    [RequiresGameDataFact]
    public void OnlyAnimTaskCarriesAnAnimDataParameter()
    {
        Dictionary<string, int> byType = [];
        foreach ((string type, string parameter, AnimDataEntry[] _) in Programmes())
        {
            byType[$"{type}.{parameter}"] = byType.GetValueOrDefault($"{type}.{parameter}") + 1;
        }

        Assert.Equal(new Dictionary<string, int> { ["AnimTask.AnimData"] = 67 }, byType);
    }

    [RequiresGameDataFact]
    public void EveryProgrammeUsesOnlyTheChannelsMeasuredInRetailData()
    {
        // 0..9 are ordinary channels and 32 is the delay. Nothing writes 33, the program counter, so
        // no retail programme jumps — which is what makes their total tick counts meaningful.
        HashSet<int> codes = [];
        foreach ((string _, string _, AnimDataEntry[] entries) in Programmes())
        {
            foreach (AnimDataEntry entry in entries)
            {
                codes.Add(entry.Code);
            }
        }

        Assert.Equal([0, 1, 2, 3, 4, 5, 6, 7, 8, 9, AnimDataProgram.WaitChannel], codes.Order());
        Assert.DoesNotContain(AnimDataProgram.ProgramCounterChannel, codes);
    }

    [RequiresGameDataFact]
    public void TheDelayIsTheOnlyChannelWrittenWithAWholeNumber()
    {
        // The parse truncates only opcode 32, so if any other channel carried an integral-only domain
        // it would be a coincidence. The delays are whole and large; the rest are small and fractional.
        foreach ((string _, string _, AnimDataEntry[] entries) in Programmes())
        {
            foreach (AnimDataEntry entry in entries)
            {
                if (entry.Code == AnimDataProgram.WaitChannel)
                {
                    Assert.Equal(entry.Value, MathF.Truncate(entry.Value));
                    Assert.InRange(entry.Value, 0f, 4096f);
                }
                else
                {
                    Assert.InRange(entry.Value, -1f, 1.25f);
                }
            }
        }
    }

    [RequiresGameDataFact]
    public void EveryProgrammeEndsWithADelayAndRunsToCompletion()
    {
        int count = 0;
        int shortest = int.MaxValue;
        int longest = 0;
        foreach ((string _, string _, AnimDataEntry[] entries) in Programmes())
        {
            count++;
            Assert.NotEmpty(entries);
            Assert.Equal(AnimDataProgram.WaitChannel, entries[^1].Code);

            AnimDataProgram program = new(entries);
            int total = program.TotalTicks();

            shortest = Math.Min(shortest, total);
            longest = Math.Max(longest, total);

            // The exact invariant, and it holds for all 67: a programme runs out after one step more
            // than the sum of its delays. That is only true because every one of them ends on a delay
            // and none of them jumps.
            int steps = 0;
            while (!program.HasFinished && steps <= total + 1)
            {
                program.Step();
                steps++;
            }

            Assert.Equal(total + 1, steps);
        }

        Assert.Equal(67, count);

        // One programme is entirely zero delays and expires in a single step; the longest runs 6988
        // ticks, just under four minutes at 30 Hz.
        Assert.Equal(0, shortest);
        Assert.Equal(6988, longest);
    }

    [RequiresGameDataFact]
    public void SteppingAndSeekingAgreeOnEveryProgramme()
    {
        foreach ((string _, string _, AnimDataEntry[] entries) in Programmes())
        {
            AnimDataProgram stepped = new(entries);
            for (int i = 0; i < 101; i++)
            {
                stepped.Step();
            }

            AnimDataProgram sought = new(entries);
            sought.SeekTo(100);

            for (int channel = 0; channel < AnimDataProgram.ChannelCount; channel++)
            {
                Assert.Equal(stepped.Channels[channel], sought.Channels[channel]);
            }
        }
    }

    private static IEnumerable<(string Type, string Parameter, AnimDataEntry[] Entries)> Programmes()
    {
        for (int level = 1; level <= 14; level++)
        {
            string path;
            try
            {
                path = GameData.Resolve($"missions/location0/level{level}/objects.qvm");
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                continue;
            }

            NativeRegistry natives = new();
            TaskTypeRegistry types = new();
            EngineTaskTypes.Register(types);
            TaskTree tree = new(types);
            ScriptVarRegistry variables = new(natives);
            TaskScriptCommands commands = new(tree, variables);
            commands.Register(natives);
            natives.RegisterConstant("TRUE", 1);
            natives.RegisterConstant("FALSE", 0);

            QvmProgram program = QvmProgram.Read(File.ReadAllBytes(path));
            QvmInterpreter interpreter = new(program, natives);
            Assert.Equal(QvmError.None, interpreter.Run());

            foreach (var found in Walk(tree.Root))
            {
                yield return found;
            }
        }
    }

    private static IEnumerable<(string Type, string Parameter, AnimDataEntry[] Entries)> Walk(GameTask task)
    {
        foreach (GameTask child in task.Children)
        {
            if (child is DeclaredTask declared)
            {
                IReadOnlyList<TaskParameter> parameters = declared.Type.Parameters;
                for (int i = 0; i < parameters.Count; i++)
                {
                    if (parameters[i].Type == ScriptParamType.AnimData
                        && declared.GetValue(i) is AnimDataEntry[] entries)
                    {
                        yield return (child.Type.Name, parameters[i].Name, entries);
                    }
                }
            }

            foreach (var found in Walk(child))
            {
                yield return found;
            }
        }
    }
}

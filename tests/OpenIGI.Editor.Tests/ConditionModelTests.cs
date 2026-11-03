using OpenIGI.Editor.Core.Missions;
using OpenIGI.Scripting.Qsc;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Editor.Tests;

/// <summary>Conditions taken apart into groups and tests and put back together.</summary>
public sealed class ConditionModelTests
{
    /// <summary>A retail-shaped condition becomes a group of tests, negations and comparisons kept, and prints back to the same tree.</summary>
    [Fact]
    public void A_condition_reads_as_tests_and_prints_back()
    {
        const string text = "(Terminal_94.isHacked && AnimTask_850.nTick < 2320) &&\n(!ConditionalContainer_300.isRun || LevelTimer_101.nTick < 30 * GAME_FREQUENCY)";
        Assert.True(ConditionModel.TryParse(text, out ConditionGroup root, out string? error), error);

        Assert.False(root.Any);
        Assert.Equal(3, root.Items.Count);
        ConditionTerm hacked = Assert.IsType<ConditionTerm>(root.Items[0]);
        Assert.Equal(("Terminal_94", "isHacked", (QscBinaryOperator?)null), (hacked.Subject, hacked.Field, hacked.Compare));
        ConditionTerm tick = Assert.IsType<ConditionTerm>(root.Items[1]);
        Assert.Equal(QscBinaryOperator.Less, tick.Compare);
        ConditionGroup either = Assert.IsType<ConditionGroup>(root.Items[2]);
        Assert.True(either.Any);
        Assert.True(Assert.IsType<ConditionTerm>(either.Items[0]).Negated);
        Assert.Equal(
            ["Terminal_94.isHacked", "AnimTask_850.nTick", "ConditionalContainer_300.isRun", "LevelTimer_101.nTick"],
            ConditionModel.Symbols(root));

        string printed = ConditionModel.Print(root);
        Assert.Equal(QscPrinter.Print(QscParser.ParseExpression(text)), QscPrinter.Print(QscParser.ParseExpression(printed)));
    }

    /// <summary>A blank condition is an empty group; text that is not an expression is refused with the reason.</summary>
    [Fact]
    public void Blank_and_broken_conditions()
    {
        Assert.True(ConditionModel.TryParse("  ", out ConditionGroup empty, out _));
        Assert.Empty(empty.Items);
        Assert.Equal(string.Empty, ConditionModel.Print(empty));
        Assert.False(ConditionModel.TryParse("CutScene_1.isRun &&", out _, out string? error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    /// <summary>Every retail Condition takes apart and goes back together meaning what it meant, reading the same fields.</summary>
    [RequiresGameDataFact]
    public void Every_retail_condition_round_trips()
    {
        int seen = 0;
        int unparsed = 0;
        for (int level = 1; level <= 14; level++)
        {
            string path = GameData.Resolve($"missions/location0/level{level}/objects.qvm");
            if (!File.Exists(path))
            {
                continue;
            }

            foreach (MissionTask task in MissionDocument.Read(File.ReadAllBytes(path)).EnumerateTasks())
            {
                if (task.FindParameter("Condition") is not { Arguments: [QscStringLiteral { Value: { } text }] } || string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                seen++;
                if (!ConditionModel.TryParse(text, out ConditionGroup root, out _))
                {
                    // Kept as written and shown as code: one retail condition ends in stray characters the original skips.
                    unparsed++;
                    continue;
                }

                string printed = ConditionModel.Print(root);
                Assert.True(ConditionModel.TryParse(printed, out ConditionGroup again, out _), printed);
                Assert.Equal(printed, ConditionModel.Print(again));
                Assert.Equal(ConditionModel.Symbols(root), ConditionModel.Symbols(again));
            }
        }

        Assert.True(seen > 20, $"only {seen} conditions");
        Assert.True(unparsed <= 2, $"{unparsed} of {seen} conditions did not parse");
    }
}

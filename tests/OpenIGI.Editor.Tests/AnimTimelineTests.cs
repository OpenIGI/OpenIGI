using OpenIGI.Editor.Core.Missions;
using OpenIGI.Scripting.Qsc;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Editor.Tests;

/// <summary>AnimData read as keys on a timeline and written back.</summary>
public sealed class AnimTimelineTests
{
    /// <summary>Waits become key ticks and come back from the gaps; the programme's tail wait is its length.</summary>
    [Fact]
    public void Waits_become_ticks_and_come_back()
    {
        QscExpression[] arguments = [.. "6, 0, -1, 32, 746.0, 0, 0, 32, 4.0, 0, -1, 32, 215.0".Split(", ").Select(QscParser.ParseExpression)];
        Assert.True(AnimTimeline.TryParse(arguments, out AnimTimeline timeline));

        Assert.Equal([(0, 0, -1.0), (746, 0, 0.0), (750, 0, -1.0)], timeline.Keys.Select(static k => (k.Tick, k.Channel, k.Value)));
        Assert.Equal(965, timeline.Length);
        Assert.Equal(Print(arguments), Print(timeline.ToArguments()));

        // A key moved later is written after the one it now follows.
        timeline.Keys[0].Tick = 748;
        timeline.Sort();
        Assert.Equal("7, 32, 746.0, 0, 0, 32, 2.0, 0, -1, 32, 2.0, 0, -1, 32, 215.0", Print(timeline.ToArguments()));
    }

    /// <summary>Waits back to back, written as integers or of no time come back as they were while their gap is untouched.</summary>
    [Fact]
    public void Odd_waits_come_back_as_written()
    {
        QscExpression[] arguments = [.. "4, 32, 4.0, 32, 5, 0, 1, 32, 0".Split(", ").Select(QscParser.ParseExpression)];
        Assert.True(AnimTimeline.TryParse(arguments, out AnimTimeline timeline));
        Assert.Equal(9, timeline.Keys[0].Tick);
        Assert.Equal(Print(arguments), Print(timeline.ToArguments()));

        timeline.Keys[0].Tick = 10;
        Assert.Equal("3, 32, 10.0, 0, 1, 32, 0", Print(timeline.ToArguments()));
    }

    /// <summary>Every retail AnimData reads as a timeline and writes back the very same arguments.</summary>
    [RequiresGameDataFact]
    public void Every_retail_programme_round_trips()
    {
        int seen = 0;
        for (int level = 1; level <= 14; level++)
        {
            string path = GameData.Resolve($"missions/location0/level{level}/objects.qvm");
            if (!File.Exists(path))
            {
                continue;
            }

            foreach (MissionTask task in MissionDocument.Read(File.ReadAllBytes(path)).EnumerateTasks())
            {
                if (task.FindParameter("AnimData") is not { } data)
                {
                    continue;
                }

                seen++;
                Assert.True(AnimTimeline.TryParse(data.Arguments, out AnimTimeline timeline), $"level {level} task {task.Id}");
                Assert.Equal(Print(data.Arguments), Print(timeline.ToArguments()));
            }
        }

        Assert.True(seen > 0);
    }

    private static string Print(IEnumerable<QscExpression> arguments) => string.Join(", ", arguments.Select(QscPrinter.Print));
}

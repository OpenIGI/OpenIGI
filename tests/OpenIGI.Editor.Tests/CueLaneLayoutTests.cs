using OpenIGI.Editor.Panels;
using Xunit;

namespace OpenIGI.Editor.Tests;

/// <summary>A lane of cues stays readable however crowded: close diamonds are one mark, and no label runs into the next.</summary>
public sealed class CueLaneLayoutTests
{
    // Text 7 px a character, cut with an ellipsis to the room it has.
    private static string Fit(string text, float room)
    {
        int fits = (int)(room / 7f);
        return text.Length <= fits ? text : fits <= 1 ? "…" : text[..(fits - 1)] + "…";
    }

    private static float Width(string text) => text.Length * 7f;

    [Fact]
    public void Diamonds_that_would_overlap_are_one_mark_naming_how_many_more()
    {
        (float, string)[] cues = [(100f, "gate"), (104f, "siren"), (109f, "voice"), (300f, "truck")];
        IReadOnlyList<CueLaneLayout.Mark> marks = CueLaneLayout.Lay(cues, Fit, mergeWithin: 10f, labelGap: 8f, shortestLabel: 24f, end: 600f);

        Assert.Equal(2, marks.Count);
        Assert.Equal((0, 3), (marks[0].First, marks[0].Count));
        Assert.Equal("gate +2", marks[0].Label);
        Assert.Equal((3, 1), (marks[1].First, marks[1].Count));
        Assert.Equal("truck", marks[1].Label);
    }

    [Fact]
    public void No_label_runs_past_the_next_mark_and_a_cramped_one_is_left_out()
    {
        (float, string)[] cues = [(100f, "a very long cue name indeed"), (170f, "second"), (190f, "third"), (400f, "last")];
        IReadOnlyList<CueLaneLayout.Mark> marks = CueLaneLayout.Lay(cues, Fit, 10f, 8f, 24f, 450f);

        for (int i = 0; i + 1 < marks.Count; i++)
        {
            Assert.True(marks[i].X + 8f + Width(marks[i].Label) <= marks[i + 1].X, $"mark {i}'s label runs into the next");
        }

        Assert.EndsWith("…", marks[0].Label, StringComparison.Ordinal);
        Assert.Equal(string.Empty, marks[1].Label); // 20 px to the next: too little for a label
        Assert.Equal("third", marks[2].Label);
        Assert.True(marks[3].X + 8f + Width(marks[3].Label) <= 450f);
    }

    [Fact]
    public void A_point_finds_the_nearest_mark_within_reach()
    {
        IReadOnlyList<CueLaneLayout.Mark> marks = CueLaneLayout.Lay([(100f, "a"), (130f, "b")], Fit, 10f, 8f, 24f, 300f);
        Assert.Equal(0, CueLaneLayout.MarkAt(marks, 104f, 8f));
        Assert.Equal(1, CueLaneLayout.MarkAt(marks, 126f, 8f));
        Assert.Equal(-1, CueLaneLayout.MarkAt(marks, 115f, 8f));
    }
}

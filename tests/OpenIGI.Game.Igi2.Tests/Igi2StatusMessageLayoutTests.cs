using OpenIGI.Game.Igi2.Flow;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>A status message's text broken into its box's lines, as <c>StatusMessageScreen_FormatMessageText</c> breaks it.</summary>
public sealed class Igi2StatusMessageLayoutTests
{
    // Every character a unit wide.
    private static float Width(string text) => text.Length;

    /// <summary>Text that fits stays one line.</summary>
    [Fact]
    public void Short_text_is_one_line() =>
        Assert.Equal(["Go to the gate."], Igi2StatusMessageLayout.Wrap("Go to the gate.", Width, 40f));

    /// <summary>A line ends at the last space before it reaches the width, the space dropped.</summary>
    [Fact]
    public void A_line_breaks_at_its_last_space() =>
        Assert.Equal(["aaa bbb", "ccc ddd"], Igi2StatusMessageLayout.Wrap("aaa bbb ccc ddd", Width, 8f));

    /// <summary>A line break ends a line where it stands.</summary>
    [Fact]
    public void A_line_break_ends_a_line() =>
        Assert.Equal(["one two", "three"], Igi2StatusMessageLayout.Wrap("one two \nthree", Width, 40f));

    /// <summary>Of more than three lines, the last three show.</summary>
    [Fact]
    public void Only_the_last_three_lines_show() =>
        Assert.Equal(["cc", "dd", "ee"], Igi2StatusMessageLayout.Wrap("aa bb cc dd ee", Width, 3f));
}

using OpenIGI.Input;
using OpenIGI.Platform.Desktop;
using OpenIGI.Platform.Web;
using Xunit;

namespace OpenIGI.Platform.Web.Tests;

/// <summary>The page's events as the window reads them, and the keys they name.</summary>
public sealed class WebInputTests
{
    [Theory]
    [InlineData("KeyW", KeyCode.W)]
    [InlineData("Digit1", KeyCode.D1)]
    [InlineData("ArrowUp", KeyCode.Up)]
    [InlineData("ControlLeft", KeyCode.LeftControl)]
    [InlineData("Backquote", KeyCode.Grave)]
    [InlineData("NumpadEnter", KeyCode.NumpadEnter)]
    public void A_browser_key_name_is_the_scan_code_at_that_position(string code, KeyCode expected) =>
        Assert.Equal(expected, WebKeyMap.Translate(code));

    [Theory]
    [InlineData("IntlRo")]
    [InlineData("")]
    [InlineData(null)]
    public void A_key_the_original_has_no_code_for_is_none(string? code) =>
        Assert.Equal(KeyCode.None, WebKeyMap.Translate(code));

    /// <summary>The browser reaches every key the desktop does.</summary>
    [Fact]
    public void The_browser_maps_as_many_keys_as_the_desktop() =>
        Assert.Equal(DesktopKeyMap.MappedKeyCount, WebKeyMap.MappedKeyCount);

    [Fact]
    public void Events_are_read_from_their_words()
    {
        Assert.Equal(new WebInputEvent(WebInputEventKind.Key, "KeyW", Down: true), WebInputEvent.Parse("key KeyW 1"));
        Assert.Equal(
            new WebInputEvent(WebInputEventKind.Move, X: 3, Y: -1.5f, Z: 640, W: 360),
            WebInputEvent.Parse("move 3 -1.5 640 360"));
        Assert.Equal(new WebInputEvent(WebInputEventKind.Button, Down: false, X: 2), WebInputEvent.Parse("button 2 0"));
        Assert.Equal(new WebInputEvent(WebInputEventKind.Wheel, X: -1), WebInputEvent.Parse("wheel -1"));
        Assert.Equal(new WebInputEvent(WebInputEventKind.Resize, X: 800, Y: 600), WebInputEvent.Parse("resize 800 600"));
        Assert.Equal(new WebInputEvent(WebInputEventKind.PointerLock, Down: true), WebInputEvent.Parse("lock 1"));
        Assert.Equal(WebInputEventKind.Close, WebInputEvent.Parse("close").Kind);
        Assert.Equal(new WebInputEvent(WebInputEventKind.Gyro, X: 0.0125f, Y: -0.004f), WebInputEvent.Parse("gyro 0.0125 -0.004"));
    }

    /// <summary>A typed space is the character after the word, not a separator.</summary>
    [Fact]
    public void A_typed_space_is_a_character()
    {
        Assert.Equal(" ", WebInputEvent.Parse("char  ").Code);
        Assert.Equal("a", WebInputEvent.Parse("char a").Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("key KeyW")]
    [InlineData("teleport 1 2")]
    public void A_line_it_cannot_read_is_unknown(string line) =>
        Assert.Equal(WebInputEventKind.Unknown, WebInputEvent.Parse(line).Kind);
}

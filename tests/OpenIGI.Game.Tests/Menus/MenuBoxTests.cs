using Xunit;
using OpenIGI.Game.Igi1.Menus;

namespace OpenIGI.Game.Tests.Menus;

/// <summary>
/// The six remaining control types, read constructor-first.
/// </summary>
/// <remarks>
/// Every one of these constructors sets both centre flags. That is the whole reason they were read
/// before their renders: a control placed by a <c>HorizontalFillOut</c> frame takes its vertical
/// position from its own centring, so an untranscribed constructor leaves it at the top of the
/// display — which is exactly what <c>ListBox</c> did.
/// </remarks>
public sealed class MenuBoxTests
{
    [Fact]
    public void EveryBoxCentresItself()
    {
        MenuWindowTask[] boxes =
        [
            new PictureBoxTask(),
            new TextBoxTask(),
            new InputBoxTask(),
            new AsciiBoxTask(),
            new ScrollListBoxTask(),
            new TypeWriterBoxTask(),
        ];

        foreach (MenuWindowTask box in boxes)
        {
            Assert.True(box.CentreHorizontally, $"{box.GetType().Name} does not centre horizontally");
            Assert.True(box.CentreVertically, $"{box.GetType().Name} does not centre vertically");
        }
    }

    [Fact]
    public void EachBoxTakesTheSizeItsConstructorWrites()
    {
        Assert.Equal((320, 240, 256, 192), Rect(new PictureBoxTask()));
        Assert.Equal((300, 230, 128, 128), Rect(new TextBoxTask()));
        Assert.Equal((0, 0, 100, 100), Rect(new AsciiBoxTask()));
        Assert.Equal((0, 0, 100, 100), Rect(new ScrollListBoxTask()));
        Assert.Equal((0, 0, 100, 100), Rect(new TypeWriterBoxTask()));
        Assert.Equal(InputBoxTask.DefaultHeight, new InputBoxTask().LayoutHeight);
    }

    [Fact]
    public void AFieldIsAlwaysClickable()
    {
        // The only one of the six whose constructor sets +76 itself rather than taking it from a
        // parameter.
        Assert.True(new InputBoxTask().IsInteractive);
    }

    [Fact]
    public void AFieldTypesBackspacesAndStopsAtItsBuffer()
    {
        InputBoxTask field = new() { MaxLength = 6 };

        foreach (char c in "abcdefgh")
        {
            field.TypeCharacter(c);
        }

        Assert.Equal("abcde", field.Data);
        Assert.Equal(5, field.Caret);

        Assert.True(field.Backspace());
        Assert.Equal("abcd", field.Data);
    }

    [Fact]
    public void AFieldNeverExceedsTheOriginalsFortyByteBuffer()
    {
        // A script asking for more than the buffer holds gets the buffer, because the buffer is what
        // truncates.
        InputBoxTask field = new() { MaxLength = 500 };

        for (int i = 0; i < 200; i++)
        {
            field.TypeCharacter('x');
        }

        Assert.Equal(InputBoxTask.DataCapacity - 1, field.Data.Length);
    }

    [Fact]
    public void AHiddenFieldShowsStarsButKeepsTheText()
    {
        InputBoxTask field = new() { HideInput = true };
        field.TypeCharacter('a');
        field.TypeCharacter('b');

        Assert.Equal("ab", field.Data);
        Assert.Equal("**", field.VisibleText);
    }

    [Fact]
    public void ATypeWriterRevealsOneCharacterEveryThreeTicks()
    {
        TypeWriterBoxTask typer = new() { Text = "abc" };

        // The first character is immediate — the countdown starts at zero — and each one after it
        // waits three ticks, which is what TypeWriterBox_Update reloads +108 with.
        typer.Advance();
        Assert.Equal("a", typer.VisibleText);

        typer.Advance();
        typer.Advance();
        typer.Advance();
        Assert.Equal("a", typer.VisibleText);

        typer.Advance();
        Assert.Equal("ab", typer.VisibleText);
    }

    [Fact]
    public void AFieldIsModalAndTakesTheKeysWhileEditing()
    {
        InputBoxTask field = new() { StringLength = 0 };

        Assert.False(field.IsEditing);
        field.BeginEditing();
        Assert.True(field.IsEditing);

        field.TypeCharacter('a');
        field.TypeCharacter('b');
        field.Edit(MenuInputEvent.Left);
        field.TypeCharacter('X');

        // The caret moved, so the insert went in the middle rather than at the end.
        Assert.Equal("aXb", field.Data);

        field.Edit(MenuInputEvent.Home);
        Assert.Equal(0, field.Caret);
        field.Edit(MenuInputEvent.End);
        Assert.Equal(3, field.Caret);

        field.Edit(MenuInputEvent.Enter);
        Assert.False(field.IsEditing);
    }

    [Fact]
    public void AFieldRefusesTheOneCharacterThatWouldBreakTheSaveFile()
    {
        InputBoxTask field = new();
        field.BeginEditing();

        // A double quote ends up inside a string literal in config.qvm; the original will not take one.
        Assert.False(field.TypeCharacter('"'));
        Assert.True(field.TypeCharacter('a'));
        Assert.Equal("a", field.Data);
    }

    [Fact]
    public void AFieldHoldsOneLessThanItsDeclaredMaximum()
    {
        InputBoxTask field = new() { MaxLength = 5 };
        field.BeginEditing();

        for (int i = 0; i < 20; i++)
        {
            field.TypeCharacter('x');
        }

        // The check is length < max - 1, so a field declaring five holds four.
        Assert.Equal(4, field.Data.Length);
    }

    private static (int, int, int, int) Rect(MenuWindowTask w) =>
        (w.LayoutX, w.LayoutY, w.LayoutWidth, w.LayoutHeight);
}

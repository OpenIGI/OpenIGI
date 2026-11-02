using OpenIGI.UI.Layout;
using Xunit;

namespace OpenIGI.UI.Tests;

/// <summary>Popups open on their preferred side, flip to the other near an edge, and never leave the screen.</summary>
public sealed class PopupLayoutTests
{
    private static readonly UiVec2 Screen = new(800f, 600f);
    private static readonly UiVec2 Menu = new(200f, 150f);

    /// <summary>With room, each placement takes its own side.</summary>
    [Fact]
    public void With_room_each_placement_takes_its_side()
    {
        UiRect row = new(100f, 100f, 120f, 20f);
        Assert.Equal(new UiRect(100f, 120f, 200f, 150f), PopupLayout.Place(row, PopupPlacement.Below, Menu, Screen));
        Assert.Equal(new UiRect(220f, 100f, 200f, 150f), PopupLayout.Place(row, PopupPlacement.Right, Menu, Screen));
        Assert.Equal(new UiRect(100f, 100f, 200f, 150f), PopupLayout.Place(new UiRect(100f, 100f, 0f, 0f), PopupPlacement.At, Menu, Screen));
    }

    /// <summary>A context menu near the bottom-right corner opens above and to the left of the pointer.</summary>
    [Fact]
    public void A_menu_at_the_pointer_flips_at_the_corner()
    {
        UiRect frame = PopupLayout.Place(new UiRect(750f, 580f, 0f, 0f), PopupPlacement.At, Menu, Screen);
        Assert.Equal(new UiRect(550f, 430f, 200f, 150f), frame);
    }

    /// <summary>A submenu with no room on the right opens on the left of its row; a list with no room below opens above its field.</summary>
    [Fact]
    public void Submenus_and_lists_change_sides()
    {
        UiRect row = new(500f, 100f, 200f, 20f);
        Assert.Equal(300f, PopupLayout.Place(row, PopupPlacement.Right, Menu, Screen).X);

        UiRect field = new(700f, 560f, 120f, 20f);
        UiRect list = PopupLayout.Place(field, PopupPlacement.Below, Menu, Screen);
        Assert.Equal(410f, list.Y);
        Assert.Equal(600f, list.X);
    }

    /// <summary>A popup larger than the screen is cut to it and kept on it.</summary>
    [Fact]
    public void A_popup_never_leaves_the_screen()
    {
        UiRect frame = PopupLayout.Place(new UiRect(10f, 10f, 0f, 0f), PopupPlacement.At, new UiVec2(900f, 700f), Screen);
        Assert.Equal(new UiRect(0f, 0f, 800f, 600f), frame);
    }
}

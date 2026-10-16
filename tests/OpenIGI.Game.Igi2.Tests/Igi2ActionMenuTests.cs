using OpenIGI.Game.Igi2.Player;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>The status screen's action menu as <c>actionmenuscreen.c</c> keeps it.</summary>
public sealed class Igi2ActionMenuTests
{
    /// <summary>A thing offered is first only after the second update, as each update shows the order the one before worked out.</summary>
    [Fact]
    public void A_new_offer_is_first_two_updates_on()
    {
        Igi2ActionMenu menu = new();
        object door = new();
        menu.Register(door, null);
        menu.Update();
        Assert.False(menu.IsFirst(door, null));

        menu.Register(door, null);
        menu.Update();
        Assert.True(menu.IsFirst(door, null));

        // Not offered again, it is still shown for the tick after, then gone.
        menu.Update();
        Assert.True(menu.IsFirst(door, null));
        menu.Update();
        Assert.False(menu.IsFirst(door, null));
    }

    /// <summary>The zoom keys rotate the order: forward sends the first to the end, back brings the last to the front.</summary>
    [Fact]
    public void A_scroll_rotates_the_order()
    {
        Igi2ActionMenu menu = new();
        object a = new(), b = new(), c = new();
        for (int tick = 0; tick < 2; tick++)
        {
            menu.Register(a, null);
            menu.Register(b, null);
            menu.Register(c, null);
            menu.Update();
        }

        Assert.True(menu.IsFirst(a, null));
        menu.RequestScroll(1);
        menu.Register(a, null);
        menu.Register(b, null);
        menu.Register(c, null);
        menu.Update();
        Assert.True(menu.IsFirst(b, null));
        Assert.Equal([b, c, a], menu.Items.Select(static i => i.Item));

        menu.RequestScroll(-1);
        menu.Register(a, null);
        menu.Register(b, null);
        menu.Register(c, null);
        menu.Update();
        Assert.True(menu.IsFirst(a, null));
    }

    /// <summary>While hidden (an activation renews ten ticks of it) nothing is first; the same thing with another context is another item.</summary>
    [Fact]
    public void Hidden_nothing_is_first_and_contexts_tell_items_apart()
    {
        Igi2ActionMenu menu = new();
        object pickup = new(), mp5 = new(), uzi = new();
        for (int tick = 0; tick < 2; tick++)
        {
            menu.Register(pickup, mp5);
            menu.Register(pickup, uzi);
            menu.Update();
        }

        Assert.True(menu.IsFirst(pickup, mp5));
        Assert.False(menu.IsFirst(pickup, uzi));
        Assert.False(menu.IsFirst(pickup, null));

        menu.SetNoDrawTimeout(2);
        Assert.False(menu.IsFirst(pickup, mp5));
    }
}

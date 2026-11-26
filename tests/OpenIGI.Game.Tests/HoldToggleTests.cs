using OpenIGI.Game.Player;
using Xunit;

namespace OpenIGI.Game.Tests;

public sealed class HoldToggleTests
{
    [Fact]
    public void Held_it_passes_the_key_through_toggled_each_press_flips_it()
    {
        HoldToggle held = new();
        Assert.True(held.Apply(true, toggles: false));
        Assert.False(held.Apply(false, toggles: false));

        HoldToggle toggle = new();
        Assert.True(toggle.Apply(true, toggles: true));
        Assert.True(toggle.Apply(true, toggles: true));
        Assert.True(toggle.Apply(false, toggles: true));
        Assert.False(toggle.Apply(true, toggles: true));
        Assert.False(toggle.Apply(false, toggles: true));
    }
}

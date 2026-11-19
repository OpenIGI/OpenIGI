using OpenIGI.Game.Igi2.Humans;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>The shared cheats as I.G.I. 2's damage takes them.</summary>
public sealed class Igi2CheatTests
{
    [Fact]
    public void InfiniteHealthTakesNoDamageAndTheHealthScaleDividesIt()
    {
        Igi2HumanDamage damage = new() { MaxDamage = 100f, IsUnharmed = true };
        damage.Add(Igi2DamagePart.Chest, 500f);
        Assert.Equal(0f, damage.Damage);
        Assert.False(damage.IsFatal);

        damage.IsUnharmed = false;
        damage.Toughness = 4f;
        damage.Add(Igi2DamagePart.Chest, 40f);
        Assert.Equal(10f, damage.Damage);
        Assert.Equal(10f, damage.PartDamage(Igi2DamagePart.Chest));
    }
}

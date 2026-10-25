using OpenIGI.Game.Weapons;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>The carry limits <c>DefineHumanPlayerAmmoLimit</c> puts on the player's ammunition.</summary>
public sealed class AmmoPoolLimitTests
{
    [Fact]
    public void AnAddIsHeldToTheLimit()
    {
        // 0x47CE00: whatever is added, the count stays within 0..limit once the type has one.
        AmmoPool pool = new();
        pool.SetLimit(3, 32);
        pool.Add(3, 20);
        pool.Add(3, 20);

        Assert.Equal(32, pool.Count(3));
        Assert.Equal(32, pool.Limit(3));
    }

    [Fact]
    public void SettingALimitCutsDownWhatIsCarried()
    {
        // 0x47CE70 clamps the count it finds as well.
        AmmoPool pool = new();
        pool.Add(5, 300);
        pool.SetLimit(5, 200);

        Assert.Equal(200, pool.Count(5));
    }

    [Fact]
    public void ATypeWithoutALimitCarriesAnyAmount()
    {
        AmmoPool pool = new();
        pool.Add(1, 5000);
        pool.SetLimit(2, 10);

        Assert.Equal(5000, pool.Count(1));
        Assert.Equal(-1, pool.Limit(1));
    }
}

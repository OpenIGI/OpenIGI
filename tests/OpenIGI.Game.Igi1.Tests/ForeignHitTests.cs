using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using Xunit;

namespace OpenIGI.Game.Igi1.Tests;

/// <summary>Another game's round and blast, read in this game's terms through the engine's shared events.</summary>
public sealed class ForeignHitTests
{
    [Fact]
    public void Another_games_round_is_cast_at_the_body_it_struck()
    {
        // A round fired level along +Y, struck at chest height a metre in front of a body facing it.
        Vector3d feet = new(0, 0, 0);
        Vector3d struck = new(0, -100, 2500);
        WeaponHit round = new(struck, default, 0.25f, new Vector3f(0f, 40000f, 0f), null, new object(), WeaponCategory.Pistol, Bone: 7);
        HumanHit met = Igi1WeaponHit.Meet(round, feet, new Vector3f(0f, -1f, 0f));
        Assert.InRange(met.Position.Z, 0.0, HumanHitTest.BodyHeightInUnits);

        Igi1WeaponHit hit = Igi1WeaponHit.FromShared(round, met);
        Assert.Equal(struck, hit.End);
        Assert.Equal(new Vector3d(0, -40100, 2500), hit.Origin);
        Assert.Equal(0.25f, hit.DamageFactor);
        Assert.Equal(WeaponCategory.Pistol, hit.Category);
        Assert.False(hit.FromPlayer);
        Assert.Null(hit.Shooter);
    }

    [Fact]
    public void A_round_whose_line_misses_the_body_lands_where_it_struck_in_the_default_zone()
    {
        Vector3d struck = new(90000, 0, 2500);
        WeaponHit round = new(struck, default, 0.25f, new Vector3f(0f, 40000f, 0f), null, new object());
        HumanHit met = Igi1WeaponHit.Meet(round, default, new Vector3f(0f, 1f, 0f));
        Assert.Equal(struck, met.Position);
        Assert.Equal(HumanDamage.DefaultZone, met.Zone);
    }

    [Fact]
    public void Another_games_blast_keeps_its_ranges_and_damage()
    {
        Igi1ExplosionHit blast = Igi1ExplosionHit.FromShared(new ExplosionHit(new Vector3d(1, 2, 3), 2, 0.5f, 4096f, 8192f, null, new object()));
        Assert.Equal(new Igi1ExplosionHit(new Vector3d(1, 2, 3), 4096f, 8192f, 0.5f, WeaponCategory.Grenade), blast);
    }
}

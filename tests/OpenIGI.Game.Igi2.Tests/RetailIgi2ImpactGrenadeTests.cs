using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Player;
using OpenIGI.TestSupport;
using Xunit;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>The M16's grenade launcher, <c>ImpactGrenade</c>: a grenade that flies and bursts on what it strikes.</summary>
public sealed class RetailIgi2ImpactGrenadeTests
{
    /// <summary>The M16A2's second fire type launches an impact grenade drawn as <c>141_01_1</c>.</summary>
    [RequiresTitleDataFact("igi2")]
    public void The_m16s_launcher_fires_impact_grenades()
    {
        Igi2HumanWeapons weapons = Igi2Session.Start().Require<Igi2HumanWeapons>();
        Igi2WeaponTypeTask m16 = weapons.Tables.WeaponTypes.Single(static t => t.WeaponId == "WEAPON_ID_M16A2");
        GameTask launcher = m16.FireType(1)!;

        Assert.NotEqual(Igi2WeaponConstants.NoTaskType, weapons.FireTaskTypes.ImpactGrenade);
        Assert.Equal(weapons.FireTaskTypes.ImpactGrenade, Assert.IsType<Igi2ProjectileLauncherTask>(launcher).ProjectileQTaskType);
        Assert.Equal("141_01_1", Igi2ImpactGrenadeParameters.Of(launcher).Model);
    }

    /// <summary>
    /// Fired straight down from four metres above mission 1's start, the grenade falls, trails smoke, and bursts on the ground:
    /// one explosion goes off where it struck and the grenade is gone.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void An_impact_grenade_bursts_on_the_ground()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load(collision: true);
        Assert.True(PlayerSpawn.TryFindGameplay(m.Level.Root, out PlayerSpawnPoint spawn));
        DynamicTasks dynamic = new();
        Igi2ExplosionWorld explosions = new(new IgiRandom(0x1234567u), m.Collision!, m.Materials, dynamic);
        Igi2HumanWeapons weapons = Igi2Session.Start().Require<Igi2HumanWeapons>();
        Igi2WeaponTypeTask m16 = weapons.Tables.WeaponTypes.Single(static t => t.WeaponId == "WEAPON_ID_M16A2");
        Vector3d above = new(spawn.Position.X, spawn.Position.Y, spawn.Position.Z + 16384.0);
        float[] down = [1f, 0f, 0f, 0f, 0f, 1f, 0f, -1f, 0f];
        Igi2ProjectileLaunch launch = new(weapons.FireTaskTypes.ImpactGrenade, above, down, default, 30f, 0f, 0, m16, null);
        Igi2ImpactGrenade grenade = new(launch, Igi2ImpactGrenadeParameters.Of(m16.FireType(1)!), explosions, m.Collision!);

        int ticks = 0;
        while (grenade.Run())
        {
            Assert.True(++ticks < 120, "the grenade never struck anything.");
            Assert.True(grenade.Speed.Z < 0f);
        }

        Igi2Explosion blast = Assert.Single(dynamic.Tasks.OfType<Igi2Explosion>());
        Assert.True(blast.Request.Position.Z < above.Z);
        Assert.Equal(Igi2ImpactGrenade.BlastSound, blast.Request.Sound);
    }
}

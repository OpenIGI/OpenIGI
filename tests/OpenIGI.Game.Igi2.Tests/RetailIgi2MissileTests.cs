using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Physics;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Player;
using OpenIGI.TestSupport;
using Xunit;
using OpenIGI.Game.Physics;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>The rocket launchers' missiles, <c>Missile</c>: pushed by their motors, steadied by their fins, set off by what they touch.</summary>
public sealed class RetailIgi2MissileTests
{
    /// <summary>
    /// The RPG-7 fires a missile; aimed down from twenty metres over mission 1's start, it flies, touches the ground and goes off
    /// there, its blast <c>explo_03_l</c>.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void An_rpg_missile_goes_off_on_the_ground()
    {
        RetailIgi2Mission1 m = RetailIgi2Mission1.Load(collision: true);
        Assert.True(PlayerSpawn.TryFindGameplay(m.Level.Root, out PlayerSpawnPoint spawn));
        Igi2Session session = Igi2Session.Start();
        Igi2HumanWeapons weapons = session.Require<Igi2HumanWeapons>();
        Igi2WeaponTypeTask rpg = weapons.Tables.WeaponTypes.Single(static t => t.WeaponId == "WEAPON_ID_RPG7");
        GameTask fire = Enumerable.Range(0, Igi2WeaponTypeTask.FireTypeSlots).Select(rpg.FireType).OfType<GameTask>().First();

        DynamicTasks dynamic = new();
        Igi2ExplosionWorld explosions = new(new IgiRandom(0x1234567u), m.Collision!, m.Materials, dynamic);
        Igi2PhysicsWorld world = new(m.Collision!, m.Materials);
        Igi2Grenades launcher = new(session.Require<PhysicsObjectTypes>(), m.Level.ModelFor, world, dynamic) { Explosions = explosions };
        Vector3d above = new(spawn.Position.X, spawn.Position.Y, spawn.Position.Z + 81920.0);
        float[] down = [1f, 0f, 0f, 0f, 0f, 1f, 0f, -1f, 0f];
        launcher.LaunchMissile(new Igi2ProjectileLaunch(weapons.FireTaskTypes.Missile, above, down, default, 30f, 0f, 0, rpg, null), fire);

        Igi2Missile missile = Assert.IsType<Igi2Missile>(Assert.Single(dynamic.Tasks));
        int ticks = 0;
        while (missile.Run())
        {
            Assert.True(++ticks < 300, $"the missile never touched anything; it is at {missile.Position}.");
        }

        Assert.True(missile.Position.Z < above.Z);
        Igi2Explosion blast = Assert.Single(dynamic.Tasks.OfType<Igi2Explosion>());
        Assert.Equal(Igi2Missile.BlastSound, blast.Request.Sound);
        Assert.Equal(5f, blast.Request.Damage);
    }
}

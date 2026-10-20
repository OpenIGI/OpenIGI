using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Physics;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;
using OpenIGI.Game.Physics;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>
/// A thrown grenade's flight: the physics object types <c>physicsobj.qvm</c> defines (<c>PhysicsObjType_DefinePhysicsObjTypeCB</c>,
/// <c>igi2 0x43B900</c>), the rigid body step (<c>Physics_Update</c>, <c>0x5A9580</c>) and the grenade flying through mission 1
/// (<c>Grenade_RunHandler</c>, <c>0x484260</c>).
/// </summary>
public sealed class RetailIgi2GrenadeFlightTests
{
    private const string Grenade = "WEAPON_ID_GRENADE";

    private static readonly Lazy<Mission1Setup> Mission1 = new(static () =>
    {
        Igi2Level level = Igi2Level.Find("location1", "level1");
        Igi2Session session = Igi2Session.Start();
        Igi2CollisionWorld world = Igi2CollisionWorld.Build(level.Scene, level.ModelFor, level.Terrain(), "terrain");
        return new Mission1Setup(level, session, world);
    });

    /// <summary>
    /// The hand grenade's model has its type: a grenade of three tenths of a unit of mass in a box of ten by ten by fifteen
    /// centimetres, five contact points on the model's first five magic vertices, each bouncing back three tenths with a
    /// friction of five, at rest under three units a tick, and exploding as an ordinary grenade.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void The_hand_grenade_has_its_physics_type()
    {
        PhysicsObjectTypes types = Mission1.Value.Session.Require<PhysicsObjectTypes>();
        Igi2WeaponTypeTask weapon = WeaponType();

        Assert.True(types.TryFind(weapon.FixedModel, out PhysicsObjectType type));
        Assert.True(types.IsMovedBy(type, "Grenade"));
        Assert.Equal(0.3f, type.Mass);
        Assert.Equal(new Vector3f(0.1f, 0.1f, 0.15f), type.Size);
        Assert.Equal([0, 1, 2, 3, 4], type.ContactPoints.Select(static p => p.MagicVertex));
        Assert.All(type.ContactPoints, static p =>
        {
            Assert.Equal(6, p.Material);
            Assert.Equal(0.3f, p.Bounce);
            Assert.Equal(5f, p.FrictionY);
        });
        Assert.Equal(3f, type.RestSpeed);
        Assert.Equal(0.016f, type.RestAngularVelocitySquared);
        Assert.Equal(Igi2GrenadeKind.Normal, type.Parameters[1].ToInt32());
        Assert.True(types.Types.Count > 10);
    }

    /// <summary>
    /// A body falls by the gravity each tick, and a spinning one keeps an orthonormal orientation as it turns, its spin
    /// damped by what the update keeps of it.
    /// </summary>
    [Fact]
    public void A_body_falls_and_turns_true()
    {
        Igi2RigidBody body = new();
        body.InitBox(0.3f, 409.6f, 409.6f, 614.4f);
        float[] m = [1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f];
        body.AddForce(new PointForce(new Vector3f(0f, 0f, -(0.3f * Igi2Grenade.Gravity)), default));
        Assert.False(body.Update(m));
        Assert.Equal(-Igi2Grenade.Gravity, body.Velocity.Z, 3);
        Assert.Equal(Igi2Grenade.Gravity, body.Speed, 3);

        body.AngularMomentum = new Vector3f(1000f, 2000f, 500f);
        for (int i = 0; i < 60; i++)
        {
            Assert.True(body.Update(m));
        }

        for (int r = 0; r < 3; r++)
        {
            Assert.Equal(1.0, (m[3 * r] * m[3 * r]) + (m[(3 * r) + 1] * m[(3 * r) + 1]) + (m[(3 * r) + 2] * m[(3 * r) + 2]), 4);
        }

        Assert.Equal(0.0, (m[0] * m[3]) + (m[1] * m[4]) + (m[2] * m[5]), 4);
    }

    /// <summary>
    /// Thrown from mission 1's start up and forward, the grenade rises, falls back to the ground, bounces and settles,
    /// sounding each bounce and telling the AI of it as of itself every fifth tick of its fuse; no contact point ends a tick deeper in the ground than one tick's fall, which the next lifts out. When its fuse runs out
    /// it explodes where it lies, with the launcher's damage and its radius and falloff in units, and is gone.
    /// </summary>
    [RequiresTitleDataFact("igi2")]
    public void A_thrown_grenade_lands_and_explodes()
    {
        Mission1Setup m = Mission1.Value;
        Assert.True(PlayerSpawn.TryFindGameplay(m.Level.Root, out PlayerSpawnPoint spawn));
        Igi2WeaponTypeTask weapon = WeaponType();
        Igi2GrenadeParameters parameters = Igi2GrenadeParameters.Of(Assert.IsType<Igi2ProjectileLauncherTask>(weapon.FireType(0)));
        Igi2PhysicsWorld world = new(m.World, m.Session.Require<Igi2GameMaterials>());
        Igi2DynamicTasks dynamic = new();
        Igi2Grenades grenades = new(m.Session.Require<PhysicsObjectTypes>(), m.Level.ModelFor, world, dynamic);
        Recording effects = new();
        grenades.Effects = effects;

        Vector3d start = new(spawn.Position.X, spawn.Position.Y, spawn.Position.Z + 6000.0);
        grenades.Launch(new Igi2GrenadeThrow(start, [1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f], new Vector3f(1500f, 0f, 1500f), 150, weapon, parameters, null));
        Igi2Grenade flying = Assert.Single(grenades.Flying);
        Assert.NotNull(flying.Body);
        Assert.Equal(5, flying.Body.ContactPoints.Count(static p => p.IsUsed));

        List<Vector3d> path = [];
        double lowest = double.PositiveInfinity;
        while (grenades.Flying.Any())
        {
            Igi2PhysicsObject body = grenades.Flying.First().Body!;
            path.Add(body.Position);
            lowest = Math.Min(lowest, LowestPointAboveGround(m.World, body));
            dynamic.Run();
        }

        Assert.Equal(151, path.Count);
        double peak = path.Max(static p => p.Z);
        Assert.True(peak > start.Z);
        Assert.True(path[^1].Z < peak);
        Assert.True(lowest >= -(Igi2Grenade.Gravity + 1.0), $"a contact point went {-lowest} units into the ground");
        Assert.True(path[^1].Z - Ground(m.World, path[^1]) < 512.0);
        Assert.True(Distance(path[^1], path[^10]) < 256.0);

        Assert.Equal(30, effects.AiEvents.Count(static e => e == (Igi2Grenade.FlightEvent, 1, -1f)));
        Assert.Contains(Igi2Grenade.BounceSound, effects.Sounds);
        Assert.Equal(
            effects.Sounds.Count(static s => s == Igi2Grenade.BounceSound),
            effects.AiEvents.Count(static e => e == (Igi2Grenade.BounceEvent, 2, Igi2Grenade.BounceRange)));

        Igi2GrenadeExplosion explosion = Assert.Single(effects.Explosions);
        Assert.Equal(path[^1], explosion.Position);
        Assert.Equal(4f, explosion.DamageFactor);
        Assert.Equal(16384f, explosion.FullRange);
        Assert.Equal(40960f, explosion.FalloffRange);
        Assert.True(explosion.Ground.IsHit);
    }

    private static Igi2WeaponTypeTask WeaponType() =>
        Mission1.Value.Session.Require<Igi2HumanWeapons>().Tables.WeaponTypes.First(static w => w.WeaponId == Grenade);

    private static double Ground(Igi2CollisionWorld world, Vector3d at)
    {
        Igi2LineTrace probe = new(1);
        probe.Starts[0] = new Vector3d(at.X, at.Y, at.Z + 4096.0);
        world.NextZ(probe, [-65536.0], 0xFFFFFFFF);
        return probe.Intersections[0].IsHit ? probe.Intersections[0].Position.Z : double.NegativeInfinity;
    }

    /// <summary>How far above the ground under it the lowest of the object's contact points is.</summary>
    private static double LowestPointAboveGround(Igi2CollisionWorld world, Igi2PhysicsObject body)
    {
        float[] o = body.Orientation;
        Vector3d c = body.Position;
        double lowest = double.PositiveInfinity;
        foreach (Igi2PhysicsContactPoint point in body.ContactPoints.Where(static p => p.IsUsed))
        {
            Vector3f q = point.LocalPosition;
            Vector3d w = new(
                c.X + (o[0] * q.X) + (o[1] * q.Y) + (o[2] * q.Z),
                c.Y + (o[3] * q.X) + (o[4] * q.Y) + (o[5] * q.Z),
                c.Z + (o[6] * q.X) + (o[7] * q.Y) + (o[8] * q.Z));
            lowest = Math.Min(lowest, w.Z - Ground(world, w));
        }

        return lowest;
    }

    private static double Distance(Vector3d a, Vector3d b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
        return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }

    private sealed class Recording : IIgi2GrenadeEffects
    {
        public List<Igi2GrenadeExplosion> Explosions { get; } = [];

        public List<string> Sounds { get; } = [];

        public List<(int Event, int Flags, float Range)> AiEvents { get; } = [];

        public void Explode(in Igi2GrenadeExplosion explosion) => Explosions.Add(explosion);

        public void Sound(string sound, Vector3d position) => Sounds.Add(sound);

        public void AiEvent(int aiEvent, int flags, Vector3d position, float range) => AiEvents.Add((aiEvent, flags, range));
    }

    private sealed record Mission1Setup(Igi2Level Level, Igi2Session Session, Igi2CollisionWorld World);
}

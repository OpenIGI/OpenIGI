using OpenIGI.Core.Numerics;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>The grenade: the read constants, the flight, the bounce and the fuse.</summary>
public sealed class GrenadeSimulationTests
{
    /// <summary>The physics rows and the task init agree on the numbers this port carries.</summary>
    [Fact]
    public void TheReadConstantsAreTheRetailOnes()
    {
        // physicsobj.qvm's TASKTYPE_GRENADE rows: 0.3 kg, 0.3 elasticity, 0.15 m half-height, and a
        // trailing explode time of 4 seconds that 0x477040 turns into ticks at thirty.
        Assert.Equal(0.3f, GrenadePhysics.MassKilograms);
        Assert.Equal(0.3f, GrenadePhysics.Restitution);
        Assert.Equal(614.4f, GrenadePhysics.HalfHeightUnits, 0.01f);
        Assert.Equal(120, GrenadePhysics.FuseTicks);
    }

    [Fact]
    public void AThrownGrenadeQueuesItsNativeTakeoffNotification()
    {
        GrenadeSimulation simulation = new();
        Vector3d origin = new(100, 200, 300);

        Assert.True(simulation.Spawn(
            origin,
            new Vector3f(0f, 1f, 0f),
            15f,
            GrenadeExplosionType.Flashbang));
        Assert.Equal(new[] { origin }, simulation.Thrown);

        simulation.ClearThrownEvents();
        Assert.Empty(simulation.Thrown);
    }

    /// <summary>A grenade detonates when its fuse runs out, with the shipped blast numbers.</summary>
    [Fact]
    public void TheFuseDetonatesOnTime()
    {
        GrenadeSimulation simulation = new();
        Assert.True(simulation.Spawn(default, new Vector3f(0f, 1f, 0f), 15f, GrenadeExplosionType.Normal));

        for (int tick = 0; tick < GrenadePhysics.FuseTicks - 1; tick++)
        {
            simulation.Tick(walls: null);
            Assert.Empty(simulation.Detonations);
        }

        simulation.Tick(walls: null);
        GrenadeDetonation blast = Assert.Single(simulation.Detonations);
        Assert.Equal(GrenadeExplosionType.Normal, blast.Type);
        Assert.Equal(1.3f, blast.DamageFactor);
        Assert.Equal(7f * 4096f, blast.RadiusUnits);
        Assert.Equal(4f * 4096f, blast.FalloffUnits);
        Assert.Empty(simulation.Grenades);
    }

    /// <summary>Thrown at fifteen metres a second, it flies and falls under the object gravity.</summary>
    [Fact]
    public void GravityBendsTheThrow()
    {
        GrenadeSimulation simulation = new();
        simulation.Spawn(default, new Vector3f(0f, 1f, 0f), 15f, GrenadeExplosionType.Normal);
        LiveGrenade grenade = simulation.Grenades[0];

        simulation.Tick(walls: null);
        float perTick = 15f * 4096f / 30f;
        Assert.Equal(perTick, grenade.Position.Y, 0.01);
        Assert.Equal(-GrenadePhysics.GravityPerTick, grenade.Position.Z, 0.01);

        // Thirty ticks on: a second of gravity has accumulated 30(31)/2 gravities of drop.
        for (int tick = 0; tick < 29; tick++)
        {
            simulation.Tick(walls: null);
        }

        Assert.Equal(30f * perTick, grenade.Position.Y, 0.1);
        Assert.Equal(-GrenadePhysics.GravityPerTick * 30f * 31f / 2f, grenade.Position.Z, 0.5);
    }

    /// <summary>A floor bounce keeps 0.3 of the vertical speed and comes back up.</summary>
    [Fact]
    public void AFloorBouncesAtTheReadElasticity()
    {
        GrenadeSimulation simulation = new();

        // Falling straight down onto a floor at z = 0, from just above it, fast.
        simulation.SpawnWithVelocity(
            new Vector3d(0, 0, 1000), new Vector3f(0f, 0f, -2000f), GrenadeExplosionType.Normal);
        LiveGrenade grenade = simulation.Grenades[0];

        static bool Floor(Vector3d origin, Vector3d end, out WorldRayHit hit)
        {
            if (origin.Z > 0.0 && end.Z <= 0.0)
            {
                hit = new WorldRayHit(new Vector3d(end.X, end.Y, 0), new Vector3f(0f, 0f, 1f), origin.Z);
                return true;
            }

            hit = default;
            return false;
        }

        simulation.Tick(Floor);
        Assert.True(grenade.Velocity.Z > 0f);

        // In: 2000 down plus one tick of gravity; out: that times 0.3 elasticity times the
        // tangential keep this port applies across the whole reflection.
        float impactSpeed = 2000f + GrenadePhysics.GravityPerTick;
        Assert.Equal(
            impactSpeed * GrenadePhysics.Restitution * GrenadePhysics.TangentialKeep,
            grenade.Velocity.Z,
            0.5f);
    }

    /// <summary>A bounce is reported once and then debounced, as <c>0x00476CC0</c> flags it.</summary>
    [Fact]
    public void ABounceIsReportedOnceAndDebounced()
    {
        GrenadeSimulation simulation = new();
        simulation.SpawnWithVelocity(
            new Vector3d(0, 0, 1000), new Vector3f(0f, 0f, -2000f), GrenadeExplosionType.Normal);
        LiveGrenade grenade = simulation.Grenades[0];

        static bool Floor(Vector3d origin, Vector3d end, out WorldRayHit hit)
        {
            if (origin.Z > 0.0 && end.Z <= 0.0)
            {
                hit = new WorldRayHit(new Vector3d(end.X, end.Y, 0), new Vector3f(0f, 0f, 1f), origin.Z);
                return true;
            }

            hit = default;
            return false;
        }

        simulation.Tick(Floor);
        Assert.Single(simulation.Bounces);
        Assert.True(grenade.BounceSounded);

        // It rebounded upward and is out of contact next tick, so nothing new is reported.
        simulation.Tick(Floor);
        Assert.Empty(simulation.Bounces);
    }

    /// <summary>Slow on a floor, it stops simulating and waits on its fuse.</summary>
    [Fact]
    public void ASlowGrenadeComesToRest()
    {
        GrenadeSimulation simulation = new();
        simulation.SpawnWithVelocity(
            new Vector3d(0, 0, 100), new Vector3f(0f, 0f, -20f), GrenadeExplosionType.Flashbang);
        LiveGrenade grenade = simulation.Grenades[0];

        static bool Floor(Vector3d origin, Vector3d end, out WorldRayHit hit)
        {
            if (origin.Z > 0.0 && end.Z <= 0.0)
            {
                hit = new WorldRayHit(new Vector3d(end.X, end.Y, 0), new Vector3f(0f, 0f, 1f), origin.Z);
                return true;
            }

            hit = default;
            return false;
        }

        for (int tick = 0; tick < 10 && !grenade.Resting; tick++)
        {
            simulation.Tick(Floor);
        }

        Assert.True(grenade.Resting);
        Assert.Equal(default, grenade.Velocity);

        Matrix3f landed = grenade.Orientation;
        for (int tick = 0; tick < 10; tick++)
        {
            simulation.Tick(Floor);
            Assert.Equal(landed, grenade.Orientation);
            Assert.True(grenade.SurfaceContact);
        }
    }

    [Theory]
    [InlineData(GrenadeExplosionType.Normal)]
    [InlineData(GrenadeExplosionType.Flashbang)]
    public void ConsecutiveFloorContactsKeepTheFirstLandingPoseWhileTheBodySettles(
        GrenadeExplosionType type)
    {
        GrenadeSimulation simulation = new();
        simulation.SpawnWithVelocity(
            new Vector3d(0, 0, GrenadePhysics.HalfHeightUnits + 100f),
            new Vector3f(1000f, 0f, -20f),
            type);
        LiveGrenade grenade = simulation.Grenades[0];

        static bool Floor(Vector3d origin, Vector3d end, out WorldRayHit hit)
        {
            if (origin.Z > GrenadePhysics.HalfHeightUnits
                && end.Z <= GrenadePhysics.HalfHeightUnits)
            {
                hit = new WorldRayHit(
                    new Vector3d(end.X, end.Y, 0),
                    new Vector3f(0f, 0f, 1f),
                    origin.Z);
                return true;
            }

            hit = default;
            return false;
        }

        Matrix3f landed = default;
        bool sawContact = false;
        for (int tick = 0; tick < 20; tick++)
        {
            simulation.Tick(Floor);
            if (!grenade.SurfaceContact)
            {
                continue;
            }

            if (!sawContact)
            {
                landed = grenade.Orientation;
                sawContact = true;
            }

            Assert.Equal(landed, grenade.Orientation);
            Assert.Equal(
                landed,
                GrenadePhysics.PresentationOrientation(grenade, alpha: 0.75f));
        }

        Assert.True(sawContact);
    }

    [Theory]
    [InlineData(GrenadeExplosionType.Normal)]
    [InlineData(GrenadeExplosionType.Flashbang)]
    public void AFirstLandingKeepsItsPresentationPoseAfterTheBodyLeavesTheContactTick(
        GrenadeExplosionType type)
    {
        GrenadeSimulation simulation = new();
        simulation.SpawnWithVelocity(
            new Vector3d(0, 0, GrenadePhysics.HalfHeightUnits + 100f),
            new Vector3f(0f, 0f, -2000f),
            type);
        LiveGrenade grenade = Assert.Single(simulation.Grenades);

        static bool Floor(Vector3d origin, Vector3d end, out WorldRayHit hit)
        {
            if (origin.Z > GrenadePhysics.HalfHeightUnits
                && end.Z <= GrenadePhysics.HalfHeightUnits)
            {
                hit = new WorldRayHit(
                    new Vector3d(end.X, end.Y, 0),
                    new Vector3f(0f, 0f, 1f),
                    origin.Z);
                return true;
            }

            hit = default;
            return false;
        }

        simulation.Tick(Floor);
        Assert.True(grenade.SurfaceContact);
        Assert.False(grenade.Resting);
        Assert.True(grenade.LandedPoseCaptured);
        Matrix3f landed = GrenadePhysics.PresentationOrientation(grenade, alpha: 0.5f);

        // The impact rebounds, so the next fixed step is no longer reported as contact. Retail keeps
        // the captured landing pose instead of switching the renderer back to a fresh tumble clock.
        simulation.Tick(Floor);
        Assert.False(grenade.SurfaceContact);
        Assert.Equal(landed, GrenadePhysics.PresentationOrientation(grenade, alpha: 0.5f));
    }

    [Fact]
    public void ASettledGrenadeKeepsItsCapturedPoseAfterTheContactFlagIsCleared()
    {
        Matrix3f landed = GrenadePhysics.PlacementOrientation(new Vector3f(0f, -1f, 0f));
        LiveGrenade grenade = new(
            default,
            default,
            GrenadeExplosionType.Flashbang,
            GrenadePhysics.FuseTicks)
        {
            Resting = true,
            SurfaceContact = false,
            Orientation = landed,
            TumbleTicks = 30,
        };

        Assert.Equal(landed, GrenadePhysics.PresentationOrientation(grenade, 0.75f));
    }

    [Fact]
    public void PlacementOrientationUsesTheNativeSecondBasisColumn()
    {
        Matrix3f orientation = GrenadePhysics.PlacementOrientation(new Vector3f(0.25f, -0.5f, 0.75f));
        Vector3f normal = orientation.Column2;

        float length = MathF.Sqrt((0.25f * 0.25f) + (0.5f * 0.5f) + (0.75f * 0.75f));
        Assert.Equal(0.25f / length, normal.X, 5);
        Assert.Equal(-0.5f / length, normal.Y, 5);
        Assert.Equal(0.75f / length, normal.Z, 5);
    }

    [Fact]
    public void AThrownGrenadeUsesItsBoxSupportDistanceAtAContact()
    {
        Assert.Equal(
            GrenadePhysics.HalfHeightUnits,
            GrenadePhysics.SupportExtentUnits(new Vector3f(0f, 0f, 1f)));
        Assert.Equal(
            GrenadePhysics.HalfWidthUnits,
            GrenadePhysics.SupportExtentUnits(new Vector3f(1f, 0f, 0f)));
    }

    /// <summary>The AI's lob formula lands exactly on its target after the chosen fly time.</summary>
    /// <remarks>
    /// <c>v = d/T + g(T+1)/2 ẑ</c> is the closed form for this integrator (<c>v.z -= g</c> then
    /// <c>p += v</c>): after <c>T</c> ticks the gravity term has paid out exactly
    /// <c>g·T(T+1)/2</c>, which is what the launch banked.
    /// </remarks>
    [Fact]
    public void TheAiLobLandsOnItsTarget()
    {
        GrenadeSimulation simulation = new();
        Vector3d origin = new(0, 0, 6000);
        Vector3d target = new(40960, 81920, 0);
        const int flyTicks = 45;

        Vector3f velocity = new(
            (float)((target.X - origin.X) / flyTicks),
            (float)((target.Y - origin.Y) / flyTicks),
            (float)((target.Z - origin.Z) / flyTicks) + (GrenadePhysics.GravityPerTick * (flyTicks + 1) * 0.5f));

        simulation.SpawnWithVelocity(origin, velocity, GrenadeExplosionType.Normal, fuseTicks: flyTicks + 30);
        LiveGrenade grenade = simulation.Grenades[0];

        for (int tick = 0; tick < flyTicks; tick++)
        {
            simulation.Tick(walls: null);
        }

        Assert.Equal(target.X, grenade.Position.X, 1.0);
        Assert.Equal(target.Y, grenade.Position.Y, 1.0);
        Assert.Equal(target.Z, grenade.Position.Z, 2.0);
    }
}

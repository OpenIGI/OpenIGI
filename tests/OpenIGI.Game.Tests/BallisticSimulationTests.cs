using OpenIGI.Core.Numerics;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.Weapons.Ballistics;
using OpenIGI.Game.World;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// The tactical-ballistics option: which weapons fly, how a round drops and slows, and what stops it.
/// </summary>
/// <remarks>
/// All against weapons and surfaces built by hand — the simulation takes the same delegates the
/// hit-scan path does, which is exactly what makes it testable without a level.
/// </remarks>
public sealed class BallisticSimulationTests
{
    /// <summary>300 m/s: 10 metres a tick at 30 Hz, which keeps every expectation legible.</summary>
    private const float TestVelocity = 300f;

    private const float UnitsPerMetre = GunShotBallistics.UnitsPerMetre;
    private const float UnitsPerTick = TestVelocity * UnitsPerMetre / 30f;

    // --- The profile: who flies at all -----------------------------------------------------------

    [Theory]
    [InlineData(WeaponCategory.Knife)]
    [InlineData(WeaponCategory.Grenade)]
    [InlineData(WeaponCategory.Binocular)]
    [InlineData(WeaponCategory.Medipack)]
    [InlineData(WeaponCategory.ProximityMine)]
    public void OnlyGunsFly(WeaponCategory category)
    {
        Assert.Null(BallisticProfile.TryCreate(Weapon(category: category), null));
    }

    /// <summary>The rocket carriers say 5 m/s — their round is the missile task, not a bullet.</summary>
    [Fact]
    public void ARocketCarrierStaysHitScan()
    {
        Assert.Null(BallisticProfile.TryCreate(Weapon(velocity: 5f), null));
    }

    /// <summary>The Dragunov's 6000 m/s is a hit-scan era convenience, and is capped.</summary>
    [Fact]
    public void TheDragunovsVelocityIsCapped()
    {
        BallisticProfile? profile = BallisticProfile.TryCreate(Weapon(velocity: 6000f), null);
        Assert.NotNull(profile);
        Assert.Equal(BallisticProfile.MaximumMetresPerSecond * UnitsPerMetre, profile.Value.UnitsPerSecond);
    }

    /// <summary>The one measured mass in the table is used as shipped; the placeholders are floored.</summary>
    [Fact]
    public void TheMassFloorOnlyRaisesPlaceholders()
    {
        // The 7.62's 12 g is real and sits above a doubled lead sphere of its diameter.
        Assert.Equal(12f, BallisticProfile.MassForDragInGrams(762, 12f));

        // The 12.7's 2 g is a placeholder; a .50 that light would shed all its speed in a hundred
        // metres. The floor hands it a lead slug's mass instead.
        Assert.True(BallisticProfile.MassForDragInGrams(127, 2f) > 20f);
    }

    /// <summary>Heavy-for-its-face carries: the 7.62 out-flies the 9×19 through the same air.</summary>
    [Fact]
    public void DragIsPerCalibre()
    {
        BallisticProfile rifle = BallisticProfile.TryCreate(
            Weapon(calibre: 762), Ammo(762, massInGrams: 12f)) !.Value;
        BallisticProfile pistol = BallisticProfile.TryCreate(
            Weapon(calibre: 919), Ammo(919, massInGrams: 3.5f)) !.Value;

        Assert.True(rifle.DragPerUnit < pistol.DragPerUnit);
        Assert.True(rifle.DragPerUnit > 0f);
    }

    // --- The flight: travel time, drop, drag -----------------------------------------------------

    /// <summary>In a vacuum with no gravity, a round covers exactly its muzzle velocity.</summary>
    [Fact]
    public void ARoundCoversItsVelocityEachTick()
    {
        BallisticSimulation simulation = Spawn(rangeMetres: 300f);
        BallisticRound round = simulation.Rounds[0];

        simulation.Tick(walls: null, humans: null, gravityScale: 0f, dragScale: 0f);
        Assert.Equal(UnitsPerTick, round.Position.Y, 1.0);

        simulation.Tick(walls: null, humans: null, gravityScale: 0f, dragScale: 0f);
        Assert.Equal(2 * UnitsPerTick, round.Position.Y, 1.0);
    }

    /// <summary>And dies exactly at its range, reporting a round that met nothing.</summary>
    [Fact]
    public void ARoundDiesAtItsRange()
    {
        // 300 m of range at 10 m a tick is thirty ticks on the nose.
        BallisticSimulation simulation = Spawn(rangeMetres: 300f);
        BallisticRound round = simulation.Rounds[0];

        for (int tick = 0; tick < 29; tick++)
        {
            simulation.Tick(walls: null, humans: null, gravityScale: 0f, dragScale: 0f);
            Assert.True(round.Alive);
            Assert.Empty(simulation.Impacts);
        }

        simulation.Tick(walls: null, humans: null, gravityScale: 0f, dragScale: 0f);
        Assert.False(round.Alive);

        BallisticImpact impact = Assert.Single(simulation.Impacts);
        Assert.False(impact.StruckHuman);
        Assert.False(impact.StruckWall);
        Assert.Equal(300f * UnitsPerMetre, round.TravelledUnits, 1.0);
    }

    /// <summary>A short authored range is raised to the minimum open-air flight, so far targets are reachable.</summary>
    [Fact]
    public void AShortRangeIsExtendedToTheMinimumFlight()
    {
        // The MP5's authored 80 m is barely a quarter-second of flight; with the default three
        // seconds the round still exists 60 ticks out.
        BallisticSimulation simulation = new();
        WeaponType weapon = Weapon(rangeMetres: 80f);
        Assert.True(simulation.Spawn(
            Projectile(80f), weapon, null, 1f, weapon.Category, null, minimumFlightSeconds: 3f));
        BallisticRound round = simulation.Rounds[0];

        for (int tick = 0; tick < 60; tick++)
        {
            simulation.Tick(walls: null, humans: null, gravityScale: 0f, dragScale: 0f);
        }

        Assert.True(round.Alive);
        Assert.True(round.TravelledUnits > 80f * UnitsPerMetre);
    }

    [Fact]
    public void AProjectileSpeedOverrideControlsItsSpawnVelocity()
    {
        BallisticSimulation simulation = new();
        WeaponType weapon = Weapon(rangeMetres: 300f);
        GunProjectile shot = new(
            default,
            new Vector3f(0f, 1f, 0f),
            300f * UnitsPerMetre,
            SpeedMetresPerSecond: 123f);

        Assert.True(simulation.Spawn(shot, weapon, null, 1f, weapon.Category, null, 0f));
        Assert.Equal(123f * UnitsPerMetre, simulation.Rounds[0].Velocity.Y, 0.01f);
    }

    [Fact]
    public void AProjectileSpeedOverrideControlsMinimumFlightRange()
    {
        BallisticSimulation simulation = new();
        WeaponType weapon = Weapon(rangeMetres: 300f);
        GunProjectile shot = new(
            default,
            new Vector3f(0f, 1f, 0f),
            300f * UnitsPerMetre,
            SpeedMetresPerSecond: 123f);

        Assert.True(simulation.Spawn(shot, weapon, null, 1f, weapon.Category, null, 3f));

        // 123 m/s for three seconds is longer than the authored 300 m range, so the selected
        // launch speed—not the profile's 300 m/s—is observable in the minimum-flight extension.
        Assert.Equal(123f * UnitsPerMetre * 3f, simulation.Rounds[0].RemainingRange, 0.01f);
    }

    /// <summary>One second of flight drops very nearly ½g — the substepped integrator's small excess.</summary>
    [Fact]
    public void GravityDropsTheRound()
    {
        BallisticSimulation simulation = Spawn(rangeMetres: 400f);
        BallisticRound round = simulation.Rounds[0];

        for (int tick = 0; tick < 30; tick++)
        {
            simulation.Tick(walls: null, humans: null, gravityScale: 1f, dragScale: 0f);
        }

        // Semi-implicit Euler over n steps lands on ½gt²(1 + 1/n); at 120 steps that is 4.94 m.
        double expectedDrop = 0.5 * 9.8 * (1.0 + (1.0 / 120.0)) * UnitsPerMetre;
        Assert.Equal(-expectedDrop, round.Position.Z, expectedDrop * 0.01);
    }

    /// <summary>Air takes speed off monotonically; a vacuum takes none.</summary>
    [Fact]
    public void DragSlowsTheRound()
    {
        BallisticSimulation simulation = Spawn(rangeMetres: 2000f, calibre: 919, ammo: Ammo(919, 3.5f));
        BallisticRound round = simulation.Rounds[0];

        double previousSpeed = round.Velocity.Length;
        for (int tick = 0; tick < 10; tick++)
        {
            simulation.Tick(walls: null, humans: null, gravityScale: 0f, dragScale: 1f);
            double speed = round.Velocity.Length;
            Assert.True(speed < previousSpeed, $"tick {tick}: {speed} !< {previousSpeed}");
            previousSpeed = speed;
        }
    }

    // --- The impact: what stops a round, and when ------------------------------------------------

    /// <summary>A round cannot hit what it has not reached — the whole point of the option.</summary>
    [Fact]
    public void ThereIsNoHitScanLeak()
    {
        // A wall 25 m out. At 10 m a tick the round reaches it on its third tick, not its first.
        BallisticSimulation simulation = Spawn(rangeMetres: 300f);

        int wallCasts = 0;
        WorldRayCast walls = (Vector3d origin, Vector3d end, out WorldRayHit hit) =>
        {
            wallCasts++;
            return TryWall(origin, end, 25f * UnitsPerMetre, out hit);
        };

        simulation.Tick(walls, humans: null, gravityScale: 0f, dragScale: 0f);
        simulation.Tick(walls, humans: null, gravityScale: 0f, dragScale: 0f);
        Assert.Empty(simulation.Impacts);
        Assert.True(simulation.Rounds[0].Alive);

        simulation.Tick(walls, humans: null, gravityScale: 0f, dragScale: 0f);
        BallisticImpact impact = Assert.Single(simulation.Impacts);
        Assert.True(impact.StruckWall);
        Assert.Equal(25f * UnitsPerMetre, impact.Wall.Position.Y, 1.0);
        Assert.False(simulation.Rounds[0].Alive);
        Assert.Equal(3, wallCasts);
    }

    /// <summary>A human in front of the wall takes the round; the wall stays clean.</summary>
    [Fact]
    public void TheNearerOfWallAndHumanWins()
    {
        BallisticSimulation simulation = Spawn(rangeMetres: 300f);

        WorldRayCast walls = (Vector3d origin, Vector3d end, out WorldRayHit hit) =>
            TryWall(origin, end, 25f * UnitsPerMetre, out hit);

        HumanShot humans = (Vector3d origin, Vector3d end, float damage, WeaponCategory category, out HumanHit hit) =>
        {
            // Standing at 22 m, three in front of the wall, zone 4 -- the head.
            double humanY = 22f * UnitsPerMetre;
            if (origin.Y <= humanY && end.Y >= humanY)
            {
                hit = new HumanHit(new Vector3d(origin.X, humanY, origin.Z), humanY - origin.Y, Zone: 4, 0f, 0f);
                return true;
            }

            hit = default;
            return false;
        };

        for (int tick = 0; tick < 3; tick++)
        {
            simulation.Tick(walls, humans, gravityScale: 0f, dragScale: 0f);
        }

        BallisticImpact impact = Assert.Single(simulation.Impacts);
        Assert.True(impact.StruckHuman);
        Assert.False(impact.StruckWall);
        Assert.Equal(4, impact.Human.Zone);
    }

    /// <summary>A shotgun spawns one round per pellet, and a sixty-fifth is dropped like retail's sixteenth.</summary>
    [Fact]
    public void ThePoolIsBounded()
    {
        BallisticSimulation simulation = new();
        WeaponType weapon = Weapon();

        for (int i = 0; i < BallisticSimulation.MaximumLiveRounds; i++)
        {
            Assert.True(simulation.Spawn(Projectile(), weapon, null, 1f, WeaponCategory.Gun));
        }

        Assert.False(simulation.Spawn(Projectile(), weapon, null, 1f, WeaponCategory.Gun));
    }

    // --- The streaks and the trail ---------------------------------------------------------------

    /// <summary>A visual round flies its resolved line, casts nothing and damages nobody.</summary>
    [Fact]
    public void AVisualRoundNeitherCastsNorDamages()
    {
        BallisticSimulation simulation = new();
        Vector3d end = new(0, 50f * UnitsPerMetre, 0);
        Assert.True(simulation.SpawnVisual(default, new Vector3f(0f, 1f, 0f), end, Weapon(), null));

        WorldRayCast walls = (Vector3d origin, Vector3d to, out WorldRayHit hit) =>
            throw new InvalidOperationException("A visual round must never cast.");
        HumanShot humans = (Vector3d origin, Vector3d to, float damage, WeaponCategory category, out HumanHit hit) =>
            throw new InvalidOperationException("A visual round must never damage.");

        BallisticRound round = simulation.Rounds[0];
        for (int tick = 0; tick < 5; tick++)
        {
            Assert.True(round.Alive);
            simulation.Tick(walls, humans, gravityScale: 1f, dragScale: 0f);
        }

        // Five ticks at 10 m a tick is the whole 50 m line: dead exactly at its resolved end, with
        // no impact -- its ray's owner already resolved one.
        Assert.False(round.Alive);
        Assert.Empty(simulation.Impacts);
        Assert.Equal(end.Y, round.Position.Y, 1.0);
        Assert.Equal(0.0, round.Position.Z, 1.0);
    }

    /// <summary>A dead round lingers while its trail fades, then leaves the pool.</summary>
    [Fact]
    public void ADeadRoundFadesAndIsRemoved()
    {
        BallisticSimulation simulation = Spawn(rangeMetres: 10f);
        simulation.Tick(walls: null, humans: null, gravityScale: 0f, dragScale: 0f);
        Assert.False(simulation.Rounds[0].Alive);

        for (int tick = 0; tick < BallisticSimulation.FadeLifetimeTicks; tick++)
        {
            Assert.Single(simulation.Rounds);
            simulation.Tick(walls: null, humans: null);
        }

        Assert.Empty(simulation.Rounds);
    }

    /// <summary>The trail records the head's past, newest first.</summary>
    [Fact]
    public void TheTrailFollowsTheRound()
    {
        BallisticSimulation simulation = Spawn(rangeMetres: 300f);
        BallisticRound round = simulation.Rounds[0];

        simulation.Tick(walls: null, humans: null, gravityScale: 0f, dragScale: 0f);
        simulation.Tick(walls: null, humans: null, gravityScale: 0f, dragScale: 0f);
        simulation.Tick(walls: null, humans: null, gravityScale: 0f, dragScale: 0f);

        Assert.Equal(3, round.TrailCount);
        Assert.Equal(round.Position.Y, round.TrailAt(0).Y);
        Assert.True(round.TrailAt(1).Y < round.TrailAt(0).Y);
        Assert.True(round.TrailAt(2).Y < round.TrailAt(1).Y);
    }

    // --- Builders --------------------------------------------------------------------------------

    private static WeaponType Weapon(
        WeaponCategory category = WeaponCategory.Gun,
        float velocity = TestVelocity,
        int calibre = 919,
        float rangeMetres = 300f) => new()
    {
        WeaponId = 1,
        ScriptId = "WEAPON_ID_TEST",
        Category = category,
        MuzzleVelocity = velocity,
        CalibreId = calibre,
        EffectiveRange = rangeMetres,
        BulletsPerRound = 1,
    };

    private static AmmoType Ammo(int calibre, float massInGrams) => new()
    {
        AmmoId = 1,
        CalibreId = calibre,
        MassInGrams = massInGrams,
    };

    private static GunProjectile Projectile(float rangeMetres = 300f) => new(
        default,
        new Vector3f(0f, 1f, 0f),
        rangeMetres * UnitsPerMetre);

    // Zero minimum flight, so the tests that reason about the WEAPON's range see exactly it; the
    // extension has its own test below.
    private static BallisticSimulation Spawn(float rangeMetres, int calibre = 919, AmmoType? ammo = null)
    {
        BallisticSimulation simulation = new();
        WeaponType weapon = Weapon(calibre: calibre, rangeMetres: rangeMetres);
        Assert.True(simulation.Spawn(
            Projectile(rangeMetres), weapon, ammo, 1f, weapon.Category, null, minimumFlightSeconds: 0f));
        return simulation;
    }

    // A wall across the +Y axis: any segment crossing the plane hits it square on.
    private static bool TryWall(Vector3d origin, Vector3d end, double wallY, out WorldRayHit hit)
    {
        if (origin.Y <= wallY && end.Y >= wallY)
        {
            hit = new WorldRayHit(
                new Vector3d(origin.X, wallY, origin.Z),
                new Vector3f(0f, -1f, 0f),
                wallY - origin.Y);
            return true;
        }

        hit = default;
        return false;
    }
}

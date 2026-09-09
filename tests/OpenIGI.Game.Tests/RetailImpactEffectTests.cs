using OpenIGI.Core;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// The original's impact effects: the <c>GunShotEffect</c> a material runs (<c>0x438440</c>), the
/// particle engine both it and the blood are built on (<c>0x4FDF00</c>/<c>0x4FE120</c>/<c>0x4FD660</c>),
/// and the blood a body throws (<c>0x463AD0</c>).
/// </summary>
public sealed class RetailImpactEffectTests
{
    [Fact]
    public void APuffNeedsItsFlagANonHumanAndTheCoin()
    {
        GunShotEffect concrete = new(
            SpawnsParticles: true,
            LeavesMark: true,
            EntryTexture: "bullethole_concrete.tex",
            ExitTexture: "dent_concrete.tex",
            SizeInMetres: 0.05f,
            Model: "1012_01_1");

        // 0x43856B draws rand() & 1, so exactly half of the hits that pass every other gate puff.
        Assert.True(GunShotImpactEffects.SpawnsPuff(concrete, struckHuman: false, coin: 1));
        Assert.False(GunShotImpactEffects.SpawnsPuff(concrete, struckHuman: false, coin: 0));

        // 0x438458 asks the struck object whether it is a Human before anything else.
        Assert.False(GunShotImpactEffects.SpawnsPuff(concrete, struckHuman: true, coin: 1));

        // Glass declares neither flag, so it marks and does nothing else.
        GunShotEffect glass = concrete with { SpawnsParticles = false, LeavesMark = false };
        Assert.False(GunShotImpactEffects.SpawnsPuff(glass, struckHuman: false, coin: 1));
        Assert.False(GunShotImpactEffects.ThrowsFragments(glass, struckHuman: false));
    }

    [Fact]
    public void AHitThrowsTwoToFourChips()
    {
        Assert.Equal(2, GunShotImpactEffects.FragmentCount(0));
        Assert.Equal(3, GunShotImpactEffects.FragmentCount(1));
        Assert.Equal(4, GunShotImpactEffects.FragmentCount(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => GunShotImpactEffects.FragmentCount(3));
    }

    [Fact]
    public void AChipLeavesInsideItsHalfConeAtTheAuthoredSpeed()
    {
        IgiRandom random = new(7u);
        Vector3f along = new(0f, 1f, 0f);

        for (int i = 0; i < 200; i++)
        {
            Vector3f velocity = GunShotImpactEffects.FragmentLaunch(along, random);
            float speed = velocity.Length;

            Assert.InRange(
                speed,
                GunShotImpactEffects.FragmentSpeedMinimum - 0.01f,
                GunShotImpactEffects.FragmentSpeedMaximum + 0.01f);

            float cosine = Vector3f.Dot(velocity * (1f / speed), along);
            float angle = MathF.Acos(Math.Clamp(cosine, -1f, 1f));
            Assert.InRange(angle, 0f, GunShotImpactEffects.FragmentConeRadians + 1e-4f);
        }
    }

    [Fact]
    public void AParticleHoldsThenFadesAndDies()
    {
        // The wall puff's own numbers: fifteen ticks at its starting alpha, then a thirtieth off each
        // tick until it is gone.
        RetailParticleSystem system = new(
            acceleration: Vector3f.Zero,
            sizeInUnits: GunShotImpactEffects.PuffSizeInUnits,
            growthPerTick: GunShotImpactEffects.PuffGrowthPerTick,
            frameCount: 1,
            ticksPerFrame: 0,
            capacity: 2);

        Assert.True(system.Emit(
            default,
            Vector3f.Zero,
            alpha: 1f,
            spin: 0f,
            angle: 0f,
            growth: 1f,
            holdTicks: GunShotImpactEffects.PuffHoldTicks,
            fadeTicks: GunShotImpactEffects.PuffFadeTicks));

        for (int tick = 0; tick < GunShotImpactEffects.PuffHoldTicks; tick++)
        {
            system.Tick();
            Assert.Equal(1, system.Count);
            Assert.Equal(1f, system.Particles[0].Alpha, 5);
        }

        // The growth has been running the whole time, so the quad is already wider than authored.
        Assert.True(system.Particles[0].Growth > 1f);

        for (int tick = 0; tick < GunShotImpactEffects.PuffFadeTicks - 1; tick++)
        {
            system.Tick();
        }

        Assert.Equal(1, system.Count);
        system.Tick();
        Assert.Equal(0, system.Count);
    }

    [Fact]
    public void AFullPoolDropsTheParticleRatherThanEvictingOne()
    {
        // 0x4FE133 only writes when live < capacity. A body already showing its ten droplets shows no
        // more until some die -- there is no oldest-first eviction anywhere in the original.
        RetailParticleSystem system = new(
            acceleration: Vector3f.Zero,
            sizeInUnits: 1f,
            growthPerTick: 0f,
            frameCount: 1,
            ticksPerFrame: 0,
            capacity: 2);

        Assert.True(system.Emit(default, Vector3f.Zero, 1f, 0f, 0f, 1f, 10, 10));
        Assert.True(system.Emit(default, Vector3f.Zero, 1f, 0f, 0f, 1f, 10, 10));
        Assert.False(system.Emit(default, Vector3f.Zero, 1f, 0f, 0f, 1f, 10, 10));
        Assert.Equal(2, system.Count);
    }

    [Fact]
    public void AParticleIntegratesTheSystemsAccelerationNotItsOwn()
    {
        RetailParticleSystem system = new(
            acceleration: new Vector3f(0f, 0f, BloodEffects.DropletGravityPerTick),
            sizeInUnits: BloodEffects.DropletSizeInUnits,
            growthPerTick: 0f,
            frameCount: 1,
            ticksPerFrame: 0,
            capacity: 4);

        system.Emit(default, new Vector3f(100f, 0f, 0f), 1f, 0f, 0f, 1f, 30, 7);
        system.Tick();

        // Velocity first, then position -- so the very first tick already carries the acceleration.
        Assert.Equal(100f, system.Particles[0].Velocity.X, 3);
        Assert.Equal(BloodEffects.DropletGravityPerTick, system.Particles[0].Velocity.Z, 3);
        Assert.Equal(100.0, system.Particles[0].Position.X, 3);
        Assert.Equal(BloodEffects.DropletGravityPerTick, system.Particles[0].Position.Z, 3);
    }

    [Fact]
    public void AMultiFrameSpriteHoldsEachFrameForItsWholeShare()
    {
        // blood1.spr ships seven frames and the mist's budget is fifteen ticks, so 15 / 7 == 2 whole
        // ticks a frame -- integer division, which is what leaves the last frame held a tick longer.
        RetailParticleSystem system = BloodEffects.CreateMistSystem(frameCount: 7);
        Assert.Equal(2, system.TicksPerFrame);

        system.Emit(default, Vector3f.Zero, 1f, 0f, 0f, 1f, BloodEffects.MistHoldTicks, 0);
        for (int tick = 0; tick < 2; tick++)
        {
            system.Tick();
        }

        Assert.Equal(1, system.Particles[0].Frame);

        for (int tick = 0; tick < 12; tick++)
        {
            system.Tick();
        }

        // Six advances in fourteen ticks reaches the last frame and stops there.
        Assert.Equal(6, system.Particles[0].Frame);
    }

    [Fact]
    public void ASingleFrameSpriteNeverAdvances()
    {
        // 0x4FD6FB computes frameCount - 1 and guards on it, so a one-frame sprite skips the whole
        // animation block rather than sitting on frame zero by luck.
        RetailParticleSystem system = BloodEffects.CreateDropletSystem();
        system.Emit(default, Vector3f.Zero, 1f, 0f, 0f, 1f, 30, 7);
        for (int tick = 0; tick < 20; tick++)
        {
            system.Tick();
        }

        Assert.Equal(0, system.Particles[0].Frame);
    }

    [Fact]
    public void ADropletIsTracedOnceEverySixteenTicks()
    {
        RetailParticleSystem.ResetStagger();
        RetailParticleSystem system = BloodEffects.CreateDropletSystem();
        system.Emit(new Vector3d(0, 0, 0), new Vector3f(0f, 10f, 0f), 1f, 0f, 0f, 1f, 60, 7);

        int traces = 0;
        bool Walls(Vector3d origin, Vector3d end, out WorldRayHit hit)
        {
            traces++;
            hit = default;
            return false;
        }

        for (int tick = 0; tick < 32; tick++)
        {
            system.Tick(Walls);
        }

        // The first particle emitted after a reset has a zero countdown, so it traces on tick one and
        // then every sixteenth tick after it.
        Assert.Equal(2, traces);
    }

    [Fact]
    public void ADropletMarksTheFaceItReachesAndNotOneItLeavesThrough()
    {
        // 0x464178 marks only where (start - end) dotted with the FACE's own normal is negative, and
        // these meshes wind their normals into the solid -- so the mark lands on an entry and not on
        // an exit. The reported normal has already been turned to oppose the ray, which is the
        // opposite vector on an entry; IsExit is the sign that flip was taken from.
        Vector3d from = new(0, 0, 0);
        Vector3d to = new(0, 100, 0);
        Vector3f facingTheRay = new(0f, -1f, 0f);

        Assert.True(BloodEffects.MarksSurface(
            from, to, new WorldRayHit(to, facingTheRay, 100.0, Material: 3, IsExit: false)));
        Assert.False(BloodEffects.MarksSurface(
            from, to, new WorldRayHit(to, facingTheRay, 100.0, Material: 3, IsExit: true)));
    }

    [Fact]
    public void OneWoundSpraysTheAuthoredCountsIntoTheirTwoCones()
    {
        IgiRandom random = new(11u);
        RetailParticleSystem mist = BloodEffects.CreateMistSystem(frameCount: 7);
        RetailParticleSystem droplets = BloodEffects.CreateDropletSystem();

        BloodEffects.Spray(mist, droplets, default, new Vector3f(0f, 1f, 0f), random);

        // One motionless mist quad at the wound.
        Assert.Equal(1, mist.Count);
        Assert.Equal(0f, mist.Particles[0].Velocity.Length, 5);

        // Three to five forward and two to four back, and the pool holds ten, so none is dropped.
        Assert.InRange(
            droplets.Count,
            BloodEffects.ForwardDropletMinimum + BloodEffects.BackDropletMinimum,
            BloodEffects.ForwardDropletMinimum
                + BloodEffects.ForwardDropletRange
                + BloodEffects.BackDropletMinimum
                + BloodEffects.BackDropletRange
                - 2);

        int forward = 0;
        int back = 0;
        foreach (RetailParticle droplet in droplets.Particles)
        {
            float speed = droplet.Velocity.Length;
            if (speed >= BloodEffects.ForwardSpeedMinimum)
            {
                forward++;
                Assert.InRange(
                    speed,
                    BloodEffects.ForwardSpeedMinimum - 0.01f,
                    BloodEffects.ForwardSpeedMaximum + 0.01f);
                Assert.True(droplet.Velocity.Y > 0f, "a forward droplet travels with the round");
            }
            else
            {
                back++;
                Assert.InRange(
                    speed,
                    BloodEffects.BackSpeedMinimum - 0.01f,
                    BloodEffects.BackSpeedMaximum + 0.01f);
                Assert.True(droplet.Velocity.Y < 0f, "a back droplet travels towards the shooter");
            }
        }

        Assert.InRange(forward, BloodEffects.ForwardDropletMinimum, 5);
        Assert.InRange(back, BloodEffects.BackDropletMinimum, 4);
    }

    [Fact]
    public void ConeEmissionStaysInsideHalfTheAuthoredAngle()
    {
        IgiRandom random = new(3u);
        RetailParticleSystem system = new(
            acceleration: Vector3f.Zero,
            sizeInUnits: 1f,
            growthPerTick: 0f,
            frameCount: 1,
            ticksPerFrame: 0,
            capacity: 256);

        Vector3f axis = new(0.3f, 0.9f, -0.2f);
        Vector3f unitAxis = axis * (1f / axis.Length);

        for (int i = 0; i < 256; i++)
        {
            system.Emit(default, Vector3f.Zero, 1f, 0f, 0f, 1f, 100, 100);
        }

        system.Clear();

        for (int i = 0; i < 200; i++)
        {
            system.Clear();
            Assert.True(system.EmitCone(
                default,
                axis,
                BloodEffects.BackConeRadians,
                BloodEffects.BackSpeedMinimum,
                BloodEffects.BackSpeedMaximum,
                alpha: 1f,
                spin: 0f,
                angle: 0f,
                growth: 1f,
                holdTicks: 10,
                fadeTicks: 10,
                random));

            Vector3f velocity = system.Particles[0].Velocity;
            float cosine = Vector3f.Dot(velocity * (1f / velocity.Length), unitAxis);
            float angle = MathF.Acos(Math.Clamp(cosine, -1f, 1f));

            // The pitch is drawn on plus or minus HALF the authored cone (0x4FE42B).
            Assert.InRange(angle, 0f, (BloodEffects.BackConeRadians * 0.5f) + 1e-4f);
        }
    }

    [Fact]
    public void TheRuntimeKeepsOneBodysSystemsAcrossHits()
    {
        ImpactEffectRuntime runtime = new(new IgiRandom(5u));
        object guard = new();

        for (int shot = 0; shot < 6; shot++)
        {
            runtime.SpawnBlood(guard, default, new Vector3f(0f, 1f, 0f), null, null, null);
        }

        // One body, one pair of systems, and the pools cap what a burst can show: two mist quads and
        // ten droplets however many rounds landed.
        Assert.Single(runtime.Blood);
        BloodSystems systems = runtime.Blood[guard];
        Assert.Equal(BloodEffects.MistCapacity, systems.Mist.Count);
        Assert.Equal(BloodEffects.DropletCapacity, systems.Droplets.Count);
    }

    [Fact]
    public void TheBloodOptionSuppressesEverythingBlood()
    {
        // 0x463AD9 reads GOIsBlood before it does anything else.
        ImpactEffectRuntime runtime = new(new IgiRandom(5u)) { BloodEnabled = false };
        runtime.SpawnBlood(new object(), default, new Vector3f(0f, 1f, 0f), null, null, null);
        Assert.Empty(runtime.Blood);
    }

    [Fact]
    public void ChipsFallSlowerThanABodyAndExpireAfterFortyFiveTicks()
    {
        ImpactEffectRuntime runtime = new(new IgiRandom(9u));
        GunShotEffect wood = new(true, true, "bullethole_wood.tex", "bullethole_wood.tex", 0.03f, "1011_01_1");

        runtime.SpawnSurfaceImpact(
            new Vector3d(0, 0, 0),
            new Vector3f(0f, -1f, 0f),
            new Vector3f(0f, 1f, 0f),
            wood,
            struckHuman: false);

        Assert.InRange(runtime.Fragments.Count, 2, 4);
        Assert.All(runtime.Fragments, fragment => Assert.Equal("1011_01_1", fragment.Model));

        // 0x5035FA applies scale x gravity x drag, which is under half of what a body falls at.
        Assert.True(GunShotImpactEffects.FragmentGravityPerTick < GunShotImpactEffects.GravityPerTick * 0.5f);

        for (int tick = 0; tick < GunShotImpactEffects.FragmentLifeTicks; tick++)
        {
            runtime.Tick(null, null, null);
        }

        Assert.Empty(runtime.Fragments);
    }

    [Fact]
    public void ABodyTakesNoDustAndNoChips()
    {
        ImpactEffectRuntime runtime = new(new IgiRandom(9u));
        GunShotEffect concrete = new(true, true, "bullethole_concrete.tex", "dent_concrete.tex", 0.05f, "1012_01_1");

        for (int shot = 0; shot < 32; shot++)
        {
            runtime.SpawnSurfaceImpact(
                default,
                new Vector3f(0f, -1f, 0f),
                new Vector3f(0f, 1f, 0f),
                concrete,
                struckHuman: true);
        }

        Assert.Equal(0, runtime.Puffs.Count);
        Assert.Empty(runtime.Fragments);
    }

    [Fact]
    public void APuffPairSitsOffTheSurfaceAndRisesTogether()
    {
        // The coin is drawn first, so seed a run that comes up heads.
        ImpactEffectRuntime runtime = new(new IgiRandom(2u));
        GunShotEffect concrete = new(true, false, "bullethole_concrete.tex", "dent_concrete.tex", 0.05f, string.Empty);

        for (int shot = 0; shot < 16 && runtime.Puffs.Count == 0; shot++)
        {
            runtime.SpawnSurfaceImpact(
                new Vector3d(0, 0, 0),
                new Vector3f(0f, 0f, 1f),
                new Vector3f(0f, 1f, 0f),
                concrete,
                struckHuman: false);
        }

        Assert.Equal(2, runtime.Puffs.Count);

        RetailParticle first = runtime.Puffs.Particles[0];
        RetailParticle second = runtime.Puffs.Particles[1];

        // 0x438624 lifts the pair three authored sizes along the normal; 0x4386B8 puts the second a
        // quarter of a size higher again, in world space rather than along the normal.
        Assert.Equal(GunShotImpactEffects.PuffLiftInUnits, first.Position.Z, 2);
        Assert.Equal(
            GunShotImpactEffects.PuffLiftInUnits + GunShotImpactEffects.PuffSecondRiseInUnits,
            second.Position.Z,
            2);

        // Both rise straight up, and the second a quarter faster (0x438757).
        Assert.Equal(0f, first.Velocity.X, 5);
        Assert.Equal(0f, first.Velocity.Y, 5);
        Assert.True(first.Velocity.Z > 0f);
        Assert.Equal(
            first.Velocity.Z * GunShotImpactEffects.PuffSecondSpeedScale, second.Velocity.Z, 3);

        // And they share the alpha draw, which is taken once for the pair.
        Assert.Equal(first.Alpha, second.Alpha, 5);
        Assert.InRange(
            first.Alpha,
            GunShotImpactEffects.PuffAlphaMinimum,
            GunShotImpactEffects.PuffAlphaMinimum + GunShotImpactEffects.PuffAlphaRange);
    }

    [Fact]
    public void TheAuthoredSizesAreTheOriginalsOwnNumbers()
    {
        // Quoted here so a stray edit to any of them fails a test rather than quietly changing the look.
        Assert.Equal(163.84f, GunShotImpactEffects.PuffSizeInUnits);
        Assert.Equal(1228.8f, BloodEffects.MistSizeInUnits);
        Assert.Equal(102.4f, BloodEffects.DropletSizeInUnits);
        Assert.Equal(12288f, BloodEffects.BackstopRangeInUnits);
        Assert.Equal(0.1f, GunShotImpactEffects.FragmentModelScale);

        // The backstop splash is five to ten times a bullet hole; a landing droplet is a fifth of it.
        Assert.Equal(614.4f, BloodEffects.BackstopRadius(0f), 3);
        Assert.Equal(1228.8f, BloodEffects.BackstopRadius(1f), 3);
        Assert.Equal(102.4f, BloodEffects.DropletMarkRadius(0f), 3);
        Assert.Equal(307.2f, BloodEffects.DropletMarkRadius(1f), 3);
    }

    [Fact]
    public void ADropletMarksWhereItLandsAndDies()
    {
        ImpactEffectRuntime runtime = new(new IgiRandom(4u));
        BulletMarks marks = new();
        GameMaterialRegistry materials = MaterialsMarking();
        object guard = new();

        RetailParticleSystem.ResetStagger();
        runtime.SpawnBlood(guard, new Vector3d(0, 0, 0), new Vector3f(0f, 1f, 0f), null, marks, materials);

        int before = runtime.Blood[guard].Droplets.Count;
        Assert.True(before > 0);

        // A floor the droplets meet on their first collision tick.
        static bool Walls(Vector3d origin, Vector3d end, out WorldRayHit hit)
        {
            hit = new WorldRayHit(end, new Vector3f(0f, -1f, 0f), 1.0, Material: 3);
            return true;
        }

        for (int tick = 0; tick < 20; tick++)
        {
            runtime.Tick(Walls, marks, materials);
        }

        Assert.True(marks.TotalAdded > 0, "a landing droplet leaves its splash");
        Assert.All(
            marks.Marks,
            mark => Assert.Contains(mark.Texture, BloodEffects.DropletTextures));
        Assert.All(
            marks.Marks,
            mark => Assert.InRange(
                mark.SizeInUnits,
                BloodEffects.DropletMarkMinimumInUnits,
                BloodEffects.DropletMarkMinimumInUnits + BloodEffects.DropletMarkRangeInUnits));
    }

    [Fact]
    public void ABloodReportSaysWhatTheBackstopFound()
    {
        // The report is what the blood_test harness reads back, and what turned "no splash on the wall"
        // from a guess into a measurement: the trace was reaching the player's MOVEMENT collision set,
        // which is a reduced list of meshes and carries no terrain at all.
        BulletMarks marks = new();
        GameMaterialRegistry materials = MaterialsMarking();
        ImpactEffectRuntime runtime = new(new IgiRandom(6u));

        static bool Wall(Vector3d origin, Vector3d end, out WorldRayHit hit)
        {
            hit = new WorldRayHit(new Vector3d(0, 6000, 0), new Vector3f(0f, -1f, 0f), 6000.0, Material: 3);
            return true;
        }

        BloodReport report = runtime.SpawnBlood(
            new object(), default, new Vector3f(0f, 1f, 0f), Wall, marks, materials);

        Assert.True(report.Sprayed);
        Assert.True(report.BackstopHit);
        Assert.Equal(3, report.BackstopMaterial);
        Assert.True(report.BackstopMarked);
        Assert.Equal(6000.0, report.BackstopDistanceUnits, 3);

        // Nothing behind the body at all: the spray still happens, the splash does not.
        static bool Open(Vector3d origin, Vector3d end, out WorldRayHit hit)
        {
            hit = default;
            return false;
        }

        BloodReport miss = new ImpactEffectRuntime(new IgiRandom(6u)).SpawnBlood(
            new object(), default, new Vector3f(0f, 1f, 0f), Open, new BulletMarks(), materials);
        Assert.True(miss.Sprayed);
        Assert.False(miss.BackstopHit);
        Assert.False(miss.BackstopMarked);
    }

    [Fact]
    public void TheBackstopSplashLandsBehindTheBodyAndOnlyOnAMarkingSurface()
    {
        BulletMarks marks = new();
        GameMaterialRegistry materials = MaterialsMarking();
        ImpactEffectRuntime runtime = new(new IgiRandom(6u));

        Vector3d wall = new(0, 6000, 0);
        bool Walls(Vector3d origin, Vector3d end, out WorldRayHit hit)
        {
            hit = new WorldRayHit(wall, new Vector3f(0f, -1f, 0f), 6000.0, Material: 3);
            return true;
        }

        runtime.SpawnBlood(new object(), default, new Vector3f(0f, 1f, 0f), Walls, marks, materials);

        Assert.Equal(1, marks.TotalAdded);
        BulletMark splash = marks.Marks[0];
        Assert.Contains(splash.Texture, BloodEffects.BackstopTextures);
        Assert.InRange(
            splash.SizeInUnits,
            BloodEffects.BackstopRadiusMinimumInUnits,
            BloodEffects.BackstopRadiusMinimumInUnits * 2f);

        // Air names no texture, so a shot into the open leaves nothing behind the body.
        BulletMarks open = new();
        GameMaterialRegistry air = MaterialsMarkingNothing();
        ImpactEffectRuntime second = new(new IgiRandom(6u));
        second.SpawnBlood(new object(), default, new Vector3f(0f, 1f, 0f), Walls, open, air);
        Assert.Equal(0, open.TotalAdded);
    }

    private static GameMaterialRegistry MaterialsMarking()
    {
        GameMaterialRegistry registry = new();
        registry.Add(new GameMaterial
        {
            Index = 3,
            Name = "Wood",
            Entry = new GunShotEffect(
                true, true, "bullethole_wood.tex", "bullethole_wood.tex", 0.03f, "1011_01_1"),
        });
        return registry;
    }

    private static GameMaterialRegistry MaterialsMarkingNothing()
    {
        GameMaterialRegistry registry = new();
        registry.Add(new GameMaterial
        {
            Index = 3,
            Name = "Air",
            Entry = new GunShotEffect(true, true, string.Empty, string.Empty, 0.03f, "1009_01_1"),
        });
        return registry;
    }
}

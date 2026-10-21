using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.Weapons;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Which passes of a gun's march draw a tracer, and how long one lives — <c>0x47C13A</c>, <c>0x47C4C0</c> and
/// <c>0x47C560</c>.
/// </summary>
public sealed class GunShotTracerTests
{
    private static readonly WeaponType Rifle = new() { EffectiveRange = 100f, PenetrationPower = 5f, CalibreId = 762 };

    private static readonly AmmoType Traced = new()
    {
        CalibreId = 762,
        DrawsTracer = true,
        NearColour = new TracerColour(250, 200, 0),
        FarColour = new TracerColour(255, 255, 0),
    };

    [Fact]
    public void APlainBurstDrawsOnePassInEleven()
    {
        // The counter starts at ten (0x47B560) and only a pass taken at zero draws; drawing sets it back to ten.
        GunShotTask task = new();
        OpenWorld world = new(Traced);
        List<int> drawnOn = [];
        for (int round = 1; round <= 22; round++)
        {
            int before = task.Tracers.Count;
            Fire(task, world);
            if (task.Tracers.Count > before)
            {
                drawnOn.Add(round);
            }

            SpendAll(task);
        }

        Assert.Equal([11, 22], drawnOn);
    }

    [Fact]
    public void APassTheBiteLetsThroughDrawsWhateverTheCounter()
    {
        // The first round, counter at ten: its pass through the wall draws (v51), the pass beyond does not.
        GunShotTask task = new();
        OpenWorld world = new(Traced) { WallAtUnits = 40960.0 };
        Fire(task, world);

        GunShotTracer tracer = Assert.Single(task.Tracers);
        Assert.Equal(0f, tracer.StartDistance);
        Assert.Equal(40960f, tracer.EndDistance, 1f);
        Assert.Equal(100f * 4096f, tracer.Range);
        Assert.Equal(40960.0, tracer.End.Y, 1);
    }

    [Fact]
    public void ADrawnTracerIsFreedOnTheNextTickAndAnUndrawnOneAfterThirty()
    {
        GunShotTask task = new();
        OpenWorld world = new(Traced) { WallAtUnits = 40960.0 };
        Fire(task, world);

        // Filed at 30 and counted down once by the same tick's 0x47C560.
        GunShotTracer tracer = Assert.Single(task.Tracers);
        Assert.Equal(GunShotBallistics.TracerLifetimeTicks - 1, tracer.Life);

        // Undrawn, it lives out its count.
        for (int tick = 0; tick < GunShotBallistics.TracerLifetimeTicks - 1; tick++)
        {
            Assert.Single(task.Tracers);
            task.AgeTracers();
        }

        Assert.Equal(0, tracer.Life);
        task.AgeTracers();
        Assert.Empty(task.Tracers);

        // Drawn (0x47ABD0 zeroes the life), it goes on the next tick.
        Fire(task, world);
        Assert.Single(task.Tracers).Life = 0;
        task.AgeTracers();
        Assert.Empty(task.Tracers);
    }

    [Fact]
    public void AmmunitionWithoutTheTracerFlagNeverDraws()
    {
        GunShotTask task = new();
        OpenWorld world = new(new AmmoType { CalibreId = 762, DrawsTracer = false }) { WallAtUnits = 40960.0 };
        for (int round = 0; round < 30; round++)
        {
            Fire(task, world);
        }

        Assert.Empty(task.Tracers);
    }

    [Fact]
    public void AGunHoldsFiveTracersAndDropsTheRest()
    {
        GunShotTask task = new();
        OpenWorld world = new(Traced) { WallAtUnits = 40960.0 };
        int filed = 0;
        GunShotTask.TracerFiled = (_, _) => filed++;
        try
        {
            for (int round = 0; round < 8; round++)
            {
                Fire(task, world);
            }
        }
        finally
        {
            GunShotTask.TracerFiled = null;
        }

        Assert.Equal(8, filed);
        Assert.Equal(GunShotTask.TracerCapacity, task.Tracers.Count);
    }

    private static void Fire(GunShotTask task, OpenWorld world)
    {
        Assert.True(task.Queue(Rifle, new Vector3d(0, 0, 0), new Vector3f(0f, 1f, 0f), 1f, shooter: null));
        task.Tick(0, new IgiRandom(), world, new GunShotSoundWindows());
    }

    private static void SpendAll(GunShotTask task)
    {
        foreach (GunShotTracer tracer in task.Tracers)
        {
            tracer.Life = 0;
        }
    }

    // Open air along +Y, with an optional thin object wall the rifle bites through.
    private sealed class OpenWorld(AmmoType ammunition) : IGunShotWorld
    {
        private static readonly GameMaterial Plank = new() { Name = "Wood", BulletResistance = 1f };

        public double? WallAtUnits { get; init; }

        public bool Cast(GunShotRound round, Vector3d start, Vector3d limit, out GunShotContact contact)
        {
            contact = default;
            if (WallAtUnits is not { } wall || start.Y >= wall)
            {
                return false;
            }

            contact = new GunShotContact(
                new Vector3d(start.X, wall, start.Z),
                new Vector3f(0f, -1f, 0f),
                wall - start.Y,
                Entry: Plank,
                Exit: null,
                HasObject: true,
                StruckHuman: false,
                Payload: null);
            return true;
        }

        public void Strike(GunShotRound round, in GunShotContact contact, Vector3f travel)
        {
        }

        public void PlaySound(string definition, Vector3d position)
        {
        }

        public void Effect(GunShotRound round, in GunShotRecord record)
        {
        }

        public AmmoType? AmmunitionOf(WeaponType weapon) => ammunition;
    }
}

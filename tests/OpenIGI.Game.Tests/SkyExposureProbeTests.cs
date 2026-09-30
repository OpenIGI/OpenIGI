using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Pins the geometry behind hearing the weather from indoors: how much sky reaches the ear, and from
/// which way.
/// </summary>
/// <remarks>
/// <para>
/// The level here is a shed built out of planes — four walls, a roof, a floor, and open ground around
/// it — rather than a retail mission, because every claim being made is about the <em>rule</em> and
/// not about any particular building. A shed with a door in one wall is the whole of the problem: the
/// rain must be audible, it must be quieter than it is outside, and it must come from the door.
/// </para>
/// <para>
/// The roof is a single plane with no thickness, which is not a simplification but the case that
/// matters. Testing shelter at the point a ray strikes cannot tell that roof's underside from its top
/// — they are the same height — so a probe that did so would report a sealed room as open sky. The
/// shed is built this way on purpose so that the mistake cannot pass.
/// </para>
/// </remarks>
public sealed class SkyExposureProbeTests
{
    private const double Units = WorldScene.UnitsPerMetre;

    /// <summary>The shed's inside half-width, in metres.</summary>
    private const double Half = 5.0;

    /// <summary>The height of the shed's roof, in metres.</summary>
    private const double RoofZ = 4.0;

    /// <summary>How far to either side of the shed's centre line the doorway reaches, in metres.</summary>
    private const double DoorHalfWidth = 1.0;

    /// <summary>How tall the doorway is, in metres.</summary>
    private const double DoorHeight = 2.2;

    private const int PlusX = 0;
    private const int MinusX = 1;
    private const int PlusY = 2;
    private const int MinusY = 3;
    private const int Up = 4;
    private const int Down = 5;

    [Fact]
    public void OpenGroundIsExposedInEveryDirection()
    {
        SkyExposureProbe probe = Probe();

        // No collision anywhere in the level means nothing can be sheltering anybody, and the probe
        // is entitled to skip its casts and say so.
        probe.Update(new Vector3d(0.0, 0.0, 1.7), ShelterHeightField.Empty, null, Units, 1f / 60f);

        Assert.Equal(1f, probe.Exposure, 3);
        for (int axis = 0; axis < SkyExposureProbe.Directions.Length; axis++)
        {
            Assert.Equal(1f, probe.DirectionalExposure[axis], 3);
        }

        Assert.Equal(0f, probe.RoofNearness, 3);
    }

    [Fact]
    public void StandingOutsideTheShedIsStillFullyExposed()
    {
        SkyExposureProbe probe = Probe();
        probe.Update(new Vector3d(0.0, 20.0, 1.7), Shed(), Shed(open: true), Units, 1f / 60f);

        // Twenty metres clear of the building: the ground below is rained on, the sky above is open,
        // and the shed itself subtends too little to matter. Anything much under one here would mean
        // the ground is being mistaken for a roof, which would make every open field sound indoors.
        Assert.True(probe.Exposure > 0.95f, $"exposure outdoors was {probe.Exposure:0.000}");
        Assert.True(probe.DirectionalExposure[Down] > 0.95f, "the ground outdoors must count as rained on");
        Assert.Equal(0f, probe.RoofNearness, 3);
    }

    [Fact]
    public void ASealedShedHearsNothingDirectly()
    {
        SkyExposureProbe probe = Probe();
        probe.Update(Centre(), Shed(), Shed(open: false), Units, 1f / 60f);

        // Every ray ends against a wall, the floor or the roof, and every one of those is under the
        // roof. A thin roof read from below is the way this fails: it would come out near one.
        Assert.Equal(0f, probe.Exposure, 3);
        for (int axis = 0; axis < SkyExposureProbe.Directions.Length; axis++)
        {
            Assert.Equal(0f, probe.DirectionalExposure[axis], 3);
        }
    }

    [Fact]
    public void AnOpenDoorIsHeardFromTheDirectionItIsIn()
    {
        SkyExposureProbe probe = Probe();
        probe.Update(Centre(), Shed(), Shed(open: true), Units, 1f / 60f);

        float towardsDoor = probe.DirectionalExposure[MinusX];
        float awayFromDoor = probe.DirectionalExposure[PlusX];

        // Audible at all, and quieter than standing outside: a door across a room is a small part of
        // the sphere, and the mix turns that fraction into a level without any distance term.
        Assert.True(probe.Exposure > 0f, "an open door must let some weather in");
        Assert.True(probe.Exposure < 0.35f, $"a door across a room let in {probe.Exposure:0.000} of the sky");

        // The claim the whole feature rests on. The wall behind the listener must not carry the rain
        // the doorway in front of them does.
        Assert.True(
            towardsDoor > awayFromDoor * 4f,
            $"the door direction read {towardsDoor:0.000} against {awayFromDoor:0.000} behind");
        Assert.True(towardsDoor > probe.DirectionalExposure[PlusY], "the door must beat the side walls");
        Assert.True(towardsDoor > probe.DirectionalExposure[MinusY], "the door must beat the side walls");
    }

    [Fact]
    public void StandingInTheDoorwayIsLouderThanStandingAcrossTheRoom()
    {
        SkyExposureProbe across = Probe();
        across.Update(Centre(), Shed(), Shed(open: true), Units, 1f / 60f);

        SkyExposureProbe inDoor = Probe();
        inDoor.Update(new Vector3d(-Half + 0.4, 0.0, 1.5), Shed(), Shed(open: true), Units, 1f / 60f);

        // A doorway subtends far more of the sphere from inside it than from across the room, so the
        // rain gets louder as the player walks towards it — with nothing in the mix that measures
        // distance. That is the property that makes the whole approach work.
        Assert.True(
            inDoor.Exposure > across.Exposure * 2f,
            $"the doorway read {inDoor.Exposure:0.000} against {across.Exposure:0.000} across the room");
        Assert.True(
            inDoor.DirectionalExposure[MinusX] > across.DirectionalExposure[MinusX],
            "the doorway must dominate its own direction more strongly from inside it");
    }

    [Fact]
    public void ARoofOverheadIsReportedAndAHighOneLessSo()
    {
        SkyExposureProbe low = Probe();
        low.Update(Centre(), Shed(), Shed(open: true), Units, 1f / 60f);

        // Two metres and a bit of headroom is a roof you can hear the rain drumming on.
        Assert.Equal(1f, low.RoofNearness, 2);

        SkyExposureProbe hangar = Probe();
        float[] heights = new float[16 * 16];
        Array.Fill(heights, 30f);
        ShelterHeightField high = ShelterHeightField.FromHeights(heights, 16, 16, -4.0, -4.0, 0.5);
        hangar.Update(new Vector3d(0.0, 0.0, 1.7), high, null, Units, 1f / 60f);

        // Thirty metres up is still a ceiling, but it does not drum.
        Assert.Equal(0f, hangar.RoofNearness, 3);
    }

    [Fact]
    public void TheRaysAreSpreadOverSeveralCallsAndStillArriveAtTheSameAnswer()
    {
        ShelterHeightField shelter = Shed();
        WorldRayCast cast = Shed(open: true);

        SkyExposureProbe atOnce = Probe();
        atOnce.Update(Centre(), shelter, cast, Units, 1f / 60f);

        // The same measurement paid for a slice at a time, which is how it runs in play.
        SkyExposureProbe amortised = new(SkyExposureProbe.DefaultRayCount)
        {
            RaysPerUpdate = SkyExposureProbe.DefaultRayCount / 8,
            SmoothingSeconds = 0f,
        };

        for (int i = 0; i < 8; i++)
        {
            amortised.Update(Centre(), shelter, cast, Units, 1f / 60f);
        }

        Assert.Equal(atOnce.Exposure, amortised.Exposure, 3);
        Assert.Equal(atOnce.DirectionalExposure[MinusX], amortised.DirectionalExposure[MinusX], 3);
    }

    [Fact]
    public void SmoothingEasesTowardsTheNewSurroundingsRatherThanJumping()
    {
        SkyExposureProbe probe = new(SkyExposureProbe.DefaultRayCount)
        {
            RaysPerUpdate = SkyExposureProbe.DefaultRayCount,
            SmoothingSeconds = 0.35f,
        };

        // One frame of an open field, from a standing start, must not already be an open field: a bed
        // that arrives at its new level in a single frame is a click.
        probe.Update(new Vector3d(0.0, 0.0, 1.7), ShelterHeightField.Empty, null, Units, 1f / 60f);
        float afterOneFrame = probe.Exposure;
        Assert.True(afterOneFrame is > 0f and < 0.2f, $"one frame moved exposure to {afterOneFrame:0.000}");

        for (int i = 0; i < 120; i++)
        {
            probe.Update(new Vector3d(0.0, 0.0, 1.7), ShelterHeightField.Empty, null, Units, 1f / 60f);
        }

        Assert.True(probe.Exposure > 0.99f, $"two seconds settled at {probe.Exposure:0.000}");
    }

    [Fact]
    public void ResetForgetsTheLastLevel()
    {
        SkyExposureProbe probe = Probe();
        probe.Update(new Vector3d(0.0, 0.0, 1.7), ShelterHeightField.Empty, null, Units, 1f / 60f);
        Assert.True(probe.Exposure > 0.9f);

        probe.Reset();

        Assert.Equal(0f, probe.Exposure, 5);
        Assert.Equal(0f, probe.DirectionalExposure[Up], 5);
    }

    /// <summary>A probe that measures everything on every call, so a test needs no settling loop.</summary>
    private static SkyExposureProbe Probe() => new(SkyExposureProbe.DefaultRayCount)
    {
        RaysPerUpdate = SkyExposureProbe.DefaultRayCount,
        SmoothingSeconds = 0f,
    };

    /// <summary>The listener, standing in the middle of the shed.</summary>
    private static Vector3d Centre() => new(0.0, 0.0, 1.7);

    /// <summary>The shed's roof raster: its footprint is roofed, everything around it is open sky.</summary>
    /// <remarks>
    /// Half-metre cells over a forty-metre square, so the shed sits in the middle of open ground the
    /// rays can reach. Only the building is in the raster; the terrain around it is not a placed
    /// object and carries no entry, exactly as in a real level.
    /// </remarks>
    private static ShelterHeightField Shed()
    {
        const double Cell = 0.5;
        const double Origin = -20.0;
        const int Side = 80;

        float[] heights = new float[Side * Side];
        Array.Fill(heights, ShelterHeightField.OpenSky);

        for (int y = 0; y < Side; y++)
        {
            for (int x = 0; x < Side; x++)
            {
                double worldX = Origin + ((x + 0.5) * Cell);
                double worldY = Origin + ((y + 0.5) * Cell);
                if (Math.Abs(worldX) <= Half && Math.Abs(worldY) <= Half)
                {
                    heights[(y * Side) + x] = (float)RoofZ;
                }
            }
        }

        return ShelterHeightField.FromHeights(heights, Side, Side, Origin, Origin, Cell);
    }

    /// <summary>The shed's collision: ground everywhere, four walls and a roof, and perhaps a doorway.</summary>
    /// <param name="open">Whether the wall at −X carries a doorway.</param>
    /// <returns>A caster over the shed, working in world units as the game's own casters do.</returns>
    private static WorldRayCast Shed(bool open)
    {
        return (Vector3d originUnits, Vector3d endUnits, out WorldRayHit hit) => CastShed(open, originUnits, endUnits, out hit);
    }

    /// <summary>One ray against the shed described by <see cref="Shed"/>.</summary>
    private static bool CastShed(bool open, Vector3d originUnits, Vector3d endUnits, out WorldRayHit hit)
    {
        Vector3d origin = originUnits / Units;
        Vector3d end = endUnits / Units;
        Vector3d ray = end - origin;

        double nearest = double.PositiveInfinity;
        Vector3f normal = Vector3f.UnitZ;

        // The ground, and the shed's floor with it: one plane under the whole world.
        Hit(2, 0.0, double.MaxValue, double.MaxValue, new Vector3f(0f, 0f, 1f));

        // The roof, and the four walls standing under it.
        Hit(2, RoofZ, Half, Half, new Vector3f(0f, 0f, 1f));
        Hit(0, Half, Half, RoofZ, new Vector3f(1f, 0f, 0f));
        Hit(1, Half, Half, RoofZ, new Vector3f(0f, 1f, 0f));
        Hit(1, -Half, Half, RoofZ, new Vector3f(0f, -1f, 0f));
        HitDoorWall();

        if (double.IsPositiveInfinity(nearest))
        {
            hit = default;
            return false;
        }

        Vector3d contact = origin + (ray * nearest);
        hit = new WorldRayHit(contact * Units, normal, (contact - origin).Length * Units);
        return true;

        // One axis-aligned plane, bounded on the other two axes. `axis` names the constant one.
        void Hit(int axis, double at, double boundA, double boundB, Vector3f planeNormal)
        {
            if (!Crosses(axis, at, boundA, boundB, out double t))
            {
                return;
            }

            if (t < nearest)
            {
                nearest = t;
                normal = planeNormal;
            }
        }

        // The −X wall, with the doorway cut out of it when the shed is open.
        void HitDoorWall()
        {
            if (!Crosses(0, -Half, Half, RoofZ, out double t))
            {
                return;
            }

            Vector3d contact = origin + (ray * t);
            if (open && Math.Abs(contact.Y) <= DoorHalfWidth && contact.Z is >= 0.0 and <= DoorHeight)
            {
                return;
            }

            if (t < nearest)
            {
                nearest = t;
                normal = new Vector3f(-1f, 0f, 0f);
            }
        }

        // Where the ray crosses one plane, if it does so ahead of its start, before its end, and
        // inside the plane's extent on the two axes it spans.
        bool Crosses(int axis, double at, double boundA, double boundB, out double t)
        {
            t = 0.0;
            double from = Component(origin, axis);
            double along = Component(ray, axis);
            if (Math.Abs(along) < 1e-9)
            {
                return false;
            }

            t = (at - from) / along;
            if (t is <= 1e-6 or > 1.0)
            {
                return false;
            }

            Vector3d contact = origin + (ray * t);
            (int a, int b) = axis switch
            {
                0 => (1, 2),
                1 => (0, 2),
                _ => (0, 1),
            };

            // The floor and the ground are one unbounded plane; a bound of MaxValue says so.
            bool insideA = boundA >= double.MaxValue || Math.Abs(Component(contact, a)) <= boundA;
            bool insideB = boundB >= double.MaxValue
                || (b == 2 ? Component(contact, b) is >= 0.0 && Component(contact, b) <= boundB
                           : Math.Abs(Component(contact, b)) <= boundB);
            return insideA && insideB;
        }

        static double Component(Vector3d value, int axis) => axis switch
        {
            0 => value.X,
            1 => value.Y,
            _ => value.Z,
        };
    }
}

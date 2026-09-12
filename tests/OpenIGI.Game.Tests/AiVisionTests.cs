using OpenIGI.Core.Numerics;
using OpenIGI.Game.Ai;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>Covers the two-cone vision test and the unit conversions its setters apply.</summary>
public sealed class AiVisionTests
{
    // The identity eye: X right, Y forward, Z up.
    private static readonly Matrix3f LookingAlongY = new(1, 0, 0, 0, 1, 0, 0, 0, 1);

    /// <summary>
    /// The constructor's defaults, in degrees and metres. The secondary cone is <b>wider horizontally</b> and
    /// less than half as long — peripheral vision, which is the third independent argument that gamma is the
    /// horizontal angle.
    /// </summary>
    [Fact]
    public void TheDefaultSecondaryConeIsWiderAndShorter()
    {
        AiViewCone primary = AiViewCone.DefaultPrimary;
        AiViewCone secondary = AiViewCone.DefaultSecondary;

        // Corrected: these were 57.29 / 59.55 and 57.29 / 62.98 / 12.5 m. 57.29 degrees is exactly one
        // radian, which is the tell that the earlier pass had read something other than these fields. The
        // constructor's raw dwords give 0x3FE921FB60000000 (pi/4 to the bit) and 0x3FEBECDE60000000 (50
        // degrees) for the primary, and 0x3FF6571860000000 (80 degrees) with 0x40E4000000000000 (10 m) for
        // the secondary -- and a live run of the original reads exactly these back out of every level 1
        // guard's eye snapshot before its script overrides them.
        Assert.Equal(45.0, Degrees(primary.Alpha), 2);
        Assert.Equal(50.0, Degrees(primary.Gamma), 2);
        Assert.Equal(26.0, primary.Length / AiViewCone.UnitsPerMetre, 6);
        Assert.Equal(26.0, primary.AlarmLength / AiViewCone.UnitsPerMetre, 6);

        Assert.Equal(45.0, Degrees(secondary.Alpha), 2);
        Assert.Equal(80.0, Degrees(secondary.Gamma), 2);
        Assert.Equal(10.0, secondary.Length / AiViewCone.UnitsPerMetre, 6);
        Assert.Equal(10.0, secondary.AlarmLength / AiViewCone.UnitsPerMetre, 6);

        // Wider horizontally, identical vertically, and much shorter.
        Assert.True(secondary.Gamma > primary.Gamma);
        Assert.Equal(primary.Alpha, secondary.Alpha);
        Assert.True(secondary.Length < primary.Length / 2);
    }

    [Fact]
    public void WorldOcclusionNeverTreatsSolidCoverAsTheTarget()
    {
        // IGI.EXE 0x489B20 reads the float at 0x534230 after the ray query. Both Ghidra and
        // radare2 identify a real five-block function here and the retail dword is 0.790000021.
        Assert.Equal(0.79000002, AiSoldier.SightObstructionTolerance, 8);

        // The recovered value belongs to a richer native query. This port's sight cast excludes
        // Humans, so every finite early hit is world geometry and must remain opaque.
        Assert.True(AiSoldier.WorldBlockerAllowsSight(100.0, double.PositiveInfinity));
        Assert.False(AiSoldier.WorldBlockerAllowsSight(100.0, 0.0));
        Assert.False(AiSoldier.WorldBlockerAllowsSight(100.0, 50.0));
        Assert.False(AiSoldier.WorldBlockerAllowsSight(100.0, 78.9));
        Assert.False(AiSoldier.WorldBlockerAllowsSight(100.0, 79.1));
        Assert.False(AiSoldier.WorldBlockerAllowsSight(100.0, 90.0));
        Assert.False(AiSoldier.WorldBlockerAllowsSight(100.0, 99.9));

        // A collision exactly at the mark is the detection point itself, not an intervening wall.
        Assert.True(AiSoldier.WorldBlockerAllowsSight(100.0, 100.0));

        // Degenerate targets have no ray to occlude. Keep the renderer's zero-distance behavior stable.
        Assert.True(AiSoldier.WorldBlockerAllowsSight(0.0, 0.0));
        Assert.True(AiSoldier.WorldBlockerAllowsSight(0.0, 25.0));
    }

    /// <summary>A point straight ahead and in range is seen; one behind is refused before any trigonometry.</summary>
    [Fact]
    public void StraightAheadIsSeenAndBehindIsNot()
    {
        var vision = new AiVision();
        var eye = new Vector3d(0, 0, 0);

        Assert.True(vision.IsVisible(eye, LookingAlongY, new Vector3d(0, 10_000, 0), alerted: false));
        Assert.False(vision.IsVisible(eye, LookingAlongY, new Vector3d(0, -10_000, 0), alerted: false));

        // Exactly on the eye plane counts as behind.
        Assert.False(vision.IsVisible(eye, LookingAlongY, new Vector3d(1000, 0, 0), alerted: false));
    }

    /// <summary>Range is inclusive at the limit and exclusive beyond it.</summary>
    [Fact]
    public void RangeIsInclusiveAtTheLimit()
    {
        var vision = new AiVision
        {
            Primary = new AiViewCone(1.0, 1.0, 4096.0, 4096.0),
            Secondary = new AiViewCone(0, 0, 0, 0),
        };
        var eye = new Vector3d(0, 0, 0);

        Assert.True(vision.IsVisible(eye, LookingAlongY, new Vector3d(0, 4096.0, 0), false));
        Assert.False(vision.IsVisible(eye, LookingAlongY, new Vector3d(0, 4096.1, 0), false));
    }

    /// <summary>
    /// <b>Alpha bounds the vertical and gamma the horizontal.</b> The cone here is deliberately lopsided, so a
    /// reader with the two swapped fails both halves.
    /// </summary>
    [Fact]
    public void AlphaBoundsTheVerticalAndGammaTheHorizontal()
    {
        var vision = new AiVision
        {
            // Very tall, very narrow: 60 degrees of elevation, 5 of azimuth.
            Primary = new AiViewCone(60 * AiViewCone.DegreesToRadians, 5 * AiViewCone.DegreesToRadians, 1e9, 1e9),
            Secondary = new AiViewCone(0, 0, 0, 0),
        };
        var eye = new Vector3d(0, 0, 0);

        // 30 degrees up: inside the tall cone.
        Assert.True(vision.IsVisible(eye, LookingAlongY, new Vector3d(0, 1000, 577), false));

        // 30 degrees to the side: outside the narrow one.
        Assert.False(vision.IsVisible(eye, LookingAlongY, new Vector3d(577, 1000, 0), false));
    }

    /// <summary>The rim is exclusive — the angle comparisons are strict where the range comparison is not.</summary>
    [Fact]
    public void TheRimIsExclusive()
    {
        double limit = 45 * Math.PI / 180.0;
        var vision = new AiVision
        {
            Primary = new AiViewCone(limit, limit, 1e9, 1e9),
            Secondary = new AiViewCone(0, 0, 0, 0),
        };
        var eye = new Vector3d(0, 0, 0);

        // Exactly 45 degrees up is on the rim and therefore out.
        Assert.False(vision.IsVisible(eye, LookingAlongY, new Vector3d(0, 1000, 1000), false));
        Assert.True(vision.IsVisible(eye, LookingAlongY, new Vector3d(0, 1000, 999), false));
    }

    /// <summary>
    /// <b>The two cones are OR'd, not selected.</b> A point outside the long narrow cone but inside the short
    /// wide one is still seen — which is the whole point of having a second cone, and what the old note's
    /// "cone selection switches on the alert latch" got wrong.
    /// </summary>
    [Fact]
    public void APointInEitherConeIsSeen()
    {
        var vision = new AiVision
        {
            // Long and narrow.
            Primary = new AiViewCone(5 * AiViewCone.DegreesToRadians, 5 * AiViewCone.DegreesToRadians, 100_000, 100_000),
            // Short and wide.
            Secondary = new AiViewCone(80 * AiViewCone.DegreesToRadians, 80 * AiViewCone.DegreesToRadians, 2000, 2000),
        };
        var eye = new Vector3d(0, 0, 0);

        // Far and straight ahead: only the primary reaches it.
        Assert.True(vision.IsVisible(eye, LookingAlongY, new Vector3d(0, 50_000, 0), false));

        // Near and well off to the side: only the secondary covers it.
        Assert.True(vision.IsVisible(eye, LookingAlongY, new Vector3d(700, 1000, 0), false));

        // Far and off to the side: neither.
        Assert.False(vision.IsVisible(eye, LookingAlongY, new Vector3d(35_000, 50_000, 0), false));
    }

    /// <summary>The alert latch lengthens each cone rather than choosing between them.</summary>
    [Fact]
    public void TheAlertLatchPicksEachConesRange()
    {
        var vision = new AiVision
        {
            Primary = new AiViewCone(1.0, 1.0, 4096.0, 40_960.0),
            Secondary = new AiViewCone(0, 0, 0, 0),
        };
        var eye = new Vector3d(0, 0, 0);
        var far = new Vector3d(0, 20_000, 0);

        Assert.False(vision.IsVisible(eye, LookingAlongY, far, alerted: false));
        Assert.True(vision.IsVisible(eye, LookingAlongY, far, alerted: true));
    }

    /// <summary>A yawed eye rotates the cone with it, which a transposed transform would get backwards.</summary>
    [Fact]
    public void TheConeTurnsWithTheEye()
    {
        // Yaw 90 degrees: local Y (forward) now points along world +X.
        // Columns are the local axes in world space, so column 1 must be (1, 0, 0).
        var yawed = new Matrix3f(
            0, 1, 0,
            -1, 0, 0,
            0, 0, 1);

        var vision = new AiVision
        {
            Primary = new AiViewCone(10 * AiViewCone.DegreesToRadians, 10 * AiViewCone.DegreesToRadians, 1e9, 1e9),
            Secondary = new AiViewCone(0, 0, 0, 0),
        };
        var eye = new Vector3d(0, 0, 0);

        Assert.True(vision.IsVisible(eye, yawed, new Vector3d(10_000, 0, 0), false));
        Assert.False(vision.IsVisible(eye, yawed, new Vector3d(0, 10_000, 0), false));
    }

    /// <summary>A target at the eye itself is degenerate and the angle helper yields zero rather than NaN.</summary>
    [Fact]
    public void ADegenerateDirectionYieldsZero()
    {
        Assert.Equal(0.0, AiVision.AngleFromForward(0, 0));

        var vision = new AiVision();
        Assert.True(vision.IsVisible(default, LookingAlongY, default, false));
    }

    /// <summary>The length setters take metres and the angle setters degrees.</summary>
    [Fact]
    public void TheSettersConvertTheirUnits()
    {
        AiViewCone cone = default;

        Assert.Equal(40 * 4096.0, cone.WithLengthInMetres(40).Length);

        // The factor is a single-precision constant that the original widens to double before multiplying,
        // which is what the cast reproduces. Doing the arithmetic in float instead is off in the tenth digit.
        Assert.Equal(50.0 * (double)AiViewCone.DegreesToRadians, cone.WithAlphaInDegrees(50).Alpha);
        Assert.Equal(45.0 * (double)AiViewCone.DegreesToRadians, cone.WithGammaInDegrees(45).Gamma);
        Assert.NotEqual(50 * AiViewCone.DegreesToRadians, cone.WithAlphaInDegrees(50).Alpha);
    }

    /// <summary>
    /// Setting the normal length above the alarm length <b>raises the alarm length to match</b>, so an alerted
    /// AI can never see less far than a calm one — unless a script sets the alarm length afterwards, which has
    /// no clamp of its own.
    /// </summary>
    [Fact]
    public void SettingTheLengthRaisesTheAlarmLengthToMatch()
    {
        AiViewCone cone = new AiViewCone(0, 0, 0, 10 * 4096.0).WithLengthInMetres(40);
        Assert.Equal(40 * 4096.0, cone.Length);
        Assert.Equal(40 * 4096.0, cone.AlarmLength);

        // But a longer alarm length is left alone.
        AiViewCone longer = new AiViewCone(0, 0, 0, 90 * 4096.0).WithLengthInMetres(40);
        Assert.Equal(90 * 4096.0, longer.AlarmLength);

        // And the alarm setter itself does not clamp, so a shorter alarm view is possible.
        AiViewCone shorter = cone.WithAlarmLengthInMetres(5);
        Assert.Equal(5 * 4096.0, shorter.AlarmLength);
        Assert.True(shorter.AlarmLength < shorter.Length);
    }

    /// <summary>
    /// The values the shipped archetypes actually use. The sniper is the telling one: 100 m of reach through a
    /// cone twice as tall as it is wide, which is what covering an approach from height needs.
    /// </summary>
    [Theory]
    [InlineData(40, 50, 50, 45)]      // guard, patrol and civilian all agree
    [InlineData(100, 110, 30, 15)]    // sniper
    public void TheRetailArchetypeValuesConvertAsExpected(int length, int alarm, int alpha, int gamma)
    {
        AiViewCone cone = AiViewCone.DefaultPrimary
            .WithLengthInMetres(length)
            .WithAlarmLengthInMetres(alarm)
            .WithAlphaInDegrees(alpha)
            .WithGammaInDegrees(gamma);

        Assert.Equal(length * 4096.0, cone.Length);
        Assert.Equal(alarm * 4096.0, cone.AlarmLength);
        Assert.Equal(alpha, Degrees(cone.Alpha), 4);
        Assert.Equal(gamma, Degrees(cone.Gamma), 4);
        Assert.True(cone.AlarmLength > cone.Length);
    }

    private static double Degrees(double radians) => radians / AiViewCone.DegreesToRadians;
}

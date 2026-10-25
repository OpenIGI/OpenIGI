using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Weapons;
using Xunit;

namespace OpenIGI.Desktop.Tests;

/// <summary>
/// The geometry the shot march resolves a surface with — the angle a round arrives at and the
/// direction it leaves in when it glances off. Both come from <c>0x47B750</c>'s deflection block.
/// </summary>
public sealed class GunShotResolutionTests
{
    /// <summary>
    /// The collision index turns every normal to face the ray, so a round arriving square on a wall
    /// has an incidence of zero and one arriving along it approaches a right angle. That orientation
    /// is what makes <see cref="GunShotBallistics.DeflectionChance"/> a grazing-angle test.
    /// </summary>
    [Fact]
    public void IncidenceIsZeroHeadOnAndAQuarterTurnAlongTheSurface()
    {
        Vector3f wall = new(0f, -1f, 0f);

        Assert.Equal(0f, GunShotBallistics.IncidenceRadians(new Vector3d(0, 1, 0), wall), 1e-4f);
        Assert.Equal(
            MathF.PI / 2f,
            GunShotBallistics.IncidenceRadians(new Vector3d(1, 0, 0), wall),
            1e-4f);
        Assert.Equal(
            MathF.PI / 4f,
            GunShotBallistics.IncidenceRadians(new Vector3d(1, 1, 0), wall),
            1e-4f);
    }

    /// <summary>A square hit never deflects and a grazing one nearly always does, off the same row.</summary>
    [Fact]
    public void TheGrazeChanceFollowsTheAngleOfArrival()
    {
        // Concrete's authored limit, 25 degrees.
        const float limit = 25f;

        Assert.True(
            GunShotBallistics.DeflectionChance(
                limit, GunShotBallistics.IncidenceRadians(new Vector3d(0, 1, 0), new Vector3f(0f, -1f, 0f))) <= 0f);

        float grazing = GunShotBallistics.DeflectionChance(
            limit, GunShotBallistics.IncidenceRadians(new Vector3d(1, 0.02, 0), new Vector3f(0f, -1f, 0f)));
        Assert.InRange(grazing, 0.9f, 1f);
    }

    /// <summary>A row that names no limit never deflects, however oblique the approach.</summary>
    [Fact]
    public void AMaterialWithNoLimitNeverDeflects()
    {
        // Flesh and Snow both declare zero.
        Assert.Equal(0f, GunShotBallistics.DeflectionChance(0f, MathF.PI / 2f));
    }

    /// <summary>The round leaves mirrored about the normal, and keeps unit length.</summary>
    [Fact]
    public void ADeflectedRoundMirrorsAboutTheSurface()
    {
        Vector3d away = PlayerShotResolver.Reflect(new Vector3d(1, -1, 0), new Vector3f(0f, 1f, 0f));

        Assert.Equal(1.0 / Math.Sqrt(2.0), away.X, 1e-6);
        Assert.Equal(1.0 / Math.Sqrt(2.0), away.Y, 1e-6);
        Assert.Equal(0.0, away.Z, 1e-6);
        Assert.Equal(1.0, Math.Sqrt((away.X * away.X) + (away.Y * away.Y) + (away.Z * away.Z)), 1e-6);
    }

    /// <summary>
    /// The authored rows drive the bite. A rifle beats wood's resistance of one outright, meets
    /// StrongMetal's ten and stops, and matches Ground's ten only six times in ten.
    /// </summary>
    [Fact]
    public void TheAuthoredResistanceDecidesWhatARoundGetsThrough()
    {
        Assert.True(GunShotBallistics.Penetrates(penetrationPower: 10f, surfaceResistance: 1f, 0.99f));
        Assert.False(GunShotBallistics.Penetrates(penetrationPower: 5f, surfaceResistance: 10f, 0.01f));

        Assert.True(GunShotBallistics.Penetrates(penetrationPower: 10f, surfaceResistance: 10f, 0.59f));
        Assert.False(GunShotBallistics.Penetrates(penetrationPower: 10f, surfaceResistance: 10f, 0.61f));

        // The original's own gap: a margin between zero and one stops the round even though the equal
        // case gets through. Kept because it is what the code does.
        Assert.False(GunShotBallistics.Penetrates(penetrationPower: 10.5f, surfaceResistance: 10f, 0.01f));
    }

    /// <summary>A round may glance once and no more, however many grazing surfaces it meets.</summary>
    [Fact]
    public void ARoundGlancesOnce()
    {
        Assert.True(GunShotBallistics.CanDeflect(hasAlreadyDeflected: false));
        Assert.False(GunShotBallistics.CanDeflect(hasAlreadyDeflected: true));
    }
}

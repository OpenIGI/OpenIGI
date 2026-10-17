using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using Xunit;
using OpenIGI.Game.Igi1.Player;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Covers the general soldier death state — which clip it names (<c>0x45C440</c>), the turn it makes
/// first (<c>0x45DAA0</c>) and the nine-ray fall solver behind it (<c>0x45D5B0</c>).
/// </summary>
public sealed class SoldierDeathTests
{
    /// <summary>A shotgun throws the body; everything else crumples it.</summary>
    [Theory]
    [InlineData(WeaponCategory.Shotgun, SoldierDeathMode.Thrown)]
    [InlineData(WeaponCategory.Gun, SoldierDeathMode.Crumple)]
    [InlineData(WeaponCategory.Pistol, SoldierDeathMode.Crumple)]
    [InlineData(WeaponCategory.Knife, SoldierDeathMode.Crumple)]
    [InlineData(WeaponCategory.Grenade, SoldierDeathMode.Crumple)]
    public void TheKillingWeaponsCategoryPicksTheDeathFamily(WeaponCategory category, SoldierDeathMode mode) =>
        Assert.Equal(mode, SoldierDeath.ModeFor(category));

    /// <summary>
    /// A thrown body falls backwards when the round came at its front and forwards when it came from
    /// behind — and the mode the constructor leaves behind is handled with the thrown one.
    /// </summary>
    [Theory]
    [InlineData(SoldierDeathMode.Thrown, true, SoldierDeath.ThrownBackwardsAnimation)]
    [InlineData(SoldierDeathMode.Thrown, false, SoldierDeath.ThrownForwardsAnimation)]
    [InlineData(SoldierDeathMode.Untouched, true, SoldierDeath.ThrownBackwardsAnimation)]
    [InlineData(SoldierDeathMode.Untouched, false, SoldierDeath.ThrownForwardsAnimation)]
    public void AThrownBodyFallsAlongTheRound(SoldierDeathMode mode, bool front, int animation) =>
        Assert.Equal(
            animation,
            SoldierDeath.Choose(SoldierDeathClips.Soldier, mode, zone: 3, HumanHitZoneTable.Soldier, front, coinFlip: 0));

    /// <summary>An ordinary kill picks one of the two crumple clips, in the order the flip indexes them.</summary>
    [Fact]
    public void ACrumpleAlternatesBetweenItsTwoClips()
    {
        Assert.Equal(23, Choose(zone: 3, flip: 0));
        Assert.Equal(22, Choose(zone: 3, flip: 1));
    }

    /// <summary>
    /// A head or neck shot takes its clip out of the hit-zone table's own row, which is what confirms a
    /// row's trailing two integers are animation ids.
    /// </summary>
    /// <remarks>
    /// <c>0x45C440</c> indexes <c>row + 4·flip + 8</c>, i.e. <c>ReactionA</c> and <c>ReactionB</c>. Retail
    /// gives the head <c>30, 30</c> and the neck <c>22, 22</c>, so the flip is a no-op on shipped data —
    /// asserted both ways so that stays a measurement rather than an assumption.
    /// </remarks>
    [Fact]
    public void AHeadOrNeckShotTakesItsClipFromTheHitZoneRow()
    {
        Assert.Equal(30, Choose(SoldierDeath.HeadZone, flip: 0));
        Assert.Equal(30, Choose(SoldierDeath.HeadZone, flip: 1));
        Assert.Equal(22, Choose(SoldierDeath.NeckZone, flip: 0));
        Assert.Equal(22, Choose(SoldierDeath.NeckZone, flip: 1));

        Assert.Equal(30, HumanHitZoneTable.Soldier[SoldierDeath.HeadZone].ReactionA);
        Assert.Equal(22, HumanHitZoneTable.Soldier[SoldierDeath.NeckZone].ReactionA);
    }

    /// <summary>The script's own override wins over all of it, and is tested before anything is computed.</summary>
    [Fact]
    public void TheScriptOverrideWins() =>
        Assert.Equal(
            9,
            SoldierDeath.Choose(
                SoldierDeathClips.Soldier,
                SoldierDeathMode.Crumple,
                SoldierDeath.HeadZone,
                HumanHitZoneTable.Soldier,
                shotFromTheFront: true,
                coinFlip: 0,
                scriptOverride: 9));

    /// <summary>
    /// The dying body turns to the round: struck from the front it faces the shooter, struck from behind
    /// it faces away. Either way the fall then plays along the round's line.
    /// </summary>
    [Fact]
    public void TheBodyTurnsToTheRound()
    {
        // Facing +Y. Forward is column 1 of the heading matrix, so (0, 1, 0).
        Vector3f forward = new(0f, 1f, 0f);

        // A round travelling -Y is coming at the front.
        float facing = 0f;
        Assert.True(SoldierDeath.TurnToTheShot(new Vector3d(0.0, -1.0, 0.0), forward, ref facing));

        // The bearing of -Y is pi; a front hit adds another pi, which is a full turn back to facing +Y.
        // Under the engine's convention a heading g looks along (-sin g, cos g), so that is straight back
        // up the round's line -- at the shooter.
        Assert.Equal(0.0, -Math.Sin(facing), 5);
        Assert.Equal(1.0, Math.Cos(facing), 5);

        // A round travelling +Y came from behind: keep facing away from the shooter.
        facing = 0f;
        Assert.False(SoldierDeath.TurnToTheShot(new Vector3d(0.0, 1.0, 0.0), forward, ref facing));
        Assert.Equal(0f, facing, 5);
    }

    /// <summary>A kill that carries no direction leaves the heading alone and falls backwards.</summary>
    [Fact]
    public void AZeroDirectionLeavesTheHeadingAlone()
    {
        float facing = 1.25f;
        Assert.True(SoldierDeath.TurnToTheShot(default, new Vector3f(0f, 1f, 0f), ref facing));
        Assert.Equal(1.25f, facing);
    }

    /// <summary>The fan is nine rays: three heights at −0.8, −0.4 and 0 m, three angles at ±22°.</summary>
    [Fact]
    public void TheFanIsNineRaysAtThreeHeightsAndThreeAngles()
    {
        Vector3d body = new(1000.0, 2000.0, 3000.0);
        Vector3d direction = new(0.0, 8192.0, 0.0);

        IReadOnlyList<(Vector3d From, Vector3d To)> rays = SoldierDeathFall.Fan(body, direction);
        Assert.Equal(SoldierDeathFall.RayCount, rays.Count);

        double[] heights = [-3276.8, -1638.4, 0.0];
        for (int i = 0; i < rays.Count; i++)
        {
            (Vector3d from, Vector3d to) = rays[i];
            Assert.Equal(body.X, from.X, 6);
            Assert.Equal(body.Y, from.Y, 6);
            Assert.Equal(body.Z + heights[i / 3], from.Z, 3);

            // Every ray is the same length as the direction, only turned.
            double dx = to.X - from.X;
            double dy = to.Y - from.Y;
            Assert.Equal(8192.0, Math.Sqrt((dx * dx) + (dy * dy)), 2);
        }

        // The middle ray of each triple is straight ahead; the outer two are 22 degrees either side, in
        // the engine's own heading convention where a bearing is atan2(-x, y).
        Assert.Equal(0.0, BearingOf(rays[1]), 5);
        Assert.Equal(-22.0, BearingOf(rays[0]) * 180.0 / Math.PI, 3);
        Assert.Equal(22.0, BearingOf(rays[2]) * 180.0 / Math.PI, 3);
    }

    /// <summary>
    /// Nothing in the way means no correction at all — the clip's root motion travels unchecked.
    /// </summary>
    [Fact]
    public void AClearFanCorrectsNothing()
    {
        Vector3d velocity = SoldierDeathFall.Solve(
            Travelling(4096f, 30f),
            new Vector3d(0.0, 0.0, 0.0),
            facing: 0f,
            (_, _) => default);

        Assert.Equal(0.0, velocity.X, 9);
        Assert.Equal(0.0, velocity.Y, 9);
        Assert.Equal(0.0, velocity.Z, 9);
    }

    /// <summary>
    /// An obstruction at the body itself takes the whole stride back, so the body ends the clip where it
    /// started.
    /// </summary>
    [Fact]
    public void AnObstructionAtTheFeetCancelsTheWholeStride()
    {
        // 30 ticks of clip, a 4096-unit stride, so the extended reach is 8192.
        BoneAnimation clip = Travelling(4096f, 30f);
        Vector3d body = new(0.0, 0.0, 0.0);

        Vector3d velocity = SoldierDeathFall.Solve(
            clip,
            body,
            facing: 0f,
            (from, _) => new SoldierDeathRay(true, new Vector3d(from.X, from.Y, from.Z), 0.0));

        // The correction runs for the clip's whole duration, so summed it is minus the extended direction.
        Assert.Equal(0.0, velocity.X, 6);
        Assert.Equal(-8192.0 / 30.0, velocity.Y, 4);
    }

    /// <summary>
    /// An obstruction part of the way along scales the correction by the shortfall, so the body travels
    /// exactly as far as there is room for.
    /// </summary>
    [Fact]
    public void ThePartialCorrectionLeavesTheBodyAtTheObstruction()
    {
        BoneAnimation clip = Travelling(4096f, 30f);
        Vector3d body = new(0.0, 0.0, 0.0);
        const double reach = 8192.0;
        const double gap = 2048.0;

        Vector3d velocity = SoldierDeathFall.Solve(
            clip,
            body,
            facing: 0f,
            (from, _) => new SoldierDeathRay(true, new Vector3d(from.X, from.Y + gap, from.Z), gap));

        // Over the clip the correction sums to -(1 - gap/reach) x reach along +Y, and the clip's own root
        // motion contributes +4096 -- so what is left is the gap, up to the extension the solver adds.
        double summed = velocity.Y * 30.0;
        Assert.Equal(-(1.0 - (gap / reach)) * reach, summed, 3);
        Assert.Equal(gap, reach + summed, 3);
    }

    /// <summary>An obstruction beyond the reach is not an obstruction: nothing is corrected.</summary>
    [Fact]
    public void AnObstructionBeyondTheReachIsIgnored()
    {
        Vector3d velocity = SoldierDeathFall.Solve(
            Travelling(4096f, 30f),
            new Vector3d(0.0, 0.0, 0.0),
            facing: 0f,
            (from, _) => new SoldierDeathRay(true, new Vector3d(from.X, from.Y + 100000.0, from.Z), 100000.0));

        Assert.Equal(0.0, velocity.Y, 9);
    }

    /// <summary>The correction is turned by the body's heading, so it always opposes the clip's own line.</summary>
    [Fact]
    public void TheCorrectionFollowsTheHeading()
    {
        // Facing +90 degrees about Z: the model's +Y forward now points along world -X.
        Vector3d velocity = SoldierDeathFall.Solve(
            Travelling(4096f, 30f),
            new Vector3d(0.0, 0.0, 0.0),
            facing: MathF.PI / 2f,
            (from, _) => new SoldierDeathRay(true, new Vector3d(from.X, from.Y, from.Z), 0.0));

        Assert.Equal(8192.0 / 30.0, velocity.X, 3);
        Assert.Equal(0.0, velocity.Y, 3);
    }

    /// <summary>A clip that does not travel needs no solving.</summary>
    [Fact]
    public void AStaticClipCorrectsNothing()
    {
        Vector3d velocity = SoldierDeathFall.Solve(
            Travelling(0f, 30f),
            new Vector3d(0.0, 0.0, 0.0),
            facing: 0f,
            (from, _) => new SoldierDeathRay(true, new Vector3d(from.X, from.Y, from.Z), 0.0));

        Assert.Equal(0.0, velocity.Y, 9);
    }

    private static double BearingOf((Vector3d From, Vector3d To) ray) =>
        Math.Atan2(-(ray.To.X - ray.From.X), ray.To.Y - ray.From.Y);

    private static int Choose(int zone, int flip) => SoldierDeath.Choose(
        SoldierDeathClips.Soldier,
        SoldierDeathMode.Crumple,
        zone,
        HumanHitZoneTable.Soldier,
        shotFromTheFront: true,
        flip);

    /// <summary>A clip whose root track walks a given distance along +Y over a given number of ticks.</summary>
    private static BoneAnimation Travelling(float distance, float ticks) => BoneAnimation.Create(
        id: 1,
        duration: ticks * BoneAnimation.FrameStep,
        rotations: [[]],
        translation:
        [
            new BoneTranslationKey(new Vector3f(0f, 0f, 3877f), 0f, default, default),
            new BoneTranslationKey(new Vector3f(0f, distance, 600f), ticks * BoneAnimation.FrameStep, default, default),
        ]);
}

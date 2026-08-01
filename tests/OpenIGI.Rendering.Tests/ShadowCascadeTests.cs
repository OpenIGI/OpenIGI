using OpenIGI.Core.Numerics;
using OpenIGI.Rendering;
using Xunit;

namespace OpenIGI.Rendering.Tests;

/// <summary>
/// Pins the two properties that decide whether shadows hold still or crawl.
/// </summary>
/// <remarks>
/// Both failures are the kind that look like "the shadows are a bit noisy" rather than like a bug in
/// the fitting: a radius that depends on the camera's rotation makes every edge boil as the player
/// turns, and a centre that moves smoothly makes the staircase along each edge swim as they walk.
/// Neither is visible in a screenshot, which is why they are asserted here.
/// </remarks>
public sealed class ShadowCascadeTests
{
    private static readonly Vector3f Sun = new Vector3f(0.433f, 0.087f, 0.866f).Normalized();
    private static readonly ShadowSettings Settings = new(CascadeCount: 3, Resolution: 2048, Distance: 200f);

    private static Camera3D CameraAt(float yaw, float pitch = 0f, double x = 0, double y = 0, double z = 0) =>
        new(new Vector3d(x, y, z), Camera3D.LookRotation(yaw, pitch));

    [Fact]
    public void TheRadiusDoesNotChangeWhenTheCameraTurns()
    {
        Span<ShadowCascade> a = stackalloc ShadowCascade[ShadowSettings.MaxCascades];
        Span<ShadowCascade> b = stackalloc ShadowCascade[ShadowSettings.MaxCascades];

        ShadowCascades.Fit(CameraAt(0f), Sun, Settings, 1.2f, 1.777f, 0.1f, a);
        ShadowCascades.Fit(CameraAt(2.3f, 0.4f), Sun, Settings, 1.2f, 1.777f, 0.1f, b);

        // A box fitted to the frustum corners would grow and shrink through the turn; a sphere through
        // the same corners cannot, because a rotation does not change distances.
        for (int i = 0; i < Settings.CascadeCount; i++)
        {
            Assert.Equal(a[i].Radius, b[i].Radius, 4);
        }
    }

    [Fact]
    public void TheProjectionOriginIsQuantisedToTheTexelGrid()
    {
        Span<ShadowCascade> cascades = stackalloc ShadowCascade[ShadowSettings.MaxCascades];

        for (int step = 0; step < 32; step++)
        {
            ShadowCascades.Fit(
                CameraAt(0.3f, 0f, x: step * 0.017, y: step * 0.009),
                Sun,
                Settings,
                1.2f,
                1.777f,
                0.1f,
                cascades);

            ShadowCascade cascade = cascades[0];

            // Recover the box's left edge from the projection: x' = 2x/(r-l) - (r+l)/(r-l), so the
            // scale gives the width and the offset gives the centre. Both must sit on the texel grid.
            float halfWidth = 1f / cascade.Projection.M11;
            float centreX = -cascade.Projection.M14 * halfWidth;
            float texels = centreX / cascade.TexelSize;

            Assert.Equal(MathF.Round(texels), texels, 2);
        }
    }

    [Fact]
    public void SlicesTileTheShadowDistanceWithoutGaps()
    {
        Span<ShadowCascade> cascades = stackalloc ShadowCascade[ShadowSettings.MaxCascades];
        int count = ShadowCascades.Fit(CameraAt(0f), Sun, Settings, 1.2f, 1.777f, 0.1f, cascades);

        Assert.Equal(3, count);
        Assert.True(cascades[0].SplitDistance < cascades[1].SplitDistance);
        Assert.True(cascades[1].SplitDistance < cascades[2].SplitDistance);
        Assert.Equal(Settings.Distance, cascades[2].SplitDistance, 2);

        // The near cascade must be much tighter than the far one, or its resolution is being spent on
        // ground the player cannot make out anyway.
        Assert.True(
            cascades[0].Radius * 4f < cascades[2].Radius,
            $"near {cascades[0].Radius} should be far tighter than far {cascades[2].Radius}");
    }

    [Fact]
    public void ASunOverheadStillProducesAUsableBasis()
    {
        // Mission 7's sun is 84 degrees up. A fixed world up would be very nearly parallel to the
        // light's forward axis and the cross product would collapse.
        Matrix3f rotation = ShadowCascades.LightRotationFor(new Vector3f(0f, 0f, 1f));

        Assert.Equal(1f, rotation.Row1.Length, 4);
        Assert.Equal(1f, rotation.Row2.Length, 4);
        Assert.Equal(1f, rotation.Row3.Length, 4);
        Assert.Equal(0f, Vector3f.Dot(rotation.Row1, rotation.Row2), 4);
        Assert.Equal(0f, Vector3f.Dot(rotation.Row2, rotation.Row3), 4);
    }

    [Fact]
    public void TheLightLooksAwayFromTheSun()
    {
        // Forward is the direction light travels, which is the opposite of the direction to the sun.
        Matrix3f rotation = ShadowCascades.LightRotationFor(new Vector3f(0f, 0f, 1f));

        Assert.Equal(-1f, rotation.Row3.Z, 4);
    }

    [Fact]
    public void TheBoxReachesBehindTheSliceSoDistantGeometryStillCasts()
    {
        Span<ShadowCascade> cascades = stackalloc ShadowCascade[ShadowSettings.MaxCascades];
        ShadowCascades.Fit(CameraAt(0f), Sun, Settings, 1.2f, 1.777f, 0.1f, cascades);

        ShadowCascade cascade = cascades[0];

        // A tower outside the near cascade still throws a shadow across it, so the light's box has to
        // open backwards far enough to contain it. Depth spans radius plus the caster reach.
        float depthSpan = 2f / cascade.Projection.M33;
        Assert.True(
            depthSpan >= ShadowCascades.CasterReach,
            $"depth span {depthSpan} must cover the caster reach {ShadowCascades.CasterReach}");
    }

    [Fact]
    public void ShadowsOffFitsNothing()
    {
        Span<ShadowCascade> cascades = stackalloc ShadowCascade[ShadowSettings.MaxCascades];

        Assert.Equal(0, ShadowCascades.Fit(CameraAt(0f), Sun, ShadowSettings.Off, 1.2f, 1.777f, 0.1f, cascades));
    }
}

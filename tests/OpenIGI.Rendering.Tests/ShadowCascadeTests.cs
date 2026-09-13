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

    /// <summary>
    /// The box's origin lands on a texel grid fixed in the world, not on one that travels with the player.
    /// </summary>
    /// <remarks>
    /// This used to assert the camera-relative centre was quantised, which every build satisfies and
    /// which pins nothing: the centre is derived from the frustum, the frustum is bolted to the camera,
    /// so that number hardly moves as the player walks and snapping it lands on the same grid position
    /// every frame while the world slides underneath. The staircase along each shadow edge crawls
    /// exactly as it would with no snapping at all. What has to be on the grid is the box's WORLD
    /// origin, which is the camera's own position in light space plus the camera-relative centre.
    /// </remarks>
    [Fact]
    public void TheProjectionOriginIsQuantisedToAWorldTexelGrid()
    {
        Span<ShadowCascade> cascades = stackalloc ShadowCascade[ShadowSettings.MaxCascades];

        // Off the origin by a level's worth of metres, because the whole difficulty is that a world
        // coordinate here is large and the offset being quantised is small.
        const double baseX = 6023.296;
        const double baseY = -13645.510;

        for (int step = 0; step < 32; step++)
        {
            Camera3D camera = CameraAt(0.3f, 0f, x: baseX + (step * 0.017), y: baseY + (step * 0.009));
            ShadowCascades.Fit(camera, Sun, Settings, 1.2f, 1.777f, 0.1f, cascades);

            ShadowCascade cascade = cascades[0];

            // Recover the box's left edge from the projection: x' = 2x/(r-l) - (r+l)/(r-l), so the
            // scale gives the width and the offset gives the centre — camera-relative, as stored.
            float halfWidth = 1f / cascade.Projection.M11;
            float centreX = -cascade.Projection.M14 * halfWidth;
            float centreY = -cascade.Projection.M24 * (1f / cascade.Projection.M22);

            // Back into the world by adding the camera, which is where the grid actually lives.
            Vector3d lightCamera = cascade.LightRotation.Transposed().Transform(camera.Position);
            double worldX = lightCamera.X + centreX;
            double worldY = lightCamera.Y + centreY;

            Assert.Equal(Math.Round(worldX / cascade.TexelSize), worldX / cascade.TexelSize, 2);
            Assert.Equal(Math.Round(worldY / cascade.TexelSize), worldY / cascade.TexelSize, 2);
        }
    }

    /// <summary>
    /// Walking a fraction of a texel does not move the box: it holds still, then jumps a whole texel.
    /// </summary>
    /// <remarks>
    /// The property the snap exists for, stated as the player experiences it. A grid anchored to the
    /// camera fails this — every step moves the box by exactly the step — and it is the one assertion
    /// that separates the two.
    /// </remarks>
    [Fact]
    public void TheBoxHoldsStillInTheWorldWhileTheCameraWalksWithinOneTexel()
    {
        Span<ShadowCascade> a = stackalloc ShadowCascade[ShadowSettings.MaxCascades];
        Span<ShadowCascade> b = stackalloc ShadowCascade[ShadowSettings.MaxCascades];

        const double baseX = 6023.296;
        const double baseY = -13645.510;

        ShadowCascades.Fit(CameraAt(0.3f, 0f, x: baseX, y: baseY), Sun, Settings, 1.2f, 1.777f, 0.1f, a);

        // A tenth of the near cascade's texel: far too small to move a grid anchored in the world.
        double stride = a[0].TexelSize * 0.1;
        ShadowCascades.Fit(
            CameraAt(0.3f, 0f, x: baseX + stride, y: baseY), Sun, Settings, 1.2f, 1.777f, 0.1f, b);

        Vector3d lightA = a[0].LightRotation.Transposed().Transform(new Vector3d(baseX, baseY, 0));
        Vector3d lightB = b[0].LightRotation.Transposed().Transform(new Vector3d(baseX + stride, baseY, 0));

        double worldA = lightA.X + (-a[0].Projection.M14 / a[0].Projection.M11);
        double worldB = lightB.X + (-b[0].Projection.M14 / b[0].Projection.M11);

        // Either it did not move at all, or it jumped a whole texel — never the fraction the camera did.
        double moved = Math.Abs(worldB - worldA);
        Assert.True(
            moved < a[0].TexelSize * 0.01 || Math.Abs(moved - a[0].TexelSize) < a[0].TexelSize * 0.01,
            $"the box moved {moved} m, which is neither nothing nor one texel of {a[0].TexelSize} m");
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

    /// <summary>
    /// Pins the one expression a backend may compose a cascade's lookup matrix from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The engine carries two multiply conventions and this is where they meet.
    /// <see cref="Matrix3f.Transform(Vector3f)"/> dots a matrix's COLUMNS — a row-vector multiply — so projecting a
    /// position onto the light's axes is spelled with a transpose, which is how
    /// <see cref="ShadowCascade.ToLightClip"/> reads. A <see cref="Matrix4f"/> dots its ROWS, so the same
    /// rotation goes into <see cref="Matrix4f.CreateRigid"/> with no transpose at all. Carrying the first
    /// spelling into the second composes the INVERSE light rotation, and the transposes are so close
    /// together in the source that the mistake reads as the careful version.
    /// </para>
    /// <para>
    /// Nothing catches it downstream. The shadow map is rasterised through
    /// <see cref="Camera3D.CreateModelView(Vector3d, Matrix3f)"/>, which gets the convention right, so casters land correctly
    /// and only the RECEIVER's lookup is rotated the other way — shadows sit somewhere other than the
    /// thing casting them, by an amount that grows with a point's distance from the camera and so slides
    /// across the world as the camera moves. The Vulkan backend shipped it; OpenGL did not. A sun on an
    /// axis, or a rotation that happens to be symmetric, hides it completely, which is why the sun here
    /// is off every axis.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheLightClipMatrixMatchesTheCascadesOwnProjection()
    {
        Span<ShadowCascade> cascades = stackalloc ShadowCascade[ShadowSettings.MaxCascades];
        int count = ShadowCascades.Fit(CameraAt(1.1f, -0.3f), Sun, Settings, 1.2f, 1.777f, 0.1f, cascades);

        // Camera-relative positions spread over a cascade, none of them symmetric about any axis, so a
        // rotation applied the wrong way round cannot land on the right answer by accident.
        ReadOnlySpan<Vector3f> samples =
        [
            new(3f, 17f, -2f),
            new(-11f, 42f, 5f),
            new(7f, 90f, -13f),
        ];

        for (int i = 0; i < count; i++)
        {
            Matrix4f composed = cascades[i].Projection * Matrix4f.CreateRigid(cascades[i].LightRotation, default);

            foreach (Vector3f sample in samples)
            {
                Vector3f expected = cascades[i].ToLightClip(sample);
                Vector3f actual = composed.TransformPoint(sample);

                Assert.Equal(expected.X, actual.X, 4);
                Assert.Equal(expected.Y, actual.Y, 4);
                Assert.Equal(expected.Z, actual.Z, 4);
            }
        }
    }

    /// <summary>
    /// Shows that the transposed spelling is a real difference, not a formality.
    /// </summary>
    /// <remarks>
    /// Without this the test above could be satisfied by a light rotation that is its own transpose, and
    /// would then pin nothing. This asserts the two spellings genuinely disagree for this sun.
    /// </remarks>
    [Fact]
    public void TransposingTheLightRotationIntoTheMatrixMovesTheLookup()
    {
        Span<ShadowCascade> cascades = stackalloc ShadowCascade[ShadowSettings.MaxCascades];
        ShadowCascades.Fit(CameraAt(1.1f, -0.3f), Sun, Settings, 1.2f, 1.777f, 0.1f, cascades);

        Vector3f sample = new(7f, 90f, -13f);
        Matrix4f transposed =
            cascades[0].Projection * Matrix4f.CreateRigid(cascades[0].LightRotation.Transposed(), default);

        Vector3f correct = cascades[0].ToLightClip(sample);
        Vector3f wrong = transposed.TransformPoint(sample);

        Assert.True(
            MathF.Abs(correct.X - wrong.X) > 0.01f || MathF.Abs(correct.Y - wrong.Y) > 0.01f,
            "the transposed spelling must not agree, or this sun cannot detect the fault");
    }
}

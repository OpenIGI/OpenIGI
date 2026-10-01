using OpenIGI.Core.Numerics;

namespace OpenIGI.Rendering;

/// <summary>
/// Fits a cascaded shadow map to what the camera can see.
/// </summary>
/// <remarks>
/// <para>
/// A directional light has no position, so a shadow map for one is a choice of box: which part of the
/// world to spend the texels on. Everything difficult about cascades is in that choice, and two
/// decisions here are what separate shadows that hold still from shadows that crawl.
/// </para>
/// <para>
/// <b>The slice is bounded by a sphere, not by its corners.</b> A box fitted to the frustum corners
/// changes size as the camera rotates, so every texel lands somewhere new each frame and shadow edges
/// boil. A sphere through the same corners has a radius that does not depend on the camera's rotation
/// at all.
/// </para>
/// <para>
/// <b>The centre is snapped to a texel grid fixed in the WORLD.</b> Even with a fixed radius, a centre
/// that moves smoothly slides the whole map by fractions of a texel and the staircase along every
/// shadow edge swims. Quantising in light space means the map moves in whole texels, so the staircase
/// stays put relative to the world — but only if the grid it lands on is anchored to the world.
/// Everything else here is camera-relative, and quantising a camera-relative centre snaps to a grid
/// that travels with the player: the box then holds the same position in front of them frame after
/// frame while the world slides beneath it, which is the crawl the snapping exists to remove rather
/// than a cure for it. The camera's own position is therefore carried into light space and added in
/// before the floor.
/// </para>
/// </remarks>
public static class ShadowCascades
{
    /// <summary>How far behind a slice, in metres, geometry is still drawn so that it can cast into it.</summary>
    /// <remarks>
    /// A caster does not have to be inside the slice to shadow it — a tower well outside the near
    /// cascade still throws a shadow across it. This is how far back the light's box is opened to
    /// catch them; too little and tall geometry stops casting, too much and depth precision is spent
    /// on empty sky.
    /// </remarks>
    public const float CasterReach = 250f;

    /// <summary>Fits one cascade per slice of the view distance.</summary>
    /// <param name="camera">The camera the slices are measured from.</param>
    /// <param name="sunDirection">The world-space direction <em>towards</em> the sun.</param>
    /// <param name="settings">How many cascades, at what resolution, and how far.</param>
    /// <param name="verticalFieldOfView">The camera's full vertical field of view, in radians.</param>
    /// <param name="aspectRatio">The viewport's width divided by its height.</param>
    /// <param name="near">The camera's near plane.</param>
    /// <param name="cascades">Receives the fitted cascades.</param>
    /// <returns>How many cascades were written.</returns>
    /// <exception cref="ArgumentException"><paramref name="cascades"/> is too short.</exception>
    public static int Fit(
        in Camera3D camera,
        Vector3f sunDirection,
        in ShadowSettings settings,
        float verticalFieldOfView,
        float aspectRatio,
        float near,
        Span<ShadowCascade> cascades)
    {
        int count = settings.EffectiveCascades;
        if (count == 0 || !settings.Enabled)
        {
            return 0;
        }

        if (cascades.Length < count)
        {
            throw new ArgumentException($"Fitting {count} cascade(s) needs room for {count}.", nameof(cascades));
        }

        Matrix3f lightRotation = LightRotationFor(sunDirection);
        float tanY = MathF.Tan(verticalFieldOfView * 0.5f);
        float tanX = tanY * aspectRatio;
        float sliceNear = MathF.Max(near, 0.01f);

        for (int i = 0; i < count; i++)
        {
            float sliceFar = SplitDistance(i + 1, count, sliceNear, settings.Distance, settings.SplitBlend);
            cascades[i] = FitSlice(camera, lightRotation, tanX, tanY, sliceNear, sliceFar, settings.Resolution);
            sliceNear = sliceFar;
        }

        return count;
    }

    /// <summary>Chooses where one slice ends.</summary>
    /// <param name="index">The one-based slice index.</param>
    /// <param name="count">How many slices there are.</param>
    /// <param name="near">The camera's near plane.</param>
    /// <param name="far">How far shadows are drawn.</param>
    /// <param name="blend">Zero for an even division, one for a logarithmic one.</param>
    /// <returns>The view depth the slice ends at.</returns>
    /// <remarks>
    /// The practical split scheme: a logarithmic division matches how quickly a perspective projection
    /// spends pixels, so it is right for the near cascade, but on its own it starves the far one. The
    /// blend is the usual compromise, weighted towards logarithmic.
    /// </remarks>
    public static float SplitDistance(int index, int count, float near, float far, float blend)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);

        float fraction = (float)index / count;
        float logarithmic = near * MathF.Pow(far / near, fraction);
        float uniform = near + ((far - near) * fraction);
        return (logarithmic * Math.Clamp(blend, 0f, 1f)) + (uniform * (1f - Math.Clamp(blend, 0f, 1f)));
    }

    /// <summary>Builds the world-to-light rotation for a sun direction.</summary>
    /// <param name="sunDirection">The world-space direction towards the sun.</param>
    /// <returns>The rotation, rows being the light's right, up and forward axes.</returns>
    /// <remarks>
    /// The light looks <em>along</em> the direction its light travels, which is away from the sun. The
    /// reference up is chosen away from the forward axis so the cross product never collapses — with a
    /// sun overhead, which several missions have, a fixed world up would be parallel to forward and the
    /// basis would be degenerate.
    /// </remarks>
    public static Matrix3f LightRotationFor(Vector3f sunDirection)
    {
        Vector3f forward = sunDirection.LengthSquared > 1e-8f
            ? new Vector3f(-sunDirection.X, -sunDirection.Y, -sunDirection.Z).Normalized()
            : new Vector3f(0f, 0f, -1f);

        Vector3f reference = MathF.Abs(forward.Z) > 0.98f ? new Vector3f(0f, 1f, 0f) : new Vector3f(0f, 0f, 1f);
        Vector3f right = Vector3f.Cross(reference, forward).Normalized();
        Vector3f up = Vector3f.Cross(forward, right).Normalized();

        return Matrix3f.FromRows(right, up, forward);
    }

    /// <summary>Quantises one axis of the cascade centre onto a world-anchored texel grid.</summary>
    /// <param name="cameraAxis">Where the camera sits on this light axis, in world light space.</param>
    /// <param name="centreAxis">The cascade centre on the same axis, relative to the camera.</param>
    /// <param name="texel">How much world space one shadow texel covers.</param>
    /// <returns>The centre, still camera-relative, moved to the nearest grid line below it.</returns>
    /// <remarks>
    /// The addition and the floor happen in double precision because the sum is a world coordinate and
    /// this game's run to tens of thousands of metres; only the difference, which is smaller than the
    /// cascade, is narrowed. Doing the floor in single precision would quantise to the grid of
    /// representable floats near 40000 rather than to the texel grid, which at the near cascade's texel
    /// size is a coarser grid than the one being asked for.
    /// </remarks>
    private static float Snap(double cameraAxis, float centreAxis, float texel)
    {
        double world = cameraAxis + centreAxis;
        return (float)((Math.Floor(world / texel) * texel) - cameraAxis);
    }

    private static ShadowCascade FitSlice(
        in Camera3D camera,
        Matrix3f lightRotation,
        float tanX,
        float tanY,
        float sliceNear,
        float sliceFar,
        int resolution)
    {
        // The slice's eight corners, in view space and then camera-relative world space. The camera
        // offset never enters: a shadow matrix is single precision and level coordinates run into the
        // thousands, which is the same reason the model-view keeps the offset out.
        Span<Vector3f> corners = stackalloc Vector3f[8];
        int index = 0;

        foreach (float depth in stackalloc[] { sliceNear, sliceFar })
        {
            float x = tanX * depth;
            float y = tanY * depth;

            corners[index++] = camera.Orientation.Transform(new Vector3f(-x, -y, depth));
            corners[index++] = camera.Orientation.Transform(new Vector3f(x, -y, depth));
            corners[index++] = camera.Orientation.Transform(new Vector3f(-x, y, depth));
            corners[index++] = camera.Orientation.Transform(new Vector3f(x, y, depth));
        }

        Vector3f centre = default;
        foreach (Vector3f corner in corners)
        {
            centre = new Vector3f(centre.X + corner.X, centre.Y + corner.Y, centre.Z + corner.Z);
        }

        centre = new Vector3f(centre.X / 8f, centre.Y / 8f, centre.Z / 8f);

        float radius = 0f;
        foreach (Vector3f corner in corners)
        {
            Vector3f offset = new(corner.X - centre.X, corner.Y - centre.Y, corner.Z - centre.Z);
            radius = MathF.Max(radius, offset.Length);
        }

        // Rounded up so a radius that drifts by a hair does not resize the box and undo the snapping.
        radius = MathF.Ceiling(radius * 16f) / 16f;

        float texel = 2f * radius / MathF.Max(1, resolution);

        // The snap has to be anchored in the WORLD, and this is the whole of why. Everything above is
        // camera-relative, so a centre computed from the frustum barely changes as the player walks --
        // the frustum is bolted to them. Quantising THAT lands on the same grid position every frame
        // while the world slides underneath, which is not a snap at all: the texel grid is rigidly
        // attached to the camera and drags across the ground continuously, so every shadow edge crawls
        // exactly as the snapping was meant to stop it doing.
        //
        // So the camera's own position is carried into light space in double precision, added in, the
        // sum quantised there, and taken back out. The matrix stays camera-relative and single precision
        // -- the result of the subtraction is within one texel of where it started -- while the grid it
        // lands on is fixed in the world. Depth is not quantised: it is the axis the light looks along,
        // and moving it would only slide the near plane.
        Vector3d lightCamera = lightRotation.Transposed().Transform(camera.Position);
        Vector3f lightCentre = lightRotation.Transposed().Transform(centre);

        lightCentre = new Vector3f(
            Snap(lightCamera.X, lightCentre.X, texel),
            Snap(lightCamera.Y, lightCentre.Y, texel),
            lightCentre.Z);

        Matrix4f projection = Matrix4f.CreateOrthographic(
            lightCentre.X - radius,
            lightCentre.X + radius,
            lightCentre.Y - radius,
            lightCentre.Y + radius,
            lightCentre.Z - radius - CasterReach,
            lightCentre.Z + radius);

        return new ShadowCascade(lightRotation, projection, centre, radius, sliceFar, texel);
    }
}

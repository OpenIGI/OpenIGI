using OpenIGI.Core.Numerics;

namespace OpenIGI.Core.Graphics;

/// <summary>
/// A world camera: a double-precision position and a single-precision orientation, exactly as the
/// original keeps them.
/// </summary>
/// <remarks>
/// <para>
/// The original stores the camera position as three <c>double</c>s and the orientation as a
/// <c>Real32x9</c>, then transforms every vertex as <c>M · (worldPosition − cameraPosition)</c>
/// (<c>sub_50FE60</c> builds the pair per frame, <c>sub_512C30</c> applies it). Subtracting in double
/// before narrowing is not an optimisation — it is what keeps a level whose coordinates run to
/// thousands of units from losing precision at the far end.
/// </para>
/// <para>
/// That is why <see cref="CreateModelView(Vector3d, Matrix3f)"/> is the member to use for drawing: it folds the camera
/// translation into each object's transform while the subtraction is still in double precision.
/// <see cref="GetViewMatrix"/> exists for the cases that genuinely want a standalone view matrix and
/// narrows the camera position, which is fine near the origin and lossy far from it.
/// </para>
/// <para>
/// View space is left-handed with +Z into the screen, matching <see cref="Matrix4f.CreateProjection"/>.
/// The world's vertical axis is Z: the terrain renderer morphs a vertex's <em>third</em> component
/// between detail levels, which is the height channel.
/// </para>
/// </remarks>
public readonly struct Camera3D
{
    /// <summary>Initializes a new instance of the <see cref="Camera3D"/> struct.</summary>
    /// <param name="position">The world position.</param>
    /// <param name="orientation">The world-to-view rotation, whose rows are the view axes.</param>
    public Camera3D(Vector3d position, Matrix3f orientation)
    {
        Position = position;
        Orientation = orientation;
    }

    /// <summary>Gets the world position.</summary>
    public Vector3d Position { get; }

    /// <summary>Gets the world-to-view rotation. Its rows are the view space axes in world space.</summary>
    public Matrix3f Orientation { get; }

    /// <summary>Gets a camera at the world origin looking along +Y with Z up.</summary>
    public static Camera3D Default => new(default, LookRotation(0f, 0f));

    /// <summary>Builds a world-to-view rotation from a yaw and a pitch, for a free camera.</summary>
    /// <param name="yaw">The rotation about the world's Z axis in radians; zero looks along +Y.</param>
    /// <param name="pitch">The rotation above the horizon in radians; positive looks up.</param>
    /// <returns>The rotation whose rows are the view right, up and forward axes.</returns>
    /// <remarks>
    /// Built for the world's Z-up convention. The rows are the view axes, so the matrix maps a world
    /// direction into view space directly — the same shape the original's camera keeps.
    /// </remarks>
    public static Matrix3f LookRotation(float yaw, float pitch)
    {
        float cy = MathF.Cos(yaw);
        float sy = MathF.Sin(yaw);
        float cp = MathF.Cos(pitch);
        float sp = MathF.Sin(pitch);

        Vector3f forward = new(sy * cp, cy * cp, sp);
        Vector3f right = new(cy, -sy, 0f);
        Vector3f up = new(-sy * sp, -cy * sp, cp);

        return Matrix3f.FromRows(right, up, forward);
    }

    /// <summary>Builds the transform that takes a model's local space straight to view space.</summary>
    /// <param name="modelPosition">The model's world position.</param>
    /// <param name="modelOrientation">The model's world orientation.</param>
    /// <returns>The combined model-to-view transform.</returns>
    /// <remarks>The camera-relative offset is computed in double precision and only then narrowed.</remarks>
    public Matrix4f CreateModelView(Vector3d modelPosition, Matrix3f modelOrientation)
    {
        Vector3d offset = new(
            modelPosition.X - Position.X,
            modelPosition.Y - Position.Y,
            modelPosition.Z - Position.Z);

        // The world-to-view rotation dots the orientation's rows, because the rows are the view axes —
        // the original's sub_512C30 computes M · (world − camera) exactly this way. Transform() applies
        // the engine's row-vector convention, which dots the columns: that is the inverse rotation, and
        // using it here shears the translation against the rotation half of the same matrix. Invisible
        // at yaw zero only because this frame's pitch-only matrices are symmetric.
        Vector3d viewOffset = Orientation.Transposed().Transform(offset);
        return Matrix4f.CreateRigid(
            Orientation * modelOrientation,
            new Vector3f((float)viewOffset.X, (float)viewOffset.Y, (float)viewOffset.Z));
    }

    /// <summary>Builds the transform that takes a model's local space to view space, unrotated.</summary>
    /// <param name="modelPosition">The model's world position.</param>
    /// <returns>The combined model-to-view transform.</returns>
    public Matrix4f CreateModelView(Vector3d modelPosition) => CreateModelView(modelPosition, Matrix3f.Identity);

    /// <summary>Builds a standalone world-to-view matrix.</summary>
    /// <returns>The view matrix.</returns>
    /// <remarks>
    /// Narrows the camera position to single precision. Prefer <see cref="CreateModelView(Vector3d, Matrix3f)"/> for drawing;
    /// this is for callers that need the matrix itself, such as culling in view space.
    /// </remarks>
    public Matrix4f GetViewMatrix()
    {
        // Rows-dot-vector for the same reason as CreateModelView: the translation must be rotated by
        // the same world-to-view rotation the matrix's 3×3 half applies.
        Vector3d translation = Orientation.Transposed().Transform(new Vector3d(-Position.X, -Position.Y, -Position.Z));
        return Matrix4f.CreateRigid(
            Orientation,
            new Vector3f((float)translation.X, (float)translation.Y, (float)translation.Z));
    }

    /// <inheritdoc/>
    public override string ToString() => $"camera at {Position}";
}

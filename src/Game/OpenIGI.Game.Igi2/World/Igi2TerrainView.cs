using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Igi2.World;

/// <summary>Where the ground is being looked at from, and the half-spaces it may be culled against.</summary>
/// <remarks>
/// <para>
/// <b>All four planes pass through the eye.</b> That is the shape
/// <c>TerrainRender_SetupClippingPlanes</c> (<c>igi2ed 0x6681A0</c>) builds when no clip window is
/// active: it zeroes each plane's point, fills in the four side normals from the projection's
/// <c>tan</c> half-angles, rotates them by the inverted view rotation and then subtracts
/// <c>tDisplacement</c> — which puts every point at the camera. A plane is stored as a point followed
/// by a normal, and <c>TerrainRender_IsSquareOutOfSight</c> (<c>0x66AE50</c>) treats
/// <c>dot(p - point, normal) &gt; 0</c> as outside.
/// </para>
/// <para>
/// The original's other branch, taken when a clip window is set, copies arbitrary points and normals
/// out of <c>Clipping_GetClipPlanes</c>. That path belongs to the editor's split views and is not
/// reproduced.
/// </para>
/// <para>
/// There is no near or far plane in the set — four side planes only. Distance is what the detail test
/// is for.
/// </para>
/// </remarks>
public readonly struct Igi2TerrainView
{
    private readonly Vector3f[]? _normals;

    /// <summary>Initializes a new instance of the <see cref="Igi2TerrainView"/> struct.</summary>
    /// <param name="eye">The eye position, in world units.</param>
    /// <param name="planeNormals">
    /// The outward normals of the half-spaces to cull against, all through <paramref name="eye"/>. Pass
    /// nothing to cull against nothing.
    /// </param>
    public Igi2TerrainView(Vector3d eye, ReadOnlySpan<Vector3f> planeNormals)
    {
        Eye = eye;
        _normals = planeNormals.IsEmpty ? null : planeNormals.ToArray();
    }

    /// <summary>Gets the eye position, in world units.</summary>
    public Vector3d Eye { get; }

    /// <summary>Gets the outward normals of the culling half-spaces, all through <see cref="Eye"/>.</summary>
    public ReadOnlySpan<Vector3f> PlaneNormals => _normals ?? [];

    /// <summary>Builds the view one frame is drawn from.</summary>
    /// <param name="camera">The eye and its orientation, in the height field's units.</param>
    /// <param name="verticalFieldOfView">The vertical field of view, in radians.</param>
    /// <param name="aspectRatio">The width over the height.</param>
    /// <returns>The view, with the frustum's four side planes.</returns>
    /// <remarks>
    /// <para>
    /// The four normals are the original's, in view space:
    /// <c>(-1, 0, -tanX)</c>, <c>(1, 0, -tanX)</c>, <c>(0, -1, -tanY)</c> and <c>(0, 1, -tanY)</c>,
    /// each normalised — left, right, bottom and top of a frustum whose apex is the eye. They are then
    /// rotated into world space by the camera's orientation, which is what
    /// <c>TerrainRender_SetupClippingPlanes</c> does with the inverted view rotation.
    /// </para>
    /// <para>
    /// One difference, stated rather than copied: the original normalises the <em>top</em> plane's Y
    /// component with <c>1 / sqrt(tanY * tanX + 1)</c> where every other component uses
    /// <c>1 / sqrt(tanY * tanY + 1)</c> (<c>0x6681A0</c>, the store to <c>flt_85233C8</c>). That mixes
    /// the two half-angles and leaves the normal non-unit whenever the aspect ratio is not one. It
    /// looks like a typo in the original and it only ever loosens the cull, so the port uses the
    /// consistent form.
    /// </para>
    /// <para>
    /// A camera with a degenerate orientation — a default-constructed one, as a test may pass — yields
    /// zero normals, which reject nothing. That is the safe direction to fail in.
    /// </para>
    /// </remarks>
    public static Igi2TerrainView FromCamera(in Camera3D camera, float verticalFieldOfView, float aspectRatio)
    {
        float tanY = MathF.Tan(verticalFieldOfView * 0.5f);
        float tanX = tanY * aspectRatio;

        Span<Vector3f> view =
        [
            (new Vector3f(-1f, 0f, -tanX)).NormalizedOrSelf(),
            (new Vector3f(1f, 0f, -tanX)).NormalizedOrSelf(),
            (new Vector3f(0f, -1f, -tanY)).NormalizedOrSelf(),
            (new Vector3f(0f, 1f, -tanY)).NormalizedOrSelf(),
        ];

        // Camera3D.Orientation's rows are the view axes in world space, so it maps world to view by
        // dotting rows; Transform() dots columns, which is exactly the view-to-world direction wanted
        // here. The same asymmetry Camera3D.CreateModelView documents, used the other way round.
        Span<Vector3f> world = stackalloc Vector3f[view.Length];
        for (int i = 0; i < view.Length; i++)
        {
            world[i] = camera.Orientation.Transform(view[i]);
        }

        return new Igi2TerrainView(camera.Position, world);
    }

}

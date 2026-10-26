using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;

namespace OpenIGI.Game.World.Sectors;

/// <summary>
/// The projection both titles' sector culls share: what screen box a portal covers, and whether a sphere falls in a window.
/// </summary>
/// <remarks>
/// <para>
/// <c>Sector_IsPortalVisible</c> is one function in both engines -- <c>IGI.exe 0x4DD7F0</c> and <c>igi2.exe 0x587D10</c> carry the
/// same plane test from the portal's first face, the same 409.6-unit relief behind it, the same near plane at <c>0.04096</c>,
/// the same outcodes against the display area and the same triangle clipper (<c>IGI.exe 0x4DAB50</c>,
/// <c>igi2.exe 0x5865B0</c>). What differs between the titles is the walk that calls it and what the result culls, which each
/// title keeps for itself.
/// </para>
/// <para>
/// Everything here is in world units, about a camera whose rows are the view's right, up and forward axes.
/// </para>
/// </remarks>
public static class SectorPortalProjection
{
    /// <summary>The view-space depth below which a vertex counts as behind the near plane.</summary>
    /// <remarks>
    /// <c>0.040959999</c> in the original, which is <c>0.04096f</c> exactly. It is used twice: as
    /// the outcode test at <c>igi2.exe 0x58823A</c> and as the plane <c>SectorObject_CheckTriangle</c>
    /// clips to, passed in as the constant <c>0x3D27C5AC</c> at <c>0x588376</c>.
    /// </remarks>
    public const float NearPlaneZ = 0.04096f;

    /// <summary>How far behind a portal's plane the camera may stand and still see through it.</summary>
    /// <remarks>
    /// <para>
    /// <b>Measured, not rounded.</b> The comparison at <c>igi2.exe 0x5880C8</c> is
    /// <c>fcomp ds:__real@4823D70B</c>, and <c>0x4823D70B</c> is <c>167772.16f</c>, whose square root is
    /// <c>409.6</c> exactly. The value compared against it is a squared distance, so the threshold is a
    /// distance of 409.6 world units.
    /// </para>
    /// <para>
    /// <b>What the distance is measured between</b> was not obvious from the original's code and had to be
    /// worked out. The original calls <c>Real32x3_ProjectPointToPlane</c> (<c>0x561A70</c>) with the
    /// camera-relative position of the portal's plane point, the unit plane normal as the direction and
    /// its negation as the plane normal, then squares the distance between the point and its projection.
    /// Substituting the normalised normal into that function collapses the whole thing to
    /// <c>dot(portalPoint − camera, planeNormal)²</c>: the <em>perpendicular</em> distance from the
    /// camera to the portal's plane, not the distance to the portal itself. A camera a kilometre to one
    /// side of a wall but level with it is still within the relief.
    /// </para>
    /// </remarks>
    public const float BackFaceRelief = 409.6f;

    /// <summary>The most vertices the original can transform for one portal.</summary>
    /// <remarks>
    /// <c>Sector_IsPortalVisible</c> transforms into <c>MultiMesh_TransformedVertex_t atVertex[100]</c>,
    /// a stack array with no bound check. The widest portal the 24 shipped levels place has 28 vertices,
    /// so the limit is recorded rather than enforced.
    /// </remarks>
    public const int VertexScratchCapacity = 100;

    private const float BackFaceReliefSquared = BackFaceRelief * BackFaceRelief;

    /// <summary>
    /// Projects one portal and returns the screen box it covers, or refuses it.
    /// </summary>
    /// <param name="camera">Where the viewer is and which way it faces.</param>
    /// <param name="projection">How view space becomes pixels.</param>
    /// <param name="position">Where the sector the portal belongs to stands, in world units.</param>
    /// <param name="orientation">How that sector is turned, as the world renderer turns it.</param>
    /// <param name="set">That sector's portals.</param>
    /// <param name="portal">The portal.</param>
    /// <param name="through">The rectangle the sector itself is being seen through.</param>
    /// <param name="bounds">
    /// Receives the portal's own screen box, clipped to the display area but <em>not</em> intersected
    /// with <paramref name="through"/> — the original leaves that to the caller.
    /// </param>
    /// <returns><see langword="false"/> when nothing can be seen through this portal.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="set"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <c>Sector_IsPortalVisible</c> (<c>IGI.exe 0x4DD7F0</c>, <c>igi2.exe 0x587D10</c>) minus its first step, the open/closed
    /// byte, which has no object to read here and is the caller's business.
    /// </remarks>
    public static bool TryGetPortalBounds(
        Camera3D camera,
        SectorScreenProjection projection,
        Vector3d position,
        Matrix3f orientation,
        MefPortalSet set,
        MefPortal portal,
        SectorScreenRect through,
        out SectorScreenRect bounds)
    {
        ArgumentNullException.ThrowIfNull(set);

        bounds = SectorScreenRect.Empty;

        if (portal.VertexCount <= 0 || portal.FaceCount <= 0)
        {
            // The original returns zero on `nNumVertices <= 0` (igi2ed 0x428850) and would read a face
            // it does not own on `nNumFaces <= 0`; neither happens on shipped data.
            return false;
        }

        // Transposed once, because Matrix3f.Transform is the row-vector product and the original's
        // Real32x3_Rotate is the column-vector one: `rotate.Transform(v)` is `orientation · v`, which is
        // what the object matrix at igi2.exe's `object + 124` is applied as.
        Matrix3f rotate = orientation.Transposed();

        if (!InFrontOrWithinRelief(camera, set, portal, position, rotate))
        {
            return false;
        }

        // The whole model-to-view chain in one transform, which is the same arithmetic the original does
        // by hand: rotate by the object matrix, add `objectPosition − cameraPosition`, then apply the
        // camera's 3×3. Camera3D.CreateModelView subtracts in double before narrowing, as the original
        // does with its `real64x3_t` positions.
        Matrix4f modelView = camera.CreateModelView(position, orientation);

        Span<ProjectedVertex> vertices = portal.VertexCount <= 32
            ? stackalloc ProjectedVertex[portal.VertexCount]
            : new ProjectedVertex[portal.VertexCount];

        bool anyInFront = false;
        for (int i = 0; i < portal.VertexCount; i++)
        {
            vertices[i] = Project(modelView.TransformPoint(set.Vertices[portal.FirstVertex + i]), projection);
            anyInFront |= (vertices[i].Code & Outcode.Near) == 0;
        }

        if (!anyInFront)
        {
            // `isVertexBeyondZ0` at igi2.exe 0x58831D: every vertex behind the near plane refuses the
            // portal outright, without clipping anything.
            return false;
        }

        SectorScreenRect box = SectorScreenRect.Empty;
        Span<ProjectedVertex> polygon = stackalloc ProjectedVertex[16];
        Span<ProjectedVertex> scratch = stackalloc ProjectedVertex[16];

        for (int f = 0; f < portal.FaceCount; f++)
        {
            MefPortalFace face = set.Faces[portal.FirstFace + f];
            int a = face.Vertex0 - portal.FirstVertex;
            int b = face.Vertex1 - portal.FirstVertex;
            int c = face.Vertex2 - portal.FirstVertex;

            // The original subtracts iStartVertex and indexes its scratch array with no check, so a face
            // naming a vertex outside its own portal's slice would read stale rows. Every face across the
            // 24 shipped levels stays inside; one that did not is skipped here rather than fabricated.
            if ((uint)a >= (uint)portal.VertexCount
                || (uint)b >= (uint)portal.VertexCount
                || (uint)c >= (uint)portal.VertexCount)
            {
                continue;
            }

            polygon[0] = vertices[a];
            polygon[1] = vertices[b];
            polygon[2] = vertices[c];
            box = Accumulate(box, polygon, scratch, projection);
        }

        bounds = box;
        return box.Overlaps(through);
    }

    private static bool InFrontOrWithinRelief(
        Camera3D camera,
        MefPortalSet set,
        MefPortal portal,
        Vector3d origin,
        Matrix3f rotate)
    {
        // The plane comes from the FIRST face alone (igi2.exe 0x587D88-0x587E03): the cross product of
        // its two edges from vertex 0, rotated into the world and then normalised. The remaining faces
        // only widen the outline.
        //
        // The sign is load-bearing below, and MefPortalSet.NormalOf now carries the original's -- it
        // used to cross from vertex 1, which for a triangle is the negation, so this reproduced the
        // maths locally rather than trust it. It is one implementation again.
        Vector3f p0 = set.Vertices[set.Faces[portal.FirstFace].Vertex0];
        Vector3f normal = rotate.Transform(set.NormalOf(portal)).Normalized();

        Vector3f rotatedP0 = rotate.Transform(p0);
        Vector3f relative = new(
            (float)(origin.X + rotatedP0.X - camera.Position.X),
            (float)(origin.Y + rotatedP0.Y - camera.Position.Y),
            (float)(origin.Z + rotatedP0.Z - camera.Position.Z));

        if (Vector3f.Dot(relative.Normalized(), normal) >= 0f)
        {
            // The camera is on the side the normal points away from. Admitted with no distance test.
            return true;
        }

        // Back-face relief. See BackFaceRelief for why the original's projection dance is a squared
        // perpendicular distance to the plane and nothing more.
        float distance = Vector3f.Dot(relative, normal);
        return distance * distance <= BackFaceReliefSquared;
    }

    private static ProjectedVertex Project(Vector3f view, SectorScreenProjection projection)
    {
        if (view.Z < NearPlaneZ)
        {
            // igi2.exe 0x5882FB writes outcode 16 and leaves the screen position unwritten; a near vertex
            // is always clipped away before anything reads it.
            return new ProjectedVertex(view, 0f, 0f, Outcode.Near);
        }

        float invZ = 1f / view.Z;
        float x = (view.X * invZ * projection.ScreenFovX) + projection.OriginX;
        float y = (view.Y * invZ * projection.ScreenFovY) + projection.OriginY;
        return new ProjectedVertex(view, x, y, OutcodeOf(x, y, projection.DisplayArea));
    }

    private static Outcode OutcodeOf(float x, float y, SectorScreenRect area)
    {
        // The four bits and their order are read out of igi2.exe 0x5882A0-0x5882F6, where the code is
        // assembled as `left | 2*(top | 2*(right | 2*bottom))`.
        Outcode code = Outcode.None;
        if (x < area.MinX)
        {
            code |= Outcode.Left;
        }

        if (y < area.MinY)
        {
            code |= Outcode.Top;
        }

        if (x > area.MaxX)
        {
            code |= Outcode.Right;
        }

        if (y > area.MaxY)
        {
            code |= Outcode.Bottom;
        }

        return code;
    }

    /// <summary>Clips one portal triangle and widens a box by whatever survives.</summary>
    /// <remarks>
    /// <para>
    /// <c>SectorObject_CheckTriangle</c> (<c>igi2.exe 0x5865B0</c>) does this by splitting the triangle
    /// against one plane at a time — near, then left, then top, then right, then bottom — recursing on
    /// the extra triangle when one vertex is outside and two are in, and widening the caller's box by
    /// the three surviving vertices at the end.
    /// </para>
    /// <para>
    /// <b>This is a convex polygon clip in the same order, not a line-for-line port of that recursion.</b>
    /// The two produce the same set of points: splitting a triangle against a half-plane and clipping it
    /// as a polygon give the same intersection, and only its extreme points reach the box. The clip order
    /// still matters and is preserved, because the near plane's interpolation happens in view space and
    /// is then reprojected, while the four screen planes interpolate the pixel positions directly — the
    /// original does exactly that split, and doing it the other way round bends the outline.
    /// </para>
    /// </remarks>
    private static SectorScreenRect Accumulate(
        SectorScreenRect box,
        Span<ProjectedVertex> polygon,
        Span<ProjectedVertex> scratch,
        SectorScreenProjection projection)
    {
        int count = 3;

        // Trivial reject, the original's `outcode0 & outcode1 & outcode2` at 0x5865D9: every vertex
        // outside the same plane means nothing of the triangle is on screen.
        Outcode shared = polygon[0].Code & polygon[1].Code & polygon[2].Code;
        if (shared != Outcode.None)
        {
            return box;
        }

        count = ClipNear(polygon, scratch, count, projection);
        if (count == 0)
        {
            return box;
        }

        count = ClipScreen(polygon, scratch, count, Edge.Left, projection.DisplayArea);
        count = ClipScreen(polygon, scratch, count, Edge.Top, projection.DisplayArea);
        count = ClipScreen(polygon, scratch, count, Edge.Right, projection.DisplayArea);
        count = ClipScreen(polygon, scratch, count, Edge.Bottom, projection.DisplayArea);

        for (int i = 0; i < count; i++)
        {
            box = box.Include(polygon[i].ScreenX, polygon[i].ScreenY);
        }

        return box;
    }

    private static int ClipNear(
        Span<ProjectedVertex> polygon,
        Span<ProjectedVertex> scratch,
        int count,
        SectorScreenProjection projection)
    {
        bool anyBehind = false;
        for (int i = 0; i < count; i++)
        {
            anyBehind |= (polygon[i].Code & Outcode.Near) != 0;
        }

        if (!anyBehind)
        {
            return count;
        }

        int written = 0;
        for (int i = 0; i < count; i++)
        {
            ProjectedVertex current = polygon[i];
            ProjectedVertex next = polygon[(i + 1) % count];
            bool currentIn = current.View.Z >= NearPlaneZ;
            bool nextIn = next.View.Z >= NearPlaneZ;

            if (currentIn)
            {
                scratch[written++] = current;
            }

            if (currentIn != nextIn)
            {
                // The original's interpolant, 0x586650: `(clipZ − a.z) / (b.z − a.z)` applied to the
                // view-space position, which is then reprojected and given a fresh outcode.
                // It is projected whatever its rounding, as the original's is: an interpolated depth a hair under the plane, read
                // as behind it, put the point at the screen's corner and cut a portal the eye stood in to a sliver.
                float t = (NearPlaneZ - current.View.Z) / (next.View.Z - current.View.Z);
                Vector3f point = Vector3f.Lerp(current.View, next.View, t);
                scratch[written++] = Project(new Vector3f(point.X, point.Y, MathF.Max(point.Z, NearPlaneZ)), projection);
            }
        }

        scratch[..written].CopyTo(polygon);
        return written;
    }

    private static int ClipScreen(
        Span<ProjectedVertex> polygon,
        Span<ProjectedVertex> scratch,
        int count,
        Edge edge,
        SectorScreenRect area)
    {
        if (count == 0)
        {
            return 0;
        }

        int written = 0;
        for (int i = 0; i < count; i++)
        {
            ProjectedVertex current = polygon[i];
            ProjectedVertex next = polygon[(i + 1) % count];
            bool currentIn = Inside(current, edge, area);
            bool nextIn = Inside(next, edge, area);

            if (currentIn)
            {
                scratch[written++] = current;
            }

            if (currentIn != nextIn)
            {
                // Interpolated in screen space, which is what the original does for these four planes
                // (0x586AF5 and its three siblings): it moves tProjected, not tRotated.
                float a = Coordinate(current, edge);
                float b = Coordinate(next, edge);
                float t = (Limit(edge, area) - a) / (b - a);
                scratch[written++] = new ProjectedVertex(
                    current.View,
                    current.ScreenX + ((next.ScreenX - current.ScreenX) * t),
                    current.ScreenY + ((next.ScreenY - current.ScreenY) * t),
                    Outcode.None);
            }
        }

        scratch[..written].CopyTo(polygon);
        return written;
    }

    private static bool Inside(ProjectedVertex vertex, Edge edge, SectorScreenRect area) => edge switch
    {
        Edge.Left => vertex.ScreenX >= area.MinX,
        Edge.Top => vertex.ScreenY >= area.MinY,
        Edge.Right => vertex.ScreenX <= area.MaxX,
        _ => vertex.ScreenY <= area.MaxY,
    };

    private static float Coordinate(ProjectedVertex vertex, Edge edge) =>
        edge is Edge.Left or Edge.Right ? vertex.ScreenX : vertex.ScreenY;

    private static float Limit(Edge edge, SectorScreenRect area) => edge switch
    {
        Edge.Left => area.MinX,
        Edge.Top => area.MinY,
        Edge.Right => area.MaxX,
        _ => area.MaxY,
    };

    [Flags]
    private enum Outcode
    {
        None = 0,
        Left = 1,
        Top = 2,
        Right = 4,
        Bottom = 8,
        Near = 16,
    }

    private enum Edge
    {
        Left,
        Top,
        Right,
        Bottom,
    }

    private readonly record struct ProjectedVertex(Vector3f View, float ScreenX, float ScreenY, Outcode Code);

    /// <summary>Tests whether a sphere falls in any of a set of windows, the way <c>ModelObj_Exclude</c> keeps an object.</summary>
    /// <param name="camera">Where the viewer is and which way it faces.</param>
    /// <param name="projection">How view space becomes pixels.</param>
    /// <param name="centre">The sphere's centre, in world units.</param>
    /// <param name="radius">Its radius, in world units.</param>
    /// <param name="windows">The windows; a sphere in any one of them is kept.</param>
    /// <returns><see langword="true"/> unless the sphere is behind the eye or outside every window.</returns>
    /// <remarks>
    /// <c>Mesh3D_ExcludeObject</c> builds a view cone from the display area narrowed to the window -- four planes through the
    /// eye and the window's edges -- and Direct3D's exclusion method refuses a sphere wholly behind one of them or behind the
    /// eye. A draw handler that tries several windows keeps the object if any one keeps it.
    /// </remarks>
    public static bool SphereInAnyWindow(
        Camera3D camera,
        SectorScreenProjection projection,
        Vector3d centre,
        float radius,
        IReadOnlyList<SectorScreenRect> windows)
    {
        ArgumentNullException.ThrowIfNull(windows);
        Vector3f view = camera.CreateModelView(centre).TransformPoint(Vector3f.Zero);
        if (view.Z < -radius || view.Z + radius < NearPlaneZ)
        {
            return false;
        }

        foreach (SectorScreenRect window in windows)
        {
            float left = (window.MinX - projection.OriginX) / projection.ScreenFovX;
            float right = (window.MaxX - projection.OriginX) / projection.ScreenFovX;
            float low = (window.MinY - projection.OriginY) / projection.ScreenFovY;
            float high = (window.MaxY - projection.OriginY) / projection.ScreenFovY;

            // The signed distance from each plane through the eye and one edge of the window: (x - slope·z)·cos(atan slope).
            if (PlaneDistance(view.X, view.Z, left) >= -radius
                && PlaneDistance(view.X, view.Z, right) <= radius
                && PlaneDistance(view.Y, view.Z, low) >= -radius
                && PlaneDistance(view.Y, view.Z, high) <= radius)
            {
                return true;
            }
        }

        return false;
    }

    private static float PlaneDistance(float across, float depth, float slope) =>
        (across - (slope * depth)) / MathF.Sqrt(1f + (slope * slope));
}

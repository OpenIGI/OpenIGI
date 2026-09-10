using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Game.Igi2.World;

/// <summary>
/// A rectangle in screen pixels, which is the only thing I.G.I. 2's portal culling ever narrows.
/// </summary>
/// <param name="MinX">The left edge.</param>
/// <param name="MinY">The top edge.</param>
/// <param name="MaxX">The right edge.</param>
/// <param name="MaxY">The bottom edge.</param>
/// <remarks>
/// The original's <c>real32x2_t</c> pair. It seeds a portal's box at <c>±FLT_MAX</c>
/// (<c>igi2.exe 0x587D1C</c>) and treats an unfilled box as a refusal, which is what
/// <see cref="Empty"/> reproduces: an inverted rectangle that overlaps nothing.
/// </remarks>
public readonly record struct Igi2ScreenRect(float MinX, float MinY, float MaxX, float MaxY)
{
    /// <summary>Gets the inverted rectangle a portal's bounding box starts from.</summary>
    public static Igi2ScreenRect Empty { get; } =
        new(float.MaxValue, float.MaxValue, -float.MaxValue, -float.MaxValue);

    /// <summary>Gets a value indicating whether this rectangle encloses nothing.</summary>
    public bool IsEmpty => MinX > MaxX || MinY > MaxY;

    /// <summary>Gets the width, which is negative for an empty rectangle.</summary>
    public float Width => MaxX - MinX;

    /// <summary>Gets the height, which is negative for an empty rectangle.</summary>
    public float Height => MaxY - MinY;

    /// <summary>Tests whether two rectangles touch or overlap.</summary>
    /// <param name="other">The rectangle to test against.</param>
    /// <returns><see langword="true"/> when they share at least an edge.</returns>
    /// <remarks>
    /// Inclusive on all four sides, because the original's test at <c>igi2.exe 0x58841E</c> is
    /// <c>&lt;=</c> and <c>&gt;=</c>: a portal that lines up exactly with the edge of the rectangle it is
    /// seen through still admits what is beyond it.
    /// </remarks>
    public bool Overlaps(Igi2ScreenRect other) =>
        MinX <= other.MaxX && MinY <= other.MaxY && MaxX >= other.MinX && MaxY >= other.MinY;

    /// <summary>Narrows this rectangle to the part inside another.</summary>
    /// <param name="other">The rectangle to clip against.</param>
    /// <returns>The intersection, which may be empty.</returns>
    /// <remarks>
    /// <c>SectorObject_TraverseSector</c> does this to the box <c>Sector_IsPortalVisible</c> hands back
    /// (<c>igi2.exe 0x5888FD</c>-<c>0x58893E</c>) before recursing. The narrowing lives in the caller,
    /// not in the visibility test, which returns the portal's own box unclipped.
    /// </remarks>
    public Igi2ScreenRect Intersect(Igi2ScreenRect other) => new(
        MathF.Max(MinX, other.MinX),
        MathF.Max(MinY, other.MinY),
        MathF.Min(MaxX, other.MaxX),
        MathF.Min(MaxY, other.MaxY));

    /// <summary>Widens this rectangle to enclose another.</summary>
    /// <param name="other">The rectangle to take in.</param>
    /// <returns>The union.</returns>
    /// <remarks>
    /// <c>SectorObject_RequestSector</c> (<c>igi2.exe 0x588470</c>) keeps one union per sector across
    /// every route that reached it, alongside the individual windows.
    /// </remarks>
    public Igi2ScreenRect Union(Igi2ScreenRect other) => new(
        MathF.Min(MinX, other.MinX),
        MathF.Min(MinY, other.MinY),
        MathF.Max(MaxX, other.MaxX),
        MathF.Max(MaxY, other.MaxY));

    /// <summary>Widens this rectangle to enclose one point.</summary>
    /// <param name="x">The point's horizontal position.</param>
    /// <param name="y">The point's vertical position.</param>
    /// <returns>The widened rectangle.</returns>
    public Igi2ScreenRect Include(float x, float y) => new(
        MathF.Min(MinX, x),
        MathF.Min(MinY, y),
        MathF.Max(MaxX, x),
        MathF.Max(MaxY, y));

    /// <inheritdoc/>
    public override string ToString() =>
        $"[{MinX:0.##}, {MinY:0.##}]..[{MaxX:0.##}, {MaxY:0.##}]";
}

/// <summary>
/// How a view-space position becomes a pixel, in the four numbers the original keeps for it.
/// </summary>
/// <param name="ScreenFovX">Pixels per unit of <c>x/z</c>; the original's <c>vScreenFOVX</c>.</param>
/// <param name="ScreenFovY">Pixels per unit of <c>y/z</c>; the original's <c>vScreenFOVY</c>.</param>
/// <param name="OriginX">The horizontal pixel <c>x/z == 0</c> lands on.</param>
/// <param name="OriginY">The vertical pixel <c>y/z == 0</c> lands on.</param>
/// <param name="DisplayArea">
/// The rectangle every outcode is measured against, and the rectangle a traversal starts from — the
/// original's <c>RenderContext_tActiveRenderContext.tDisplayArea{Min,Max}</c>.
/// </param>
/// <remarks>
/// <para>
/// <c>Sector_IsPortalVisible</c> projects with scalar arithmetic rather than a matrix
/// (<c>igi2.exe 0x588257</c>-<c>0x58828D</c>):
/// <c>screenX = x/z · vScreenFOVX + vScreenOriginX</c> and the same for <c>y</c>. Those four fields sit
/// in <c>TransContext_s</c> at <c>0x48</c>, <c>0x4C</c>, <c>0x50</c> and <c>0x54</c>.
/// </para>
/// <para>
/// <see cref="FromViewport"/> derives them the way <c>TransContext_Create</c> (<c>igi2.exe 0x569D20</c>)
/// does, which is the only place they are written.
/// </para>
/// </remarks>
public readonly record struct Igi2ScreenProjection(
    float ScreenFovX,
    float ScreenFovY,
    float OriginX,
    float OriginY,
    Igi2ScreenRect DisplayArea)
{
    /// <summary>Builds the projection of a viewport, as the original builds it.</summary>
    /// <param name="viewport">The viewport in pixels.</param>
    /// <param name="tangentHalfFovX">
    /// The original's <c>vFOVX</c>: the value of <c>x/z</c> that lands on the viewport's right edge, so
    /// the tangent of half the horizontal field of view.
    /// </param>
    /// <param name="pixelAspect">
    /// The original's <c>(modeHeight · 4) / (modeWidth · 3)</c> — one for a 4:3 display mode, and the
    /// correction that keeps a circle round on one that is not.
    /// </param>
    /// <returns>The projection.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tangentHalfFovX"/> is not positive.</exception>
    /// <remarks>
    /// Read out of <c>TransContext_Create</c> (<c>igi2.exe 0x569D20</c>), which writes
    /// <c>vScreenFOVX = clipRect.vHalfWidth / vFOVX</c>, <c>vScreenFOVY = pixelAspect · vScreenFOVX</c>
    /// and the two origins as the clipping rectangle's centre. The original keeps the clipping rectangle
    /// and the display area apart; for a full-viewport render they are the same rectangle, which is what
    /// this assumes.
    /// </remarks>
    public static Igi2ScreenProjection FromViewport(
        Igi2ScreenRect viewport,
        float tangentHalfFovX,
        float pixelAspect = 1f)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tangentHalfFovX);

        float halfWidth = viewport.Width * 0.5f;
        float halfHeight = viewport.Height * 0.5f;
        float scaleX = halfWidth / tangentHalfFovX;

        return new Igi2ScreenProjection(
            scaleX,
            pixelAspect * scaleX,
            viewport.MinX + halfWidth,
            viewport.MinY + halfHeight,
            viewport);
    }
}

/// <summary>One sector a traversal reached, and the screen rectangles it was reached through.</summary>
/// <remarks>
/// The original's request record: <c>SectorObject_RequestSector</c> (<c>igi2.exe 0x588470</c>) keeps a
/// pool entry per sector holding the union of every rectangle that reached it, and pushes each
/// individual rectangle onto the sector's own <c>ptClipWindowsList</c>. Both are kept here because both
/// are used — <c>SectorObject_DrawSector</c> hands the list to the clipper and the union to the
/// enclosing window.
/// </remarks>
public sealed class Igi2VisibleSector
{
    private readonly List<Igi2ScreenRect> _windows = [];

    internal Igi2VisibleSector(int index, Igi2Sector sector, Igi2ScreenRect first)
    {
        Index = index;
        Sector = sector;
        Bounds = first;
        _windows.Add(first);
    }

    /// <summary>Gets the sector's index in <see cref="Igi2SectorGraph.Sectors"/>.</summary>
    public int Index { get; }

    /// <summary>Gets the sector itself.</summary>
    public Igi2Sector Sector { get; }

    /// <summary>Gets the union of every rectangle this sector was reached through.</summary>
    public Igi2ScreenRect Bounds { get; private set; }

    /// <summary>Gets each rectangle this sector was reached through, in the order it was reached.</summary>
    public IReadOnlyList<Igi2ScreenRect> ClipWindows => _windows;

    /// <inheritdoc/>
    public override string ToString() =>
        $"sector {Index} ({Sector.Placement.Model}), {_windows.Count} windows, {Bounds}";

    internal void Add(Igi2ScreenRect window)
    {
        _windows.Add(window);
        Bounds = Bounds.Union(window);
    }
}

/// <summary>
/// I.G.I. 2's portal culling: which sectors a camera can see from the one it stands in, and through
/// what part of the screen.
/// </summary>
/// <remarks>
/// <para>
/// The runtime half of <see cref="Igi2SectorGraph"/>, ported from three functions of the shipped
/// binaries: <c>Sector_IsPortalVisible</c> (<c>igi2.exe 0x587D10</c>, <c>igi2ed.exe 0x428850</c>),
/// <c>SectorObject_TraverseSector</c> (<c>0x588830</c>) and <c>SectorObject_RequestSector</c>
/// (<c>0x588470</c>), with the entry point <c>SectorObject_EnterHandler</c> (<c>0x588A70</c>) supplying
/// the starting rectangle. The dedicated server and the editor carry the same code; the editor's copy
/// differs only by a call to <c>Sector_DrawPortal</c> that draws the outline it just computed.
/// </para>
/// <para>
/// <b>This is rectangle narrowing, not frustum clipping.</b> A portal is projected, its triangles are
/// clipped to the screen, their bounding box is taken, and that box is intersected with the rectangle
/// the portal was itself seen through. Nothing carries a plane set forward; the whole state passed
/// between sectors is four floats.
/// </para>
/// <para>
/// <b>What is deliberately not modelled.</b> The original picks a level of detail per candidate
/// (<c>ModelObj_GetLOD</c>) and skips one that returns <c>-1</c>; it also skips a portal whose
/// per-object status byte is zero, which is what a door drives
/// (<c>Sector_SetPortalOpen</c>, <c>igi2ed.exe 0x42AFD0</c>, writes <c>ptSector + iPortal + 352</c>).
/// Neither has a home in the port yet, so LOD is ignored and the open/closed state arrives through the
/// <c>isPortalOpen</c> delegate.
/// </para>
/// </remarks>
public sealed class Igi2PortalCulling
{
    /// <summary>The view-space depth below which a vertex counts as behind the near plane.</summary>
    /// <remarks>
    /// <c>0.040959999</c> in the decompilation, which is <c>0.04096f</c> exactly. It is used twice: as
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
    /// <b>What the distance is measured between</b> was not obvious from the decompilation and had to be
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

    /// <summary>How many sector traversals one <see cref="Visit"/> will run before it stops.</summary>
    /// <remarks>
    /// <b>A port-only guard; the original has none.</b> <c>SectorObject_RequestSector</c> recurses
    /// unconditionally even when the sector already has a request, so the only thing bounding the
    /// original is the discard list, which forbids a sector from re-entering itself on the path it is
    /// already on. That makes the traversal explore every simple path through a container's sectors,
    /// which is factorial in the worst case and merely fine on the shipped levels. This cap keeps a
    /// pathological level from hanging a frame.
    /// </remarks>
    public const int DefaultTraversalBudget = 4096;

    private const float BackFaceReliefSquared = BackFaceRelief * BackFaceRelief;

    private readonly Igi2SectorGraph _graph;

    /// <summary>Prepares to cull against one level's sectors.</summary>
    /// <param name="graph">The level's sectors and their portals.</param>
    /// <exception cref="ArgumentNullException"><paramref name="graph"/> is <see langword="null"/>.</exception>
    public Igi2PortalCulling(Igi2SectorGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        _graph = graph;
    }

    /// <summary>Gets the graph this culls against.</summary>
    public Igi2SectorGraph Graph => _graph;

    /// <summary>Walks the sectors a camera can see from the one it stands in.</summary>
    /// <param name="camera">Where the viewer is and which way it faces.</param>
    /// <param name="projection">How view space becomes pixels, and the screen rectangle to start from.</param>
    /// <param name="startSector">The index into <see cref="Igi2SectorGraph.Sectors"/> of the sector the camera is in.</param>
    /// <param name="isPortalOpen">
    /// Answers whether a sector's portal slot is open, given the sector's index and the slot. The
    /// original reads a byte per slot on the object and refuses a closed one outright
    /// (<c>igi2.exe 0x587D56</c>). <see langword="null"/> treats every portal as open.
    /// </param>
    /// <param name="traversalBudget">
    /// How many sector traversals to run before stopping; see <see cref="DefaultTraversalBudget"/>.
    /// </param>
    /// <returns>
    /// Every sector reached, in the order the original's request pool would hold them — which is the
    /// order <c>SectorObject_EnterHandler</c> then draws them in.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="startSector"/> is not a sector of this level.</exception>
    /// <remarks>
    /// <para>
    /// The starting rectangle is the whole display area, which is what
    /// <c>SectorObject_EnterHandler</c> seeds its first request with (<c>igi2.exe 0x588C27</c>).
    /// </para>
    /// <para>
    /// <b>The candidate set is the starting sector's container, for the whole walk.</b> That is worth
    /// stating because it is not what the recursion suggests: <c>SectorObject_TraverseSector</c> begins
    /// its candidate loop at <c>SectorObject_ptActiveSectorObject</c> — the sector the camera is in —
    /// and then walks <em>that</em> object's <c>ptSectorContainer</c>'s children, no matter how deep the
    /// recursion has gone. So a sector in another container is unreachable however many portals line up,
    /// and this differs from <see cref="Igi2SectorGraph.Beyond"/>, which asks the sector being looked out
    /// of rather than the one the camera stands in. The two agree whenever a walk stays inside one
    /// container, which on the shipped levels is always.
    /// </para>
    /// </remarks>
    public IReadOnlyList<Igi2VisibleSector> Visit(
        Camera3D camera,
        Igi2ScreenProjection projection,
        int startSector,
        Func<int, int, bool>? isPortalOpen = null,
        int traversalBudget = DefaultTraversalBudget)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(startSector);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(startSector, _graph.Sectors.Count);

        Walk walk = new(_graph, camera, projection, isPortalOpen, traversalBudget)
        {
            Container = _graph.Sectors[startSector].ContainerId,
        };

        walk.Request(startSector, projection.DisplayArea);
        return walk.Requests;
    }

    /// <summary>
    /// Projects one portal and returns the screen box it covers, or refuses it.
    /// </summary>
    /// <param name="camera">Where the viewer is and which way it faces.</param>
    /// <param name="projection">How view space becomes pixels.</param>
    /// <param name="sector">The sector the portal belongs to, which supplies its world transform.</param>
    /// <param name="portal">The portal.</param>
    /// <param name="through">The rectangle the sector itself is being seen through.</param>
    /// <param name="bounds">
    /// Receives the portal's own screen box, clipped to the display area but <em>not</em> intersected
    /// with <paramref name="through"/> — the original leaves that to the caller.
    /// </param>
    /// <returns><see langword="false"/> when nothing can be seen through this portal.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="sector"/> has no portal set.</exception>
    /// <remarks>
    /// <c>Sector_IsPortalVisible</c> (<c>igi2.exe 0x587D10</c>) minus its first step, the open/closed
    /// byte, which has no object to read here and is the caller's business.
    /// </remarks>
    public static bool TryGetPortalBounds(
        Camera3D camera,
        Igi2ScreenProjection projection,
        Igi2Sector sector,
        MefPortal portal,
        Igi2ScreenRect through,
        out Igi2ScreenRect bounds)
    {
        ArgumentNullException.ThrowIfNull(sector.Portals);

        bounds = Igi2ScreenRect.Empty;

        MefPortalSet set = sector.Portals;
        if (portal.VertexCount <= 0 || portal.FaceCount <= 0)
        {
            // The original returns zero on `nNumVertices <= 0` (igi2ed 0x428850) and would read a face
            // it does not own on `nNumFaces <= 0`; neither happens on shipped data.
            return false;
        }

        Matrix3f orientation = WorldScene.ToOrientation(sector.Placement.Orientation);

        // Transposed once, because Matrix3f.Transform is the row-vector product and the original's
        // Real32x3_Rotate is the column-vector one: `rotate.Transform(v)` is `orientation · v`, which is
        // what the object matrix at igi2.exe's `object + 124` is applied as.
        Matrix3f rotate = orientation.Transposed();

        if (!InFrontOrWithinRelief(camera, set, portal, sector.Placement.Position, rotate))
        {
            return false;
        }

        // The whole model-to-view chain in one transform, which is the same arithmetic the original does
        // by hand: rotate by the object matrix, add `objectPosition − cameraPosition`, then apply the
        // camera's 3×3. Camera3D.CreateModelView subtracts in double before narrowing, as the original
        // does with its `real64x3_t` positions.
        Matrix4f modelView = camera.CreateModelView(sector.Placement.Position, orientation);

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

        Igi2ScreenRect box = Igi2ScreenRect.Empty;
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

    private static ProjectedVertex Project(Vector3f view, Igi2ScreenProjection projection)
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

    private static Outcode OutcodeOf(float x, float y, Igi2ScreenRect area)
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
    private static Igi2ScreenRect Accumulate(
        Igi2ScreenRect box,
        Span<ProjectedVertex> polygon,
        Span<ProjectedVertex> scratch,
        Igi2ScreenProjection projection)
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
        Igi2ScreenProjection projection)
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
                float t = (NearPlaneZ - current.View.Z) / (next.View.Z - current.View.Z);
                scratch[written++] = Project(Lerp(current.View, next.View, t), projection);
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
        Igi2ScreenRect area)
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

    private static bool Inside(ProjectedVertex vertex, Edge edge, Igi2ScreenRect area) => edge switch
    {
        Edge.Left => vertex.ScreenX >= area.MinX,
        Edge.Top => vertex.ScreenY >= area.MinY,
        Edge.Right => vertex.ScreenX <= area.MaxX,
        _ => vertex.ScreenY <= area.MaxY,
    };

    private static float Coordinate(ProjectedVertex vertex, Edge edge) =>
        edge is Edge.Left or Edge.Right ? vertex.ScreenX : vertex.ScreenY;

    private static float Limit(Edge edge, Igi2ScreenRect area) => edge switch
    {
        Edge.Left => area.MinX,
        Edge.Top => area.MinY,
        Edge.Right => area.MaxX,
        _ => area.MaxY,
    };

    private static Vector3f Lerp(Vector3f a, Vector3f b, float t) => a + ((b - a) * t);

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

    /// <summary>One run of the traversal, holding the state the original keeps in globals.</summary>
    private sealed class Walk(
        Igi2SectorGraph graph,
        Camera3D camera,
        Igi2ScreenProjection projection,
        Func<int, int, bool>? isPortalOpen,
        int budget)
    {
        private readonly Dictionary<int, Igi2VisibleSector> _byIndex = [];
        private readonly List<Igi2VisibleSector> _requests = [];
        private readonly List<int> _discarded = [];
        private int _traversals;

        /// <summary>Gets or sets the container every candidate must hang under.</summary>
        public int Container { get; init; }

        /// <summary>Gets the sectors reached, in the order they were first requested.</summary>
        public IReadOnlyList<Igi2VisibleSector> Requests => _requests;

        /// <summary>Records a rectangle a sector was reached through, then walks out of it.</summary>
        /// <remarks><c>SectorObject_RequestSector</c>, <c>igi2.exe 0x588470</c>.</remarks>
        public void Request(int index, Igi2ScreenRect window)
        {
            if (_byIndex.TryGetValue(index, out Igi2VisibleSector? existing))
            {
                existing.Add(window);
            }
            else
            {
                Igi2VisibleSector visible = new(index, graph.Sectors[index], window);
                _byIndex.Add(index, visible);
                _requests.Add(visible);
            }

            // Unconditional, even for a sector already in the pool: the original recurses whether or not
            // it found a request. A second route into a room is a second, differently shaped window, and
            // what is visible through it is not what was visible through the first.
            Traverse(index, window);
        }

        private void Traverse(int index, Igi2ScreenRect window)
        {
            if (++_traversals > budget)
            {
                return;
            }

            _discarded.Add(index);
            Igi2Sector sector = graph.Sectors[index];

            foreach (MefPortal portal in sector.Portals.Portals)
            {
                if (isPortalOpen is not null && !isPortalOpen(index, portal.Slot))
                {
                    continue;
                }

                if (!TryGetPortalBounds(camera, projection, sector, portal, window, out Igi2ScreenRect box))
                {
                    continue;
                }

                Igi2ScreenRect beyond = box.Intersect(window);
                Admit(portal.Slot, beyond);
            }

            _discarded.RemoveAt(_discarded.Count - 1);
        }

        private void Admit(int slot, Igi2ScreenRect beyond)
        {
            for (int candidate = 0; candidate < graph.Sectors.Count; candidate++)
            {
                if (graph.Sectors[candidate].ContainerId != Container || _discarded.Contains(candidate))
                {
                    continue;
                }

                foreach (MefPortal other in graph.Sectors[candidate].Portals.Portals)
                {
                    if (other.Slot == slot)
                    {
                        // Once per matching portal, not once per sector: the original calls
                        // SectorObject_RequestSector inside the portal loop (0x5889D2) and does not break
                        // out of it, so a sector with two portals of one slot is requested twice.
                        Request(candidate, beyond);
                    }
                }
            }
        }
    }
}

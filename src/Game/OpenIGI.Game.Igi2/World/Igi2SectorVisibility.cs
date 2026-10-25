using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Game.World.Sectors;

namespace OpenIGI.Game.Igi2.World;

/// <summary>
/// I.G.I. 2's sector cull, decided once a frame from the eye: which buildings, rooms, objects and soldiers are drawn, whether the
/// outside is, and through what part of the screen.
/// </summary>
/// <remarks>
/// <para>
/// <b>Where the camera stands decides the frame.</b> <c>ViewportQTask_DrawHandler</c> asks which room the camera is in
/// (<c>ViewPortQTask_FindSector</c>, without tolerance), and <c>World_Draw</c> hands the answer to everything it draws:
/// </para>
/// <list type="bullet">
/// <item>The building the camera stands in is walked from its room through the whole screen (<c>SectorObject_EnterHandler</c>).
/// Its shell is drawn only if the walk reaches it, which it does through a portal of the room that shares a slot with one of
/// the shell's -- the outside is then seen, through the rectangles that reached the shell
/// (<c>SectorObject_DrawSector</c> sets <c>QCube_isDrawOutside</c> and <c>Clipping_tClipData.tClipWindows</c>).</item>
/// <item>Until then nothing outside is drawn: the terrain and the forests test <c>QCube_isDrawOutside</c>, every other building
/// returns without a request, and every rigid mesh standing in no room returns from <c>Direct3DRender_DrawRigidMesh</c> -- a
/// door and a conveyor belt excepted. Once it is, another building is walked from its shell through each of those rectangles,
/// a mesh outside is drawn if its sphere falls in one of them, and the terrain and the forests are clipped to the rectangle
/// enclosing them all (<c>Clipping_SetupClippingPlanes</c>'s enclosing frustum, which only they read).</item>
/// <item>With the camera in no room, every building is walked from its shell through the whole screen; its rooms are drawn
/// only if reached.</item>
/// <item>An interior object standing in a room is drawn only while that room is reached, and only if its sphere falls in one of
/// the rectangles the room was reached through. A soldier is decided the same way by the room its nodes place it in
/// (<see cref="IsHumanVisible"/>).</item>
/// </list>
/// <para>
/// <b>What is not reproduced.</b> A building whose level of detail is <c>-1</c> is not skipped; the renderer's own distance
/// handling stands in for it.
/// </para>
/// </remarks>
public sealed class Igi2SectorVisibility : IPlacementVisibility
{
    private readonly Igi2SectorGraph _graph;
    private readonly Igi2PortalCulling _culling;
    private readonly Igi2SectorLocator _locator;
    private readonly Func<int, int, bool>? _isPortalOpen;
    private readonly Dictionary<Igi2VisibilityKey, Resolved> _keys = [];

    // Per frame: each sector's rectangles when reached, else null; the outside's rectangles, null for none, and whether the
    // outside is unbounded (the camera stands in no room).
    private readonly IReadOnlyList<SectorScreenRect>?[] _reached;
    private IReadOnlyList<SectorScreenRect>? _outsideWindows;
    private bool _outsideUnbounded = true;
    private SectorScreenProjection _projection;
    private Camera3D _eye = Camera3D.Default;

    /// <summary>Initializes a new instance of the <see cref="Igi2SectorVisibility"/> class over a level.</summary>
    /// <param name="graph">The level's sector objects.</param>
    /// <param name="locator">Finds the room a point stands in.</param>
    /// <param name="keys">Every key the level's placements carry, which this resolves once to the sectors they name.</param>
    /// <param name="isPortalOpen">Answers whether a sector's portal slot is open, or <see langword="null"/> for every portal open.</param>
    public Igi2SectorVisibility(
        Igi2SectorGraph graph,
        Igi2SectorLocator locator,
        IEnumerable<Igi2VisibilityKey> keys,
        Func<int, int, bool>? isPortalOpen = null)
    {
        _graph = graph ?? throw new ArgumentNullException(nameof(graph));
        _locator = locator ?? throw new ArgumentNullException(nameof(locator));
        ArgumentNullException.ThrowIfNull(keys);
        _culling = new Igi2PortalCulling(graph);
        _isPortalOpen = isPortalOpen;
        _reached = new IReadOnlyList<SectorScreenRect>?[graph.Sectors.Count];

        foreach (Igi2VisibilityKey key in keys)
        {
            if (!_keys.ContainsKey(key))
            {
                _keys.Add(key, Resolve(key));
            }
        }
    }

    /// <summary>Gets the room the eye stood in when the frame was decided, or <see langword="null"/> for none.</summary>
    public Igi2Sector? EyeRoom { get; private set; }

    /// <inheritdoc/>
    public bool IsOutsideVisible => _outsideUnbounded || _outsideWindows is not null;

    /// <inheritdoc/>
    /// <remarks>
    /// The rectangle enclosing every window the shell was reached through, <c>tEnclosingWindow</c>: the frustum
    /// <c>TerrainRender_SetupClippingPlanes</c> and the forest's draw clip to once <c>Clipping_SetupClippingPlanes</c> has built it.
    /// </remarks>
    public ViewClip? OutsideClip
    {
        get
        {
            if (_outsideUnbounded || _outsideWindows is not { Count: > 0 } windows)
            {
                return null;
            }

            SectorScreenRect enclosing = windows[0];
            for (int i = 1; i < windows.Count; i++)
            {
                enclosing = enclosing.Union(windows[i]);
            }

            return _projection.ToViewClip(enclosing);
        }
    }

    /// <inheritdoc/>
    public string DescribeView() =>
        EyeRoom is { } room
            ? FormattableString.Invariant(
                $"sector: {room.Model} (room {room.Room} of {_graph.Objects[room.ObjectIndex].Shell.Model}), {(_outsideWindows is null ? "sees no way out" : "sees out")}, {ReachedCount} sectors reached")
            : FormattableString.Invariant($"sector: none, {ReachedCount} sectors reached");

    /// <summary>Gets how many sectors the last frame reached.</summary>
    public int ReachedCount { get; private set; }

    /// <summary>Collects the keys a scene's placements carry, each once.</summary>
    /// <param name="scene">The scene.</param>
    /// <returns>The keys.</returns>
    public static IEnumerable<Igi2VisibilityKey> KeysOf(WorldScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        HashSet<Igi2VisibilityKey> seen = [];
        foreach (WorldPlacement placement in scene.Placements)
        {
            if (placement.VisibilityKey is Igi2VisibilityKey key && seen.Add(key))
            {
                yield return key;
            }
        }
    }

    /// <inheritdoc/>
    public void BeginView(in Camera3D eye, float tangentHalfFovX, float tangentHalfFovY)
    {
        if (!(tangentHalfFovX > 0f) || !(tangentHalfFovY > 0f))
        {
            return;
        }

        // The eye in world units, which is what every position here is kept in; its axes are the camera's already.
        Camera3D units = new(WorldScene.ToUnits(eye.Position), eye.Orientation);
        Decide(units, SectorScreenProjection.ForView(tangentHalfFovX, tangentHalfFovY));
    }

    /// <summary>Decides the frame from an eye in world units.</summary>
    /// <param name="eye">The eye, in world units.</param>
    /// <param name="projection">How view space becomes the rectangles' pixels.</param>
    public void Decide(Camera3D eye, SectorScreenProjection projection)
    {
        _eye = eye;
        _projection = projection;
        Frame++;
        Array.Clear(_reached);
        _outsideWindows = null;
        ReachedCount = 0;

        Igi2Sector? room = _locator.SectorAt(eye.Position);
        EyeRoom = room;
        _outsideUnbounded = room is null;

        if (room is not null)
        {
            // The building the camera stands in first: World_Draw forces it into the first group drawn, so the outside's
            // rectangles exist before anything outside asks for them.
            Store(_culling.Visit(eye, projection, room.Index, _isPortalOpen));
            Igi2Sector shell = _graph.Objects[room.ObjectIndex].Shell;
            _outsideWindows = _reached[shell.Index];
        }

        IReadOnlyList<SectorScreenRect>? starts = _outsideUnbounded ? [projection.DisplayArea] : _outsideWindows;
        if (starts is null)
        {
            return;
        }

        foreach (Igi2SectorObject building in _graph.Objects)
        {
            if (room is not null && building.Index == room.ObjectIndex)
            {
                continue;
            }

            // A building whose whole sphere is out of view has nothing to reach; the cube tree never offers it.
            if (!InAnyWindow(building.Shell.Position, 2f * building.Radius, [projection.DisplayArea]))
            {
                continue;
            }

            Store(_culling.Visit(eye, projection, building.Shell.Index, starts, _isPortalOpen));
        }
    }

    /// <inheritdoc/>
    public bool IsVisible(WorldPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(placement);
        if (placement.VisibilityKey is not Igi2VisibilityKey key || !_keys.TryGetValue(key, out Resolved resolved))
        {
            return true;
        }

        return IsVisible(key, resolved);
    }

    /// <summary>Gets a value indicating whether the object a key names is drawn this frame.</summary>
    /// <param name="key">The key.</param>
    /// <returns><see langword="true"/> when the cull keeps it; a key this does not know is kept.</returns>
    public bool IsVisible(Igi2VisibilityKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return !_keys.TryGetValue(key, out Resolved resolved) || IsVisible(key, resolved);
    }

    /// <summary>Gets how many frames have been decided, which tells a cache of what was drawn that the answer may have changed.</summary>
    public long Frame { get; private set; }

    /// <summary>Gets or sets the model detail's level-of-detail bias, <c>ModelObj_vLODBias</c>: nought at full detail.</summary>
    /// <remarks>
    /// <c>1 - vModelDetail</c> (<c>Config_SetGameToCurrentPerformanceLevel</c>). Above nought the original culls a soldier by its
    /// room even with the camera outdoors; the port draws at full detail, where it is nought.
    /// </remarks>
    public float LodBias { get; set; }

    /// <summary>Gets a value indicating whether a soldier's body is drawn this frame: <c>Human_EnterHandler</c>'s sector test.</summary>
    /// <param name="body">The soldier.</param>
    /// <param name="infraEnabled">Whether the player's view sees in infrared, <c>isInfraEnabled</c>, which shows every body.</param>
    /// <returns><see langword="false"/> when the cull leaves it out.</returns>
    /// <remarks>
    /// <para>
    /// The test applies while the camera stands in a building (or the bias is above nought), to a soldier whose mind is not
    /// between two sectors (<c>HumanAI_s.isBetweenSectors</c>), and not while the view is infrared. A soldier in a room
    /// (<c>Human_s.ptSectorQTask</c>, <see cref="Soldiers.Igi2HumanSoldier.Sector"/>) is drawn only while that room is reached and
    /// its sphere falls in one of the room's windows; one in no room, through the outside's windows while the outside is seen,
    /// and not at all while it is not.
    /// </para>
    /// <para>
    /// The soldier's sphere is its mesh's render sphere, placed as <c>BoneDynCubeObj_Exclude</c> places it
    /// (<see cref="Humans.Igi2BodyModel.CullSphere"/>).
    /// </para>
    /// </remarks>
    public bool IsHumanVisible(Soldiers.Igi2HumanSoldier body, bool infraEnabled)
    {
        ArgumentNullException.ThrowIfNull(body);
        if ((_outsideUnbounded && LodBias <= 0f)
            || infraEnabled
            || body.Mind is not Ai.Runtime.Igi2HumanAi { IsBetweenSectors: false })
        {
            return true;
        }

        (Vector3d centre, float radius) = body.Model is { } model
            ? model.CullSphere(body.Orientation, body.Position, body.RootTranslation)
            : (body.Position, body.ModelRadius);
        if (body.Sector is Igi2Sector room)
        {
            return _reached[room.Index] is { } windows && InAnyWindow(centre, radius, windows);
        }

        if (_outsideWindows is { } outside)
        {
            return InAnyWindow(centre, radius, outside);
        }

        return _outsideUnbounded;
    }

    /// <summary>Gets the sector a key was resolved to when the level loaded.</summary>
    /// <param name="key">The key.</param>
    /// <returns>
    /// The shell or room a building's key names; the room an interior object stands in; <see langword="null"/> for an interior
    /// object standing in none, or for a key this does not know.
    /// </returns>
    public Igi2Sector? SectorOf(Igi2VisibilityKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _keys.TryGetValue(key, out Resolved resolved) && resolved.Sector >= 0 ? _graph.Sectors[resolved.Sector] : null;
    }

    private bool IsVisible(Igi2VisibilityKey key, Resolved resolved)
    {
        switch (key.Kind)
        {
            case Igi2VisibilityKind.Shell:
            case Igi2VisibilityKind.Room:
                // A building the graph does not hold, or one whose walk was never started because it was out of view: the
                // renderer's frustum decides, as it would have.
                if (resolved.Sector < 0)
                {
                    return true;
                }

                if (_reached[resolved.Sector] is not null)
                {
                    return true;
                }

                // A building drawn from outside is requested from its shell whatever its portals show, so its shell -- and
                // its glass and ladders with it -- stands whenever the outside does.
                return key.Kind == Igi2VisibilityKind.Shell && _outsideUnbounded;

            default:
                if (resolved.Sector >= 0)
                {
                    return _reached[resolved.Sector] is { } windows && InAnyWindow(key.Centre, key.Radius, windows);
                }

                if (_outsideWindows is { } outside)
                {
                    return InAnyWindow(key.Centre, key.Radius, outside);
                }

                // Direct3DRender_DrawRigidMesh: inside a building that does not see out, a mesh in no room is not drawn -- unless
                // it is a door or a conveyor belt.
                return _outsideUnbounded || key.IsShownUnseen;
        }
    }

    private void Store(IReadOnlyList<Igi2VisibleSector> visited)
    {
        foreach (Igi2VisibleSector visible in visited)
        {
            if (_reached[visible.Index] is null)
            {
                ReachedCount++;
            }

            _reached[visible.Index] = visible.ClipWindows;
        }
    }

    private Resolved Resolve(Igi2VisibilityKey key)
    {
        if (key.Kind == Igi2VisibilityKind.Object)
        {
            // SectorObject_RegisterInteriorDynCubeObjSpecificSectorObject: the room its sphere's centre stands in, asked with
            // tolerance; standing in a building that holds no room is standing in none. Only an interior object registers.
            return new Resolved(key.RegistersInRooms ? _locator.SectorAt(key.Centre, lift: true)?.Index ?? -1 : -1);
        }

        foreach (Igi2SectorObject building in _graph.Objects)
        {
            Igi2Sector shell = building.Shell;
            if (shell.LevelId != key.LevelId
                || shell.Position != key.Position
                || !string.Equals(shell.Model, key.Model, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (key.Kind == Igi2VisibilityKind.Shell)
            {
                return new Resolved(shell.Index);
            }

            return (uint)key.Room < (uint)building.Rooms.Count ? new Resolved(building.Rooms[key.Room].Index) : new Resolved(-1);
        }

        return new Resolved(-1);
    }

    // ModelObj_Exclude with the display area narrowed to each window in turn.
    private bool InAnyWindow(Vector3d centre, float radius, IReadOnlyList<SectorScreenRect> windows) =>
        SectorPortalProjection.SphereInAnyWindow(_eye, _projection, centre, radius, windows);

    private readonly record struct Resolved(int Sector);
}

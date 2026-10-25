using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;

namespace OpenIGI.Game.Igi2.World;

/// <summary>One enclosed space: a building's shell, or one of the rooms it carries, with the openings in it.</summary>
/// <param name="Index">Its index in <see cref="Igi2SectorGraph.Sectors"/>.</param>
/// <param name="ObjectIndex">The building it belongs to, its index in <see cref="Igi2SectorGraph.Objects"/>.</param>
/// <param name="Room">Which of the building's rooms it is, counted from nought, or <see cref="Shell"/> for the building itself.</param>
/// <param name="TypeName">The placing task's type, which with <paramref name="LevelId"/> names the solids it was built as.</param>
/// <param name="LevelId">The placing task's level id.</param>
/// <param name="Model">The model it is.</param>
/// <param name="Position">Where it stands, in world units.</param>
/// <param name="Orientation">How it is turned, as the world renderer turns it: rows dotted with a model-space vector give its world direction.</param>
/// <param name="Portals">Its mesh's portals, in its own model space.</param>
public sealed record Igi2Sector(
    int Index,
    int ObjectIndex,
    int Room,
    string TypeName,
    int LevelId,
    string Model,
    Vector3d Position,
    Matrix3f Orientation,
    MefPortalSet Portals)
{
    /// <summary>The <see cref="Room"/> of a building's own shell.</summary>
    public const int Shell = -1;

    /// <summary>Gets a value indicating whether this is a building's shell rather than one of its rooms.</summary>
    public bool IsShell => Room == Shell;
}

/// <summary>
/// A sector object, I.G.I. 2's <c>SectorObject_s</c>: a building whose shell is a sector in its own right and whose rooms hang in
/// its <c>ptSectorContainer</c>.
/// </summary>
/// <param name="Index">Its index in <see cref="Igi2SectorGraph.Objects"/>.</param>
/// <param name="Shell">The building's own model as a sector: its portals are the openings to the outside.</param>
/// <param name="Rooms">The rooms its model attaches, in attachment order.</param>
/// <param name="Radius">
/// The radius it is registered in the world with, its model's bound times its largest scale (<c>Building_RegisterHandler</c>), in
/// world units; <c>SectorObject_GetSectorHandler</c> traces twice this far up and down.
/// </param>
public sealed record Igi2SectorObject(int Index, Igi2Sector Shell, IReadOnlyList<Igi2Sector> Rooms, float Radius)
{
    /// <summary>Gets the sectors a walk inside it may reach, in the order <c>SectorObject_TraverseSector</c> offers them: the shell, then the rooms.</summary>
    public IEnumerable<Igi2Sector> Sectors
    {
        get
        {
            yield return Shell;
            foreach (Igi2Sector room in Rooms)
            {
                yield return room;
            }
        }
    }
}

/// <summary>
/// The sector objects an I.G.I. 2 level places, each a shell and its rooms, and which of them a portal can lead to.
/// </summary>
/// <remarks>
/// <para>
/// <b>Only a <c>Building</c> is a sector object.</b> <c>SectorObject</c> derives from <c>Sector</c> (<c>SectorObject_Open</c>), and of
/// every task type the client registers only <c>Building</c> derives from <c>SectorObject</c> (<c>building.c</c>). Its model is its
/// shell, a sector whose portals are the building's openings to the outside.
/// </para>
/// <para>
/// <b>Its rooms are its model's attach objects that are sectors.</b> <c>SectorObject_SetModel</c> gives the object two containers,
/// <c>ptMagicObjContainer</c> and <c>ptSectorContainer</c>, and <c>SectorObject_CreateAttachObjects</c> makes every attach point of
/// the shell's model an object, moving each one derived from <c>Sector</c> into the second. The port reads the same thing the
/// collision world does: an attach point the magic table does not hold whose model carries portals. Across the 24 shipped levels
/// that is 375 rooms in 230 of the 491 buildings.
/// </para>
/// <para>
/// <b>Linkage is by portal slot within one building.</b> <c>SectorObject_TraverseSector</c> (<c>igi2.exe 0x588830</c>), having found
/// a portal visible, offers the building being drawn -- <c>SectorObject_ptActiveSectorObject</c> -- and then every child of its
/// <c>ptSectorContainer</c>, and admits each with a portal whose <see cref="MefPortal.Slot"/> equals the slot just stepped through.
/// A room's doorway onto the street and the shell's opening in the same place carry the same number; nothing pairs them in space.
/// No portal leads from one building into another.
/// </para>
/// </remarks>
public sealed class Igi2SectorGraph
{
    /// <summary>The task type whose objects are sector objects.</summary>
    public const string SectorObjectType = Igi2BuildingTask.TypeName;

    private readonly IReadOnlyList<Igi2SectorObject> _objects;
    private readonly IReadOnlyList<Igi2Sector> _sectors;

    private Igi2SectorGraph(IReadOnlyList<Igi2SectorObject> objects, IReadOnlyList<Igi2Sector> sectors)
    {
        _objects = objects;
        _sectors = sectors;
    }

    /// <summary>Gets an empty graph, for a level that places no building.</summary>
    public static Igi2SectorGraph Empty { get; } = new([], []);

    /// <summary>Gets every sector object the level places, in declaration order.</summary>
    public IReadOnlyList<Igi2SectorObject> Objects => _objects;

    /// <summary>Gets every sector, each object's shell followed by its rooms.</summary>
    public IReadOnlyList<Igi2Sector> Sectors => _sectors;

    /// <summary>Gets how many portals the level's sectors have between them.</summary>
    public int PortalCount => _sectors.Sum(static s => s.Portals.Portals.Count);

    /// <summary>Collects the sector objects a level places, with their rooms.</summary>
    /// <param name="scene">What the level places.</param>
    /// <param name="models">Resolves a model name to its mesh, or <see langword="null"/> when nothing ships it.</param>
    /// <param name="magic">The magic object types attach points are looked up in, or <see langword="null"/> to take every name as a model.</param>
    /// <returns>The graph.</returns>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public static Igi2SectorGraph Build(Igi2WorldScene scene, Func<string, MefModel?> models, Igi2MagicObjTypes? magic = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(models);

        List<Igi2SectorObject> objects = [];
        List<Igi2Sector> sectors = [];
        foreach (Igi2Placement placement in scene.StaticPlacements)
        {
            if (!string.Equals(placement.TypeName, SectorObjectType, StringComparison.Ordinal)
                || placement.Geometry is not null
                || models(placement.Model) is not { } shellModel)
            {
                continue;
            }

            int objectIndex = objects.Count;
            Igi2Pose pose = placement.Frame is { } frame
                ? Igi2Pose.FromMatrix(placement.Position, frame)
                : Igi2Pose.FromAngles(placement.Position, placement.Orientation);
            Igi2Sector shell = new(
                sectors.Count, objectIndex, Igi2Sector.Shell, placement.TypeName, placement.LevelId, placement.Model,
                pose.Position, pose.ToMatrix(), shellModel.Portals);
            sectors.Add(shell);

            List<Igi2Sector> rooms = [];
            foreach (MefAttachment attachment in shellModel.Attachments)
            {
                if (RoomModel(attachment, magic) is not { } roomName || models(roomName) is not { IsSector: true } roomModel)
                {
                    continue;
                }

                Igi2Pose at = pose.Attach(attachment);
                Igi2Sector room = new(
                    sectors.Count, objectIndex, rooms.Count, placement.TypeName, placement.LevelId, roomName,
                    at.Position, at.ToMatrix(), roomModel.Portals);
                sectors.Add(room);
                rooms.Add(room);
            }

            objects.Add(new Igi2SectorObject(objectIndex, shell, rooms, shellModel.NativeMarkerBound));
        }

        return new Igi2SectorGraph(objects, sectors);
    }

    /// <summary>
    /// Gets the model an attach point of a building's shell makes a room of, or <see langword="null"/> when what it makes is no room.
    /// </summary>
    /// <param name="attachment">The attach point.</param>
    /// <param name="magic">The magic object types, or <see langword="null"/> to take the point's name as its model.</param>
    /// <returns>The room's model name, which the caller confirms carries portals.</returns>
    /// <remarks>
    /// A point the magic table holds is a magic object -- a pane of glass, a ladder -- and stays in the building's
    /// <c>ptMagicObjContainer</c>; only a point named for a model is made a sector, which is the same reading the collision world's
    /// <see cref="Collision.Igi2CollisionOwner.IsSector"/> makes.
    /// </remarks>
    public static string? RoomModel(MefAttachment attachment, Igi2MagicObjTypes? magic) =>
        magic is null ? attachment.Name : magic.Find(attachment.Name) is null ? magic.ModelOf(attachment.Name) : null;

    /// <summary>Gets the sectors a portal of one sector can lead into.</summary>
    /// <param name="from">The sector being looked out of.</param>
    /// <param name="slot">The slot of the portal being stepped through.</param>
    /// <returns>The sectors the original would admit, which may be empty.</returns>
    /// <remarks>
    /// The rule from <c>SectorObject_TraverseSector</c> for a walk one step deep: the sectors of the same building other than the
    /// one being looked out of, the shell first, each with a portal of the same slot.
    /// </remarks>
    public IEnumerable<Igi2Sector> Beyond(Igi2Sector from, int slot)
    {
        ArgumentNullException.ThrowIfNull(from);
        foreach (Igi2Sector candidate in _objects[from.ObjectIndex].Sectors)
        {
            if (candidate.Index == from.Index)
            {
                continue;
            }

            foreach (MefPortal portal in candidate.Portals.Portals)
            {
                if (portal.Slot == slot)
                {
                    yield return candidate;
                    break;
                }
            }
        }
    }

    /// <inheritdoc/>
    public override string ToString() =>
        $"I.G.I. 2 sector graph, {_objects.Count} sector objects, {_sectors.Count} sectors, {PortalCount} portals";
}

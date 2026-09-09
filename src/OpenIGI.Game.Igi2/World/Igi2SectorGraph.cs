using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.World;

namespace OpenIGI.Game.Igi2.World;

/// <summary>One placed model that encloses space, with the openings in its shell.</summary>
/// <param name="Placement">Where and what it is.</param>
/// <param name="Portals">Its mesh's portals, in the model's own space.</param>
/// <param name="ContainerId">
/// The level task id of the task it hangs under, or <see cref="GameTask.NoId"/> when that task has none.
/// Sectors sharing this are candidates for being linked — see <see cref="Igi2SectorGraph"/>.
/// </param>
/// <param name="ContainerType">The type name of that task, kept because it is the evidence for the above.</param>
public readonly record struct Igi2Sector(
    Igi2Placement Placement,
    MefPortalSet Portals,
    int ContainerId,
    string ContainerType);

/// <summary>
/// The sectors an I.G.I. 2 level places, and which of them a portal can lead to.
/// </summary>
/// <remarks>
/// <para>
/// A sector is any placement whose mesh carries portals — 242 placements across the 24 shipped levels. Everything else is a prop standing in the open.
/// </para>
/// <para>
/// <b>Linkage is by portal slot within a container, not by geometry.</b> That is worth stating flatly
/// because the geometric reading is the one anybody would reach for first, and it is wrong:
/// of the 199 portals belonging to sectors placed without rotation, <b>4 coincide in world space</b>.
/// Sectors do not meet portal-to-portal. A portal is a hole in one sector's own shell, and the sector
/// behind it has a hole of its own somewhere near, not in the same place.
/// </para>
/// <para>
/// What <c>SectorObject_TraverseSector</c> (<c>igi2.exe 0x588830</c>) actually does, having found a
/// portal visible, is walk the sector objects hanging off <c>ptSectorContainer</c> and admit every one
/// with a portal whose <see cref="MefPortal.Slot"/> equals the slot just stepped through. So the level
/// author pairs rooms by numbering their doorways alike.
/// </para>
/// <para>
/// <b>One thing here is a reading rather than a fact:</b> that <c>ptSectorContainer</c> is the task the
/// sector hangs under. It is not proven from the binary — the field's assignment was not found — but it
/// fits what the levels contain: 241 of the 242 sector placements sit directly under a
/// <c>Container</c>, <c>ConditionalContainer</c> or <c>Building</c>, and the remaining one under
/// <c>Dynamic</c>. <see cref="Igi2Sector.ContainerType"/> carries that so a later reader can check the
/// claim rather than inherit it.
/// </para>
/// </remarks>
public sealed class Igi2SectorGraph
{
    private readonly IReadOnlyList<Igi2Sector> _sectors;

    private Igi2SectorGraph(IReadOnlyList<Igi2Sector> sectors) => _sectors = sectors;

    /// <summary>Gets every sector the level places, in declaration order.</summary>
    public IReadOnlyList<Igi2Sector> Sectors => _sectors;

    /// <summary>Gets how many portals the level's sectors have between them.</summary>
    public int PortalCount => _sectors.Sum(static s => s.Portals.Portals.Count);

    /// <summary>Walks a loaded level and collects the sectors it places.</summary>
    /// <param name="levelRoot">The level's task tree.</param>
    /// <param name="portalsOf">
    /// Resolves a placement's model name to its mesh's portals, returning <see cref="MefPortalSet.Empty"/>
    /// or <see langword="null"/> for anything that is not a sector.
    /// </param>
    /// <returns>The graph.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <remarks>
    /// The tree is walked rather than <see cref="Igi2WorldScene.Placements"/> reused, because a sector
    /// needs to know what it hangs under and the scene is deliberately flat. Loading models is the
    /// caller's business, which is what keeps this type free of archives and testable without them.
    /// </remarks>
    public static Igi2SectorGraph Build(GameTask levelRoot, Func<string, MefPortalSet?> portalsOf)
    {
        ArgumentNullException.ThrowIfNull(levelRoot);
        ArgumentNullException.ThrowIfNull(portalsOf);

        List<Igi2Sector> sectors = [];
        Collect(levelRoot, portalsOf, sectors);
        return new Igi2SectorGraph(sectors);
    }

    /// <summary>Gets the sectors a portal of one sector can lead into.</summary>
    /// <param name="from">The sector being looked out of.</param>
    /// <param name="slot">The slot of the portal being stepped through.</param>
    /// <returns>The sectors the original would admit, which may be empty.</returns>
    /// <remarks>
    /// The rule from <c>SectorObject_TraverseSector</c>, with the container narrowed to the one
    /// <see cref="Igi2Sector.ContainerId"/> records: the same container, a different object, and a portal
    /// of the same slot. A sector never leads to itself, which is the discard list the original pushes
    /// its current sector onto before recursing.
    /// </remarks>
    public IEnumerable<Igi2Sector> Beyond(Igi2Sector from, int slot)
    {
        foreach (Igi2Sector candidate in _sectors)
        {
            if (candidate.ContainerId != from.ContainerId
                || candidate.Placement.LevelId == from.Placement.LevelId)
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
        $"I.G.I. 2 sector graph, {_sectors.Count} sectors, {PortalCount} portals";

    private static void Collect(GameTask task, Func<string, MefPortalSet?> portalsOf, List<Igi2Sector> into)
    {
        foreach (GameTask child in task.Children)
        {
            if (child is DeclaredTask declared
                && declared.TryGetValue(Igi2WorldScene.ModelParameter, out object? raw)
                && raw is string model
                && !string.IsNullOrWhiteSpace(model)
                && portalsOf(model) is { IsSector: true } portals
                && Igi2WorldScene.TryPlace(declared, out Igi2Placement placement))
            {
                into.Add(new Igi2Sector(placement, portals, task.Id, task.Type.Name));
            }

            Collect(child, portalsOf, into);
        }
    }
}

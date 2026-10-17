using System.Diagnostics.CodeAnalysis;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Titles;
using OpenIGI.Engine.Vfs;
using OpenIGI.Scripting.Runtime;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.World;

/// <summary>
/// A title's missions cut to the opening stretch of its campaign: the first so many along the
/// progression, and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// For a build that offers only part of the game -- the browser build offers the first six missions. The
/// cut is made here, at the one table every route into a mission already consults, rather than at each
/// route: the campaign list, the retail menu's fills, a save slot, <c>-mission</c> and a finished
/// mission's successor all look their mission up in the catalogue, so a mission this does not know is one
/// none of them can reach, and none of them has to know there is a limit.
/// </para>
/// <para>
/// Counted along <see cref="IMissionCatalogue{TDescriptor}.InProgressionOrder"/>, the order the game is
/// played in, not by id and not in scan order. A mission the campaign never reaches -- a mod's extra
/// level -- is outside the opening stretch by definition, so it is not offered either.
/// </para>
/// </remarks>
public sealed class CampaignLimitedMissionCatalogue : IMissionCatalogue<MissionDescriptor>
{
    private readonly IMissionCatalogue<MissionDescriptor> _inner;
    private readonly int _limit;
    private List<MissionDescriptor> _offered = [];
    private HashSet<int> _offeredIds = [];

    /// <summary>Initializes a new instance of the <see cref="CampaignLimitedMissionCatalogue"/> class.</summary>
    /// <param name="inner">The title's own catalogue, which does the scanning.</param>
    /// <param name="limit">How many missions, from the start of the campaign, are offered.</param>
    /// <exception cref="ArgumentNullException"><paramref name="inner"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="limit"/> is less than one.</exception>
    public CampaignLimitedMissionCatalogue(IMissionCatalogue<MissionDescriptor> inner, int limit)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        _inner = inner;
        _limit = limit;
        Refresh();
    }

    /// <summary>Gets how many missions, from the start of the campaign, are offered.</summary>
    public int Limit => _limit;

    /// <inheritdoc/>
    /// <remarks>The offered missions, in campaign order.</remarks>
    public IReadOnlyList<MissionDescriptor> Missions => _offered;

    /// <inheritdoc/>
    public int Count => _offered.Count;

    /// <inheritdoc/>
    public void Register(NativeRegistry natives) => _inner.Register(natives);

    /// <inheritdoc/>
    /// <returns>How many missions are offered, which is at most <see cref="Limit"/>.</returns>
    public int Scan(VirtualFileSystem fileSystem, ResourceRegistry resources, NativeRegistry natives)
    {
        _inner.Scan(fileSystem, resources, natives);
        Refresh();
        return Count;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The same as <see cref="InProgressionOrder"/>: the missions the campaign does not reach, which a
    /// browser would otherwise list after it, are never inside the opening stretch.
    /// </remarks>
    public IEnumerable<MissionDescriptor> InCampaignOrder() => _offered;

    /// <inheritdoc/>
    public IEnumerable<MissionDescriptor> InProgressionOrder() => _offered;

    /// <inheritdoc/>
    public bool TryGet(int id, [NotNullWhen(true)] out MissionDescriptor? descriptor)
    {
        if (_offeredIds.Contains(id))
        {
            return _inner.TryGet(id, out descriptor);
        }

        descriptor = null;
        return false;
    }

    /// <summary>Brings a campaign position back inside the missions offered.</summary>
    /// <param name="missionId">A mission id, such as a profile's furthest unlocked mission.</param>
    /// <returns>
    /// <paramref name="missionId"/> when it is offered or the campaign never reaches it; the last offered
    /// mission when it lies further along the campaign than the limit.
    /// </returns>
    /// <remarks>
    /// A profile made by the full game can have reached mission ten. Left alone, every screen that looks
    /// for that id among the offered missions fails to find it and treats the profile as though it had
    /// reached nothing, so a player further along than the limit would find all but the first mission
    /// locked.
    /// </remarks>
    public int ClampToOffered(int missionId)
    {
        if (_offeredIds.Contains(missionId) || _offered.Count == 0)
        {
            return missionId;
        }

        foreach (MissionDescriptor mission in _inner.InProgressionOrder())
        {
            if (mission.Id == missionId)
            {
                return _offered[^1].Id;
            }
        }

        return missionId;
    }

    private void Refresh()
    {
        _offered = [.. _inner.InProgressionOrder().Take(_limit)];
        _offeredIds = [.. _offered.Select(mission => mission.Id)];
    }
}

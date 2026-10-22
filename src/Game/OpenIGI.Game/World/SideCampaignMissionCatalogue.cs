using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Titles;
using OpenIGI.Engine.Vfs;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.World;

/// <summary>A title's missions with the campaigns beside its own kept out of its progression.</summary>
/// <remarks>
/// <para>
/// A mod's mission is defined by its own <c>mission.qvm</c> and found by the title's scan like any of the game's, so it can
/// be started, loaded and saved as one. What it must not do is join the game's campaign: I.G.I. 2 orders every single-player
/// mission by id, and a mod's would be numbered among the game's own. Its campaign lists it instead.
/// </para>
/// <para>
/// Everything else is the inner catalogue's, read through, so the limit a browser build puts on the campaign
/// (<see cref="CampaignLimitedMissionCatalogue"/>) and this compose in either order.
/// </para>
/// </remarks>
/// <param name="inner">The title's missions.</param>
/// <param name="campaigns">The campaigns beside the title's own.</param>
public sealed class SideCampaignMissionCatalogue(IMissionCatalogue<MissionDescriptor> inner, IReadOnlyList<TitleCampaign> campaigns)
    : IMissionCatalogue<MissionDescriptor>
{
    private readonly HashSet<int> _side = [.. campaigns.SelectMany(static c => c.Missions)];

    /// <summary>Gets the campaigns beside the title's own.</summary>
    public IReadOnlyList<TitleCampaign> Campaigns { get; } = campaigns;

    /// <inheritdoc/>
    public int Count => inner.Count;

    /// <inheritdoc/>
    public IReadOnlyList<MissionDescriptor> Missions => inner.Missions;

    /// <inheritdoc/>
    public void Register(NativeRegistry natives) => inner.Register(natives);

    /// <inheritdoc/>
    public int Scan(VirtualFileSystem fileSystem, ResourceRegistry resources, NativeRegistry natives) =>
        inner.Scan(fileSystem, resources, natives);

    /// <inheritdoc/>
    public bool TryGet(int id, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out MissionDescriptor? descriptor) =>
        inner.TryGet(id, out descriptor);

    /// <inheritdoc/>
    public IEnumerable<MissionDescriptor> InCampaignOrder() =>
        inner.InCampaignOrder().Where(mission => !_side.Contains(mission.Id));

    /// <inheritdoc/>
    public IEnumerable<MissionDescriptor> InProgressionOrder() =>
        inner.InProgressionOrder().Where(mission => !_side.Contains(mission.Id));

    /// <summary>Gets a side campaign's missions the title found, in the campaign's order.</summary>
    /// <param name="campaign">The campaign.</param>
    /// <returns>Its missions; one the scan did not find is left out.</returns>
    public IEnumerable<MissionDescriptor> InSideCampaignOrder(TitleCampaign campaign)
    {
        ArgumentNullException.ThrowIfNull(campaign);
        foreach (int id in campaign.Missions)
        {
            if (inner.TryGet(id, out MissionDescriptor? mission))
            {
                yield return mission;
            }
        }
    }
}

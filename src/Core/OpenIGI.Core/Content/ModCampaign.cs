namespace OpenIGI.Core.Content;

/// <summary>A campaign a mod adds beside the game's own: a name, and its missions in the order they are played.</summary>
/// <param name="Name">The name shown for it.</param>
/// <param name="Missions">The ids of its missions, which the mod's own mission scripts define, in order.</param>
public sealed record ModCampaign(string Name, IReadOnlyList<int> Missions);

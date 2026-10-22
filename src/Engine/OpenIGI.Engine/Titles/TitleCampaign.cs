namespace OpenIGI.Engine.Titles;

/// <summary>A campaign beside a title's own: a mod's missions, in the order they are played.</summary>
/// <param name="Name">The name shown for it.</param>
/// <param name="Missions">The ids of its missions, in order.</param>
public sealed record TitleCampaign(string Name, IReadOnlyList<int> Missions);

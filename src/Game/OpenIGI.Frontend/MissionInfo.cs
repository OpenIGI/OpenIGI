namespace OpenIGI.Frontend;

/// <summary>
/// A mission resolved for the shell to show: its id and its display strings, already looked up from the
/// language table so a screen needs no string resolution of its own.
/// </summary>
/// <param name="Id">The mission id, as passed to start it.</param>
/// <param name="Number">The mission's ordinal for display, 1-based.</param>
/// <param name="Name">The resolved display name.</param>
/// <param name="Description">The resolved description or briefing text.</param>
/// <param name="BriefingSprite">The briefing sprite resource name, e.g. <c>mission1.spr</c>.</param>
/// <param name="Campaign">
/// The campaign the mission is one of: <see langword="null"/> for the game's own, which the profile's progress unlocks, or a
/// mod's, whose missions are all open.
/// </param>
public sealed record MissionInfo(int Id, int Number, string Name, string Description, string BriefingSprite, string? Campaign = null);

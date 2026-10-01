namespace OpenIGI.Game.World;

/// <summary>
/// One mission's descriptor: the eleven values a level's <c>mission.qvm</c> passes to
/// <c>DefineMission</c>.
/// </summary>
/// <remarks>
/// <para>
/// The original keeps these in a 1192-byte record inside a fixed table at <c>0x5C1590</c>
/// (<c>Mission_GetDescriptor</c>, <c>0x485170</c>, is a linear scan of it for a matching id). The table
/// is <em>empty</em> in the executable image — every entry is written at runtime by the
/// <c>DefineMission</c> native (<c>0x484F60</c>), which is why searching the binary for the mission list
/// finds nothing.
/// </para>
/// <para>
/// Level 1's whole <c>mission.qvm</c> is a single call:
/// <c>DefineMission(1, "Mission 1 Name", "Mission 1 Desc", "", 2, "missions/location0/level1",
/// "missions/location0/common", "missions/location0/level1", "location0", "level1", "mission1.spr")</c>.
/// </para>
/// </remarks>
/// <param name="Id">The mission id, unique across the registry.</param>
/// <param name="Name">The display name, 32 bytes in the original's record.</param>
/// <param name="Description">The description, 256 bytes.</param>
/// <param name="Tag">A third string, 32 bytes, empty in every retail mission.</param>
/// <param name="NextMissionId">
/// The mission that follows this one, or <see cref="NextMissionUndefined"/>. The original registers that
/// sentinel as the script constant <c>MISSION_NEXT_MISSION_UNDEFINED</c>.
/// </param>
/// <param name="MissionDirectory">The mission's own directory, for example <c>missions/location0/level1</c>.</param>
/// <param name="CommonPackDirectory">The directory the common pack is opened from.</param>
/// <param name="LevelPackDirectory">The directory the level pack is opened from.</param>
/// <param name="CommonPackName">The common pack's name, for example <c>location0</c>.</param>
/// <param name="LevelPackName">The level pack's name, for example <c>level1</c>.</param>
/// <param name="BriefingSprite">The briefing image, for example <c>mission1.spr</c>.</param>
public sealed record MissionDescriptor(
    int Id,
    string Name,
    string Description,
    string Tag,
    int NextMissionId,
    string MissionDirectory,
    string CommonPackDirectory,
    string LevelPackDirectory,
    string CommonPackName,
    string LevelPackName,
    string BriefingSprite)
{
    /// <summary>Gets the packs this mission opens, in order, or null for the two the first game opens.</summary>
    /// <remarks>
    /// <para>
    /// I.G.I. 2 opens four and the order is behaviour, so its catalogue names them here. A descriptor
    /// that names none falls back to <see cref="PacksToOpen"/>'s pair, which is exactly what Project
    /// I.G.I. has always opened.
    /// </para>
    /// </remarks>
    public IReadOnlyList<PackReference>? Packs { get; init; }

    /// <summary>Gets the four-character mode this mission belongs to, or empty when the title has none.</summary>
    /// <remarks>
    /// <para>
    /// I.G.I. 2's <c>DefineMission</c> takes it as <b>four separate single-character arguments</b>, which
    /// <c>Mission_DefineMissionCB</c> (<c>igi2 0x40E2D0</c>) packs big-endian into one word it calls
    /// <c>aazGameModeID</c>. The shipped campaign spells <c>SNGL</c> and the shipped multiplayer maps
    /// <c>MULT</c>.
    /// </para>
    /// <para>
    /// It is where <see cref="NextMissionId"/> sits in the first game's argument list, and it replaces
    /// it: I.G.I. 2 names no next mission at all, so a campaign order has to come from somewhere else.
    /// </para>
    /// </remarks>
    public string GameMode { get; init; } = string.Empty;

    /// <summary>Gets the packs to open, in order, falling back to the pair the first game names.</summary>
    public IReadOnlyList<PackReference> PacksToOpen =>
        Packs is { Count: > 0 } named
            ? named
            : [new PackReference($"LOCAL:{CommonPackDirectory}/", CommonPackName), new PackReference($"LOCAL:{LevelPackDirectory}/", LevelPackName)];

    /// <summary>The value of <see cref="NextMissionId"/> that means "no mission follows this one".</summary>
    public const int NextMissionUndefined = -1;

    /// <summary>The script constant the original registers for <see cref="NextMissionUndefined"/>.</summary>
    public const string NextMissionUndefinedConstant = "MISSION_NEXT_MISSION_UNDEFINED";

    /// <summary>Gets the resource prefix the <c>MISSION:</c> alias is bound to, with its trailing slash.</summary>
    public string MissionAliasTarget => $"LOCAL:{MissionDirectory}/";

    /// <summary>Gets the extensionless base path the terrain files hang off.</summary>
    /// <remarks>
    /// The original builds this as <c>"%sterrain/terrain"</c> from the mission alias target and lets the
    /// terrain loader append each extension itself.
    /// </remarks>
    public string TerrainBasePath => $"{MissionAliasTarget}terrain/terrain";

    /// <inheritdoc/>
    public override string ToString() => $"mission {Id} '{Name}' ({MissionDirectory})";
}

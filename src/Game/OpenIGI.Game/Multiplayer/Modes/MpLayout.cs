using System.Text.Json;
using System.Text.Json.Serialization;
using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Multiplayer.Modes;

/// <summary>What a point on a multiplayer map is for.</summary>
public enum MpPointKind
{
    /// <summary>A place a team takes and holds (domination, hardpoint).</summary>
    Capture,

    /// <summary>A site a bomb is planted at (search and destroy).</summary>
    BombSite,

    /// <summary>Where a team's flag stands (capture the flag).</summary>
    Flag,

    /// <summary>Where a hostage is held at the start (hostage rescue).</summary>
    Hostage,

    /// <summary>Where hostages are brought to be rescued.</summary>
    Rescue,

    /// <summary>The ground a team buys its weapons in.</summary>
    BuyZone,
}

/// <summary>A box of a map, in world units: where the match is played.</summary>
/// <param name="Min">Its lowest corner.</param>
/// <param name="Max">Its highest corner.</param>
public sealed record MpBox(double[] Min, double[] Max)
{
    /// <summary>Gets whether a place is inside.</summary>
    /// <param name="position">The place.</param>
    /// <returns><see langword="true"/> when within every side.</returns>
    public bool Contains(Vector3d position) =>
        Min.Length == 3 && Max.Length == 3
        && position.X >= Min[0] && position.X <= Max[0]
        && position.Y >= Min[1] && position.Y <= Max[1]
        && position.Z >= Min[2] && position.Z <= Max[2];
}

/// <summary>A place a player starts at.</summary>
/// <param name="Position">Where, in world units.</param>
/// <param name="Yaw">Which way it faces, in degrees.</param>
/// <param name="Team">The team that starts there, or -1 for anyone (free for all).</param>
public sealed record MpSpawn(double[] Position, float Yaw = 0, int Team = -1);

/// <summary>A weapon a map hands its players, and the rounds of its ammunition they are filled to.</summary>
/// <param name="Weapon">The weapon's script id (<c>WEAPON_ID_AK47</c>).</param>
/// <param name="Rounds">The rounds its ammunition is filled to; nought for the weapon alone.</param>
public sealed record MpLoadout(string Weapon, int Rounds = 0);

/// <summary>A point a mode plays for: a flag, a site, a hill.</summary>
/// <param name="Id">Its name as the HUD shows it, as <c>A</c>.</param>
/// <param name="Kind">What it is for.</param>
/// <param name="Position">Where, in world units.</param>
/// <param name="Radius">How near counts as at it, in world units.</param>
/// <param name="Team">The team it belongs to, or -1.</param>
public sealed record MpPoint(string Id, MpPointKind Kind, double[] Position, double Radius, int Team = -1);

/// <summary>
/// A multiplayer map: which part of a level is played, where the players start, and the points the modes play for -- data
/// beside the level, so a campaign mission becomes an arena and a modder's map plays every mode it lays out.
/// </summary>
/// <remarks>
/// One file per map, <c>multiplayer/&lt;title&gt;/&lt;map&gt;.json</c> in the game's content or a mod's. Positions are in the
/// title's world units. A level played this way runs without its mission's soldiers and scripts unless it says otherwise.
/// </remarks>
public sealed record MpLayout
{
    /// <summary>Gets where a map's layout is read from in the game's content (a mod's first): one file a map, under its game.</summary>
    /// <param name="title">The game, as <c>igi1</c>.</param>
    /// <param name="map">The map, as the session names it.</param>
    /// <returns>The content path.</returns>
    public static string ContentPath(string title, string map) => $"LOCAL:multiplayer/{title}/{map}.json";

    /// <summary>The layout format this build reads.</summary>
    public const int CurrentFormat = 1;

    /// <summary>Gets the layout format.</summary>
    public int Format { get; init; } = CurrentFormat;

    /// <summary>Gets the game the map is for, as <c>igi1</c>.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Gets the level it plays, as the session names maps (a mission number, or a level's name).</summary>
    public string Map { get; init; } = string.Empty;

    /// <summary>Gets the map's name as the server list shows it.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Gets the modes it is laid out for; empty for any its points allow.</summary>
    public string[] Modes { get; init; } = [];

    /// <summary>Gets the boxes the match is played in; outside every one is out of bounds. Empty for the whole level.</summary>
    public MpBox[] Bounds { get; init; } = [];

    /// <summary>Gets the seconds a player may stay out of bounds before it dies.</summary>
    public double OutOfBoundsSeconds { get; init; } = 5;

    /// <summary>Gets the places players start at.</summary>
    public MpSpawn[] Spawns { get; init; } = [];

    /// <summary>Gets the points the modes play for.</summary>
    public MpPoint[] Points { get; init; } = [];

    /// <summary>
    /// Gets what a player is handed each time it is put back, beside what the mission gives its own player: weapons by their
    /// script id, each with the rounds its ammunition is filled to.
    /// </summary>
    public MpLoadout[] Loadout { get; init; } = [];

    /// <summary>Gets a value indicating whether the level's own soldiers are kept (a horde map); otherwise none are made.</summary>
    public bool KeepsSoldiers { get; init; }

    /// <summary>Gets a value indicating whether the mission's scripts run; otherwise its objectives and scenes are off.</summary>
    public bool KeepsScripts { get; init; }

    /// <summary>Gets whether a place is in bounds.</summary>
    /// <param name="position">The place.</param>
    /// <returns><see langword="true"/> when inside a box, or when the map has none.</returns>
    public bool InBounds(Vector3d position) => Bounds.Length == 0 || Bounds.Any(box => box.Contains(position));

    /// <summary>Gets whether the map is laid out for a mode.</summary>
    /// <param name="mode">The mode's id.</param>
    /// <returns><see langword="true"/> when listed, or when it lists none.</returns>
    public bool Plays(string mode) => Modes.Length == 0 || Modes.Contains(mode, StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets the spawns a team may start at: its own and anyone's.</summary>
    /// <param name="team">The team, or -1 for a player of no team.</param>
    /// <returns>The spawns.</returns>
    public IEnumerable<MpSpawn> SpawnsFor(int team) => Spawns.Where(spawn => spawn.Team < 0 || team < 0 || spawn.Team == team);

    /// <summary>Reads a layout.</summary>
    /// <param name="json">Its text.</param>
    /// <param name="problem">Why it could not be read, when it could not.</param>
    /// <returns>The layout, or <see langword="null"/> when it is malformed, of a newer format, or lays out nothing to play.</returns>
    public static MpLayout? Parse(string json, out string? problem)
    {
        ArgumentNullException.ThrowIfNull(json);
        MpLayout? layout;
        try
        {
            layout = JsonSerializer.Deserialize(json, MpLayoutJson.Default.MpLayout);
        }
        catch (JsonException ex)
        {
            problem = $"not a layout: {ex.Message}";
            return null;
        }

        problem = layout switch
        {
            null => "empty",
            { Format: > CurrentFormat } => $"format {layout.Format} is newer than this game reads ({CurrentFormat})",
            { Spawns.Length: 0 } => "no spawns",
            _ when layout.Spawns.Any(s => s.Position.Length != 3) || layout.Points.Any(p => p.Position.Length != 3) => "a position is not x, y, z",
            _ when layout.Bounds.Any(b => b.Min.Length != 3 || b.Max.Length != 3) => "a bound is not two corners of x, y, z",
            _ => null,
        };
        return problem is null ? layout : null;
    }

    /// <summary>Writes the layout, as the editor saves it.</summary>
    /// <returns>Its text.</returns>
    public string ToJson() => JsonSerializer.Serialize(this, MpLayoutJson.Default.MpLayout);
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(MpLayout))]
internal sealed partial class MpLayoutJson : JsonSerializerContext;

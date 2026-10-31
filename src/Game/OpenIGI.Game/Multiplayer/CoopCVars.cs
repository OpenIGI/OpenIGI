using OpenIGI.Core.Configuration;

namespace OpenIGI.Game.Multiplayer;

/// <summary>How a co-op room plays, as its host sets it: the host's game decides, so a joiner's settings are not read.</summary>
[CVarContainer]
public static class CoopCVars
{
    /// <summary>Whether co-op players' rounds and blasts hurt one another.</summary>
    public static readonly BoolCVar FriendlyFire = CVar.Bool(
        "coop_friendlyfire",
        false,
        CVarOptions.Archive,
        "Whether co-op players' rounds hurt one another. The host's setting is the room's.");

    /// <summary>How much tougher the soldiers are for each player past the first, in percent of their health.</summary>
    public static readonly NumericCVar<int> EnemyHealthPerPlayer = CVar.Int(
        "coop_enemy_health",
        0,
        0,
        200,
        CVarOptions.Archive,
        "Percent more health the soldiers have for each co-op player past the first. The host's setting is the room's.");

    /// <summary>How long a teammate stands beside a downed player to bring it back, in seconds.</summary>
    public static readonly NumericCVar<int> ReviveSeconds = CVar.Int(
        "coop_revive_seconds",
        5,
        1,
        30,
        CVarOptions.Archive,
        "Seconds a teammate stands beside a downed co-op player to bring it back. The host's setting is the room's.");
}

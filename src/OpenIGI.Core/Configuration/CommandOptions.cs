namespace OpenIGI.Core.Configuration;

/// <summary>
/// Behavioural attributes of a console command.
/// </summary>
[Flags]
public enum CommandOptions
{
    /// <summary>No special treatment.</summary>
    None = 0,

    /// <summary>The command may only run while cheats are enabled.</summary>
    Cheat = 1 << 0,

    /// <summary>The command belongs to the development tooling rather than to player-facing features.</summary>
    Developer = 1 << 1,

    /// <summary>The command is omitted from listings and completion unless explicitly named.</summary>
    Hidden = 1 << 2,
}

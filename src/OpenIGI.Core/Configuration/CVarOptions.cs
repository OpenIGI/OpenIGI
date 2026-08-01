namespace OpenIGI.Core.Configuration;

/// <summary>
/// Behavioural attributes of a console variable.
/// </summary>
/// <remarks>
/// The flags decide how a variable is treated by the three views onto the registry — the settings
/// file, the developer console and the options menu — so that all three agree without any of them
/// keeping its own list of what is persistable, hidden or protected.
/// </remarks>
[Flags]
public enum CVarOptions
{
    /// <summary>No special treatment.</summary>
    None = 0,

    /// <summary>The value is written to, and restored from, the settings file.</summary>
    /// <remarks>Only variables that differ from their default are written, so the file stays a diff.</remarks>
    Archive = 1 << 0,

    /// <summary>The value may only be changed while cheats are enabled.</summary>
    Cheat = 1 << 1,

    /// <summary>The value is published for inspection but cannot be assigned from the console.</summary>
    ReadOnly = 1 << 2,

    /// <summary>A change takes effect only after the game is restarted.</summary>
    /// <remarks>The options menu shows a badge for these; the console prints a note on assignment.</remarks>
    RequiresRestart = 1 << 3,

    /// <summary>The variable is omitted from listings and completion unless explicitly named.</summary>
    Hidden = 1 << 4,

    /// <summary>The variable belongs to the development tooling rather than to player-facing settings.</summary>
    Developer = 1 << 5,
}

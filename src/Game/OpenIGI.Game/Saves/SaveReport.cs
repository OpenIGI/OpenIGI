namespace OpenIGI.Game.Saves;

/// <summary>How a save or a load went, as the player is told it.</summary>
public enum SaveReport
{
    /// <summary>Written: <c>Saving complete</c> (<c>Game_SaveToSlot</c>).</summary>
    Saved,

    /// <summary>Not written: <c>Saving failed</c>.</summary>
    SaveFailed,

    /// <summary>Not written, the battery spent. The remaster's own line: retail's computer never offers a save then.</summary>
    NoSavesLeft,

    /// <summary>Read onto the level: <c>Loading complete</c> (<c>Game_LoadFromSlot</c>).</summary>
    Loaded,

    /// <summary>Its header checked but its state did not read: <c>Loading failed</c>.</summary>
    LoadFailed,
}

/// <summary>The lines a <see cref="SaveReport"/> is said with.</summary>
public static class SaveReports
{
    /// <summary>
    /// The line for a report: retail's own words, which are also the keys I.G.I. 2's language files translate them under
    /// (<c>StatusMessage_New</c> with <c>isLocalize</c>).
    /// </summary>
    /// <param name="report">The report.</param>
    /// <returns>The line.</returns>
    public static string Line(SaveReport report) => report switch
    {
        SaveReport.Saved => "Saving complete",
        SaveReport.SaveFailed => "Saving failed",
        SaveReport.NoSavesLeft => "No saves are left for this mission",
        SaveReport.Loaded => "Loading complete",
        _ => "Loading failed",
    };

    /// <summary>Gets whether a report's line is one of retail's, which its language files translate.</summary>
    /// <param name="report">The report.</param>
    /// <returns><see langword="false"/> for the remaster's own line.</returns>
    public static bool IsRetailLine(SaveReport report) => report != SaveReport.NoSavesLeft;
}

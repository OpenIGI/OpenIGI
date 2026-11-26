namespace OpenIGI.Frontend.Settings;

/// <summary>A value of a row that, once chosen, also sets another setting.</summary>
/// <param name="Value">The row's value, as stored, that sets the other.</param>
/// <param name="Variable">The setting it sets.</param>
/// <param name="Sets">What it sets it to, as stored.</param>
/// <remarks>
/// Applied only as the row changes to <paramref name="Value"/> while the page is open, never when the settings are
/// loaded, so the player can set the other setting back afterwards and keep it.
/// </remarks>
public sealed record RowEffect(string Value, string Variable, string Sets)
{
    /// <summary>Whether a change of the row's value is the one that sets the other setting.</summary>
    /// <param name="previous">The row's value before, as stored.</param>
    /// <param name="current">The row's value now, as stored.</param>
    /// <returns><see langword="true"/> when the row has just been changed to <see cref="Value"/>.</returns>
    public bool Fires(string previous, string current) =>
        string.Equals(current, Value, StringComparison.OrdinalIgnoreCase)
        && !string.Equals(previous, Value, StringComparison.OrdinalIgnoreCase);
}

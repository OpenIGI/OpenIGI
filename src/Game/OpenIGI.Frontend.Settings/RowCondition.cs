namespace OpenIGI.Frontend.Settings;

/// <summary>A row shown only while another setting is on, or only while it is off.</summary>
/// <param name="Variable">The setting the row follows.</param>
/// <param name="WhileOff">Whether the row is shown while that setting is off, rather than while it is on.</param>
/// <remarks>
/// Rows that mean nothing alone — a day's length without the cycle, a crosshair's colour without the crosshair —
/// stay out of the way until the setting that gives them meaning is chosen, and come in right under it.
/// </remarks>
public sealed record RowCondition(string Variable, bool WhileOff = false)
{
    /// <summary>Whether a value of the followed setting shows the row.</summary>
    /// <param name="valueText">The setting's value, as stored.</param>
    /// <returns><see langword="true"/> to show the row.</returns>
    public bool Shows(string valueText) => IsOn(valueText) != WhileOff;

    /// <summary>
    /// Whether a stored value reads as on: anything but empty, nought, or a choice named off, none or false.
    /// </summary>
    /// <param name="valueText">The value, as stored.</param>
    /// <returns><see langword="true"/> when the value is on.</returns>
    public static bool IsOn(string valueText)
    {
        string text = (valueText ?? string.Empty).Trim();
        if (text.Length == 0
            || text.Equals("off", StringComparison.OrdinalIgnoreCase)
            || text.Equals("none", StringComparison.OrdinalIgnoreCase)
            || text.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double number)
            || number != 0d;
    }
}

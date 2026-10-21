using System.Globalization;

namespace OpenIGI.Core.Configuration;

/// <summary>A resolution as a setting holds it: <c>WIDTHxHEIGHT</c>, the way <c>-size</c> takes it.</summary>
/// <remarks>
/// One spelling, read and written in one place, so the settings page that offers a mode and the host that
/// applies it cannot disagree about what a value means.
/// </remarks>
public static class ResolutionText
{
    /// <summary>Writes a resolution.</summary>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    /// <returns>The text, e.g. <c>1920x1080</c>.</returns>
    public static string Format(int width, int height) =>
        string.Create(CultureInfo.InvariantCulture, $"{width}x{height}");

    /// <summary>Reads a resolution.</summary>
    /// <param name="text">The text, e.g. <c>1920x1080</c>.</param>
    /// <param name="width">Receives the width.</param>
    /// <param name="height">Receives the height.</param>
    /// <returns><see langword="true"/> when it names a size; <see langword="false"/> for empty or anything else.</returns>
    public static bool TryParse(string? text, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string[] parts = text.Trim().Split('x', 'X', '*');
        if (parts.Length != 2
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int parsedWidth)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int parsedHeight)
            || parsedWidth <= 0
            || parsedHeight <= 0)
        {
            return false;
        }

        width = parsedWidth;
        height = parsedHeight;
        return true;
    }
}

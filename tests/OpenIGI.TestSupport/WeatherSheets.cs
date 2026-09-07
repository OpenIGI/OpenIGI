namespace OpenIGI.TestSupport;

/// <summary>
/// Locates the storm's painted streak sheets for tests that check the renderer's arithmetic against
/// the real images.
/// </summary>
/// <remarks>
/// <para>
/// The sheets are licensed artwork and are not in this repository, for the same reason no original
/// game file is: they are not ours to redistribute. A build without them still runs and still rains —
/// <c>StormRenderer.BuildDropSheet</c> falls back to streaks it generates itself — so their absence is
/// silent, and the tests that measure the shipped sheets would otherwise fail on every machine that
/// has not been given them.
/// </para>
/// <para>
/// Drop <c>rain-streaks.png</c> and <c>rain-streaks-defocused.png</c> into
/// <c>apps/OpenIGI.Desktop/Assets/weather/</c> and they are copied beside the test binaries by the
/// desktop project's own asset glob, which is where this looks for them.
/// </para>
/// </remarks>
public static class WeatherSheets
{
    /// <summary>The directory, relative to a build's output, the sheets are copied into.</summary>
    public const string AssetDirectory = "Assets/weather";

    private static readonly string[] RequiredSheets =
    [
        "rain-streaks.png",
        "rain-streaks-defocused.png",
    ];

    /// <summary>Gets a value indicating whether every sheet the storm loads is present.</summary>
    /// <remarks>
    /// All of them or none: the renderer needs both strips before it will use either, so a directory
    /// holding one of the two produces the generated fallback exactly as an empty one does.
    /// </remarks>
    public static bool AreAvailable => RequiredSheets.All(sheet => File.Exists(PathTo(sheet)));

    /// <summary>Resolves one sheet's path beside the running test binary.</summary>
    /// <param name="sheet">The file name, such as <c>rain-streaks.png</c>.</param>
    /// <returns>The absolute path, which is not checked for existence.</returns>
    public static string PathTo(string sheet) =>
        Path.Combine(AppContext.BaseDirectory, "Assets", "weather", sheet);
}

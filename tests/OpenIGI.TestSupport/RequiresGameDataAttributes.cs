using Xunit;

namespace OpenIGI.TestSupport;

/// <summary>A <see cref="FactAttribute"/> that skips when no original game installation is configured.</summary>
public sealed class RequiresGameDataFactAttribute : FactAttribute
{
    /// <summary>Initializes a new instance of the <see cref="RequiresGameDataFactAttribute"/> class.</summary>
    public RequiresGameDataFactAttribute()
    {
        if (!GameData.IsAvailable)
        {
            Skip = $"Set {GameData.EnvironmentVariable} to an original installation to run this test.";
        }
    }
}

/// <summary>A <see cref="TheoryAttribute"/> that skips when no original game installation is configured.</summary>
public sealed class RequiresGameDataTheoryAttribute : TheoryAttribute
{
    /// <summary>Initializes a new instance of the <see cref="RequiresGameDataTheoryAttribute"/> class.</summary>
    public RequiresGameDataTheoryAttribute()
    {
        if (!GameData.IsAvailable)
        {
            Skip = $"Set {GameData.EnvironmentVariable} to an original installation to run this test.";
        }
    }
}

/// <summary>A <see cref="FactAttribute"/> that skips when the storm's painted sheets are not present.</summary>
/// <remarks>
/// For the tests that measure the shipped artwork rather than the streaks the renderer generates when
/// it is missing. See <see cref="WeatherSheets"/> for why the images are not in the repository and
/// where to put them.
/// </remarks>
public sealed class RequiresWeatherSheetsFactAttribute : FactAttribute
{
    /// <summary>Initializes a new instance of the <see cref="RequiresWeatherSheetsFactAttribute"/> class.</summary>
    public RequiresWeatherSheetsFactAttribute()
    {
        if (!WeatherSheets.AreAvailable)
        {
            Skip = $"Put the storm's painted sheets in {WeatherSheets.AssetDirectory} to run this test.";
        }
    }
}

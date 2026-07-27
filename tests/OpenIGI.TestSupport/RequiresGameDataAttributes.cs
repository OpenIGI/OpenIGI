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

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

/// <summary>A <see cref="FactAttribute"/> that skips unless the named title's installation is configured.</summary>
/// <remarks>
/// The per-title sibling of <see cref="RequiresGameDataFactAttribute"/>, which stays as the I.G.I. 1
/// spelling so the tests already using it do not have to change. A golden test is not shared between
/// titles — it asserts what one specific retail file decodes to, and the two games ship different files
/// — so each title's tests carry their own attribute and skip independently.
/// </remarks>
/// <param name="titleId">The title's stable id, for example <c>igi2</c>.</param>
public sealed class RequiresTitleDataFactAttribute(string titleId) : FactAttribute
{
    private string TitleId { get; } = titleId;

    /// <inheritdoc/>
    public override string? Skip
    {
        get => SkipFor(TitleId) ?? base.Skip;
        set => base.Skip = value;
    }

    internal static string? SkipFor(string titleId)
    {
        GameDataSource source = GameDataSource.For(titleId);
        return source.IsAvailable
            ? null
            : $"Set {source.EnvironmentVariable} to a {source.Title.DisplayName} installation to run this test.";
    }
}

/// <summary>A <see cref="TheoryAttribute"/> that skips unless the named title's installation is configured.</summary>
/// <param name="titleId">The title's stable id, for example <c>igi2</c>.</param>
public sealed class RequiresTitleDataTheoryAttribute(string titleId) : TheoryAttribute
{
    private string TitleId { get; } = titleId;

    /// <inheritdoc/>
    public override string? Skip
    {
        get => RequiresTitleDataFactAttribute.SkipFor(TitleId) ?? base.Skip;
        set => base.Skip = value;
    }
}

/// <summary>
/// A <see cref="FactAttribute"/> that skips unless an environment variable names an existing directory.
/// </summary>
/// <remarks>
/// For a check that needs data too large to keep in the repository — reference decodes produced by an
/// independent tool, for instance. The check is worth having and worth running deliberately; it is not
/// worth nineteen megabytes in everybody's clone, and a test that quietly passed when the data was
/// absent would be worse than one that says it did not run.
/// </remarks>
/// <param name="variable">The environment variable naming the directory.</param>
/// <param name="what">What the directory should hold, for the skip message.</param>
public sealed class RequiresDirectoryFactAttribute(string variable, string what) : FactAttribute
{
    private string Variable { get; } = variable;

    private string What { get; } = what;

    /// <inheritdoc/>
    public override string? Skip
    {
        get
        {
            string? path = Environment.GetEnvironmentVariable(Variable);
            return !string.IsNullOrWhiteSpace(path) && Directory.Exists(path)
                ? base.Skip
                : $"Set {Variable} to a directory holding {What} to run this test.";
        }

        set => base.Skip = value;
    }
}

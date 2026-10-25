using System.Reflection;
using OpenIGI.Core.Configuration;
using Xunit;

namespace OpenIGI.Rendering.Tests;

/// <summary>
/// Pins every console variable the rendering assembly declares: its name, its default and whether the
/// settings file keeps it.
/// </summary>
/// <remarks>
/// <para>
/// The renderer's variables used to live in one class and now live in a container per domain beside
/// <see cref="DisplayCVars"/>. Moving a declaration between containers must not rename it, change its
/// default or drop it from the settings file, and none of those would fail a build: a renamed variable is a
/// saved setting silently ignored, a lost <see cref="CVarOptions.Archive"/> is a preference forgotten at the
/// next start.
/// </para>
/// <para>
/// The baseline was produced from the declarations as they stood before the split, by reading their source
/// rather than running it, so it records what was written and not what this build makes of it. A variable
/// added, renamed or re-tuned on purpose updates the baseline in the same change.
/// </para>
/// </remarks>
public sealed class RenderCVarInventoryTests
{
    private static readonly string BaselinePath =
        Path.Combine(AppContext.BaseDirectory, "Baselines", "render-cvars.txt");

    /// <summary>The rendering containers declare exactly the variables the baseline lists, with its defaults and flags.</summary>
    [Fact]
    public void DeclaredVariablesMatchTheBaseline()
    {
        string[] expected =
        [
            .. File.ReadAllLines(BaselinePath)
                .Where(line => line.Length > 0),
        ];

        string[] actual =
        [
            .. Declared()
                .Select(variable => string.Join(
                    '\t',
                    variable.Name,
                    variable.DefaultText,
                    variable.Flags.HasFlag(CVarOptions.Archive) ? "archive" : "-"))
                .Order(StringComparer.Ordinal),
        ];

        string[] missing = [.. expected.Except(actual, StringComparer.Ordinal)];
        string[] unexpected = [.. actual.Except(expected, StringComparer.Ordinal)];

        Assert.True(
            missing.Length == 0 && unexpected.Length == 0,
            "The rendering variables differ from the baseline.\nExpected but not declared:\n  "
                + string.Join("\n  ", missing)
                + "\nDeclared but not expected:\n  "
                + string.Join("\n  ", unexpected));
        Assert.Equal(expected, actual);
    }

    /// <summary>No two rendering containers declare a variable under the same name.</summary>
    [Fact]
    public void EveryNameIsDeclaredOnce()
    {
        string[] duplicates =
        [
            .. Declared()
                .GroupBy(variable => variable.Name, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key),
        ];

        Assert.Empty(duplicates);
    }

    /// <summary>Every console variable held by a <c>[CVarContainer]</c> class in the rendering assembly.</summary>
    private static IEnumerable<ICVar> Declared() =>
        typeof(DisplayCVars).Assembly
            .GetTypes()
            .Where(type => type.IsDefined(typeof(CVarContainerAttribute), inherit: false))
            .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            .Where(field => typeof(ICVar).IsAssignableFrom(field.FieldType))
            .Select(field => (ICVar)field.GetValue(null)!);
}

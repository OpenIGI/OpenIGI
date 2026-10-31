using System.Globalization;
using System.Text;
using OpenIGI.Core.Configuration;
using OpenIGI.Core.Configuration.Presets;
using OpenIGI.Frontend.Settings;
using OpenIGI.Rendering;
using Xunit;

namespace OpenIGI.Frontend.Tests;

/// <summary>
/// Keeps the settings page and the graphics presets from drifting apart.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="GraphicsMenu"/> claims its steps are the values the four presets use, and the claim is the
/// reason the page works at all: choosing High makes every row read High, and moving one row drops the
/// preset to Custom because the settings really are no longer High. Nothing enforced it, and it had
/// quietly stopped being true in four places at once — Texture Quality's top step wrote a material detail
/// no preset used, Shadow Quality's wrote a resolution and a softness no preset used, Image Style's
/// strongest grade matched nothing, and Ambient Occlusion offered an eight-direction step no preset could
/// produce.
/// </para>
/// <para>
/// None of that was visible. A drifted step does not throw or log; the row simply reads Custom, or reads
/// the step below the one that is set, and a player is left unable to tell whether the menu or the
/// renderer is lying to them. So the check has to be mechanical, and it has to say precisely what
/// disagreed — "reading Custom" names neither the variable nor its value, and the Global Illumination
/// group names thirteen of them.
/// </para>
/// </remarks>
[Collection(GraphicsSettingsRegistries.Name)]
public sealed class GraphicsMenuPresetAlignmentTests : IDisposable
{
    private static readonly GraphicsPreset[] Tiers =
        [GraphicsPreset.Vanilla, GraphicsPreset.Enhanced, GraphicsPreset.High, GraphicsPreset.Ultra];

    private readonly Dictionary<ICVar, string> _restore;

    /// <summary>Remembers every managed value, so applying presets here does not leak into other tests.</summary>
    public GraphicsMenuPresetAlignmentTests()
    {
        _ = LightingCVars.Lighting; // force the CVar container's static initialiser
        _restore = GraphicsPresetRegistry.Default.Managed.ToDictionary(
            variable => variable, variable => variable.ValueText);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach ((ICVar variable, string value) in _restore)
        {
            variable.TrySetText(value, out _);
        }

        GraphicsPresetRegistry.Default.Detect();
    }

    /// <summary>Every preset leaves every row of the page naming a step, rather than reading Custom.</summary>
    [Fact]
    public void EveryPresetLandsEveryGroupOnAStepRatherThanCustom()
    {
        foreach (GraphicsPreset preset in Tiers)
        {
            GraphicsPresetRegistry.Default.Apply(preset);

            foreach (QualityGroup group in GraphicsMenu.Groups)
            {
                Assert.True(
                    QualityBinding.Detect(CVarRegistry.Default, group) is not null,
                    Explain(group, preset));
            }
        }
    }

    /// <summary>
    /// Every step the page offers is some preset's values, so no row can offer a setting the presets
    /// cannot reach.
    /// </summary>
    /// <remarks>
    /// The converse of the check above, and it catches the other half of the drift: a step nobody can ever
    /// arrive at by choosing a preset is a rung invented in the menu, and picking it puts the player
    /// somewhere the presets have never been measured. Compared against values read back AFTER applying a
    /// preset rather than against the preset table's declared text, so a value whose spelling does not
    /// survive the round trip — <c>0.70</c> stored and read back as <c>0.7</c> — fails here too.
    /// </remarks>
    [Fact]
    public void EveryStepIsSomePresetsValues()
    {
        var columns = new List<Dictionary<string, string>>();
        foreach (GraphicsPreset preset in Tiers)
        {
            GraphicsPresetRegistry.Default.Apply(preset);
            columns.Add(GraphicsPresetRegistry.Default.Managed.ToDictionary(
                variable => variable.Name, variable => variable.ValueText, StringComparer.Ordinal));
        }

        foreach (QualityGroup group in GraphicsMenu.Groups)
        {
            foreach (QualityLevel level in group.Levels)
            {
                bool matched = false;
                for (int i = 0; i < columns.Count && !matched; i++)
                {
                    matched = level.Values.All(pair =>
                        columns[i].TryGetValue(pair.Variable, out string? held)
                        && CVarRegistry.Default.TryGet(pair.Variable, out ICVar? cvar)
                        && cvar is not null
                        && string.Equals(held, Normalise(cvar, pair.Value), StringComparison.OrdinalIgnoreCase));
                }

                Assert.True(
                    matched,
                    $"{group.Caption} offers \"{level.Name}\", which is no preset's values — so choosing it "
                        + "puts the player on a setting the presets have never been measured at.");
            }
        }
    }

    /// <summary>
    /// Every archived graphics variable is either a row on the page or a declared console-only setting.
    /// </summary>
    /// <remarks>
    /// The page is deliberately much smaller than the renderer, and the risk in that is a variable falling
    /// through the gap: not shown, and not written down as deliberately not shown, so nobody can tell
    /// whether it was a decision. <see cref="GraphicsMenu.ConsoleOnly"/> is where that decision is
    /// recorded, and this is what makes recording it compulsory.
    /// </remarks>
    [Fact]
    public void EveryArchivedGraphicsVariableIsOnThePageOrDeclaredConsoleOnly()
    {
        HashSet<string> accounted =
        [
            .. GraphicsMenu.Groups.SelectMany(group => group.Variables),
            .. GraphicsMenu.Settings.Select(setting => setting.Variable),
            .. GraphicsMenu.InterfaceSettings.Select(setting => setting.Variable),
            .. GraphicsMenu.DisplaySettings.Select(setting => setting.Variable),
            .. GraphicsMenu.ConsoleOnly,
        ];

        string[] adrift =
        [
            .. CVarRegistry.Default.All
                .Where(variable => variable.Name.StartsWith("r_", StringComparison.Ordinal))
                .Where(variable => variable.Flags.HasFlag(CVarOptions.Archive))
                .Where(variable => !variable.Flags.HasFlag(CVarOptions.Hidden))
                .Select(variable => variable.Name)
                .Where(name => !accounted.Contains(name))
                .Order(StringComparer.Ordinal),
        ];

        Assert.True(
            adrift.Length == 0,
            "These archived graphics settings are neither shown on the settings page nor listed in "
                + "GraphicsMenu.ConsoleOnly with a reason, so nobody can tell whether leaving them out was "
                + "a decision: " + string.Join(", ", adrift));
    }

    /// <summary>Every variable declared console-only is one the renderer actually has.</summary>
    [Fact]
    public void EveryConsoleOnlyVariableExists()
    {
        foreach (string name in GraphicsMenu.ConsoleOnly)
        {
            Assert.True(
                CVarRegistry.Default.TryGet(name, out ICVar? cvar) && cvar is not null,
                $"GraphicsMenu.ConsoleOnly names {name}, which is not a registered variable.");
        }
    }

    /// <summary>Says which variable disagreed with which step, and by what, rather than "Custom".</summary>
    private static string Explain(QualityGroup group, GraphicsPreset preset)
    {
        var message = new StringBuilder()
            .Append(CultureInfo.InvariantCulture, $"The {preset} preset leaves \"{group.Caption}\" matching none of its steps, so the row ")
            .Append("reads Custom on a preset the player has just chosen. Per step:");

        foreach (QualityLevel level in group.Levels)
        {
            message.Append(CultureInfo.InvariantCulture, $"\n  {level.Name}:");
            foreach ((string variable, string value) in level.Values)
            {
                if (!CVarRegistry.Default.TryGet(variable, out ICVar? cvar) || cvar is null)
                {
                    message.Append(CultureInfo.InvariantCulture, $"\n    {variable} is not registered");
                }
                else if (!cvar.MatchesText(value))
                {
                    message.Append(CultureInfo.InvariantCulture, $"\n    {variable} is {cvar.ValueText}, the step says {value}");
                }
            }
        }

        return message.ToString();
    }

    /// <summary>The text a value takes once the variable has parsed and stored it.</summary>
    private static string Normalise(ICVar cvar, string value)
    {
        string held = cvar.ValueText;
        if (!cvar.TrySetText(value, out _))
        {
            return value;
        }

        string normalised = cvar.ValueText;
        cvar.TrySetText(held, out _);
        return normalised;
    }
}

/// <summary>
/// Serialises the test classes that drive the process-wide CVar and preset registries.
/// </summary>
/// <remarks>
/// <see cref="GraphicsPresetRegistry.Default"/> and <see cref="CVarRegistry.Default"/> are one instance
/// each for the whole process, and applying a preset writes about a hundred of them. xUnit runs test
/// classes in the same assembly in parallel, so without this the alignment tests and
/// <see cref="QualityBindingTests"/> would take turns pulling the same variables out from under each
/// other — and would do it intermittently, which is the worst way to find out. Run alone, not merely together: the shell's
/// tests make whole settings pages, and register more of the same process's variables while they do.
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class GraphicsSettingsRegistries
{
    /// <summary>The collection's name.</summary>
    public const string Name = "graphics settings registries";
}

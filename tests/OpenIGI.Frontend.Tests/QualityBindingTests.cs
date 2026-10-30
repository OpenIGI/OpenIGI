using OpenIGI.Core.Configuration;
using OpenIGI.Frontend.Settings;
using OpenIGI.UI.Text;
using OpenIGI.UI.Widgets;
using Xunit;

namespace OpenIGI.Frontend.Tests;

/// <summary>
/// Covers the graphics page's quality groups: one control over several variables, both ways, and the
/// Custom reading that says something has moved one of them from elsewhere.
/// </summary>
/// <remarks>
/// "Elsewhere" used to mean the advanced page, which listed every variable as its own row. That page is
/// gone and the console took its place, but the requirement is unchanged and is if anything sharper: a
/// player who types <c>r_shadows 512</c> is owed a Shadow Quality row that admits it no longer reads
/// High.
/// </remarks>
[Collection(GraphicsSettingsRegistries.Name)]
public sealed class QualityBindingTests
{
    private static readonly QualityGroup Shadows = new(
        "Shadow Quality",
        "Sun shadows.",
        new QualityLevel("Off", ("r_shadows", "0"), ("r_shadow_cascades", "1")),
        new QualityLevel("Low", ("r_shadows", "1024"), ("r_shadow_cascades", "2")),
        new QualityLevel("High", ("r_shadows", "2048"), ("r_shadow_cascades", "3")));

    private static UiTheme Theme()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "fonts", "Barlow-Regular.ttf");
        return new UiTheme(UiFont.ForMeasurement(FontAtlasBuilder.Build(File.ReadAllBytes(path), 48f)));
    }

    private static CVarRegistry Registry(string shadows = "2048", string cascades = "3")
    {
        var registry = new CVarRegistry();
        CVar.Int("r_shadows", 2048, 0, 4096, CVarOptions.Archive, "Shadow map resolution.", registry)
            .TrySetText(shadows, out _);
        CVar.Int("r_shadow_cascades", 3, 1, 4, CVarOptions.Archive, "Cascade count.", registry)
            .TrySetText(cascades, out _);
        return registry;
    }

    [Fact]
    public void A_group_opens_on_the_step_its_variables_already_are()
    {
        CVarRegistry registry = Registry(shadows: "1024", cascades: "2");
        Dropdown control = Assert.IsType<Dropdown>(QualityBinding.CreateControl(Theme(), registry, Shadows));

        Assert.Equal("Low", control.SelectedOption);
    }

    [Fact]
    public void Choosing_a_step_writes_every_variable_under_it()
    {
        CVarRegistry registry = Registry(shadows: "0", cascades: "1");
        Dropdown control = QualityBinding.CreateControl(Theme(), registry, Shadows)!;
        Assert.Equal("Off", control.SelectedOption);

        control.SelectedIndex = 2; // High

        Assert.True(registry.TryGet("r_shadows", out ICVar? shadows));
        Assert.True(registry.TryGet("r_shadow_cascades", out ICVar? cascades));
        Assert.Equal("2048", shadows!.ValueText);
        Assert.Equal("3", cascades!.ValueText);

        // And it stays on the step it wrote rather than being knocked to Custom by its own writes echoing
        // back through the variables' change events.
        Assert.Equal("High", control.SelectedOption);
    }

    [Fact]
    public void Moving_one_variable_by_hand_reads_as_Custom()
    {
        CVarRegistry registry = Registry(shadows: "2048", cascades: "3");
        Dropdown control = QualityBinding.CreateControl(Theme(), registry, Shadows)!;
        Assert.Equal("High", control.SelectedOption);

        // What the Advanced tab does: one row, on its own. The set is now between the group's steps.
        Assert.True(registry.TryGet("r_shadow_cascades", out ICVar? cascades));
        cascades!.TrySetText("4", out _);

        Assert.Equal(QualityBinding.CustomOption, control.SelectedOption);

        // Custom is shown so the row can say so, and disabled because there is nothing for it to write.
        Assert.NotNull(control.OptionEnabled);
        Assert.False(control.OptionEnabled!(control.Options.Count - 1));
        Assert.True(control.OptionEnabled(0));
    }

    [Fact]
    public void Landing_back_on_a_step_by_hand_reads_as_that_step()
    {
        CVarRegistry registry = Registry(shadows: "2048", cascades: "3");
        Dropdown control = QualityBinding.CreateControl(Theme(), registry, Shadows)!;

        Assert.True(registry.TryGet("r_shadows", out ICVar? shadows));
        Assert.True(registry.TryGet("r_shadow_cascades", out ICVar? cascades));
        shadows!.TrySetText("1024", out _);
        Assert.Equal(QualityBinding.CustomOption, control.SelectedOption); // half way there

        cascades!.TrySetText("2", out _);
        Assert.Equal("Low", control.SelectedOption);
    }

    [Fact]
    public void A_group_naming_a_variable_that_is_not_registered_builds_nothing()
    {
        var registry = new CVarRegistry();
        CVar.Int("r_shadows", 2048, 0, 4096, CVarOptions.Archive, "Shadow map resolution.", registry);

        // r_shadow_cascades is missing, so the row could only half work.
        Assert.Null(QualityBinding.CreateControl(Theme(), registry, Shadows));
    }

    [Fact]
    public void Every_shipped_group_names_variables_the_renderer_actually_has()
    {
        CVarRegistry registry = CVarRegistry.Default;

        // Registered as the game registers them before it reads its settings (HostConsole): every container of the
        // assemblies the menu's rows come from, not only the ones some other test happened to touch first.
        OpenIGI.Rendering.Generated.CVarRegistrations.RegisterAll();
        OpenIGI.Game.Generated.CVarRegistrations.RegisterAll();

        // CreateControl returns null if ANY variable a group names is unregistered, so a single typo in a
        // thirteen-name group does not produce a broken row -- it produces no row at all, silently. This
        // is what notices.
        foreach (QualityGroup group in GraphicsMenu.Groups)
        {
            foreach (string variable in group.Variables)
            {
                Assert.True(
                    registry.TryGet(variable, out ICVar? cvar) && cvar is not null,
                    $"{group.Caption} names {variable}, which is not registered.");
            }
        }

        foreach (GraphicsSetting setting in AllSettings())
        {
            Assert.True(
                registry.TryGet(setting.Variable, out ICVar? direct) && direct is not null,
                $"The settings menu shows {setting.Caption} ({setting.Variable}), which is not registered.");
        }
    }

    /// <summary>
    /// The page is a small vocabulary over a large one, which is the only reason it is worth having.
    /// </summary>
    /// <remarks>
    /// This used to justify splitting the page in two. There is no split any more — the reduction is now
    /// the whole design rather than one half of it, and the ratio matters more than it did, because what
    /// a row does not cover is reachable only from the console.
    /// </remarks>
    [Fact]
    public void The_page_is_a_fraction_of_the_variables_it_drives()
    {
        CVarRegistry registry = CVarRegistry.Default;
        _ = OpenIGI.Rendering.FrameCVars.VSync;

        int graphicsVariables = 0;
        foreach (ICVar cvar in registry.All.ToList())
        {
            if (cvar.Name.StartsWith("r_", StringComparison.Ordinal) && !cvar.Flags.HasFlag(CVarOptions.Hidden))
            {
                graphicsVariables++;
            }
        }

        // The preset, a row per group, and the settings that are one number.
        int rows = 1 + GraphicsMenu.Groups.Count + GraphicsMenu.Settings.Count;

        Assert.True(
            rows * 2 < graphicsVariables,
            $"The graphics page has {rows} rows against {graphicsVariables} graphics variables, which is "
                + "not enough of a reduction to be worth a page of names for.");
    }

    [Fact]
    public void No_group_drives_a_variable_the_page_also_shows_directly()
    {
        // Two controls over one number, disagreeing about what it is. They need not be on the same page to
        // disagree: the Gameplay and Display rows write the same registry the graphics groups read.
        List<string> direct = [.. AllSettings().Select(setting => setting.Variable)];

        foreach (QualityGroup group in GraphicsMenu.Groups)
        {
            foreach (string variable in group.Variables)
            {
                Assert.DoesNotContain(variable, direct);
            }
        }
    }

    /// <summary>
    /// Nothing on the page is also declared console-only, and nothing is declared twice.
    /// </summary>
    /// <remarks>
    /// <c>ConsoleOnly</c> is the page's own record of what it deliberately does not show and why. It is
    /// only worth anything if it is true, and the way it stops being true is that somebody adds a row for
    /// a variable without removing the line that says there is no row for it.
    /// </remarks>
    [Fact]
    public void Nothing_is_both_shown_and_declared_console_only()
    {
        List<string> shown =
        [
            .. GraphicsMenu.Groups.SelectMany(group => group.Variables),
            .. AllSettings().Select(setting => setting.Variable),
        ];

        foreach (string name in GraphicsMenu.ConsoleOnly)
        {
            Assert.DoesNotContain(name, shown);
        }

        Assert.Equal(GraphicsMenu.ConsoleOnly.Count, GraphicsMenu.ConsoleOnly.Distinct().Count());
        Assert.Equal(shown.Count, shown.Distinct().Count());
    }

    /// <summary>
    /// Nothing a player reads on the settings page is named after a variable or a technique.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The page's whole purpose. Its captions used to be produced by <c>CVarBinding.Humanize</c>, which
    /// turns a variable name into title case and nothing more — so the menu offered rows called "Sdf
    /// Resolution", "Cards Density", "Gi Amortize" and "Taa Sharpen". Every one of those is a correct
    /// name for the thing and a useless name for the choice.
    /// </para>
    /// <para>
    /// Checked as data rather than through a built screen, because the words are the deliverable and they
    /// are wrong in the source before they are ever wrong on screen. The list is of fragments that only
    /// ever arrive by machine-casing a variable name; a caption that legitimately needs one of these words
    /// should say it in full, which is what the whole-word forms allow.
    /// </para>
    /// </remarks>
    [Fact]
    public void No_caption_or_step_is_named_after_a_variable_or_a_technique()
    {
        // Whole words: "Lut" is jargon, "Resolution" is not. "R_" is the underscore check below.
        string[] jargon = ["Sdf", "Ssr", "Ssgi", "Taa", "Msaa", "Aa", "Hdr", "Lut", "Cvar"];

        List<string> words = [.. GraphicsMenu.Groups.Select(group => group.Caption)];
        words.AddRange(GraphicsMenu.Groups.SelectMany(group => group.Levels).Select(level => level.Name));
        words.AddRange(AllSettings().Select(setting => setting.Caption));
        words.AddRange(GraphicsMenu.Sections.Select(section => section.Title));

        foreach (string word in words)
        {
            string[] said = word.Split([' ', '-', '/', '(', ')', ','], StringSplitOptions.RemoveEmptyEntries);
            foreach (string term in jargon)
            {
                Assert.False(
                    said.Any(part => part.Equals(term, StringComparison.OrdinalIgnoreCase)),
                    $"The settings page shows \"{word}\", which reads as a variable name rather than a choice.");
            }

            Assert.False(
                word.Contains('_'),
                $"The settings page shows \"{word}\", which is a variable name.");
        }
    }

    /// <summary>
    /// Every step a player can pick can also be read back, and no two steps are the same values.
    /// </summary>
    /// <remarks>
    /// <c>Detect</c> returns the FIRST step whose values all match, so two steps carrying identical values
    /// make the second unreachable: it sits in the list, is selectable, and then displays as the one above
    /// it. That is not a theoretical failure — the Ambient Occlusion row shipped with an Ultra step of
    /// <c>r_ao 8</c> that no preset ever wrote, so choosing it showed High and a player had no way to tell
    /// whether the setting or the display was lying. Two presets that genuinely agree must be ONE step.
    /// </remarks>
    [Fact]
    public void No_two_steps_of_a_group_write_the_same_values()
    {
        foreach (QualityGroup group in GraphicsMenu.Groups)
        {
            var seen = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (QualityLevel level in group.Levels)
            {
                string key = string.Join(
                    ";",
                    level.Values.OrderBy(v => v.Variable, StringComparer.Ordinal)
                        .Select(v => $"{v.Variable}={v.Value}"));

                Assert.False(
                    seen.TryGetValue(key, out string? first),
                    $"{group.Caption} offers \"{level.Name}\" and \"{first}\" as the same values, so the "
                        + "second can never be read back and displays as the first.");

                seen[key] = level.Name;
            }
        }
    }

    /// <summary>Every step of a group writes the same variables, so no step can shadow another.</summary>
    /// <remarks>
    /// <c>Detect</c> only reads the variables a STEP names. If one step named a subset of another's and
    /// came first, it would match whenever its own few agreed — reporting a step the rest of the group
    /// flatly contradicts.
    /// </remarks>
    [Fact]
    public void Every_step_of_a_group_names_the_same_variables()
    {
        foreach (QualityGroup group in GraphicsMenu.Groups)
        {
            foreach (QualityLevel level in group.Levels)
            {
                Assert.Equal(
                    group.Variables.OrderBy(name => name, StringComparer.Ordinal),
                    level.Values.Select(value => value.Variable).OrderBy(name => name, StringComparer.Ordinal));
            }
        }
    }

    /// <summary>Every row the settings screen shows as a single variable, wherever it is shown.</summary>
    private static IEnumerable<GraphicsSetting> AllSettings() =>
        GraphicsMenu.Settings
            .Concat(GraphicsMenu.InterfaceSettings)
            .Concat(GraphicsMenu.DisplaySettings);

    [Fact]
    public void Every_shipped_group_can_read_back_every_step_it_writes()
    {
        CVarRegistry registry = CVarRegistry.Default;
        _ = OpenIGI.Rendering.FrameCVars.VSync;

        // The one way a hand-written table goes wrong quietly: a value that is in range and applies fine,
        // but whose text does not round-trip through the variable -- "1.50" against "1.5" -- so the row
        // reports Custom the instant after it was set. Applying each step and asking the group to name it
        // is the only check that catches it.
        foreach (QualityGroup group in GraphicsMenu.Groups)
        {
            for (int index = 0; index < group.Levels.Count; index++)
            {
                foreach ((string variable, string value) in group.Levels[index].Values)
                {
                    Assert.True(registry.TryGet(variable, out ICVar? cvar) && cvar is not null);
                    Assert.True(
                        cvar!.TrySetText(value, out string? error),
                        $"{group.Caption} / {group.Levels[index].Name} sets {variable}={value}: {error}");
                }

                Assert.Equal(index, QualityBinding.Detect(registry, group));
            }
        }
    }
}

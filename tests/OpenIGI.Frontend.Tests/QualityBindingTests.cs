using OpenIGI.Core.Configuration;
using OpenIGI.Frontend.Settings;
using OpenIGI.UI.Text;
using OpenIGI.UI.Widgets;
using Xunit;

namespace OpenIGI.Frontend.Tests;

/// <summary>
/// Covers the basic graphics page's quality groups: one control over several variables, both ways, and
/// the Custom reading that says the advanced page has moved one of them.
/// </summary>
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
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", "fonts", "Saira-Regular.ttf");
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
        _ = OpenIGI.Rendering.RenderCVars.VSync; // force the container's static initialiser

        foreach (QualityGroup group in BasicGraphics.Groups)
        {
            foreach (string variable in group.Variables)
            {
                Assert.True(
                    registry.TryGet(variable, out ICVar? cvar) && cvar is not null,
                    $"{group.Caption} names {variable}, which is not registered.");
            }
        }

        foreach (string variable in BasicGraphics.DirectVariables)
        {
            Assert.True(
                registry.TryGet(variable, out ICVar? direct) && direct is not null,
                $"The basic page shows {variable}, which is not registered.");
        }
    }

    [Fact]
    public void The_basic_page_is_a_fraction_of_the_variables_it_drives()
    {
        CVarRegistry registry = CVarRegistry.Default;
        _ = OpenIGI.Rendering.RenderCVars.VSync;

        int graphicsVariables = 0;
        foreach (ICVar cvar in registry.All)
        {
            if (cvar.Name.StartsWith("r_", StringComparison.Ordinal) && !cvar.Flags.HasFlag(CVarOptions.Hidden))
            {
                graphicsVariables++;
            }
        }

        // The preset, a row per group, and the few settings that are one number.
        int basicRows = 1 + BasicGraphics.Groups.Count + BasicGraphics.DirectVariables.Count;

        Assert.True(
            basicRows * 2 < graphicsVariables,
            $"The basic page has {basicRows} rows against {graphicsVariables} graphics variables, which is "
                + "not enough of a reduction to be worth splitting the page for.");
    }

    [Fact]
    public void No_group_drives_a_variable_the_basic_page_also_shows_directly()
    {
        // Two controls over one number, on the same page, disagreeing about what it is.
        foreach (QualityGroup group in BasicGraphics.Groups)
        {
            foreach (string variable in group.Variables)
            {
                Assert.DoesNotContain(variable, BasicGraphics.DirectVariables);
            }
        }
    }

    [Fact]
    public void Every_shipped_group_can_read_back_every_step_it_writes()
    {
        CVarRegistry registry = CVarRegistry.Default;
        _ = OpenIGI.Rendering.RenderCVars.VSync;

        // The one way a hand-written table goes wrong quietly: a value that is in range and applies fine,
        // but whose text does not round-trip through the variable -- "1.50" against "1.5" -- so the row
        // reports Custom the instant after it was set. Applying each step and asking the group to name it
        // is the only check that catches it.
        foreach (QualityGroup group in BasicGraphics.Groups)
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

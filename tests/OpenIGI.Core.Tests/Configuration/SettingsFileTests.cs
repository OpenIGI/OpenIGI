using OpenIGI.Core.Configuration;
using Xunit;

namespace OpenIGI.Core.Tests.Configuration;

/// <summary>
/// Pins that a settings file is a console script and that saving then loading is lossless.
/// </summary>
public sealed class SettingsFileTests
{
    [Fact]
    public void LastAssignment_FindsTheLastValueGivenAVariable()
    {
        string[] lines =
        [
            "// Variables",
            "r_renderer vulkan",
            "r_vsync 0; r_renderer \"webgpu\"",
            "bind f10 r_switch_renderer",
        ];

        Assert.Equal("webgpu", SettingsFile.LastAssignment(lines, "r_renderer"));
        Assert.Null(SettingsFile.LastAssignment(lines, "r_window_mode"));
    }

    [Fact]
    public void ReadAssignment_IsNullForAFileThatIsNotThere() =>
        Assert.Null(SettingsFile.ReadAssignment(Path.Combine(Path.GetTempPath(), "no-such-openigi-settings.cfg"), "r_renderer"));

    [Theory]
    [InlineData("1920x1080", 1920, 1080)]
    [InlineData(" 2560X1440 ", 2560, 1440)]
    public void ResolutionText_ReadsWhatItWrites(string text, int width, int height)
    {
        Assert.True(ResolutionText.TryParse(text, out int parsedWidth, out int parsedHeight));
        Assert.Equal((width, height), (parsedWidth, parsedHeight));
        Assert.Equal($"{width}x{height}", ResolutionText.Format(width, height));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("desktop")]
    [InlineData("1920x")]
    [InlineData("0x1080")]
    [InlineData("-1920x1080")]
    public void ResolutionText_RefusesAnythingThatIsNotASize(string? text) =>
        Assert.False(ResolutionText.TryParse(text, out _, out _));

    [Fact]
    public void Render_WritesOnlyArchivedVariablesThatDifferFromTheirDefault()
    {
        CVarRegistry variables = new();
        ConsoleSystem console = new(variables);

        NumericCVar<int> archived = CVar.Int("r_test_archived", 1, 0, 8, CVarOptions.Archive, registry: variables);
        NumericCVar<int> transient = CVar.Int("r_test_transient", 1, 0, 8, registry: variables);
        CVar.Int("r_test_untouched", 1, 0, 8, CVarOptions.Archive, registry: variables);

        archived.Value = 6;
        transient.Value = 6;

        string text = SettingsFile.Render(console);

        Assert.Contains("r_test_archived 6", text, StringComparison.Ordinal);
        Assert.DoesNotContain("r_test_transient", text, StringComparison.Ordinal);
        Assert.DoesNotContain("r_test_untouched", text, StringComparison.Ordinal);
    }

    [Fact]
    public void SavingThenLoadingRestoresEverySetting()
    {
        CVarRegistry saved = new();
        ConsoleSystem saving = new(saved);
        BuiltInConsoleCommands.Register(saving);

        NumericCVar<float> scale = CVar.Float("r_scale", 1f, 0.5f, 2f, CVarOptions.Archive, registry: saved);
        StringCVar profile = CVar.Text("ui_profile", "default", CVarOptions.Archive, registry: saved);
        EnumCVar<ConsoleSeverity> verbosity = CVar.Choice("con_verbosity", ConsoleSeverity.Output, CVarOptions.Archive, registry: saved);

        scale.Value = 0.75f;
        profile.Value = "Jones, David";
        verbosity.Value = ConsoleSeverity.Warning;
        saving.Execute("alias fogoff \"r_fog 0; echo off\"");
        saving.Execute("bind f5 \"r_scale 1\"");

        string text = SettingsFile.Render(saving);

        // A fresh registry with the same declarations, as a restart would produce.
        CVarRegistry loaded = new();
        ConsoleSystem loading = new(loaded);
        BuiltInConsoleCommands.Register(loading);

        NumericCVar<float> scale2 = CVar.Float("r_scale", 1f, 0.5f, 2f, CVarOptions.Archive, registry: loaded);
        StringCVar profile2 = CVar.Text("ui_profile", "default", CVarOptions.Archive, registry: loaded);
        EnumCVar<ConsoleSeverity> verbosity2 = CVar.Choice("con_verbosity", ConsoleSeverity.Output, CVarOptions.Archive, registry: loaded);

        foreach (string line in text.Split('\n'))
        {
            loading.Execute(line);
        }

        Assert.Equal(0.75f, scale2.Value);
        Assert.Equal("Jones, David", profile2.Value);
        Assert.Equal(ConsoleSeverity.Warning, verbosity2.Value);
        Assert.Equal("r_fog 0; echo off", loading.Aliases["fogoff"]);
        Assert.Equal("r_scale 1", loading.Binds["f5"]);
    }

    [Fact]
    public void SaveAndLoadRoundTripThroughARealFile()
    {
        string directory = Path.Combine(Path.GetTempPath(), "openigi-settings-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, SettingsPaths.SettingsFileName);

        try
        {
            CVarRegistry variables = new();
            ConsoleSystem console = new(variables);
            NumericCVar<int> quality = CVar.Int("r_fog_quality", 3, 0, 4, CVarOptions.Archive, registry: variables);
            quality.Value = 1;

            Assert.True(SettingsFile.Save(console, path));
            Assert.True(File.Exists(path));

            // Saving twice must also work: the second write replaces the first rather than failing
            // because the target already exists.
            quality.Value = 4;
            Assert.True(SettingsFile.Save(console, path));
            Assert.False(File.Exists(path + ".tmp"));

            quality.Value = 3;
            Assert.True(SettingsFile.Load(console, path));
            Assert.Equal(4, quality.Value);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void ASettingSinceRemovedIsSkippedWithoutAnError()
    {
        string directory = Path.Combine(Path.GetTempPath(), "openigi-settings-" + Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, SettingsPaths.SettingsFileName);

        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllLines(path, ["r_dof_range 18", "r_fog_quality 1"]);

            CVarRegistry variables = new();
            ConsoleSystem console = new(variables);
            NumericCVar<int> quality = CVar.Int("r_fog_quality", 3, 0, 4, CVarOptions.Archive, registry: variables);

            List<ConsoleLine> errors = [];
            console.LineWritten += (_, line) =>
            {
                if (line.Severity == ConsoleSeverity.Error)
                {
                    errors.Add(line);
                }
            };

            Assert.True(SettingsFile.Load(console, path));

            // The line after it still applies, and the retired name said nothing.
            Assert.Equal(1, quality.Value);
            Assert.Empty(errors);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void LoadingAMissingFileIsNotAnError()
    {
        ConsoleSystem console = new(new CVarRegistry());

        Assert.False(SettingsFile.Load(console, Path.Combine(Path.GetTempPath(), "no-such-openigi-settings.cfg")));
    }
}

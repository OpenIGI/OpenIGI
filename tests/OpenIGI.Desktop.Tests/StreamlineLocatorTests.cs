using OpenIGI.Host.Configuration;
using OpenIGI.Rendering.Vulkan;
using Xunit;

namespace OpenIGI.Desktop.Tests;

/// <summary>
/// Pins where the game looks for the optional NVIDIA Streamline runtime, and in which order.
/// </summary>
/// <remarks>
/// The order is the contract with the launcher: its runtime pack is named through an environment
/// variable, an explicit <c>-streamline</c> beats it, <c>-nostreamline</c> beats everything, and a
/// development checkout is found by walking up from the build output with nothing configured.
/// </remarks>
public sealed class StreamlineLocatorTests
{
    private const string Log = @"C:\log";

    [Fact]
    public void NoStreamline_beats_every_source()
    {
        HostOptions options = HostOptions.Parse(["-nostreamline", "-streamline", @"C:\cli"]);

        StreamlineOptions? found = Locate(options, env: @"C:\pack", existing: [@"C:\cli", @"C:\pack"]);

        Assert.Null(found);
    }

    [Fact]
    public void Command_line_beats_the_launcher_pack()
    {
        HostOptions options = HostOptions.Parse(["-streamline", @"C:\cli"]);

        StreamlineOptions? found = Locate(options, env: @"C:\pack", existing: [@"C:\cli", @"C:\pack"]);

        Assert.Equal(Path.GetFullPath(@"C:\cli"), found?.Directory);
    }

    [Fact]
    public void Launcher_pack_beats_a_development_checkout()
    {
        string checkout = Path.Combine(@"C:\repo", StreamlineLocator.DevelopmentRelativeDirectory);

        StreamlineOptions? found = Locate(HostOptions.Parse([]), env: @"C:\pack", existing: [@"C:\pack", checkout]);

        Assert.Equal(Path.GetFullPath(@"C:\pack"), found?.Directory);
    }

    [Fact]
    public void Development_checkout_is_found_above_the_build_output()
    {
        // Built from this platform's root and separators: the walk goes up through real parent folders.
        string repo = Path.Combine(Path.GetPathRoot(Path.GetTempPath())!, "repo");
        string checkout = Path.Combine(repo, StreamlineLocator.DevelopmentRelativeDirectory);

        StreamlineOptions? found = Locate(
            HostOptions.Parse([]),
            env: null,
            existing: [checkout],
            baseDirectory: Path.Combine(repo, "apps", "OpenIGI.Desktop", "bin", "Release", "net10.0"));

        Assert.Equal(Path.GetFullPath(checkout), found?.Directory);
    }

    [Fact]
    public void A_named_folder_that_does_not_exist_is_nothing()
    {
        StreamlineOptions? found = Locate(HostOptions.Parse([]), env: @"C:\gone", existing: []);

        Assert.Null(found);
    }

    [Fact]
    public void Not_on_windows_is_nothing()
    {
        StreamlineOptions? found = Locate(HostOptions.Parse([]), env: @"C:\pack", existing: [@"C:\pack"], windows: false);

        Assert.Null(found);
    }

    [Fact]
    public void Development_flag_selects_the_development_subfolder()
    {
        HostOptions options = HostOptions.Parse(["-streamline", @"C:\cli", "-streamline-dev"]);

        StreamlineOptions? found = Locate(options, env: null, existing: [@"C:\cli"]);

        Assert.NotNull(found);
        Assert.True(found.Development);
        Assert.Equal(Path.Combine(Path.GetFullPath(@"C:\cli"), "development"), found.EffectiveDirectory);
        Assert.Equal(Log, found.LogDirectory);
    }

    private static StreamlineOptions? Locate(
        HostOptions options,
        string? env,
        string[] existing,
        string baseDirectory = @"C:\elsewhere\bin",
        bool windows = true) =>
        StreamlineLocator.Locate(
            options,
            name => name == StreamlineLocator.EnvironmentVariable ? env : null,
            baseDirectory,
            path => existing.Any(e => string.Equals(Path.GetFullPath(e), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)),
            windows,
            Log);
}

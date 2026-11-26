using OpenIGI.Audio;
using OpenIGI.Composition;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Threading;
using OpenIGI.Core.Titles;
using OpenIGI.Engine.Configuration;
using OpenIGI.Engine.Titles;
using OpenIGI.Engine.Vfs;
using OpenIGI.Host;
using OpenIGI.Host.Configuration;
using OpenIGI.Host.Infrastructure;
using OpenIGI.Platform.Headless;
using OpenIGI.Rendering;
using OpenIGI.TestSupport;

namespace OpenIGI.Parity.Tests;

/// <summary>A mission brought up headless in lockstep from the installed game, as the parity suites run one.</summary>
internal static class ParityHost
{
    /// <summary>Makes a host that plays a mission headless, its picture drawn into a device the caller reads, or not at all.</summary>
    /// <param name="data">Where the title's files are.</param>
    /// <param name="title">The title.</param>
    /// <param name="mission">The mission.</param>
    /// <param name="device">The device it draws into.</param>
    /// <param name="frames">How many frames it runs for at most.</param>
    /// <param name="simulationOnly">Whether it only simulates, as the dedicated server does: nothing drawn, no level withheld.</param>
    /// <param name="arguments">More of the command line.</param>
    /// <returns>The host, not started.</returns>
    public static GameHost Create(
        GameDataSource data,
        GameTitle title,
        int mission,
        IGraphicsDevice device,
        int frames,
        bool simulationOnly = false,
        params string[] arguments)
    {
        // The console variables are process-wide; a test run before this one may have left any of them changed. Lockstep reads
        // no saved settings, so the defaults are the whole configuration.
        Core.Configuration.CVarRegistry.Default.ResetAll();

        string root = data.RequireRoot();
        IGameTitleRuntime runtime = TitleCatalog.For(title);
        VirtualFileSystem fileSystem = new(NullLogger.Instance);
        fileSystem.RegisterDevice(new LocalFileDevice(root));
        FileSystemConfig.LoadOrDefault(Path.Combine(root, FileSystemConfig.DefaultFileName), runtime.Layout).ApplyTo(fileSystem);

        HostOptions options = HostOptions.Parse(
        [
            "-mission", mission.ToString(System.Globalization.CultureInfo.InvariantCulture), "-skipcutscene", "-lockstep",
            "-frames", frames.ToString(System.Globalization.CultureInfo.InvariantCulture), .. arguments,
        ]);
        if (simulationOnly)
        {
            options = options with { PlayCutscenes = false, SimulationOnly = true };
        }

        return new GameHost(
            new HeadlessPlatformHost(),
            new HeadlessGameWindow(),
            device,
            new SilentAudioFactory(),
            InlineWorkScheduler.Instance,
            AlwaysReadyLoadGate.Instance,
            TitleCatalog.Catalog,
            runtime.Title,
            fileSystem,
            options,
            root,
            NullLogger.Instance);
    }

    /// <summary>The repository's root, above the test binary.</summary>
    /// <returns>The folder holding <c>OpenIGI.slnx</c>.</returns>
    public static string RepositoryRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OpenIGI.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("OpenIGI.slnx was not found above the test binary.");
    }

    private sealed class SilentAudioFactory : IAudioDeviceFactory
    {
        public IAudioDevice Open(AudioDeviceOptions options, IOpenIgiLogger logger) => NullAudioDevice.Instance;
    }
}

using OpenIGI.Audio;
using OpenIGI.Composition;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Threading;
using OpenIGI.Engine.Configuration;
using OpenIGI.Engine.Titles;
using OpenIGI.Engine.Vfs;
using OpenIGI.Host;
using OpenIGI.Host.Configuration;
using OpenIGI.Host.Infrastructure;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Desktop.Tests;

/// <summary>
/// The host driven one frame at a time -- Start, Step until it finishes, Stop -- which is how a host that
/// cannot block, a browser's frame callback, has to drive it.
/// </summary>
/// <remarks>
/// Against the recording devices and a headless window, with a benchmark path so the run saves nothing of
/// the person's own settings on the way out.
/// </remarks>
public sealed class GameHostStepTests : IDisposable
{
    private readonly string _scratch = Path.Combine(Path.GetTempPath(), $"openigi-step-{Guid.NewGuid():N}");

    public GameHostStepTests() => Directory.CreateDirectory(_scratch);

    [RequiresGameDataFact]
    public void A_mission_run_one_step_at_a_time_reaches_its_frames_and_ends_itself()
    {
        using RecordingGraphicsDevice device = new();
        HeadlessGameWindow window = new();
        GameHost host = Host(device, window, "-mission", "1", "-skipcutscene", "-lockstep", "-frames", "6");

        host.Start();

        // Start builds everything and stops at the top of the first frame: nothing has been presented.
        Assert.Equal(0, device.SwapCount);

        int steps = 0;
        while (host.Step())
        {
            steps++;
            Assert.True(steps < 10_000, "The run did not end.");
        }

        host.Stop();

        // One frame a step, and the mission drew its world into them.
        Assert.True(device.SwapCount >= 6, $"{device.SwapCount} frame(s) presented.");
        Assert.True(steps >= 6, $"{steps} step(s).");
        Assert.NotEmpty(device.MeshDraws);

        // Finished is finished: another step does nothing.
        Assert.False(host.Step());
    }

    [RequiresGameDataFact]
    public void Stopping_part_way_ends_the_run_through_its_own_shutdown()
    {
        using RecordingGraphicsDevice device = new();
        HeadlessGameWindow window = new();
        GameHost host = Host(device, window, "-lockstep", "-frames", "100000");

        host.Start();

        for (int i = 0; i < 3; i++)
        {
            Assert.True(host.Step());
        }

        int presented = device.SwapCount;
        host.Stop();

        // No further frame is drawn once asked to stop, and the host no longer steps.
        Assert.Equal(presented, device.SwapCount);
        Assert.False(host.Step());
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        try
        {
            Directory.Delete(_scratch, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private GameHost Host(RecordingGraphicsDevice device, HeadlessGameWindow window, params string[] args)
    {
        string root = GameData.RequireRoot();
        IGameTitleRuntime runtime = TitleCatalog.Default;
        IIgiLogger logger = NullLogger.Instance;

        VirtualFileSystem fileSystem = new(logger);
        fileSystem.RegisterDevice(new LocalFileDevice(root));
        FileSystemConfig
            .LoadOrDefault(Path.Combine(root, FileSystemConfig.DefaultFileName), runtime.Layout)
            .ApplyTo(fileSystem);

        HostOptions options = HostOptions.Parse([.. args, "-benchmark", Path.Combine(_scratch, "benchmark.json")]);

        return new GameHost(
            new HeadlessPlatformHost(),
            window,
            device,
            new RecordingAudioFactory(),
            InlineWorkScheduler.Instance,
            AlwaysReadyLoadGate.Instance,
            TitleCatalog.Catalog,
            runtime.Title,
            fileSystem,
            options,
            root,
            logger);
    }

    private sealed class RecordingAudioFactory : IAudioDeviceFactory
    {
        public IAudioDevice Open(AudioDeviceOptions options, IIgiLogger logger) => new RecordingAudioDevice();
    }
}

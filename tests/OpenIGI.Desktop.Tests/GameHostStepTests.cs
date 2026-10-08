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

    /// <summary>A step is a frame with a picture in it: the ticks between pictures do not each take one.</summary>
    /// <remarks>
    /// A browser steps once per display refresh. A step that returned after a tick handed it back a
    /// refresh with nothing drawn, which halved the rate at best and, once the clock fell behind, left it
    /// catching up one tick a refresh.
    /// </remarks>
    [RequiresGameDataFact]
    public void Each_step_of_a_running_mission_presents_a_frame()
    {
        using RecordingGraphicsDevice device = new();
        GameHost host = Host(device, new HeadlessGameWindow(), "-mission", "1", "-skipcutscene", "-lockstep", "-frames", "100000");

        host.Start();
        for (int i = 0; i < 30; i++)
        {
            Assert.True(host.Step());
        }

        int before = device.SwapCount;
        const int Steps = 60;
        for (int i = 0; i < Steps; i++)
        {
            Assert.True(host.Step());
        }

        host.Stop();
        Assert.True(device.SwapCount - before >= Steps - 1, $"{device.SwapCount - before} frame(s) in {Steps} step(s).");
    }

    /// <summary>A played mission's armoured cars carry the wreck their CAR record names.</summary>
    /// <remarks>
    /// The host's scene walk was handed no vehicle table, so no car was ever bound to its wreck and the renderer
    /// dropped a destroyed APC or T80 outright, where retail's countdown (<c>0x42E5A0</c>) leaves 601_08_1 or
    /// 614_08_1 in its place. Mission 6's two APCs, cars 800 and 803.
    /// </remarks>
    [RequiresGameDataFact]
    public void A_played_missions_armoured_cars_are_bound_to_their_wrecks()
    {
        using RecordingGraphicsDevice device = new();
        GameHost host = Host(device, new HeadlessGameWindow(), "-mission", "6", "-skipcutscene", "-lockstep", "-frames", "100000");

        host.Start();
        Assert.True(host.Step());
        var scene = (OpenIGI.Game.World.WorldScene)typeof(GameHost)
            .GetField("_scene", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(host)!;
        host.Stop();

        OpenIGI.Game.World.WorldPlacement[] apcs = [.. scene.Placements.Where(placement =>
            placement.Owner is OpenIGI.Engine.Tasks.DeclaredTask { Type.Name: "Car" }
            && string.Equals(placement.Instance.Name, "601_01_1", StringComparison.OrdinalIgnoreCase))];
        Assert.Equal(2, apcs.Length);
        Assert.All(apcs, apc => Assert.Equal("601_08_1", apc.DestroyedReplacement?.Instance.Name));
    }

    /// <summary>The menu's backdrop is a mission too, and is not built until the gate has its files.</summary>
    [RequiresGameDataFact]
    public void The_menu_backdrop_waits_until_the_gate_has_its_mission()
    {
        const int Refusals = 5;

        using RecordingGraphicsDevice device = new();
        RefusingLoadGate gate = new(Refusals);
        GameHost host = Host(device, new HeadlessGameWindow(), gate, "-lockstep", "-frames", "100000");

        host.Start();
        while (gate.Asked.Count < Refusals)
        {
            Assert.True(host.Step());
            Assert.True(device.SwapCount < 1_000, "The menu never asked the gate for its backdrop.");
        }

        // Refused every time so far: the menu asked for its showcase, and no level has been drawn.
        Assert.All(gate.Asked, id => Assert.Equal(1, id));
        Assert.Empty(device.MeshDraws);

        // Once the gate says yes, the backdrop comes up.
        for (int i = 0; i < 1_000 && device.MeshDraws.Count == 0; i++)
        {
            Assert.True(host.Step());
        }

        host.Stop();
        Assert.NotEmpty(device.MeshDraws);
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

    private GameHost Host(RecordingGraphicsDevice device, HeadlessGameWindow window, params string[] args) =>
        Host(device, window, AlwaysReadyLoadGate.Instance, args);

    private GameHost Host(RecordingGraphicsDevice device, HeadlessGameWindow window, ILoadGate gate, params string[] args)
    {
        string root = GameData.RequireRoot();
        IGameTitleRuntime runtime = TitleCatalog.Default;
        IOpenIgiLogger logger = NullLogger.Instance;

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
            gate,
            TitleCatalog.Catalog,
            runtime.Title,
            fileSystem,
            options,
            root,
            logger);
    }

    /// <summary>A gate that says no a number of times, then yes, and remembers what it was asked about.</summary>
    private sealed class RefusingLoadGate(int refusals) : ILoadGate
    {
        public List<int> Asked { get; } = [];

        public bool IsReady(Game.World.MissionDescriptor mission, out string? waitingFor)
        {
            Asked.Add(mission.Id);
            bool ready = Asked.Count > refusals;
            waitingFor = ready ? null : "the test";
            return ready;
        }
    }

    private sealed class RecordingAudioFactory : IAudioDeviceFactory
    {
        public IAudioDevice Open(AudioDeviceOptions options, IOpenIgiLogger logger) => new RecordingAudioDevice();
    }
}

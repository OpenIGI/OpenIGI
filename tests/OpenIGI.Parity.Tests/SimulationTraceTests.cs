using System.Runtime.InteropServices;
using OpenIGI.Audio;
using OpenIGI.Composition;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Core.Threading;
using OpenIGI.Core.Titles;
using OpenIGI.Engine.Configuration;
using OpenIGI.Engine.Titles;
using OpenIGI.Engine.Vfs;
using OpenIGI.Host;
using OpenIGI.Host.Configuration;
using OpenIGI.Host.Infrastructure;
using OpenIGI.Rendering;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Parity.Tests;

/// <summary>
/// The refactor's oracle: a mission run headless in lockstep for a fixed number of frames, each frame reduced to one hash of
/// everything it drew.
/// </summary>
/// <remarks>
/// <para>
/// What a frame draws is where everything in the simulation stands: the player, every soldier and door, each round and
/// grenade, and the interface. So a change to the code that moves the same simulation to new homes must leave every frame's
/// hash as it was, and one that changes what the game does cannot. The draws are hashed as an unordered set of their
/// transforms and vertex bits -- not their handles, which follow creation order -- so moving where a draw is issued from does
/// not fail it, and moving where something is does.
/// </para>
/// <para>
/// The baselines are <c>tests/OpenIGI.Parity.Tests/Baselines/&lt;title&gt;-m&lt;mission&gt;.txt</c>, one hash a frame. Set
/// <c>OPENIGI_PARITY_UPDATE=1</c> to record them, only from a commit whose behaviour is the one to keep.
/// </para>
/// </remarks>
[Collection(ParityGroup.Name)]
public sealed class SimulationTraceTests : IDisposable
{
    private const string UpdateVariable = "OPENIGI_PARITY_UPDATE";
    // Four frames a tick in lockstep: 1200 frames are ten seconds of play.
    private const int Frames = 1200;

    private readonly string _scratch = Path.Combine(Path.GetTempPath(), $"openigi-parity-{Guid.NewGuid():N}");

    public SimulationTraceTests() => Directory.CreateDirectory(_scratch);

    [RequiresGameDataTheory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(8)]
    [InlineData(14)]
    public void Igi1_mission_draws_as_it_did(int mission) =>
        Trace(GameDataSource.Igi1, GameTitle.Igi1, mission);

    // The player walks forward for three seconds, then fires a burst, then lets go.
    [RequiresTitleDataTheory("igi2")]
    [InlineData(21)]
    [InlineData(31)]
    public void Igi2_mission_draws_as_it_did(int mission) =>
        Trace(GameDataSource.Igi2, GameTitle.Igi2, mission, "-runat", "200", "title_hold MoveUp", "-runat", "560", "title_hold Fire", "-runat", "680", "title_hold none");

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

    private void Trace(GameDataSource data, GameTitle title, int mission, params string[] script)
    {
        // The console variables are process-wide; a test run before this one may have left any of them changed. Lockstep reads
        // no saved settings, so the defaults are the whole configuration.
        Core.Configuration.CVarRegistry.Default.ResetAll();

        string root = data.RequireRoot();
        IGameTitleRuntime runtime = TitleCatalog.For(title);
        using RecordingGraphicsDevice device = new();

        VirtualFileSystem fileSystem = new(NullLogger.Instance);
        fileSystem.RegisterDevice(new LocalFileDevice(root));
        FileSystemConfig.LoadOrDefault(Path.Combine(root, FileSystemConfig.DefaultFileName), runtime.Layout).ApplyTo(fileSystem);

        HostOptions options = HostOptions.Parse(
        [
            "-mission", mission.ToString(System.Globalization.CultureInfo.InvariantCulture), "-skipcutscene", "-lockstep",
            "-frames", Frames.ToString(System.Globalization.CultureInfo.InvariantCulture), .. script,
            "-benchmark", Path.Combine(_scratch, "benchmark.json"),
        ]);

        GameHost host = new(
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

        List<string> hashes = [];
        host.Start();
        while (host.Step() && hashes.Count < Frames)
        {
            hashes.Add(FrameHash(device));
        }

        host.Stop();

        string name = $"{title.Id}-m{mission}.txt";
        string path = Path.Combine(RepositoryRoot(), "tests", "OpenIGI.Parity.Tests", "Baselines", name);
        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllLines(path, hashes);
            return;
        }

        Assert.True(File.Exists(path), $"No parity baseline {name}; record one with {UpdateVariable}=1 from a known-good commit.");
        string[] expected = File.ReadAllLines(path);
        for (int i = 0; i < Math.Min(expected.Length, hashes.Count); i++)
        {
            Assert.True(expected[i] == hashes[i], $"{name}: frame {i} drew differently from its baseline (first difference): expected {expected[i]}, got {hashes[i]}.");
        }

        Assert.Equal(expected.Length, hashes.Count);
    }

    // One frame: every 3D draw's transform and blend, every water sheet's, and every 2D draw's vertices, as an unordered set.
    private static string FrameHash(RecordingGraphicsDevice device)
    {
        List<ulong> entries = [];
        foreach (RecordingGraphicsDevice.MeshDrawCall draw in device.MeshDraws)
        {
            Matrix4f modelView = draw.ModelView;
            ulong hash = Fnv(MemoryMarshal.AsBytes(new ReadOnlySpan<Matrix4f>(in modelView)));
            entries.Add(Mix(hash, (ulong)draw.Blend, (ulong)draw.IndexCount));
        }

        foreach ((_, int indexCount, Matrix4f modelView, _, WaterShading shading) in device.WaterDraws)
        {
            Matrix4f transform = modelView;
            ulong hash = Fnv(MemoryMarshal.AsBytes(new ReadOnlySpan<Matrix4f>(in transform)));
            entries.Add(Mix(hash, (ulong)indexCount, shading.IsAboveWater ? 1UL : 0UL));
        }

        foreach (RecordingGraphicsDevice.DrawCall draw in device.Draws)
        {
            entries.Add(Mix(Fnv(MemoryMarshal.AsBytes(draw.Vertices.AsSpan())), (ulong)draw.Blend, (ulong)draw.Indices.Length));
        }

        device.MeshDraws.Clear();
        device.WaterDraws.Clear();
        device.Draws.Clear();

        entries.Sort();
        ulong frame = Fnv(MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(entries)));
        return frame.ToString("x16", System.Globalization.CultureInfo.InvariantCulture) + " " + entries.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static ulong Mix(ulong hash, ulong a, ulong b) => ((hash ^ a) * 1099511628211UL) ^ (b * 0x9E3779B97F4A7C15UL);

    private static ulong Fnv(ReadOnlySpan<byte> bytes)
    {
        ulong hash = 14695981039346656037UL;
        foreach (byte b in bytes)
        {
            hash = (hash ^ b) * 1099511628211UL;
        }

        return hash;
    }

    private static string RepositoryRoot()
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

/// <summary>The parity traces run alone: the host's process-wide state (console variables, probes) must not change under them.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ParityGroup
{
    /// <summary>The collection's name.</summary>
    public const string Name = "Parity";
}

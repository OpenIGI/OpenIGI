using OpenIGI.Audio;
using OpenIGI.Composition;
using OpenIGI.Core.Content;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Threading;
using OpenIGI.Core.Titles;
using OpenIGI.Engine.Configuration;
using OpenIGI.Engine.Titles;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Hosting;
using OpenIGI.Game.Level;
using OpenIGI.Host;
using OpenIGI.Host.Configuration;
using OpenIGI.Host.Infrastructure;
using OpenIGI.Host.Titles;
using OpenIGI.Modding;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Parity.Tests;

/// <summary>A mod's system joins the host's I.G.I. mission systems by their names, and reads what the ones before it left.</summary>
[Collection(ParityGroup.Name)]
public sealed class MissionSystemModTests
{
    [RequiresGameDataFact]
    public void A_mods_system_runs_among_the_hosts_where_it_was_inserted()
    {
        Probe probe = new();
        TitlePackage shipped = TitleCatalog.Titles.Get(GameTitle.Igi1);
        ModSet mods = new([new InstalledMod(new ModManifest("probe", "Probe", "1.0", ModApi.Version, []), string.Empty, probe)]);
        TitleCatalog.Titles.Replace(shipped with { Runtime = mods.Extend(shipped.Runtime) });
        try
        {
            Core.Configuration.CVarRegistry.Default.ResetAll();
            string root = GameDataSource.Igi1.RequireRoot();
            IGameTitleRuntime runtime = TitleCatalog.For(GameTitle.Igi1);
            using RecordingGraphicsDevice device = new();
            VirtualFileSystem fileSystem = new(NullLogger.Instance);
            fileSystem.RegisterDevice(new LocalFileDevice(root));
            FileSystemConfig.LoadOrDefault(Path.Combine(root, FileSystemConfig.DefaultFileName), runtime.Layout).ApplyTo(fileSystem);
            string scratch = Path.Combine(Path.GetTempPath(), $"openigi-modprobe-{Guid.NewGuid():N}");
            Directory.CreateDirectory(scratch);
            HostOptions options = HostOptions.Parse(
            [
                "-mission", "1", "-skipcutscene", "-lockstep", "-frames", "3", "-benchmark", Path.Combine(scratch, "benchmark.json"),
            ]);
            GameHost host = new(
                new HeadlessPlatformHost(), new HeadlessGameWindow(), device, new SilentAudio(), InlineWorkScheduler.Instance,
                AlwaysReadyLoadGate.Instance, TitleCatalog.Catalog, runtime.Title, fileSystem, options, root, NullLogger.Instance);
            host.Start();
            int frames = 0;
            while (host.Step() && ++frames < 3)
            {
            }

            host.Stop();
        }
        finally
        {
            TitleCatalog.Titles.Replace(shipped);
        }

        Assert.Equal(1, probe.Runs);
        Assert.True(probe.SawScene, "The probe ran before the scene was walked.");
        Assert.False(probe.SawGraphs, "The probe ran after the nav graphs it was inserted ahead of.");
    }

    private sealed class Probe : IOpenIgiMod
    {
        public int Runs { get; private set; }

        public bool SawScene { get; private set; }

        public bool SawGraphs { get; private set; }

        public void Configure(IModBuilder builder) =>
            builder.EditLevelSystems<MissionLevelBuild>(systems => systems.InsertAfter(
                MissionSystemNames.Cameras,
                new LevelSystem<MissionLevelBuild>("Probe.AfterCameras", build =>
                {
                    Runs++;
                    SawScene = build.Scene is not null && build.Solids is not null;
                    SawGraphs = build.Graphs is not null;
                })));
    }

    private sealed class SilentAudio : IAudioDeviceFactory
    {
        public IAudioDevice Open(AudioDeviceOptions options, IOpenIgiLogger logger) => NullAudioDevice.Instance;
    }
}

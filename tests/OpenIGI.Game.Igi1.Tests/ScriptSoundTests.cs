using OpenIGI.Audio;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Audio;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Hosting;
using OpenIGI.Game.Igi1.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi1.Tests;

/// <summary>A soldier's script and animation sounds: an attached one moves with its owner (<c>0x51B4F0</c>).</summary>
public sealed class ScriptSoundTests
{
    [RequiresGameDataFact]
    public void AnAttachedSoundFollowsItsSoldierAndAnUnattachedOneStays()
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        fileSystem.SetAlias(VirtualFileSystem.MissionAlias, "LOCAL:missions/location0/level1/");
        SoundBank bank = new(fileSystem, null);
        bank.MountDirectory("LOCAL:common/sounds");
        GameLoop loop = new(new ManualTimeSource());
        SoundDefinitionRegistry definitions = new();
        definitions.Register(loop.Natives);
        definitions.LoadForMission(new ResourceRegistry(fileSystem), loop.Natives);
        RecordingAudioDevice device = new();
        SoundSystem sounds = new(device, bank, fileSystem, null) { Definitions = definitions };
        Igi1MissionAudio audio = new(new MissionWorld(), new Host(loop, sounds));

        Vector3d soldier = Vector3d.Zero;
        audio.PlayScriptSound("ai_death_1", soldier, () => soldier);
        audio.PlayScriptSound("ai_death_1", soldier, null);
        Assert.Equal(2, device.Played.Count);

        soldier = new Vector3d(4096 * 3, 0, 0);
        audio.UpdateScriptSoundPositions();

        Assert.Equal(3.0, Assert.NotNull(device.Current(device.Played[0].Voice)).Position.X, 6);
        Assert.Equal(0.0, Assert.NotNull(device.Current(device.Played[1].Voice)).Position.X, 6);
    }

    private sealed class Host(GameLoop game, SoundSystem sounds) : IMissionSoundHost
    {
        public GameLoop Game { get; } = game;

        public IOpenIgiLogger Logger => NullLogger.Instance;

        public bool IsRaining => false;

        public SoundSystem? SoundOutput { get; } = sounds;
    }
}

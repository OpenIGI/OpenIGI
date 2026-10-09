using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi2.Audio;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.Interaction;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Igi2.Scripting;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Player;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi2.Tests;

/// <summary>
/// Mission 1 loaded and running its level tick, with its switches, doors and sound generators given the level's models and
/// sounds, and, when asked, its doors bound to the collision world they move.
/// </summary>
internal sealed record RetailIgi2Mission1(Igi2Level Level, Igi2LevelRun Run, Igi2Sounds Sounds, IReadOnlyList<GameTask> Tree, Igi2CollisionWorld? Collision)
{
    public IReadOnlyList<Igi2SwitchTask> Switches => [.. Tree.OfType<Igi2SwitchTask>()];

    public IReadOnlyList<Igi2DoorTask> Doors => [.. Tree.OfType<Igi2DoorTask>()];

    /// <summary>Gets the session's materials, loaded with its tables.</summary>
    public Igi2GameMaterials Materials { get; init; } = new();

    /// <summary>Gets the session's tables, with the AI's tuning, the weapons and the player's tuning loaded.</summary>
    public Engine.Titles.IServiceCatalog? Services { get; init; }

    /// <summary>Gets the screen the level's status messages print on, which <see cref="Step"/> runs after the level.</summary>
    public Igi2StatusMessageScreen Screen { get; init; } = new();

    /// <summary>Gets the language the level's status messages are looked up in.</summary>
    public Igi2Strings? Strings { get; init; }

    /// <summary>A tick of the level and then of the status message screen, the order gameplay runs them in.</summary>
    public void Step()
    {
        Run.Tick();
        Screen.Run();
    }

    public static RetailIgi2Mission1 Load(bool collision = false, bool soundLengths = false)
    {
        Igi2Level level = Igi2Level.Find("location1", "level1");
        RetailIgi2LevelLoadTests.Harness harness = new(Path.Combine(level.Directory, "objects.qvm"), loadTables: true);
        harness.Loader.LoadScript(RetailIgi2LevelLoadTests.Harness.ScriptName);
        harness.Session.Services.Require<Igi2FlowState>().Reset();
        Igi2LevelRun run = new(harness.LevelRoot, harness.Loop.Natives, harness.Session.Services.Require<Igi2ScriptFunctions>());

        VirtualFileSystem files = new();
        files.RegisterDevice(new LocalFileDevice(GameDataSource.Igi2.RequireRoot()));
        files.SetAlias(VirtualFileSystem.MissionAlias, "LOCAL:missions/location1/level1/");
        Igi2SoundDefinitions definitions = new();
        definitions.Load(new ResourceRegistry(files), ["LOCAL:common/sounds", "LOCAL:missions/location1/common/sounds", "MISSION:sounds"]);
        Engine.Audio.SoundBank? bank = null;
        if (soundLengths)
        {
            bank = new Engine.Audio.SoundBank(files);
            foreach (string directory in (string[])["LOCAL:common/sounds", "LOCAL:missions/location1/common/sounds", "MISSION:sounds"])
            {
                bank.MountDirectory(directory);
                bank.MountDirectory(directory + "/" + Igi2SoundDefinitions.DefaultLanguage);
            }
        }

        Igi2Sounds sounds = new(definitions, new Audible(bank));

        List<GameTask> tree = [.. All(harness.LevelRoot)];
        foreach (Igi2SwitchTask button in tree.OfType<Igi2SwitchTask>())
        {
            button.Models = level.ModelFor;
            button.Sounds = sounds;
        }

        foreach (Igi2SoundGeneratorTask generator in tree.OfType<Igi2SoundGeneratorTask>())
        {
            generator.Sounds = sounds;
        }

        Igi2CollisionWorld? world = collision
            ? Igi2CollisionWorld.Build(level.Scene, level.ModelFor, level.Terrain(), Igi2Gameplay.TerrainOwner)
            : null;
        foreach (Igi2DoorTask door in tree.OfType<Igi2DoorTask>())
        {
            door.Models = level.ModelFor;
            door.Sounds = sounds;
            if (world is not null)
            {
                door.Bind(world.SolidsOf(Igi2DoorTask.TypeName, door.Id));
            }
        }

        ResourceRegistry resources = new(files);
        foreach (string archive in Igi2Strings.Archives)
        {
            resources.Register(archive, files.ReadAllBytes(archive));
        }

        Igi2Strings strings = new(resources);
        Igi2StatusMessageScreen screen = new();
        foreach (Igi2StatusMessageTask message in tree.OfType<Igi2StatusMessageTask>())
        {
            message.Strings = strings;
            message.Screen = screen;
            message.Sounds = sounds;
            message.InitText();
        }

        return new RetailIgi2Mission1(level, run, sounds, tree, world)
        {
            Materials = harness.Session.Services.Require<Igi2GameMaterials>(),
            Services = harness.Session.Services,
            Screen = screen,
            Strings = strings,
        };
    }

    /// <summary>Gets a tick's input with nothing pressed.</summary>
    public static TitlePlayerInput Idle => new(0f, 0f, default, default, 0.5f, false);

    /// <summary>Stands the level's player at a spot, turned to a heading, with one thing to activate, and lets it settle for two seconds.</summary>
    public static Igi2TitlePlayer Stand(Igi2CollisionWorld world, Igi2Level level, IIgi2Activatable? target, Vector3d at, float gamma, Igi2Sounds? sounds = null)
    {
        Assert.True(PlayerSpawn.TryFindGameplay(level.Root, out PlayerSpawnPoint point));
        Formats.World.MefModel body = level.ModelFor(point.Model)!;
        Igi2TitlePlayer player = Igi2TitlePlayer.Spawn(
            world, Igi2Session.Start().Require<Igi2PlayerTuning>().General, at, gamma, AiDifficulty.Easy, null, body.BoneHierarchy, sounds: sounds);
        player.Human.ModelRadius = body.NativeMarkerBound;
        player.Human.RootTranslation = body.BoneHierarchy!.Translations[0];
        if (target is not null)
        {
            player.Activatables.Add(target);
        }
        for (int tick = 0; tick < 60; tick++)
        {
            player.Tick(Idle);
        }

        return player;
    }

    public Igi2SwitchTask Switch(int id) => Switches.Single(s => s.Id == id);

    public Igi2DoorTask Door(int id) => Doors.Single(d => d.Id == id);

    public static IEnumerable<GameTask> All(GameTask task)
    {
        yield return task;
        foreach (GameTask child in task.Children)
        {
            foreach (GameTask inner in All(child))
            {
                yield return inner;
            }
        }
    }

    /// <summary>A mixer whose voices sound until stopped, so a made sound stays among the level's playing ones.</summary>
    /// <param name="bank">The bank sounds' lengths are read from, or <see langword="null"/> for none.</param>
    internal sealed class Audible(Engine.Audio.SoundBank? bank = null) : ITitleAudio
    {
        public double SampleSeconds(string bankKey) =>
            bank is not null && bank.TryGet(bankKey, out Engine.Audio.SoundEntry entry) ? entry.SampleSeconds : 0.0;

        public void UseDefinitions(Engine.Audio.ISoundDefinitionLookup definitions, IReadOnlyList<string> bankDirectories)
        {
        }

        public ITitleVoice? Play(string definition, Vector3d positionInUnits, bool? looping = null) => new Voice();

        private sealed class Voice : ITitleVoice
        {
            public bool IsPlaying { get; private set; } = true;

            public void MoveTo(Vector3d positionInUnits)
            {
            }

            public void Silence() => IsPlaying = false;
        }
    }
}

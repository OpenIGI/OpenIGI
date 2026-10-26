using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi1.Systems;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Presentation.Igi1.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Igi1.Tests;

/// <summary>Project I.G.I.'s soldiers in another game's level are drawn by the game's own soldier renderer, armed.</summary>
public sealed class Igi1HostedLevelPresenterTests
{
    [RequiresGameDataFact]
    public void A_hosted_soldier_is_drawn_once_its_level_has_ticked()
    {
        string model = FirstSoldierModel();
        Igi1GuestContent content = Igi1GuestContent.Load(GameData.RequireRoot(), [model]);
        Igi1GuestLevel level = Igi1GuestLevel.Open(content);
        level.AddSoldier(new Igi1GuestSoldier(model, "AITYPE_PATROL_AK", -1, Vector3d.Zero, 0f, 1) { Weapons = ["WEAPON_ID_AK47"] });
        Igi1HostedLevel hosted = level.Start(new Igi1GuestLevelStart(new Igi2CollisionWorld([], null), new Player(), new IgiRandom(), AiDifficulty.Normal));
        RecordingGraphicsDevice device = new();
        using Igi1HostedLevelPresenter presenter = new(device, content, hosted, NullLogger.Instance);

        hosted.Tick();
        device.MeshDraws.Clear();
        presenter.Draw(new Camera3D(new Vector3d(0.0, -10.0, 1.5), Camera3D.LookRotation(0f, 0f)), Matrix4f.Identity, 1f);

        Assert.NotEmpty(device.MeshDraws);
        Assert.Equal("WEAPON_ID_AK47", Assert.Single(hosted.Groups).Roster.Agents[0].WeaponType?.ScriptId);
        hosted.Leave();
    }

    // The body Project I.G.I.'s first mission dresses its first soldier in.
    private static string FirstSoldierModel()
    {
        VirtualFileSystem fileSystem = new();
        fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fileSystem);
        GameLoop loop = new(new ManualTimeSource());
        PlayerTaskTypes.RegisterAll(loop.Types);
        AiTaskTypes.RegisterAll(loop.Types);
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        MissionRegistry missions = new();
        missions.Register(loop.Natives);
        missions.Scan(fileSystem, resources, loop.Natives);
        _ = new MissionLoader(missions, fileSystem, resources, new LevelLoader(root, loop.TaskCommands, loop.Natives, resources)).Load(1);
        return root.Descendants().OfType<HumanSoldierTask>().First().Model;
    }

    // A player far off, whom the soldier does not see.
    private sealed class Player : IAiPlayer
    {
        public Vector3d Position => new(0, 4096.0 * 500, 0);

        public int Team => 0;

        public bool IsDead => false;

        public Vector3d OriginalViewPosition => Position + new Vector3d(0, 0, 4096.0 * 1.7);

        public bool IsKeepingLow => false;

        public void NoteDetected()
        {
        }
    }
}

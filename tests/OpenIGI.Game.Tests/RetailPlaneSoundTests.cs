using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>A plane sounds its jet set, with the parameters its own physics step leaves.</summary>
/// <remarks>
/// <c>0x433690</c> instantiates <c>su27</c> while the plane's "Play sound" holds, as <c>0x4316D0</c> does
/// <c>helicopter_1</c>. The set asks the plane for its parameters (<c>0x434A30</c>): a two-entry array, laid out by
/// <c>0x4E6AB0</c> as a count then the values, which the physics step (<c>0x433B50</c>) fills after each step --
/// parameter 0 the thrust (<c>+1824</c>), parameter 1 <c>thrust &gt; 0.01</c>. <c>plane_01</c> reads parameter 1.
/// </remarks>
public sealed class RetailPlaneSoundTests
{
    [RequiresGameDataFact]
    public void MissionThreeIntroPlaneSoundsItsJetSetWithItsThrust()
    {
        VirtualFileSystem fs = new();
        fs.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fs);
        GameLoop loop = new(new ManualTimeSource());
        loop.Types.Register<CutSceneTask>(CutSceneTask.TypeName);
        loop.Types.Register<ConditionalContainerTask>(ConditionalContainerTask.TypeName);
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        MissionRegistry missions = new();
        missions.Register(loop.Natives);
        missions.Scan(fs, resources, loop.Natives);
        MagicObjectRegistry magic = new();
        magic.Register(loop.Natives);
        magic.Load(resources, loop.Natives);
        VehiclePhysicsRegistry physics = new();
        physics.Load(resources);
        LoadedMission mission = new MissionLoader(
            missions,
            fs,
            resources,
            new LevelLoader(root, loop.TaskCommands, loop.Natives, resources),
            terrainLoader: new TerrainLoader(resources, loop.Natives)).Load(3);
        WorldScene world = WorldScene.Build(mission, root, magic);
        ObjectCollisionIndex solids = ObjectCollisionIndex.Build(world.Placements);
        new ConditionalContainerGate(loop.Variables).Prepare(root);
        TerrainData terrain = mission.Terrain ?? throw new InvalidOperationException("mission 3 has no terrain");

        // As GameHost builds it: the runtime, the wheels, then the retail vehicle world the plane's body needs.
        CutsceneRuntime runtime = new(root, loop.Variables, vehiclePhysics: physics, terrain: terrain)
        {
            CollisionIndex = solids,
        };
        runtime.BindVehicleWheels(world);
        Func<string, MefModel?> models = name => mission.TryGetModel(name, out MefModel? model) ? model : null;
        runtime.ConfigureRetailCars(
            new RetailCarWorld(
                new RetailTerrainRay(
                    terrain.Octree,
                    terrain.Commands,
                    TerrainDiscardBinding.For(terrain).Regions,
                    TerrainHeightBindings.Bind(terrain, root)),
                RetailWorldObjects.Build(root, models, name => magic.TryGet(name, out MagicObjectDefinition attached) ? attached : null),
                runtime.RetailVehicleModels,
                static owner => owner is not GameTask task || ConditionalContainerGate.IsTaskActive(task)),
            models,
            name => magic.TryGet(name, out MagicObjectDefinition attached) ? attached : null);

        // Plane 1988 flies past the security camera in the intro.
        GameTask plane = root.Tree.FromId(1988)!;
        VehicleSoundSource? heard = null;
        int listed = 0;
        float most = 0f;
        for (int tick = 0; tick < 4000 && heard is null; tick++)
        {
            runtime.Tick();
            runtime.FinishExpressionTick();
            foreach (VehicleSoundSource source in runtime.VehicleSoundSources())
            {
                if (ReferenceEquals(source.Vehicle, plane))
                {
                    listed++;
                    most = Math.Max(most, source.Throttle);
                    if (source.Throttle > 0.01f)
                    {
                        heard = source;
                    }
                }
            }
        }

        Assert.True(heard is not null, $"listed {listed} ticks, highest thrust {most}, active {ConditionalContainerGate.IsTaskActive(plane)}");
        VehicleSoundSource jet = heard.Value;
        Assert.Equal("su27", jet.SoundSet);
        Assert.True(jet.SoundOn);
        Assert.Equal(1f, jet.Running);
        Assert.Equal(0f, jet.Speed);
    }
}

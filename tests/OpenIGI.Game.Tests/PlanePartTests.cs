using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>A plane's folding parts: its legs, the hatch they wait on, its canopy and its control surfaces.</summary>
[Collection(RetailMissionWorld.Name)]
public sealed class PlanePartTests
{
    private static readonly float[] Identity = [1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f];

    [Fact]
    public void TheGearComesDownOnlyOnceTheHatchHasOpened()
    {
        RetailPlaneHatch hatch = new(Vector3d.Zero, Identity, 90f, 0);
        RetailPlaneGear leg = new(Vector3d.Zero, Identity, new MagicGear(90f, false, 0f, 0f, 0f), Vector3f.Zero);
        hatch.Seat(gearDown: false);
        leg.Seat(gearDown: false);
        float folded = leg.Angle;

        int ticks = 0;
        sbyte gearState = 0;
        sbyte hatchState;
        while ((hatchState = hatch.Update(gearDown: true, gearState)) != 1)
        {
            gearState = leg.Update(gearDown: true, hatchState);
            Assert.Equal(folded, leg.Angle);
            ticks++;
        }

        // A quarter turn kept at 0.97 a tick falls under a tenth on the 91st.
        Assert.Equal(90, ticks);
        leg.Update(gearDown: true, hatchState);
        Assert.True(leg.Angle < folded);
    }

    [Fact]
    public void TheHatchShutsOnlyOnceTheGearHasFoldedAway()
    {
        RetailPlaneHatch hatch = new(Vector3d.Zero, Identity, 90f, 0);
        RetailPlaneGear leg = new(Vector3d.Zero, Identity, new MagicGear(90f, false, 0f, 0f, 0f), Vector3f.Zero);
        hatch.Seat(gearDown: true);
        leg.Seat(gearDown: true);

        sbyte gearState = 1;
        while (gearState != 0)
        {
            Assert.Equal(1, hatch.Update(gearDown: false, gearState));
            Assert.Equal(0f, hatch.Angle);
            gearState = leg.Update(gearDown: false, 1);
        }

        hatch.Update(gearDown: false, gearState);
        Assert.True(hatch.Angle > 0f);
    }

    [Theory]
    [InlineData(0.05f, 1)]
    [InlineData(-0.05f, 1)]
    [InlineData(0.8f, -1)]
    [InlineData(1.5f, 0)]
    public void AFoldingPartReportsRestFullOrBetween(float angle, int state)
    {
        X87 full = (X87)1.5707964f;

        Assert.Equal((sbyte)state, RetailPlaneGear.PartState(full, angle));
    }

    [Theory]
    [InlineData(0, -0.17453292f)]
    [InlineData(1, -0.087266462f)]
    [InlineData(2, 0.05f)]
    [InlineData(3, 0.087266462f)]
    public void AControlSurfaceThrowsWithTheControlsItAnswers(int kind, float expected)
    {
        RetailPlaneRudder surface = new(Vector3d.Zero, Identity, new MagicRudder(kind, 20f));

        // Controls 0.5, 0.25 and -0.25 at a 20-degree throw; only the right taileron reaches the 0.05 cap.
        surface.Aim([0.5f, 0.25f, -0.25f], 0.05f);

        Assert.Equal(expected, surface.Angle, 5);
    }

    [RequiresGameDataFact]
    public void TheSu27sHatchIsReadFromTheMagicObjectScript()
    {
        Assert.True(RetailMissionData.Get(7).Magic.TryGet("702_11_1", out MagicObjectDefinition hatch));
        Assert.NotNull(hatch.HatchDegrees);
        Assert.True(hatch.HatchDegrees > 0f);
    }

    [RequiresGameDataFact]
    public void AParkedPlanesLegsAndHatchArePosedOnTheirPlacementsWithTheGearDown()
    {
        VirtualFileSystem fs = new();
        fs.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fs);
        GameLoop loop = new(new ManualTimeSource());
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
            new LevelLoader(root, loop.TaskCommands, loop.Natives, resources)).Load(3);
        WorldScene scene = WorldScene.Build(mission, root, magic);

        // Mission 3's Plane_1988 is parked with its gear down, its canopy shut and its controls centred: every part at rest on
        // its attachment,
        // its mirrored left leg drawn as the mirror it is attached as.
        DeclaredTask task = root.Descendants().OfType<DeclaredTask>().Single(t => t.Name == "Plane_1988");
        string model = Assert.IsType<string>(task.TryGetValue(WorldScene.ModelParameter, out object? raw) ? raw : null);
        Assert.True(physics.TryGetPlane(model, out PlanePhysicsDefinition definition));
        Assert.True(mission.TryGetModel(model, out MefModel? mef));
        RetailPlaneBody plane = RetailPlaneSpawn.Create(
            definition,
            mef!,
            RetailPlaneSpawn.Placement(task),
            name => magic.TryGet(name, out MagicObjectDefinition found) ? found : null,
            name => mission.TryGetModel(name, out MefModel? part) ? part : null);
        Assert.NotNull(plane.Hatch);
        Assert.NotNull(plane.Shield);

        int leg = 0;
        int surface = 0;
        int checkedParts = 0;
        foreach (WorldPlacement placement in scene.Placements.Where(p => ReferenceEquals(p.Owner, task)))
        {
            RetailPlanePart? part = placement.MagicTaskTypeName switch
            {
                "TASKTYPE_GEAR" => plane.Gears[leg++],
                "TASKTYPE_HATCH" => plane.Hatch,
                "TASKTYPE_RUDDER" => plane.Rudders[surface++],
                "TASKTYPE_COCKPITSHIELD" => plane.Shield,
                _ => null,
            };
            if (part is null)
            {
                continue;
            }

            Vector3d at = placement.Position * WorldScene.UnitsPerMetre;
            Assert.Equal(at.X, part.World[0], 1.0);
            Assert.Equal(at.Y, part.World[1], 1.0);
            Assert.Equal(at.Z, part.World[2], 1.0);
            Matrix3f drawn = part.DrawOrientation;
            Assert.Equal(placement.Orientation.M11, drawn.M11, 3);
            Assert.Equal(placement.Orientation.M12, drawn.M12, 3);
            Assert.Equal(placement.Orientation.M13, drawn.M13, 3);
            Assert.Equal(placement.Orientation.M21, drawn.M21, 3);
            Assert.Equal(placement.Orientation.M22, drawn.M22, 3);
            Assert.Equal(placement.Orientation.M23, drawn.M23, 3);
            Assert.Equal(placement.Orientation.M31, drawn.M31, 3);
            Assert.Equal(placement.Orientation.M32, drawn.M32, 3);
            Assert.Equal(placement.Orientation.M33, drawn.M33, 3);
            checkedParts++;
        }

        Assert.Equal(6, plane.Rudders.Count);
        Assert.All(plane.Rudders, rudder => Assert.InRange(rudder.Record.Kind, 0, 3));
        Assert.Equal(plane.Gears.Count + plane.Rudders.Count + 2, checkedParts);
    }
}

using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// Builds mission 7's APC and T80 from the level, the physics type and the model, as the original creates a
/// car, and demands the state the running game held at the car's first physics tick.
/// </summary>
public sealed class RetailCarSpawnTests
{
    [Theory]
    [InlineData(352, "carstep-m7-apc352.log.gz")]
    [InlineData(357, "carstep-m7-t80-357.log.gz")]
    public void ACarIsCreatedExactlyAsRetailHoldsItAtItsFirstTick(int id, string fixture)
    {
        RetailMissionData data = RetailMissionData.Get(7);
        RetailCarStepCapture capture = RetailCarStepCapture.Load(fixture);
        byte[] first = capture.Ticks[0].Entry;

        DeclaredTask task = Assert.IsType<DeclaredTask>(data.Root.Tree.FromId(id));
        string modelName = Assert.IsType<string>(task.TryGetValue(WorldScene.ModelParameter, out object? value) ? value : null);
        Assert.True(data.Vehicles.TryGetCar(modelName, out CarPhysicsDefinition physics));
        Assert.True(data.Mission.TryGetModel(modelName, out MefModel? model));

        RetailCarBody car = RetailCarSpawn.Create(physics, model!, RetailCarSpawn.Placement(task, wheelAttachments: 16));

        // The fixed definition against the captured TYPE and record.
        RetailCarDefinition expected = RetailCarMemory.Definition(capture.TypeMemory, capture.RecordMemory);
        Assert.Equal(expected.Mass, car.Definition.Mass);
        Assert.Equal(expected.SteeringRetention, car.Definition.SteeringRetention);
        Assert.Equal(expected.EngineForce, car.Definition.EngineForce);
        Assert.Equal(expected.ThrottleRetention, car.Definition.ThrottleRetention);
        Assert.Equal(expected.SkidSteer, car.Definition.SkidSteer);
        Assert.Equal(expected.Wheels, car.Definition.Wheels);
        if (capture.TypeMemory.Length >= 724)
        {
            Assert.Equal(BitConverter.ToSingle(capture.TypeMemory, 716), car.Definition.SleepSpeed);
            Assert.Equal(BitConverter.ToSingle(capture.TypeMemory, 720), car.Definition.SleepSpin);
        }

        // The state, where the first tick has not already changed it.
        RetailCarBody retail = new(expected);
        RetailCarMemory.Load(retail, first);
        List<string> diff = [];
        void Same(string name, float port, float original)
        {
            if (BitConverter.SingleToInt32Bits(port) != BitConverter.SingleToInt32Bits(original))
            {
                diff.Add($"{name}: port {port:R} retail {original:R}");
            }
        }

        void SameD(string name, double port, double original)
        {
            if (BitConverter.DoubleToInt64Bits(port) != BitConverter.DoubleToInt64Bits(original))
            {
                diff.Add($"{name}: port {port:R} retail {original:R}");
            }
        }

        void SameI(string name, long port, long original)
        {
            if (port != original)
            {
                diff.Add($"{name}: port {port} retail {original}");
            }
        }

        Same("mass", car.Mass, retail.Mass);
        Same("inverseMass", car.InverseMass, retail.InverseMass);
        Same("radius", car.Radius, retail.Radius);
        for (int k = 0; k < 3; k++)
        {
            Same($"inverseInertia{k}", car.InverseInertia[k], retail.InverseInertia[k]);
            SameD($"position{k}", car.Position[k], retail.Position[k]);
            SameD($"previousPosition{k}", car.PreviousPosition[k], retail.PreviousPosition[k]);
            Same($"dimensions{k}", car.Dimensions[k], retail.Dimensions[k]);
        }

        for (int k = 0; k < 9; k++)
        {
            Same($"M{k}", car.M[k], retail.M[k]);
            Same($"previousM{k}", car.PreviousM[k], retail.PreviousM[k]);
        }

        SameI("collideEnabled", car.CollideEnabled, retail.CollideEnabled);
        SameI("runCollide", car.RunCollide ? 1 : 0, retail.RunCollide ? 1 : 0);
        SameI("planar", car.Planar ? 1 : 0, retail.Planar ? 1 : 0);
        SameI("drivenWheels", car.DrivenWheels, retail.DrivenWheels);
        for (int p = 0; p < RetailCarBody.PointCount; p++)
        {
            RetailCarPoint a = car.Points[p], b = retail.Points[p];
            SameI($"point{p}.side", a.Side, b.Side);
            Same($"point{p}.restitution", a.Restitution, b.Restitution);
            Same($"point{p}.x", a.Position[0], b.Position[0]);
            Same($"point{p}.y", a.Position[1], b.Position[1]);
            Same($"point{p}.z", a.Position[2], b.Position[2]);
            SameI($"point{p}.collides", a.Collides ? 1 : 0, b.Collides ? 1 : 0);
            SameI($"point{p}.isCollisionPoint", a.IsCollisionPoint ? 1 : 0, b.IsCollisionPoint ? 1 : 0);
            Same($"point{p}.frictionX", a.FrictionX, b.FrictionX);
            Same($"point{p}.frictionY", a.FrictionY, b.FrictionY);
        }

        for (int i = 0; i < RetailCarBody.MaxWheels; i++)
        {
            SameI($"wheel{i}.enabled", car.Wheels[i].Enabled ? 1 : 0, retail.Wheels[i].Enabled ? 1 : 0);
        }

        Assert.True(diff.Count == 0, string.Join("; ", diff));
    }
}

using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.World;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

public sealed class GenericPickupRegistryTests
{
    [Fact]
    public void PickupRangeUsesTheRetailHorizontalAndVerticalThresholds()
    {
        Vector3d pickup = new(1000.0, 2000.0, 3000.0);

        Assert.True(PickupReach.IsWithinRange(
            new Vector3d(1000.0 + PickupReach.Radius - 0.1, 2000.0, 3000.0), pickup));
        Assert.False(PickupReach.IsWithinRange(
            new Vector3d(1000.0 + PickupReach.Radius, 2000.0, 3000.0), pickup));
        Assert.True(PickupReach.IsWithinRange(
            new Vector3d(1000.0, 2000.0, 3000.0 + PickupReach.VerticalTolerance - 0.1), pickup));
        Assert.False(PickupReach.IsWithinRange(
            new Vector3d(1000.0, 2000.0, 3000.0 + PickupReach.VerticalTolerance), pickup));
    }

    [Fact]
    public void CollectionPublishesIsPickedUpAndKeepsTheTaskForMissionExpressions()
    {
        GameLoop loop = new();
        QvmProgram level = QvmProgram.Read(QvmExpression.BuildProgram(
            QvmExpression.Call(
                "Task_DeclareParameters",
                QvmExpression.Text("GenericPickup"),
                QvmExpression.Text("Position"), QvmExpression.Text("ObjectPos"),
                QvmExpression.Text("Orientation"), QvmExpression.Text("Real32x9"),
                QvmExpression.Text("Model"), QvmExpression.Text("String16")),
            QvmExpression.Call(
                "Task_New",
                QvmExpression.Int(870),
                QvmExpression.Text("GenericPickup"),
                QvmExpression.Text("C4"),
                QvmExpression.Real(1000f), QvmExpression.Real(2000f), QvmExpression.Real(3000f),
                QvmExpression.Real(0f), QvmExpression.Real(0f), QvmExpression.Real(0f),
                QvmExpression.Text("140_01_1"))));

        Assert.Equal(QvmError.None, new QvmInterpreter(level, loop.Natives).Run());

        GenericPickupRegistry registry = GenericPickupRegistry.Build(loop.Tasks.Root, loop.Variables);
        GenericPickup pickup = Assert.Single(registry.Items);
        Assert.True(loop.Variables.TryRead("GenericPickup_870.isPickedUp", out QvmValue before));
        Assert.Equal(0, before.Integer);

        Assert.Empty(registry.CollectAt(new Vector3d(1000.0 + PickupReach.Radius, 2000.0, 3000.0)));
        Assert.Single(registry.CollectAt(pickup.Position));
        Assert.True(pickup.IsCollected);
        Assert.True(loop.Variables.TryRead("GenericPickup_870.isPickedUp", out QvmValue after));
        Assert.Equal(1, after.Integer);
        Assert.False(pickup.Task.IsDeleted);
        Assert.Same(pickup.Task, loop.Tasks.FromId(870));
        Assert.Empty(registry.CollectAt(pickup.Position));
    }

    [Fact]
    public void AmmoScriptStoredInModelIsNormalizedToItsWorldMesh()
    {
        GameLoop loop = new();
        QvmProgram level = QvmProgram.Read(QvmExpression.BuildProgram(
            QvmExpression.Call(
                "Task_DeclareParameters",
                QvmExpression.Text("GenericPickup"),
                QvmExpression.Text("Position"), QvmExpression.Text("ObjectPos"),
                QvmExpression.Text("Model"), QvmExpression.Text("String16")),
            QvmExpression.Call(
                "Task_New",
                QvmExpression.Int(871),
                QvmExpression.Text("GenericPickup"),
                QvmExpression.Text("Ammo"),
                QvmExpression.Real(1000f), QvmExpression.Real(2000f), QvmExpression.Real(3000f),
                QvmExpression.Text("AMMO_ID_556"))));

        Assert.Equal(QvmError.None, new QvmInterpreter(level, loop.Natives).Run());

        GenericPickup pickup = Assert.Single(
            GenericPickupRegistry.Build(loop.Tasks.Root, loop.Variables).Items);
        Assert.Equal("115_01_1", pickup.ModelName);
        Assert.Equal("115_01_1", Assert.Single(WorldScene.PlacedModelNames(pickup.Task)));
    }
}

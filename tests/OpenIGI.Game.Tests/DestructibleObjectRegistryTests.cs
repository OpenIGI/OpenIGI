using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.World;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>Headless regression coverage for authored destructible task placement identity.</summary>
public sealed class DestructibleObjectRegistryTests
{
    [Fact]
    public void OneDeclaredExplodeObjectSharesItsDestroyedStateWithEveryOwnedPlacement()
    {
        GameLoop loop = new();
        QvmProgram script = QvmProgram.Read(QvmExpression.BuildProgram(
            QvmExpression.Call(
                "Task_DeclareParameters",
                QvmExpression.Text("ExplodeObject"),
                QvmExpression.Text("Position"), QvmExpression.Text("ObjectPos"),
                QvmExpression.Text("Model"), QvmExpression.Text("String16"),
                QvmExpression.Text("Destroyed model"), QvmExpression.Text("String16"),
                QvmExpression.Text("Damage scale"), QvmExpression.Text("Real32")),
            QvmExpression.Call(
                "Task_New",
                QvmExpression.Int(7),
                QvmExpression.Text("ExplodeObject"),
                QvmExpression.Text("Two monitors"),
                QvmExpression.Real(1f), QvmExpression.Real(2f), QvmExpression.Real(3f),
                QvmExpression.Text("203_01_1"),
                QvmExpression.Text("203_02_1"),
                QvmExpression.Real(1f))));
        Assert.Equal(QvmError.None, new QvmInterpreter(script, loop.Natives).Run());

        DeclaredTask task = Assert.IsType<DeclaredTask>(loop.Tasks.FromId(7));
        WorldPlacement first = Placement(task, "Model");
        WorldPlacement second = Placement(task, "Model 2");
        DestructibleObjectRegistry registry = DestructibleObjectRegistry.Build(
            loop.Tasks.Root,
            [first, second],
            loop.Variables);

        DestructibleObject item = Assert.Single(registry.Objects);
        Assert.Null(registry.TryDestroy(task, second));
        Assert.Equal(1, ExplodeObjectCountdown.RunUntilExploded(registry, item));
        Assert.True(registry.IsDestroyed(first));
        Assert.True(registry.IsDestroyed(second));
        Assert.Equal("203_02_1", item.DestroyedModelName);
    }

    [Fact]
    public void AGeneratorWithNoDestroyedModelStillExplodesAndKeepsItsModel()
    {
        // Mission 7's Generator_99 authors an empty Destroyed model, and its own On expression is
        // !Generator_99.isExploded (issue #134).
        GameLoop loop = new();
        QvmProgram script = QvmProgram.Read(QvmExpression.BuildProgram(
            QvmExpression.Call(
                "Task_DeclareParameters",
                QvmExpression.Text("Generator"),
                QvmExpression.Text("Position"), QvmExpression.Text("ObjectPos"),
                QvmExpression.Text("Model"), QvmExpression.Text("String16"),
                QvmExpression.Text("Destroyed model"), QvmExpression.Text("String16"),
                QvmExpression.Text("Damage scale"), QvmExpression.Text("Real32")),
            QvmExpression.Call(
                "Task_New",
                QvmExpression.Int(99),
                QvmExpression.Text("Generator"),
                QvmExpression.Text(string.Empty),
                QvmExpression.Real(1f), QvmExpression.Real(2f), QvmExpression.Real(3f),
                QvmExpression.Text("300_01_1"),
                QvmExpression.Text(string.Empty),
                QvmExpression.Real(1f))));
        Assert.Equal(QvmError.None, new QvmInterpreter(script, loop.Natives).Run());

        DeclaredTask task = Assert.IsType<DeclaredTask>(loop.Tasks.FromId(99));
        WorldPlacement model = Placement(task, "Model");
        DestructibleObjectRegistry registry = DestructibleObjectRegistry.Build(
            loop.Tasks.Root,
            [model],
            loop.Variables);
        LevelExpressionRuntime expressions = new(loop.Variables);

        DestructibleObject item = Assert.Single(registry.Objects);
        Assert.True(item.KeepsModelWhenDestroyed);
        Assert.True(expressions.EvaluateCondition("!Generator_99.isExploded", emptyValue: false));

        // A round arms the countdown at 0 (0x481E60); the object explodes on its next update, and only
        // that explosion (0x481A60) raises isExploded.
        Assert.Null(registry.TryDestroy(task, model));
        Assert.False(item.IsDestroyed);
        Assert.Equal(1, ExplodeObjectCountdown.RunUntilExploded(registry, item));

        Assert.True(item.IsDestroyed);
        Assert.True(expressions.EvaluateCondition("Generator_99.isExploded", emptyValue: false));
        Assert.False(registry.IsDestroyed(model));
    }

    /// <summary>
    /// An ExplodeObject authored with no health -- mission 5's Terminal_550, "Damage scale" 0 -- is set off by any blast's
    /// message, however far: 0x481EA0 compares the dose, nought included, against the health as authored. Measured on
    /// retail: an M203 34 m away blows it on the round's tick.
    /// </summary>
    [Fact]
    public void AnExplodeObjectWithNoHealthGoesOffAtAnyBlast()
    {
        GameLoop loop = new();
        QvmProgram script = QvmProgram.Read(QvmExpression.BuildProgram(
            QvmExpression.Call(
                "Task_DeclareParameters",
                QvmExpression.Text("Terminal"),
                QvmExpression.Text("Position"), QvmExpression.Text("ObjectPos"),
                QvmExpression.Text("Model"), QvmExpression.Text("String16"),
                QvmExpression.Text("Destroyed model"), QvmExpression.Text("String16"),
                QvmExpression.Text("Damage scale"), QvmExpression.Text("Real32")),
            QvmExpression.Call(
                "Task_New",
                QvmExpression.Int(550),
                QvmExpression.Text("Terminal"),
                QvmExpression.Text(string.Empty),
                QvmExpression.Real(1f), QvmExpression.Real(2f), QvmExpression.Real(3f),
                QvmExpression.Text("300_01_1"),
                QvmExpression.Text(string.Empty),
                QvmExpression.Real(0f))));
        Assert.Equal(QvmError.None, new QvmInterpreter(script, loop.Natives).Run());

        DeclaredTask task = Assert.IsType<DeclaredTask>(loop.Tasks.FromId(550));
        WorldPlacement model = Placement(task, "Model");
        DestructibleObjectRegistry registry = DestructibleObjectRegistry.Build(loop.Tasks.Root, [model], loop.Variables);
        DestructibleObject item = Assert.Single(registry.Objects);
        Assert.True(item.ExplodesOnCountdown);

        DestructibleObjectRegistry.ExplosionHit(item, null, new Vector3d(140000, 0, 0), 20480f, 0f, 1f);
        Assert.Equal(1, ExplodeObjectCountdown.RunUntilExploded(registry, item));
        Assert.True(item.IsDestroyed);
    }

    [Fact]
    public void ARadioIsAnExplodeObject()
    {
        // TASKTYPE_RADIO (0x43AD80) registers under ExplodeObject (word_54097C): a round only arms the
        // countdown, and the explosion that follows swaps the model and raises isExploded (issue #125).
        GameLoop loop = new();
        QvmProgram script = QvmProgram.Read(QvmExpression.BuildProgram(
            QvmExpression.Call(
                "Task_DeclareParameters",
                QvmExpression.Text("Radio"),
                QvmExpression.Text("Position"), QvmExpression.Text("ObjectPos"),
                QvmExpression.Text("Model"), QvmExpression.Text("String16"),
                QvmExpression.Text("Destroyed model"), QvmExpression.Text("String16"),
                QvmExpression.Text("Damage scale"), QvmExpression.Text("Real32")),
            QvmExpression.Call(
                "Task_New",
                QvmExpression.Int(3366),
                QvmExpression.Text("Radio"),
                QvmExpression.Text(string.Empty),
                QvmExpression.Real(1f), QvmExpression.Real(2f), QvmExpression.Real(3f),
                QvmExpression.Text("204_01_1"),
                QvmExpression.Text("204_02_1"),
                QvmExpression.Real(1f))));
        Assert.Equal(QvmError.None, new QvmInterpreter(script, loop.Natives).Run());

        DeclaredTask task = Assert.IsType<DeclaredTask>(loop.Tasks.FromId(3366));
        WorldPlacement model = Placement(task, "Model");
        DestructibleObjectRegistry registry = DestructibleObjectRegistry.Build(loop.Tasks.Root, [model], loop.Variables);
        LevelExpressionRuntime expressions = new(loop.Variables);

        DestructibleObject item = Assert.Single(registry.Objects);
        Assert.True(item.ExplodesOnCountdown);
        Assert.Null(registry.TryDestroy(task, model));
        Assert.False(expressions.EvaluateCondition("Radio_3366.isExploded", emptyValue: false));
        Assert.Equal(1, ExplodeObjectCountdown.RunUntilExploded(registry, item));
        Assert.True(registry.IsDestroyed(model));
        Assert.True(expressions.EvaluateCondition("Radio_3366.isExploded", emptyValue: false));
    }

    /// <summary>
    /// The damage sum is x87 arithmetic under chop rounding (<c>sub_498460</c>). Probed on mission 7's
    /// Radio_3366 with the Glock (0.2 a round): 0.2, 0.400000005, 0.599999964, 0.799999952, 0.99999994 --
    /// still short of 1 -- and the sixth round arms it.
    /// </summary>
    [Fact]
    public void FiveGlockRoundsLeaveAnExplodeObjectJustShort()
    {
        GameLoop loop = new();
        QvmProgram script = QvmProgram.Read(QvmExpression.BuildProgram(
            QvmExpression.Call(
                "Task_DeclareParameters",
                QvmExpression.Text("Radio"),
                QvmExpression.Text("Position"), QvmExpression.Text("ObjectPos"),
                QvmExpression.Text("Model"), QvmExpression.Text("String16"),
                QvmExpression.Text("Destroyed model"), QvmExpression.Text("String16"),
                QvmExpression.Text("Damage scale"), QvmExpression.Text("Real32")),
            QvmExpression.Call(
                "Task_New",
                QvmExpression.Int(3366),
                QvmExpression.Text("Radio"),
                QvmExpression.Text(string.Empty),
                QvmExpression.Real(1f), QvmExpression.Real(2f), QvmExpression.Real(3f),
                QvmExpression.Text("204_01_1"),
                QvmExpression.Text("204_02_1"),
                QvmExpression.Real(1f))));
        Assert.Equal(QvmError.None, new QvmInterpreter(script, loop.Natives).Run());

        DeclaredTask task = Assert.IsType<DeclaredTask>(loop.Tasks.FromId(3366));
        WorldPlacement model = Placement(task, "Model");
        DestructibleObjectRegistry registry = DestructibleObjectRegistry.Build(loop.Tasks.Root, [model], loop.Variables);
        DestructibleObject item = Assert.Single(registry.Objects);

        float[] retail = [0.2f, 0.400000005f, 0.599999964f, 0.799999952f, 0.99999994f];
        foreach (float expected in retail)
        {
            Assert.Null(registry.TryDestroy(task, model, damage: 0.2f));
            Assert.Equal(expected, item.ExplosionDamageAccumulated);
            Assert.Equal(-1, item.Countdown);
        }

        Assert.Null(registry.TryDestroy(task, model, damage: 0.2f));
        Assert.Equal(0, item.Countdown);
    }

    private static WorldPlacement Placement(GameTask owner, string taskName) => new(
        Position: default,
        Orientation: default,
        Instance: default,
        Pack: null!,
        Model: null!,
        TaskName: taskName,
        Owner: owner);
}

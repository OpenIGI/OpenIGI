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

    private static WorldPlacement Placement(GameTask owner, string taskName) => new(
        Position: default,
        Orientation: default,
        Instance: default,
        Pack: null!,
        Model: null!,
        TaskName: taskName,
        Owner: owner);
}

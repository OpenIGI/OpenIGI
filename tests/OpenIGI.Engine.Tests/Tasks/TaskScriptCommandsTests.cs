using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Engine.Tests.Tasks;

public sealed class TaskScriptCommandsTests
{
    [Fact]
    public void NestedTaskNewCallsBecomeChildren()
    {
        Fixture fixture = new();
        fixture.Types.Register<ContainerTask>("Container");

        fixture.Run(QvmExpression.Call(
            "Task_New",
            QvmExpression.Int(-1),
            QvmExpression.Text("Container"),
            QvmExpression.Text("EnvironmentalEffects"),
            QvmExpression.Call(
                "Task_New",
                QvmExpression.Int(-1),
                QvmExpression.Text("Container"),
                QvmExpression.Text("Inner"),
                QvmExpression.Call(
                    "Task_New",
                    QvmExpression.Int(-1),
                    QvmExpression.Text("Container"),
                    QvmExpression.Text("Leaf"))),
            QvmExpression.Call(
                "Task_New",
                QvmExpression.Int(-1),
                QvmExpression.Text("Container"),
                QvmExpression.Text("Sibling"))));

        GameTask outer = Assert.Single(fixture.Tree.Root.Children);
        Assert.Equal("EnvironmentalEffects", outer.Label);

        Assert.Equal(["Inner", "Sibling"], outer.Children.Select(c => c.Label));

        GameTask inner = outer.FirstChild!;
        GameTask leaf = Assert.Single(inner.Children);
        Assert.Equal("Leaf", leaf.Label);
    }

    [Fact]
    public void ParametersAreBoundPositionallyInDeclarationOrder()
    {
        Fixture fixture = new();
        fixture.Types.Register<HeightMapTask>("HeightMap");

        // The exact shape of a retail location0/level1 call.
        fixture.Run(QvmExpression.Call(
            "Task_New",
            QvmExpression.Int(-1),
            QvmExpression.Text("HeightMap"),
            QvmExpression.Text(string.Empty),
            QvmExpression.Real(22128590f),
            QvmExpression.Real(-57264816f),
            QvmExpression.Real(174634432f),
            QvmExpression.Int(11),
            QvmExpression.Int(128),
            QvmExpression.Int(0),
            QvmExpression.Int(0),
            QvmExpression.Int(8),
            QvmExpression.Int(7)));

        var task = (HeightMapTask)Assert.Single(fixture.Tree.Root.Children);

        Assert.Equal(new Vector3d(22128590d, -57264816d, 174634432d), task.Position);
        Assert.Equal(11, task.Level);
        Assert.Equal(128, task.Size);
        Assert.False(task.IsEdit);
        Assert.Equal(0, task.BitmapId);
        Assert.Equal(8, task.NormalSmooth);
        Assert.Equal(7, task.BlurSmooth);
    }

    [Fact]
    public void ChildrenFollowTheParametersRatherThanBeingSwallowedByThem()
    {
        Fixture fixture = new();
        fixture.Types.Register<HeightMapTask>("HeightMap");
        fixture.Types.Register<ContainerTask>("Container");

        fixture.Run(QvmExpression.Call(
            "Task_New",
            QvmExpression.Int(-1),
            QvmExpression.Text("HeightMap"),
            QvmExpression.Text(string.Empty),
            QvmExpression.Real(1f), QvmExpression.Real(2f), QvmExpression.Real(3f),
            QvmExpression.Int(4), QvmExpression.Int(5), QvmExpression.Int(0),
            QvmExpression.Int(6), QvmExpression.Int(7), QvmExpression.Int(8),
            QvmExpression.Call(
                "Task_New",
                QvmExpression.Int(-1),
                QvmExpression.Text("Container"),
                QvmExpression.Text("AfterTheParameters"))));

        var task = (HeightMapTask)Assert.Single(fixture.Tree.Root.Children);
        Assert.Equal(8, task.BlurSmooth);

        GameTask child = Assert.Single(task.Children);
        Assert.Equal("AfterTheParameters", child.Label);
    }

    [Fact]
    public void GeneratedParameterDescriptorsMatchTheDeclaredOrderAndTypes()
    {
        TaskTypeRegistry types = new();
        TaskType type = types.Register<HeightMapTask>("HeightMap");

        Assert.Equal(
            [
                new TaskParameter("Position", ScriptParamType.ObjectPos),
                new TaskParameter("Level", ScriptParamType.Int32),
                new TaskParameter("Size", ScriptParamType.Int32),
                new TaskParameter("isEdit", ScriptParamType.Bool8),
                new TaskParameter("Bitmap ID", ScriptParamType.Int32),
                new TaskParameter("NORMALSMOOTH", ScriptParamType.Int32),
                new TaskParameter("BLURSMOOTH", ScriptParamType.Int32),
            ],
            type.Parameters);

        Assert.Equal(9, type.ArgumentCount);
    }

    [Fact]
    public void DeclaredParametersBuildATypeWithNoClassBehindIt()
    {
        Fixture fixture = new();

        fixture.Run(
            QvmExpression.Call(
                "Task_DeclareParameters",
                QvmExpression.Text("MipMapControl"),
                QvmExpression.Text("LOD blend"), QvmExpression.Text("bool8"),
                QvmExpression.Text("MipMap mode"), QvmExpression.Text("Int32"),
                QvmExpression.Text("LOD bias (Standard MipMapping)"), QvmExpression.Text("Real32"),
                QvmExpression.Text("LOD bias (Trilinear MipMapping)"), QvmExpression.Text("Real32")),
            QvmExpression.Call(
                "Task_New",
                QvmExpression.Int(-1),
                QvmExpression.Text("MipMapControl"),
                QvmExpression.Text(string.Empty),
                QvmExpression.Int(1),
                QvmExpression.Int(1),
                QvmExpression.Real(-0.4f),
                QvmExpression.Real(-0.4f)));

        TaskType type = fixture.Types.Get("MipMapControl");
        Assert.True(type.IsDeclarationOnly);
        Assert.Equal(4, type.ArgumentCount);

        var task = (DeclaredTask)Assert.Single(fixture.Tree.Root.Children);
        Assert.True(task.TryGetValue("LOD blend", out object? blend));
        Assert.Equal(true, blend);
        Assert.True(task.TryGetValue("LOD bias (Trilinear MipMapping)", out object? bias));
        Assert.Equal(-0.4f, bias);
    }

    [Fact]
    public void ADeclarationThatDisagreesWithTheClassIsFatal()
    {
        Fixture fixture = new();
        fixture.Types.Register<HeightMapTask>("HeightMap");

        Core.Diagnostics.EngineFatalException error = Assert.Throws<Core.Diagnostics.EngineFatalException>(
            () => fixture.Run(QvmExpression.Call(
                "Task_DeclareParameters",
                QvmExpression.Text("HeightMap"),
                QvmExpression.Text("Position"), QvmExpression.Text("ObjectPos"),
                QvmExpression.Text("Level"), QvmExpression.Text("Real32"))));

        Assert.Contains("HeightMap", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyTasksWithAnIdExportNamedVariables()
    {
        Fixture fixture = new();
        fixture.Types.Register<SwitchTask>("Switch");

        fixture.Run(
            QvmExpression.Call(
                "Task_New",
                QvmExpression.Int(94),
                QvmExpression.Text("Switch"),
                QvmExpression.Text("MainSwitch")),
            QvmExpression.Call(
                "Task_New",
                QvmExpression.Int(-1),
                QvmExpression.Text("Switch"),
                QvmExpression.Text("Anonymous")));

        GameTask named = fixture.Tree.Root.FirstChild!;
        GameTask anonymous = fixture.Tree.Root.LastChild!;

        Assert.True(named.IsExported);
        Assert.False(anonymous.IsExported);
        Assert.Equal(["Switch_94.isPressed", "Switch_94.isLastPressed"], fixture.Variables.GetBoundNames(named));
    }

    [Fact]
    public void ExportedVariablesAreLiveAliasesInBothDirections()
    {
        Fixture fixture = new();
        fixture.Types.Register<SwitchTask>("Switch");

        fixture.Run(QvmExpression.Call(
            "Task_New",
            QvmExpression.Int(94),
            QvmExpression.Text("Switch"),
            QvmExpression.Text(string.Empty)));

        var task = (SwitchTask)fixture.Tree.Root.FirstChild!;

        // Engine writes are visible to scripts...
        task.Pressed.Value = true;
        Assert.True(fixture.Variables.TryRead("Switch_94.isPressed", out QvmValue read));
        Assert.Equal(1, read.Integer);

        // ...and script writes are visible to the engine.
        Assert.True(fixture.Variables.TryWrite("Switch_94.isPressed", QvmValue.FromInt32(0)));
        Assert.False(task.Pressed.Value);
    }

    [Fact]
    public void TheLastShadowLagsByOneTickSoEdgesAreDetectable()
    {
        Fixture fixture = new();
        fixture.Types.Register<SwitchTask>("Switch");

        fixture.Run(QvmExpression.Call(
            "Task_New",
            QvmExpression.Int(94),
            QvmExpression.Text("Switch"),
            QvmExpression.Text(string.Empty)));

        var task = (SwitchTask)fixture.Tree.Root.FirstChild!;

        task.Pressed.Value = true;
        Assert.True(task.Pressed.IsRisingEdge);
        Assert.Equal(0, ReadInt(fixture, "Switch_94.isLastPressed"));

        fixture.Tree.Update();

        Assert.False(task.Pressed.IsRisingEdge);
        Assert.Equal(1, ReadInt(fixture, "Switch_94.isLastPressed"));
    }

    [Fact]
    public void UnexportingRemovesEveryBinding()
    {
        Fixture fixture = new();
        fixture.Types.Register<SwitchTask>("Switch");

        fixture.Run(QvmExpression.Call(
            "Task_New",
            QvmExpression.Int(94),
            QvmExpression.Text("Switch"),
            QvmExpression.Text(string.Empty)));

        GameTask task = fixture.Tree.Root.FirstChild!;

        Assert.True(fixture.Variables.Unexport(task));
        Assert.False(fixture.Variables.TryRead("Switch_94.isPressed", out _));
        Assert.Empty(fixture.Variables.GetBoundNames(task));
    }

    [Fact]
    public void PostLoadReachesTheWholeTreeAndParsedReachesEachTaskOnce()
    {
        Fixture fixture = new();
        fixture.Types.Register<ContainerTask>("Container");

        fixture.Run(QvmExpression.Call(
            "Task_New",
            QvmExpression.Int(-1),
            QvmExpression.Text("Container"),
            QvmExpression.Text("outer"),
            QvmExpression.Call(
                "Task_New",
                QvmExpression.Int(-1),
                QvmExpression.Text("Container"),
                QvmExpression.Text("inner"))));

        var outer = (ContainerTask)fixture.Tree.Root.FirstChild!;
        var inner = (ContainerTask)outer.FirstChild!;

        Assert.Equal(1, outer.ParsedCount);
        Assert.Equal(1, inner.ParsedCount);
        Assert.Equal(0, outer.PostLoadCount);

        fixture.Tree.PostLoad();

        Assert.Equal(1, outer.PostLoadCount);
        Assert.Equal(1, inner.PostLoadCount);
    }

    [Fact]
    public void ParsedArrivesAfterAChildHasBeenAttached()
    {
        Fixture fixture = new();
        fixture.Types.Register<ContainerTask>("Container");

        fixture.Run(QvmExpression.Call(
            "Task_New",
            QvmExpression.Int(-1),
            QvmExpression.Text("Container"),
            QvmExpression.Text("outer"),
            QvmExpression.Call(
                "Task_New",
                QvmExpression.Int(-1),
                QvmExpression.Text("Container"),
                QvmExpression.Text("inner"))));

        var outer = (ContainerTask)fixture.Tree.Root.FirstChild!;

        // Parsed fires before the nested children are evaluated, matching the original's ordering:
        // property binding, then ExportVars, then the message, then the child expressions.
        Assert.Equal(0, outer.ChildCountAtParsed);
        Assert.Equal(1, outer.ChildCount);
    }

    private static int ReadInt(Fixture fixture, string symbol)
    {
        Assert.True(fixture.Variables.TryRead(symbol, out QvmValue value));
        return value.Integer;
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            Types = new TaskTypeRegistry();
            Tree = new TaskTree(Types);
            Variables = new ScriptVarRegistry(Natives);
            Commands = new TaskScriptCommands(Tree, Variables);
            Commands.Register(Natives);
        }

        public NativeRegistry Natives { get; } = new();

        public TaskTypeRegistry Types { get; }

        public TaskTree Tree { get; }

        public ScriptVarRegistry Variables { get; }

        public TaskScriptCommands Commands { get; }

        public void Run(params QvmExpression[] statements)
        {
            QvmProgram program = QvmProgram.Read(QvmExpression.BuildProgram(statements));
            QvmInterpreter interpreter = new(program, Natives);
            QvmError error = interpreter.Run();
            Assert.Equal(QvmError.None, error);
        }
    }
}

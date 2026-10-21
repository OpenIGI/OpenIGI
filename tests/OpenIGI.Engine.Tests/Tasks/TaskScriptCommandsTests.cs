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
        fixture.Types.Register<LifecycleProbeTask>("Container");

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
        fixture.Types.Register<LifecycleProbeTask>("Container");

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
    public void A_type_replaced_with_an_adapter_reads_the_levels_parameters_and_hands_them_to_it()
    {
        Fixture fixture = new();
        fixture.Types.Register<HeightMapTask>("HeightMap");
        fixture.Types.Register<LifecycleProbeTask>("Container");
        HeightMapToElevation adapter = new();
        fixture.Types.Replace("HeightMap", static () => new ElevationTask(), adapter);

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

        var task = (ElevationTask)Assert.Single(fixture.Tree.Root.Children);
        Assert.Equal(new Vector3d(1d, 2d, 3d), task.Origin);
        Assert.Equal(4, task.Detail);
        Assert.True(task.Parsed, "The adapter ran after the task was parsed.");
        Assert.Equal(7, adapter.Seen!.Count);
        Assert.Equal("AfterTheParameters", Assert.Single(task.Children).Label);
    }

    [Fact]
    public void A_class_with_the_same_names_in_another_order_is_set_by_name()
    {
        Fixture fixture = new();
        fixture.Types.Register<HeightMapTask>("HeightMap");
        fixture.Types.Replace("HeightMap", static () => new LaterHeightMapTask(), ByNameParameterAdapter.Instance);

        fixture.Run(QvmExpression.Call(
            "Task_New",
            QvmExpression.Int(-1),
            QvmExpression.Text("HeightMap"),
            QvmExpression.Text(string.Empty),
            QvmExpression.Real(1f), QvmExpression.Real(2f), QvmExpression.Real(3f),
            QvmExpression.Int(4), QvmExpression.Int(5), QvmExpression.Int(0),
            QvmExpression.Int(6), QvmExpression.Int(7), QvmExpression.Int(8)));

        var task = (LaterHeightMapTask)Assert.Single(fixture.Tree.Root.Children);
        Assert.Equal(new Vector3d(1d, 2d, 3d), task.Position);
        Assert.Equal(4, task.Level);
        Assert.Equal(5, task.Size);
        Assert.Equal(3, task.Smoothing);
        Assert.False(task.TrySetScriptValue("Level", 1.5f), "A value of another type is refused.");
        Assert.Equal(4, task.Level);
    }

    [Fact]
    public void A_declaration_after_an_adapted_replacement_is_what_the_level_is_read_by()
    {
        TaskTypeRegistry types = new();
        types.Register<HeightMapTask>("HeightMap");
        TaskType type = types.Replace("HeightMap", static () => new ElevationTask(), new HeightMapToElevation());

        types.Declare("HeightMap", [new TaskParameter("Position", ScriptParamType.ObjectPos)]);

        Assert.Equal([new TaskParameter("Position", ScriptParamType.ObjectPos)], type.Parameters);
        Assert.True(type.IsDeclared);

        types.Replace<HeightMapTask>("HeightMap");
        Assert.Null(type.ParameterAdapter);
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

    /// <summary>A script may declare fewer parameters than the class carries, and the rest read as zero.</summary>
    /// <remarks>
    /// <para>
    /// <c>Level_DeclareTaskParameters</c> (<c>igi2 0x543F10</c>) builds a per-script list of
    /// name/type pairs for the type and never compares it against the type's own list at all — a script
    /// states the shape it was compiled against, and a build older or newer than the engine's is
    /// ordinary rather than broken. I.G.I. 2 ships both: <c>location1/level1</c> declares ten parameters
    /// for <c>HumanPlayer</c> where every other level declares eleven.
    /// </para>
    /// <para>
    /// A shortfall cannot shift a field, because the reader takes arguments in order and simply runs out.
    /// </para>
    /// </remarks>
    [Fact]
    public void ADeclarationShorterThanTheClassBindsWhatItNames()
    {
        Fixture fixture = new();
        fixture.Types.Register<HeightMapTask>("HeightMap");

        fixture.Run(
            QvmExpression.Call(
                "Task_DeclareParameters",
                QvmExpression.Text("HeightMap"),
                QvmExpression.Text("Position"), QvmExpression.Text("ObjectPos"),
                QvmExpression.Text("Level"), QvmExpression.Text("Int32")),
            QvmExpression.Call(
                "Task_New",
                QvmExpression.Int(7),
                QvmExpression.Text("HeightMap"),
                QvmExpression.Text(string.Empty),
                QvmExpression.Real(1f), QvmExpression.Real(2f), QvmExpression.Real(3f),
                QvmExpression.Int(4)));

        var task = (HeightMapTask)Assert.Single(fixture.Tree.Root.Children);

        // What the script named is bound.
        Assert.Equal(new Core.Numerics.Vector3d(1, 2, 3), task.Position);
        Assert.Equal(4, task.Level);

        // What it did not name reads as zero, which is where the argument list ended.
        Assert.Equal(0, task.Size);
    }

    [Fact]
    public void AIGraphGraphdataConsumesThreeValuesAndKeepsTuningAndChildrenAligned()
    {
        Fixture fixture = new();
        fixture.Types.Register<LifecycleProbeTask>("Container");

        fixture.Run(
            QvmExpression.Call(
                "Task_DeclareParameters",
                QvmExpression.Text("AIGraph"),
                QvmExpression.Text("Graph position"), QvmExpression.Text("ObjectPos"),
                QvmExpression.Text("Update"), QvmExpression.Text("PushButton"),
                QvmExpression.Text("Graphdata"), QvmExpression.Text("Graph"),
                QvmExpression.Text("Node cover midoffset"), QvmExpression.Text("Real64"),
                QvmExpression.Text("Node cover topoffset"), QvmExpression.Text("Real64"),
                QvmExpression.Text("Max height difference between linked nodes"), QvmExpression.Text("Real64"),
                QvmExpression.Text("Width of node links"), QvmExpression.Text("Real64"),
                QvmExpression.Text("Link maximum distance to ground"), QvmExpression.Text("Real64"),
                QvmExpression.Text("Use precise link method (SLOW!)"), QvmExpression.Text("bool8"),
                QvmExpression.Text("Precise link method step value"), QvmExpression.Text("Real64")),
            QvmExpression.Call(
                "Task_New",
                QvmExpression.Int(1),
                QvmExpression.Text("AIGraph"),
                QvmExpression.Text("The big one"),
                QvmExpression.Real(112507580f),
                QvmExpression.Real(-55808670f),
                QvmExpression.Real(175286940f),
                QvmExpression.Int(0),
                // Retail Level 4 graph 1's Graph payload precedes its seven tuning values.
                QvmExpression.Int(168),
                QvmExpression.Int(200),
                QvmExpression.Int(403),
                QvmExpression.Real(0.7f),
                QvmExpression.Real(2f),
                QvmExpression.Real(3f),
                QvmExpression.Real(0.4f),
                QvmExpression.Real(0.4f),
                QvmExpression.Int(0),
                QvmExpression.Real(0.05f),
                QvmExpression.Call(
                    "Task_New",
                    QvmExpression.Int(-1),
                    QvmExpression.Text("Container"),
                    QvmExpression.Text("AfterAIGraphPayload"))));

        var graph = (DeclaredTask)Assert.Single(fixture.Tree.Root.Children);

        Assert.Equal([168f, 200f, 403f], Assert.IsType<float[]>(graph.GetValue(2)));
        Assert.Equal(0.7, Assert.IsType<double>(graph.GetValue(3)), precision: 5);
        Assert.Equal(2.0, Assert.IsType<double>(graph.GetValue(4)));
        Assert.Equal(3.0, Assert.IsType<double>(graph.GetValue(5)));
        Assert.Equal(0.4, Assert.IsType<double>(graph.GetValue(6)), precision: 5);
        Assert.Equal(0.4, Assert.IsType<double>(graph.GetValue(7)), precision: 5);
        Assert.False(Assert.IsType<bool>(graph.GetValue(8)));
        Assert.Equal(0.05, Assert.IsType<double>(graph.GetValue(9)), precision: 5);

        GameTask child = Assert.Single(graph.Children);
        Assert.Equal("AfterAIGraphPayload", child.Label);
    }

    [Fact]
    public void GeneratedGraphParameterReceivesTheAuthoredFloatTriplet()
    {
        Fixture fixture = new();
        fixture.Types.Register<GraphValueTask>("GraphValue");

        fixture.Run(QvmExpression.Call(
            "Task_New",
            QvmExpression.Int(-1),
            QvmExpression.Text("GraphValue"),
            QvmExpression.Text("GraphPayload"),
            QvmExpression.Int(168),
            QvmExpression.Int(200),
            QvmExpression.Int(403)));

        GraphValueTask graph = Assert.IsType<GraphValueTask>(Assert.Single(fixture.Tree.Root.Children));
        Assert.Equal([168f, 200f, 403f], graph.Curve);
    }

    /// <summary>A parameter that eats a different number of arguments is still fatal.</summary>
    /// <remarks>
    /// The one disagreement that corrupts a parse rather than truncating it: <c>Task_New</c> reads
    /// positionally out of one flat list, so a parameter of a different arity shifts every field after
    /// it. Length is negotiable; arity inside the shared prefix is not.
    /// </remarks>
    [Fact]
    public void ADeclarationThatChangesAParametersArityIsFatal()
    {
        Fixture fixture = new();
        fixture.Types.Register<HeightMapTask>("HeightMap");

        Core.Diagnostics.EngineFatalException error = Assert.Throws<Core.Diagnostics.EngineFatalException>(
            () => fixture.Run(QvmExpression.Call(
                "Task_DeclareParameters",
                QvmExpression.Text("HeightMap"),
                QvmExpression.Text("Position"), QvmExpression.Text("Real32"),
                QvmExpression.Text("Level"), QvmExpression.Text("Int32"))));

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
        fixture.Types.Register<LifecycleProbeTask>("Container");

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

        var outer = (LifecycleProbeTask)fixture.Tree.Root.FirstChild!;
        var inner = (LifecycleProbeTask)outer.FirstChild!;

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
        fixture.Types.Register<LifecycleProbeTask>("Container");

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

        var outer = (LifecycleProbeTask)fixture.Tree.Root.FirstChild!;

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

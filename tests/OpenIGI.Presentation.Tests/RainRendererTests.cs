using OpenIGI.Presentation.Rendering;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.World;
using OpenIGI.Rendering;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Presentation.Tests;

public sealed class RainRendererTests
{
    private static TaskType DeclareRainEffect(TaskTypeRegistry types) => types.Declare("RainEffect", [
        new TaskParameter("Is Rain", ScriptParamType.VarString),
        new TaskParameter("Traceline start", ScriptParamType.Real32),
        new TaskParameter("Traceline end", ScriptParamType.Real32),
        new TaskParameter("Is Active", ScriptParamType.VarString),
        new TaskParameter("Rain Alpha", ScriptParamType.Real32),
    ]);

    /// <summary>
    /// Builds a task tree by running a script of <c>Task_New</c> calls, mirroring how level data builds
    /// a mission: types are registered, parameters are parsed positionally, and the resulting tree's
    /// tasks carry their bound values. Declared tasks (no backing class) populate <c>_values</c> this way,
    /// which is exactly how a level's <c>RainEffect</c> is authored.
    /// </summary>
    private static TaskTree BuildLevel(params QvmExpression[] statements)
    {
        NativeRegistry natives = new();
        TaskTypeRegistry types = new();
        EngineTaskTypes.Register(types);
        DeclareRainEffect(types);

        TaskTree tree = new(types);
        ScriptVarRegistry variables = new(natives);
        TaskScriptCommands commands = new(tree, variables);
        commands.Register(natives);
        natives.RegisterConstant("TRUE", 1);
        natives.RegisterConstant("FALSE", 0);

        QvmProgram program = QvmProgram.Read(QvmExpression.BuildProgram(statements));
        QvmInterpreter interpreter = new(program, natives);
        Assert.Equal(QvmError.None, interpreter.Run());

        return tree;
    }

    private static QvmExpression RainEffect(
        string isRain, float start, float end, string isActive, float alpha) => QvmExpression.Call(
        "Task_New",
        QvmExpression.Int(-1),
        QvmExpression.Text("RainEffect"),
        QvmExpression.Text("Rain"),
        QvmExpression.Text(isRain),
        QvmExpression.Real(start),
        QvmExpression.Real(end),
        QvmExpression.Text(isActive),
        QvmExpression.Real(alpha));

    private static TaskTree LoadTaskTree(string path)
    {
        NativeRegistry natives = new();
        TaskTypeRegistry types = new();
        EngineTaskTypes.Register(types);

        TaskTree tree = new(types);
        ScriptVarRegistry variables = new(natives);
        TaskScriptCommands commands = new(tree, variables);
        commands.Register(natives);
        natives.RegisterConstant("TRUE", 1);
        natives.RegisterConstant("FALSE", 0);

        QvmProgram program = QvmProgram.Read(File.ReadAllBytes(path));
        QvmInterpreter interpreter = new(program, natives);
        Assert.Equal(QvmError.None, interpreter.Run());
        return tree;
    }

    [Fact]
    public void FindRainEffectLocatesTaskInTaskTree()
    {
        // A RainEffect task nested under the level root is locatable by the renderer.
        TaskTree tree = BuildLevel(RainEffect("TRUE", 9.0f, 3.0f, "TRUE", 0.5f));

        DeclaredTask? found = RainRenderer.FindRainEffect(tree.Root);
        Assert.NotNull(found);
        Assert.Equal("RainEffect", found!.Type.Name);
    }

    [Fact]
    public void ReadParamsEnablesRainWhenBothFlagsAreTrue()
    {
        // The retail case: Is Rain and Is Active are both true, so the renderer turns on.
        TaskTree tree = BuildLevel(RainEffect("TRUE", 9.0f, 3.0f, "TRUE", 0.5f));

        RecordingGraphicsDevice device = new();
        using RainRenderer renderer = new(device);
        bool configured = renderer.ReadParams(tree.Root);

        Assert.True(configured);
        Assert.True(renderer.IsActive);
        Assert.Equal(9.0f, renderer.StartMeters);
        Assert.Equal(3.0f, renderer.EndMeters);
        Assert.Equal(0.5f, renderer.Alpha);
    }

    [Fact]
    public void ReadParamsDisablesRainWhenIsRainIsFalse()
    {
        // Level 7 / 12 shape: a RainEffect task exists but Is Rain is FALSE, so it stays off.
        TaskTree tree = BuildLevel(RainEffect("FALSE", 10f, 2f, "TRUE", 0.5f));

        RecordingGraphicsDevice device = new();
        using RainRenderer renderer = new(device);
        bool configured = renderer.ReadParams(tree.Root);

        Assert.False(configured);
        Assert.False(renderer.IsActive);
    }

    [Fact]
    public void ReadParamsDisablesRainWhenIsActiveIsFalse()
    {
        // Level 4 / 8 / 11 shape: Is Active is FALSE even though Is Rain is true.
        TaskTree tree = BuildLevel(RainEffect("TRUE", 10f, 2f, "FALSE", 0.5f));

        RecordingGraphicsDevice device = new();
        using RainRenderer renderer = new(device);
        bool configured = renderer.ReadParams(tree.Root);

        Assert.False(configured);
        Assert.False(renderer.IsActive);
    }

    [Fact]
    public void ReadParamsTurnsRainOffWhenNoRainEffectExists()
    {
        // No RainEffect task at all: the renderer keeps whatever it had off and reports false.
        TaskTree tree = BuildLevel(
            QvmExpression.Call("Task_New", QvmExpression.Int(1), QvmExpression.Text("QTask"), QvmExpression.Text("Root")));

        RecordingGraphicsDevice device = new();
        using RainRenderer renderer = new(device);
        renderer.SetParams(true, 10f, 2f, 0.5f);
        bool configured = renderer.ReadParams(tree.Root);

        Assert.False(configured);
        Assert.False(renderer.IsActive);
    }

    [RequiresGameDataFact]
    public void RetailRainLevelsReadTheirObjectsQvmWeatherParameters()
    {
        var expected = new Dictionary<string, (float Start, float End, float Alpha)>
        {
            ["level3"] = (50f, 20f, 0.13f),
            ["level9"] = (50f, 20f, 0.15f),
            ["level10"] = (50f, 20f, 0.135f),
        };

        string missionsRoot = Path.Combine(GameData.RequireRoot(), "MISSIONS");
        foreach ((string level, (float start, float end, float alpha) values) in expected)
        {
            string path = Path.Combine(missionsRoot, "location0", level, "objects.qvm");
            TaskTree tree = LoadTaskTree(path);
            RecordingGraphicsDevice device = new();
            using RainRenderer renderer = new(device);

            Assert.True(renderer.ReadParams(tree.Root), $"{level} did not enable RainEffect.");
            Assert.Equal(values.start, renderer.StartMeters);
            Assert.Equal(values.end, renderer.EndMeters);
            Assert.Equal(values.alpha, renderer.Alpha);
        }
    }

    [Theory]
    [InlineData("TRUE", true)]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData("1", true)]
    [InlineData("2", true)]
    [InlineData("\"TRUE\"", true)]
    [InlineData("FALSE", false)]
    [InlineData("false", false)]
    [InlineData("0", false)]
    [InlineData("\"FALSE\"", false)]
    [InlineData("", false)]
    [InlineData("banana", false)]
    public void IsTrueTextAcceptsRetailSpelling(string text, bool expected)
    {
        Assert.Equal(expected, RainRenderer.IsTrueText(text));
    }

    [Fact]
    public void IndoorsFlagSuppressesActiveRendering()
    {
        RecordingGraphicsDevice device = new();
        using RainRenderer renderer = new(device);

        renderer.SetParams(active: true, startMeters: 10f, endMeters: 2f, alpha: 0.5f);
        Assert.True(renderer.IsActive);

        renderer.Indoors = true;
        Assert.False(renderer.IsActive);

        renderer.Indoors = false;
        Assert.True(renderer.IsActive);
    }

    [Fact]
    public void IndoorsFlagStopsTheActualMeshDrawUntilTheCameraLeaves()
    {
        RecordingGraphicsDevice device = new();
        using RainRenderer renderer = new(device, numDrops: 1);
        renderer.SetParams(active: true, startMeters: 10f, endMeters: 2f, alpha: 0.5f);

        renderer.Draw(Camera3D.Default, Matrix4f.Identity, seconds: 0f);
        Assert.Single(device.MeshDraws);

        renderer.Indoors = true;
        renderer.Draw(Camera3D.Default, Matrix4f.Identity, seconds: 1f);
        Assert.Single(device.MeshDraws);

        renderer.Indoors = false;
        renderer.Draw(Camera3D.Default, Matrix4f.Identity, seconds: 2f);
        Assert.Equal(2, device.MeshDraws.Count);
    }

    [Fact]
    public void DrawBuildsEveryDropWithAWorldVerticalDownwardStreak()
    {
        RecordingGraphicsDevice device = new();
        using RainRenderer renderer = new(device, numDrops: 1);
        renderer.SetParams(active: true, startMeters: 10f, endMeters: 2f, alpha: 0.5f);

        Camera3D camera = new(
            new Vector3d(100.0, 200.0, 1000.0),
            Camera3D.LookRotation(0f, 0f));
        renderer.Draw(camera, Matrix4f.Identity, seconds: 0f);

        Vertex3D[] vertices = Assert.Single(device.Meshes.Values).Vertices;
        Assert.Equal(vertices[0].Position.X, vertices[3].Position.X);
        Assert.Equal(vertices[0].Position.Y, vertices[3].Position.Y);
        Assert.True(vertices[2].Position.Z > vertices[0].Position.Z);
        Assert.InRange(
            vertices[2].Position.Z - vertices[0].Position.Z,
            RainRenderer.StreakLengthMeters - 0.00001f,
            RainRenderer.StreakLengthMeters + 0.00001f);
    }

    [Fact]
    public void DefaultRainIsSparseSlowAndShortEnoughForSmallDroplets()
    {
        RecordingGraphicsDevice device = new();
        using RainRenderer renderer = new(device);

        Assert.Equal(1200, renderer.DropCount);
        Assert.Equal(0.08f, RainRenderer.StreakLengthMeters);
        Assert.InRange(
            RainRenderer.CalculateFallSpeed(70f, 0f),
            5.599f,
            5.601f);
        Assert.InRange(
            RainRenderer.CalculateFallSpeed(70f, 1f),
            12.599f,
            12.601f);
    }

    [Fact]
    public void UserWeatherSettingsCanDisableRainAndChangeItsSpeed()
    {
        RecordingGraphicsDevice device = new();
        using RainRenderer renderer = new(device);
        renderer.ReadParams(BuildLevel(RainEffect("TRUE", 50f, 20f, "TRUE", 0.13f)).Root);

        renderer.ApplySettings(enabled: false, WeatherKind.Rain, speedMultiplier: 1.5f);
        Assert.False(renderer.IsActive);
        Assert.False(renderer.IsWeatherEnabled);
        Assert.Equal(1.5f, renderer.SpeedMultiplier);

        renderer.ApplySettings(enabled: true, WeatherKind.Rain, speedMultiplier: 0.25f);
        Assert.True(renderer.IsActive);
        Assert.True(renderer.IsWeatherEnabled);
        Assert.Equal(0.25f, renderer.SpeedMultiplier);
    }

    [Fact]
    public void HeightBoundsCalculateCorrectlyAroundCameraZ()
    {
        // The reference stores the lower traceline as a positive distance below the camera.
        RecordingGraphicsDevice device = new();
        using RainRenderer renderer = new(device);

        renderer.SetParams(active: true, startMeters: 12f, endMeters: 4f, alpha: 0.5f);
        (float start, float end) = renderer.CalculateHeightBounds(100.0);

        Assert.Equal(112.0f, start);
        Assert.Equal(96.0f, end);
    }
}

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

public sealed class SnowRendererTests
{
    [Fact]
    public void ReadParamsEnablesSnowForActiveNonRainWeather()
    {
        TaskTree tree = BuildLevel(RainEffect("FALSE", 50f, 20f, "TRUE", 0.06f));
        RecordingGraphicsDevice device = new();
        using SnowRenderer renderer = new(device);

        Assert.True(renderer.ReadParams(tree.Root));
        Assert.True(renderer.IsActive);
        Assert.Equal(50f, renderer.StartMeters);
        Assert.Equal(20f, renderer.EndMeters);
        Assert.Equal(0.06f, renderer.Alpha);
    }

    [Fact]
    public void ReadParamsLeavesSnowOffForActiveRainWeather()
    {
        TaskTree tree = BuildLevel(RainEffect("TRUE", 50f, 20f, "TRUE", 0.13f));
        RecordingGraphicsDevice device = new();
        using SnowRenderer renderer = new(device);

        Assert.False(renderer.ReadParams(tree.Root));
        Assert.False(renderer.IsActive);
    }

    [Fact]
    public void UserWeatherSettingsCanSwitchSnowOffAndOn()
    {
        RecordingGraphicsDevice device = new();
        using SnowRenderer renderer = new(device);
        renderer.ReadParams(BuildLevel(RainEffect("FALSE", 50f, 20f, "TRUE", 0.06f)).Root);

        renderer.ApplySettings(enabled: true, WeatherKind.Rain, speedMultiplier: 2f);
        Assert.False(renderer.IsActive);
        Assert.False(renderer.IsWeatherEnabled);

        renderer.ApplySettings(enabled: true, WeatherKind.Snow, speedMultiplier: 0.5f);
        Assert.True(renderer.IsActive);
        Assert.True(renderer.IsWeatherEnabled);
        Assert.Equal(0.5f, renderer.SpeedMultiplier);
    }

    [Fact]
    public void DrawBuildsSmallHorizontalFlakesAtCameraRelativeHeight()
    {
        RecordingGraphicsDevice device = new();
        using SnowRenderer renderer = new(device, particleCount: 1);
        renderer.ReadParams(BuildLevel(RainEffect("FALSE", 50f, 20f, "TRUE", 0.06f)).Root);
        Camera3D camera = new(new Vector3d(100, 200, 1000), Camera3D.LookRotation(0f, 0f));

        renderer.Draw(camera, Matrix4f.Identity, seconds: 0f);

        Vertex3D[] vertices = Assert.Single(device.Meshes.Values).Vertices;
        Assert.True(vertices[2].Position.Z > vertices[0].Position.Z);
        Assert.InRange(
            vertices[2].Position.Z - vertices[0].Position.Z,
            (2f * 0.045f) - 0.00001f,
            (2f * 0.045f) + 0.00001f);
        Assert.True(vertices[1].Position.X > vertices[0].Position.X);
        Assert.True(vertices[2].Position.Y > vertices[1].Position.Y);
    }

    [RequiresGameDataFact]
    public void RetailSnowLevelsReadTheirObjectsQvmWeatherParameters()
    {
        var expected = new Dictionary<string, float>
        {
            ["level7"] = 0.06f,
            ["level12"] = 0.3125f,
        };
        string missionsRoot = Path.Combine(GameData.RequireRoot(), "MISSIONS");

        foreach ((string level, float alpha) in expected)
        {
            string path = Path.Combine(missionsRoot, "location0", level, "objects.qvm");
            TaskTree tree = LoadTaskTree(path);
            RecordingGraphicsDevice device = new();
            using SnowRenderer renderer = new(device);

            Assert.True(renderer.ReadParams(tree.Root), $"{level} did not enable snow.");
            Assert.Equal(50f, renderer.StartMeters);
            Assert.Equal(20f, renderer.EndMeters);
            Assert.Equal(alpha, renderer.Alpha);
        }
    }

    private static TaskTree BuildLevel(params QvmExpression[] statements)
    {
        NativeRegistry natives = new();
        TaskTypeRegistry types = new();
        EngineTaskTypes.Register(types);
        types.Declare("RainEffect", [
            new TaskParameter("Is Rain", ScriptParamType.VarString),
            new TaskParameter("Traceline start", ScriptParamType.Real32),
            new TaskParameter("Traceline end", ScriptParamType.Real32),
            new TaskParameter("Is Active", ScriptParamType.VarString),
            new TaskParameter("Rain Alpha", ScriptParamType.Real32),
        ]);

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

    private static QvmExpression RainEffect(string isRain, float start, float end, string isActive, float alpha) =>
        QvmExpression.Call(
            "Task_New",
            QvmExpression.Int(-1),
            QvmExpression.Text("RainEffect"),
            QvmExpression.Text("Weather"),
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
}

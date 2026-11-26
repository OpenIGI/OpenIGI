using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Level;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Game.Tests;

/// <summary>
/// A cutscene shot's filter colour reaches the frame as the level gives it: the original reads it by its place in the
/// EditCamera's table (editcamera.c), so the level's own name for it, "Filter color", must be the one read.
/// </summary>
public sealed class CutsceneFilterColourTests
{
    /// <summary>Every shot of every level that holds one colour to the next shot draws in that colour.</summary>
    [RequiresGameDataFact]
    public void Shots_draw_in_their_authored_filter_colour()
    {
        int coloured = 0;
        List<string> failures = [];
        for (int mission = 1; mission <= 14; mission++)
        {
            (LevelTask root, CutsceneRuntime runtime) = Load(mission);
            foreach (CutSceneTask scene in root.Descendants().OfType<CutSceneTask>())
            {
                DeclaredTask[] cameras = [.. scene.Descendants().OfType<DeclaredTask>().Where(static t => t.Type.Name == "EditCamera")];
                double start = 0;
                for (int i = 0; i < cameras.Length; i++)
                {
                    double length = Math.Max(0f, DeclaredTaskValues.RealStrict(cameras[i], "Duration")) * scene.TimeScale;
                    Vector3f colour = DeclaredTaskValues.Vector32(cameras[i], "Filter color");
                    bool held = i + 1 < cameras.Length && DeclaredTaskValues.Vector32(cameras[i + 1], "Filter color") == colour;
                    if (colour != default && held && length > 0.2)
                    {
                        CutsceneFrame? frame = runtime.EvaluateSceneFrame(scene, start + (length * 0.5));
                        if (frame?.ShotIndex == i)
                        {
                            coloured++;
                            if (frame.FilterColor != colour)
                            {
                                failures.Add($"mission {mission} scene {scene.Id} shot {i}: {frame.FilterColor} drawn, {colour} authored");
                            }
                        }
                    }

                    start += length;
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join('\n', failures));
        Assert.True(coloured > 10, $"Only {coloured} coloured shots were checked.");
    }

    private static (LevelTask Root, CutsceneRuntime Runtime) Load(int mission)
    {
        VirtualFileSystem fs = new();
        fs.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
        ResourceRegistry resources = new(fs);
        GameLoop loop = new(new ManualTimeSource());
        PlayerTaskTypes.RegisterAll(loop.Types);
        loop.Types.Register<CutSceneTask>(CutSceneTask.TypeName);
        OpenIGI.Game.Flow.Igi1LevelTaskTypes.Register(loop.Types);
        LevelTask root = (LevelTask)loop.Tasks.New(loop.Tasks.Root, loop.Types.Get(EngineTaskTypes.Level));
        MissionRegistry missions = new();
        missions.Register(loop.Natives);
        missions.Scan(fs, resources, loop.Natives);
        _ = new MissionLoader(missions, fs, resources, new LevelLoader(root, loop.TaskCommands, loop.Natives, resources)).Load(mission);
        new ConditionalContainerGate(loop.Variables).Prepare(root);
        return (root, new CutsceneRuntime(root, loop.Variables));
    }
}

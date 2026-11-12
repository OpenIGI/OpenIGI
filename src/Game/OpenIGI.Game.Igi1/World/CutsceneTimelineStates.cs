using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.World;

/// <summary>One CutScene task and its flattened EditCamera knots, with the link and target poses its shots latched.</summary>
internal sealed class CutsceneSceneState(CutSceneTask task, List<EditCameraShot> shots)
{
    public CutSceneTask Task { get; } = task;
    public List<EditCameraShot> Shots { get; } = shots;
    public int LastShotIndex { get; set; } = -1;

    /// <summary>The tick this tick's frame is set from -- the count before its increment; null before the first.</summary>
    public int? FrameTicks { get; set; }

    public Vector3d? LinkedPosition { get; set; }
    public Matrix3f? LinkedOrientation { get; set; }
    public Matrix3f? TargetOrientation { get; set; }
    public double TotalTime(float scale) => Shots.Sum(shot => Math.Max(0f, shot.Duration * scale));
    public List<CutsceneTimedShot> Timed(float scale)
    {
        List<CutsceneTimedShot> result = [];
        double time = 0;
        foreach (EditCameraShot shot in Shots)
        {
            result.Add(new CutsceneTimedShot(shot, time));
            time += Math.Max(0f, shot.Duration * scale);
        }
        return result;
    }
}

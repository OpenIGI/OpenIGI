using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>One CutScene task and its flattened EditCamera knots, with the link and target poses its shots latched.</summary>
internal sealed class CutsceneSceneState(CutSceneTask task, List<EditCameraShot> shots)
{
    public CutSceneTask Task { get; } = task;
    public List<EditCameraShot> Shots { get; private set; } = shots;

    /// <summary>Gets the knots as the shared camera path reads them, made with the scene and again with its shots.</summary>
    public List<CutsceneKey> Keys { get; private set; } = [.. shots.Select(static s => s.Key)];

    /// <summary>Takes the scene's shots again, read from its cameras after an editor changed them in a level that is running.</summary>
    /// <param name="shots">The shots.</param>
    public void ReplaceShots(List<EditCameraShot> shots)
    {
        Shots = shots;
        Keys = [.. shots.Select(static s => s.Key)];
    }
    public int LastShotIndex { get; set; } = -1;

    /// <summary>The tick this tick's frame is set from -- the count before its increment; null before the first.</summary>
    public int? FrameTicks { get; set; }

    /// <summary>
    /// Gets or sets the space the shown frame's key was carried to the world through: its link's pose as loaded and as taken for
    /// the frame, or the world for a key that rides nothing; null before a frame was built.
    /// </summary>
    public CutsceneShotSpace? ShotSpace { get; set; }

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

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

/// <summary>One LevelTimer task: its expressions and the run byte and tick count it exports.</summary>
internal sealed record CutsceneTimerState(DeclaredTask Task, string Run, string Reset, bool InitialRun)
{
    public bool IsRun { get; set; } = InitialRun;
    public int Tick { get; set; }
}

/// <summary>One AnimTask: its target, its place in the walk and the AnimData programme it plays.</summary>
internal sealed record CutsceneAnimationState(
    DeclaredTask Task,
    int TargetId,
    int Order,
    string Run,
    bool InitialRun,
    AnimDataProgram Program)
{
    /// <summary>Gets or sets the run expression; settable so a suite can make every AnimTask run.</summary>
    public string Run { get; set; } = Run;

    /// <summary>Gets or sets the initial run byte; settable so a suite can make every AnimTask run.</summary>
    public bool InitialRun { get; set; } = InitialRun;

    /// <summary>Running-tick counter exported as <c>AnimTask+&lt;id&gt;.nTick</c> (retail AnimTask state var).</summary>
    public int Tick { get; set; }

    /// <summary>Gets or sets whether the task drove its target on the current tick.</summary>
    public bool RanThisTick { get; set; }

    /// <summary>Gets or sets the run byte (<c>+131316</c>), exported as <c>isRun</c>; it starts at Initial run.</summary>
    public bool IsRun { get; set; } = InitialRun;
}

/// <summary>One ConditionalSound task and the edges its condition has produced.</summary>
internal sealed record CutsceneSoundState(
    DeclaredTask Task,
    string Condition,
    string Sound,
    Vector3d Position,
    bool Simple,
    bool OneShot,
    bool RelativeToMicrophone)
{
    public bool IsRun { get; set; }
    public bool HasPlayed { get; set; }
}

/// <summary>One EditCamera knot at the scene time it starts.</summary>
internal sealed record CutsceneTimedShot(EditCameraShot Shot, double Start);

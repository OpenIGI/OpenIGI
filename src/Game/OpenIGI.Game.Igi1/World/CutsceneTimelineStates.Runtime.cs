using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;

namespace OpenIGI.Game.World;

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

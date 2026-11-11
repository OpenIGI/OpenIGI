using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Level;

namespace OpenIGI.Game.World;

/// <summary>
/// A task of one of the Project I.G.I. types the cutscene runtime keeps the state of: the animations that move a level's
/// objects and the sounds its story plays.
/// </summary>
/// <remarks>
/// Each has a run handler of its own in the original; <see cref="CutsceneRuntime"/> holds what they share -- the animation
/// packets, the sound sink -- and each task runs its part where the level's walk reaches it. One the runtime does not keep,
/// or a level with no runtime, is walked as a plain container is.
/// </remarks>
public abstract class CutsceneRuntimeTask : DeclaredLevelTask
{
    /// <summary>Gets or sets the level's cutscene runtime, which steps this task; none until it is built.</summary>
    internal CutsceneRuntime? Cutscenes { get; set; }

    /// <inheritdoc/>
    public sealed override void Run(LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        if (Cutscenes is not { } cutscenes || !cutscenes.RunsIn(level) || !Run(cutscenes))
        {
            level.RunChildren(this);
        }
    }

    /// <summary>Runs the type's own handler.</summary>
    /// <param name="cutscenes">The level's cutscene runtime.</param>
    /// <returns>Whether the runtime keeps this task and stepped it.</returns>
    private protected abstract bool Run(CutsceneRuntime cutscenes);
}

/// <summary>An animation played on another task: Project I.G.I.'s <c>AnimTask</c> (run handler <c>0x4F3B40</c>).</summary>
public sealed class AnimTaskTask : CutsceneRuntimeTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "AnimTask";

    /// <summary>Gets or sets the animation as it plays, or <see langword="null"/> for a task the level gave no keys.</summary>
    internal CutsceneAnimationState? Playing { get; set; }

    /// <inheritdoc/>
    private protected override bool Run(CutsceneRuntime cutscenes)
    {
        if (Playing is not { } playing)
        {
            return false;
        }

        cutscenes.RunAnimation(playing);
        return true;
    }

    /// <summary>Gets the id of the task the animation moves ("TargetID"), or -1 where the level gives none.</summary>
    public int TargetId => DeclaredTaskValues.IntStrict(this, "TargetID", DeclaredTaskValues.IntStrict(this, "Target ID", -1));

    /// <summary>Gets the expression that says whether the animation runs ("Run").</summary>
    public string RunExpression => DeclaredTaskValues.TextOrEmpty(this, "Run");

    /// <summary>Gets a value indicating whether the animation runs from the start of the level ("Initial run").</summary>
    public bool InitialRun => DeclaredTaskValues.BoolStrict(this, "Initial run");

    /// <summary>Gets the keys of the animation ("AnimData"), or <see langword="null"/> where the level gives none.</summary>
    public IReadOnlyList<AnimDataEntry>? AnimData =>
        TryGetValue("AnimData", out object? data) ? data as IReadOnlyList<AnimDataEntry> : null;
}

/// <summary>A sound that plays while its script holds: Project I.G.I.'s <c>ConditionalSound</c>.</summary>
public sealed class ConditionalSoundTask : CutsceneRuntimeTask
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "ConditionalSound";

    /// <summary>Gets or sets the sound as it plays, or <see langword="null"/> until a runtime keeps it.</summary>
    internal CutsceneSoundState? Playing { get; set; }

    /// <inheritdoc/>
    private protected override bool Run(CutsceneRuntime cutscenes)
    {
        if (Playing is not { } playing)
        {
            return false;
        }

        cutscenes.RunSound(playing);
        return true;
    }

    /// <summary>Gets the expression that says whether the sound plays (the parameter named for its condition).</summary>
    public string Condition => DeclaredTaskValues.FirstTextNamed(this, "condition");

    /// <summary>Gets the definition played (the parameter named for its sound).</summary>
    public string Sound => DeclaredTaskValues.FirstTextNamed(this, "sound");

    /// <summary>Gets where the sound is heard from ("Position").</summary>
    public OpenIGI.Core.Numerics.Vector3d Position => DeclaredTaskValues.Vector(this, "Position");

    /// <summary>Gets a value indicating whether the sound is played without a place in the world ("Simple").</summary>
    public bool IsSimple => DeclaredTaskValues.BoolStrict(this, "Simple");

    /// <summary>Gets a value indicating whether the sound plays once ("OneShot").</summary>
    public bool IsOneShot => DeclaredTaskValues.BoolStrict(this, "OneShot");

    /// <summary>Gets a value indicating whether its place is measured from the ear ("Relative to microphone").</summary>
    public bool IsRelativeToMicrophone => DeclaredTaskValues.BoolStrict(this, "Relative to microphone");
}

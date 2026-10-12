using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Weapons;

namespace OpenIGI.Game.Igi2.Interaction;

/// <summary>
/// A minefield, I.G.I. 2's <c>MineField</c> (minefield.c): when its <c>Explode</c> expression holds, a blast goes off at the task it is
/// told to explode close to, and again after its delay between explosions.
/// </summary>
/// <remarks>
/// <para>
/// <c>MineField_RunHandler</c>: the pause left counts down; at nought, the expression is run, and if it holds the field explodes and the
/// pause is set to the delay between explosions in ticks. A delay of -1 makes the pause -30, which never comes back to nought: it
/// explodes once. A pause of exactly -1 stops it.
/// </para>
/// <para>
/// <c>MineField_Explode</c>: nothing goes off at a dead human; elsewhere the target's camera-target position, snapped to the ground
/// under or over it when asked, takes an explosion (<c>Explosion0_New</c>): once, after its delay, with its damage scale over its
/// radius and falloff, a burn of 1.5 metres, its fragments (<c>1009_01_1</c>) and fireballs and sound, no AI event, the minefield as its
/// owner, falling off in a straight line.
/// </para>
/// </remarks>
public sealed partial class Igi2MineFieldTask : Igi2Task
{
    /// <summary>The script name level data instantiates this type under.</summary>
    public const string TypeName = "MineField";

    /// <summary>The model its fragments are thrown as.</summary>
    public const string FragmentModel = "1009_01_1";

    private Igi2LevelExpression? _explode;

    /// <summary>Gets or sets where the editor shows it.</summary>
    [ScriptParam(ScriptParamType.ObjectPos, "Position")]
    public Vector3d Position { get; set; }

    /// <summary>Gets or sets the expression it explodes under.</summary>
    [ScriptParam(ScriptParamType.VarString, "Explode")]
    public string ExplodeExpression { get; set; } = string.Empty;

    /// <summary>Gets or sets how far the full damage reaches, in metres.</summary>
    [ScriptParam(ScriptParamType.Real32, "Explosion radius (meter)")]
    public float Radius { get; set; } = 3f;

    /// <summary>Gets or sets how far past that it falls away, in metres.</summary>
    [ScriptParam(ScriptParamType.Real32, "Explosion falloff radius (meter)")]
    public float FalloffRadius { get; set; } = 1.5f;

    /// <summary>Gets or sets the damage at its heart.</summary>
    [ScriptParam(ScriptParamType.Real32, "Explosion damage scale")]
    public float DamageScale { get; set; } = 3f;

    /// <summary>Gets or sets the seconds before a blast goes off.</summary>
    [ScriptParam(ScriptParamType.Real32, "Explosion delay (seconds)")]
    public float Delay { get; set; }

    /// <summary>Gets or sets how many fragments a blast throws.</summary>
    [ScriptParam(ScriptParamType.Int32, "Explosion fragments")]
    public int Fragments { get; set; } = 3;

    /// <summary>Gets or sets how many fireballs a blast throws.</summary>
    [ScriptParam(ScriptParamType.Int32, "Explosion fireballs")]
    public int Fireballs { get; set; } = 3;

    /// <summary>Gets or sets a blast's sound.</summary>
    [ScriptParam(ScriptParamType.String16, "Explosion sound")]
    public string Sound { get; set; } = string.Empty;

    /// <summary>Gets or sets the task a blast goes off at.</summary>
    [ScriptParam(ScriptParamType.Int32, "Explode close to task ID")]
    public int TargetId { get; set; }

    /// <summary>Gets or sets the seconds between blasts; -1 for one only.</summary>
    [ScriptParam(ScriptParamType.Real32, "Explosion delay between explosions (seconds) -1: explode once")]
    public float DelayBetween { get; set; } = 5f;

    /// <summary>Gets or sets a value indicating whether a blast is put on the ground.</summary>
    [ScriptParam(ScriptParamType.Bool8, "Snap explosion to ground")]
    public bool SnapToGround { get; set; } = true;

    /// <summary>Gets the ticks before it may explode again, <c>nDelayBetweenExplosions</c>.</summary>
    public int Pause { get; private set; }

    /// <summary>Gets or sets the blasts it sets off.</summary>
    public Igi2ExplosionWorld? Explosions { get; set; }

    /// <summary>Gets or sets the world its blasts are put on the ground of.</summary>
    public Igi2CollisionWorld? Collision { get; set; }

    /// <summary>Gets or sets the level's tasks by id, as <c>Level_atLevelQTaskID</c> holds them.</summary>
    public Func<int, object?> TaskById { get; set; } = static _ => null;

    /// <inheritdoc/>
    public override void Run(Igi2LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        if (Pause == -1)
        {
            return;
        }

        if (Pause != 0)
        {
            Pause--;
            return;
        }

        _explode ??= new Igi2LevelExpression(ExplodeExpression, Name, level.Natives);
        if (!_explode.IsEmpty && level.Evaluate(_explode) != 0.0)
        {
            Explode();
            Pause = Fpu.ToInt32((Fpu)DelayBetween * 30.0);
        }
    }

    /// <summary>Sets off a blast at its target, <c>MineField_Explode</c>.</summary>
    public void Explode()
    {
        if ((uint)TargetId >= 0x1000u || TaskById(TargetId) is not { } target || target is Igi2Human { IsDead: true })
        {
            return;
        }

        if (TargetPosition(target) is not { } at)
        {
            return;
        }

        if (SnapToGround)
        {
            at = Ground(at, target);
        }

        Explosions?.New(
            at,
            1,
            Fpu.ToInt32((Fpu)Delay * 30.0),
            DamageScale,
            ((Fpu)Radius * 4096.0).F,
            ((Fpu)FalloffRadius * 4096.0).F,
            6144f,
            Fragments,
            Fireballs,
            null,
            FragmentModel,
            Sound,
            -1,
            0,
            307200f,
            null,
            null,
            Igi2ExplosionFalloff.Linear);
    }

    /// <summary>Where a task stands for a camera, as it answers <c>GetCameraTargetInfo</c>.</summary>
    private static Vector3d? TargetPosition(object target) => target switch
    {
        Flow.IIgi2CameraTarget camera => camera.CameraTargetPosition,
        Igi2Human human => human.Position,
        GameTask task when task.TryGetScriptValue("Position", out object? value) && value is Vector3d position => position,
        _ => null,
    };

    /// <summary>
    /// The ground under a point, or else over it, ignoring the target (<c>GameFunctions_SnapToGround</c>): four hundred metres down,
    /// and if nothing is struck, four hundred up.
    /// </summary>
    private Vector3d Ground(Vector3d at, object target)
    {
        if (Collision is not { } world)
        {
            return at;
        }

        Igi2LineTrace trace = new(1) { Includes = owner => !ReferenceEquals(owner, target) };
        foreach (double reach in (double[])[-1638400.0, 1638400.0])
        {
            trace.Starts[0] = at;
            world.NextZ(trace, [reach], 0xFFFFFFFFu);
            if (trace.Intersections[0].IsHit)
            {
                return new Vector3d(at.X, at.Y, trace.Intersections[0].Position.Z);
            }
        }

        return at;
    }
}

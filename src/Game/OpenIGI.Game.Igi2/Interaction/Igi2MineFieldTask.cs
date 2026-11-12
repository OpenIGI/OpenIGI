using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Game.Physics;

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
public sealed partial class Igi2MineFieldTask : OpenIGI.Game.World.MineFieldTask
{
    /// <summary>Gets or sets the blasts it sets off.</summary>
    [LevelService]
    public Igi2ExplosionWorld? Explosions { get; set; }

    /// <summary>Gets or sets the world its blasts are put on the ground of.</summary>
    public Igi2CollisionWorld? Collision { get; set; }

    /// <summary>Gets or sets the level's tasks by id, as <c>Level_atLevelQTaskID</c> holds them.</summary>
    public Func<int, object?> TaskById { get; set; } = static _ => null;

    /// <inheritdoc/>
    /// <remarks>The sequel's build multiplies in extended precision before it cuts.</remarks>
    protected override int DelayBetweenTicks => Fpu.ToInt32((Fpu)DelayBetween * 30.0);

    /// <summary>Sets off a blast at its target, <c>MineField_Explode</c>.</summary>
    public override void Explode()
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

        WorldLineTrace trace = new(1) { Includes = owner => !ReferenceEquals(owner, target) };
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

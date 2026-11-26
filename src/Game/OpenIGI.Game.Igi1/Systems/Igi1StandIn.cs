using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;

namespace OpenIGI.Game.Igi1.Systems;

/// <summary>
/// One of Project I.G.I.'s soldiers as another game's level finds it: where it stands and how it is seen, the events it hears,
/// and the rounds and blasts that strike it, their damage scaled from the level's game to this one's.
/// </summary>
/// <remarks>
/// It stands in for the human the level placed (<see cref="IHostedHuman.StandIn"/>), takes the hits the soldier's collider meets
/// in the level's world (<see cref="World.Igi1HumanLineCollider.Handler"/>), and is joined to the level's blasts, AI events and
/// detectors (<see cref="Game.World.LevelJoiners"/>). The soldier itself answers each, as its own game's level finds it.
/// Through the level's cutscenes it stands and is posed as a body of the level's game is (<see cref="IHostPosedHuman"/>).
/// </remarks>
/// <param name="soldier">The soldier.</param>
/// <param name="damageScale">What the level's rounds' and blasts' damage is multiplied by as it strikes the soldier.</param>
public sealed class Igi1StandIn(HumanSoldierTask soldier, float damageScale) :
    IAiHuman, IAiDetectable, IWeaponHitTarget, IExplosionHitTarget, IHumanAiEventTarget, IHostPosedHuman
{
    // The level's rig the last pose came on, its bones matched to the soldier's; and the soldier's own pose as it was lent, for
    // the bones the level's rig lacks.
    private BoneSkeleton? _rig;
    private int[] _match = [];
    private BonePose? _own;

    /// <summary>Gets the soldier.</summary>
    public HumanSoldierTask Soldier { get; } = soldier ?? throw new ArgumentNullException(nameof(soldier));

    /// <inheritdoc/>
    public Vector3d Position => ((IAiHuman)Soldier).Position;

    /// <inheritdoc/>
    public int Team => Soldier.Team;

    /// <inheritdoc/>
    public bool IsDead => Soldier.IsDead;

    /// <inheritdoc/>
    public Vector3d OriginalViewPosition => Soldier.OriginalViewPosition;

    /// <inheritdoc/>
    public Vector3d DetectablePosition => ((IAiDetectable)Soldier).DetectablePosition;

    /// <inheritdoc/>
    public float DetectableRadius => ((IAiDetectable)Soldier).DetectableRadius;

    /// <inheritdoc/>
    public float? VisibilityFactor => ((IAiDetectable)Soldier).VisibilityFactor;

    /// <inheritdoc/>
    public bool IsGone => ((IAiDetectable)Soldier).IsGone;

    /// <inheritdoc/>
    public AiDetectionInfo DetectionInfo() => Soldier.DetectionInfo();

    /// <inheritdoc/>
    public void WeaponHit(in WeaponHit hit) => Soldier.WeaponHit(hit with { Damage = hit.Damage * damageScale });

    /// <inheritdoc/>
    public void ExplosionHit(in ExplosionHit hit) => Soldier.ExplosionHit(hit with { Damage = hit.Damage * damageScale });

    /// <inheritdoc/>
    public void SendEvent(in HumanAiEvent aiEvent) => Soldier.SendEvent(aiEvent);

    /// <summary>Gets or sets the soldiers' simulation, which runs the soldier and lends it to the level's body; <see langword="null"/> for none.</summary>
    public SoldierSimulation? Simulation { get; init; }

    /// <inheritdoc/>
    public float Facing => View() is { } view ? SoldierPuppet.FacingOf(view.Orientation) : 0f;

    /// <inheritdoc/>
    /// <remarks>The pose is carried onto the soldier's skeleton bone by bone (<see cref="Igi2ClipRetarget.ToPose"/>).</remarks>
    public void Follow(in HostPose pose)
    {
        if (Simulation is not { } simulation || View() is not { } view)
        {
            return;
        }

        if (!simulation.IsLent(view))
        {
            _own = view.UnrotatedPose();
        }

        if (!ReferenceEquals(_rig, pose.Rig))
        {
            _rig = pose.Rig;
            _match = Igi2ClipRetarget.Match(pose.Rig, view.Bones.Skeleton);
        }

        BonePose own = _own ?? view.UnrotatedPose();
        simulation.Lend(view, pose.Position, pose.Orientation, Igi2ClipRetarget.ToPose(pose.Pose, _match, view.Bones.Skeleton, own));
    }

    /// <inheritdoc/>
    public void Release(Vector3d position, Matrix3f orientation)
    {
        if (Simulation is { } simulation && View() is { } view)
        {
            simulation.EndLending(view, position, orientation);
        }

        _own = null;
    }

    private SoldierView? View() => Simulation?.ViewOf(Soldier);
}

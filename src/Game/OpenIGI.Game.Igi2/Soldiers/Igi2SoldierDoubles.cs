using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Igi2.Ai;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Player;

namespace OpenIGI.Game.Igi2.Soldiers;

/// <summary>
/// The soldiers another game's stand for, run by this game where its level directs them: while a cutscene runs, each such soldier's double
/// (<see cref="Igi2HumanSoldierTask.Double"/>) runs as the level's own soldier would -- its mind directed by the scripts, its body
/// walked and animated by its clips -- and the other game's soldier stands where it stands and as it is posed
/// (<see cref="IHostPosedHuman"/>). When the cutscene ends the other game's soldier goes on from there with its own mind.
/// </summary>
/// <remarks>
/// <para>
/// A soldier whose graph rides a moving object -- a boat, a lorry, an aircraft -- is its double's throughout: the other game's
/// graphs stand still, and its own mind would walk it off the object onto nodes left where the object first stood.
/// </para>
/// <para>
/// A double starts each run where the soldier it doubles stands, turned as it is turned. One whose soldier has died runs no
/// more. The doubles run as each tick ends, after the level's own humans, in the level's order.
/// </para>
/// </remarks>
public sealed class Igi2SoldierDoubles
{
    private readonly Igi2LevelRun _level;
    private readonly Igi2HumanSoldierTask[] _soldiers;
    private readonly bool[] _always;
    private readonly bool[] _following;
    private readonly Dictionary<Igi2BoneHierarchy, BoneSkeleton> _rigs = new(ReferenceEqualityComparer.Instance);
    private bool _running;

    private Igi2SoldierDoubles(Igi2LevelRun level, Igi2HumanSoldierTask[] soldiers, bool[] always)
    {
        _level = level;
        _soldiers = soldiers;
        _always = always;
        _following = new bool[soldiers.Length];
    }

    /// <summary>Has the level run the doubles of its soldiers another game's stand for, as each tick ends.</summary>
    /// <param name="level">The level's run.</param>
    /// <param name="root">The level.</param>
    /// <param name="ridesMover">Whether a soldier's graph rides a moving object, which keeps it its double's throughout.</param>
    /// <returns>The doubles, or <see langword="null"/> where the level has none.</returns>
    public static Igi2SoldierDoubles? Attach(Igi2LevelRun level, GameTask root, Func<Igi2HumanSoldierTask, bool> ridesMover)
    {
        ArgumentNullException.ThrowIfNull(level);
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(ridesMover);
        Igi2HumanSoldierTask[] soldiers = [.. root.Descendants().OfType<Igi2HumanSoldierTask>().Where(static t => t.Double is not null)];
        if (soldiers.Length == 0)
        {
            return null;
        }

        Igi2SoldierDoubles doubles = new(level, soldiers, [.. soldiers.Select(ridesMover)]);
        level.AddTickEnd(doubles.Tick);
        return doubles;
    }

    /// <summary>Gets whether a cutscene ran the last tick, and with it every double.</summary>
    public bool IsCutsceneRunning => _running;

    /// <summary>Runs the doubles a tick where they run, and hands each soldier back as its double stops.</summary>
    public void Tick()
    {
        bool cutscene = _level.IsCutsceneRunning;
        for (int i = 0; i < _soldiers.Length; i++)
        {
            Igi2HumanSoldierTask task = _soldiers[i];
            if (task.Double is not { } body || task.StandIn is not IHostPosedHuman soldier || soldier.IsDead)
            {
                continue;
            }

            if (cutscene || _always[i])
            {
                if (!_following[i])
                {
                    StandAt(body, soldier.Position, soldier.Facing);
                    _following[i] = true;
                }

                body.RunDelayed();
                if (body.BodyPose() is { } pose && body.Animation is Igi2HumanAnimation { Rig: { } rig })
                {
                    soldier.Follow(new HostPose(body.Position, body.Orientation, pose.Pose, Skeleton(rig)));
                }
            }
            else if (_following[i])
            {
                soldier.Release(body.Position, body.Orientation);
                _following[i] = false;
            }
        }

        _running = cutscene;
    }

    // A double stood where its soldier stands, turned as it is, before the cutscene runs it.
    private static void StandAt(Igi2HumanSoldier body, Vector3d position, float facing)
    {
        body.Position = position;
        body.Gamma = facing;
        body.IdealGamma = facing;
        float[] m = EngineEuler.Rotation(0f, 0f, facing);
        body.Orientation = new Matrix3f(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8]);
        body.TouchPose();
    }

    private BoneSkeleton Skeleton(Igi2BoneHierarchy rig)
    {
        if (!_rigs.TryGetValue(rig, out BoneSkeleton? skeleton))
        {
            skeleton = rig.CreateSkeleton();
            _rigs[rig] = skeleton;
        }

        return skeleton;
    }
}

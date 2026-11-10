using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Ai;
using OpenIGI.Game.Ai;

namespace OpenIGI.Game.Igi1.Ai;

/// <summary>
/// What a mod's behaviour may have one guard do, handed to it as it is made (<see cref="SoldierSimulation.AttachGuardBehaviour"/>).
/// Each is something the game's own code already does to him -- his script, his fight, his patrol -- so a behaviour drives him
/// as the game would; and each keeps him whole as it does it. <b>Not retail.</b>
/// </summary>
public sealed class GuardControl
{
    private readonly Igi1HumanAi _guard;

    internal GuardControl(Igi1HumanAi guard) => _guard = guard;

    /// <summary>Gets the guard, to read: where he is, what he knows, what he is doing.</summary>
    public Igi1HumanAi Guard => _guard;

    /// <summary>Gets the route he is walking, waypoint by waypoint.</summary>
    public IReadOnlyList<AiWaypoint> Waypoints => _guard.Waypoints;

    /// <summary>Gets the nav graph he walks, or <see langword="null"/>.</summary>
    public GraphFile? Graph => _guard.Graph;

    /// <summary>Gets which graph nodes his side's soldiers have claimed, or <see langword="null"/>.</summary>
    public AiNodeOccupancy? Occupancy => _guard.Occupancy;

    /// <summary>Gets the humans offered to his eyes this tick: who he could see, before his sweep.</summary>
    public IReadOnlyList<AiDetectionCandidate> OfferedHumans => _guard.OfferedHumans;

    /// <summary>Gets the name of the action of his script he is running, or <see langword="null"/>.</summary>
    public string? RunningActionName => _guard.RunningActionName;

    /// <summary>Gets whether a move of the patrol fight's own is under way: lying prone, a one-shot (a grenade, a blow) or a roll.</summary>
    public bool PatrolMoveUnderWay => _guard.PatrolMoveUnderWay;

    /// <summary>Gets or sets the point he looks at until he gets there or is told otherwise, or <see langword="null"/> for none.</summary>
    public Vector3d? LookingAt
    {
        get => _guard.LookingAt;
        set => _guard.LookingAt = value;
    }

    /// <summary>Gets whether an action of his script, by its name, is the one running.</summary>
    /// <param name="name">The action's name.</param>
    /// <returns>Whether it runs.</returns>
    public bool IsRunning(string name) => _guard.IsActionActive(name);

    /// <summary>Gets the graph node nearest a point, or -1 with no graph.</summary>
    /// <param name="point">The point, in world units.</param>
    /// <returns>The node.</returns>
    public int NodeNearest(Vector3d point) => _guard.NearestNode(point);

    /// <summary>Draws from his own stream of chance, so a behaviour's choices for him replay as the game's do.</summary>
    /// <param name="bound">One past the largest value wanted.</param>
    /// <returns>A value from nought to one less than <paramref name="bound"/>.</returns>
    public int NextRandom(int bound) => _guard.NextBounded(bound);

    /// <summary>Gets whether he could see from one point to another with nothing in between: his own line of sight.</summary>
    /// <param name="from">From, in world units.</param>
    /// <param name="to">To, in world units.</param>
    /// <param name="seen">The human looked for, whom the line steps past (his own cover, a gun he mans), or <see langword="null"/>.</param>
    /// <returns>Whether the line is clear; true when he has no way to tell.</returns>
    public bool CanSeeBetween(Vector3d from, Vector3d to, int? seen = null) => _guard.CanSeeBetween(from, to, seen);

    /// <summary>Gets whether a point lies in the narrow cone a soldier on the move fires within.</summary>
    /// <param name="point">The point, in world units.</param>
    /// <returns>Whether it does.</returns>
    public bool FacesForMovingFire(Vector3d point) => _guard.FacesForFollowFire(point);

    /// <summary>Applies actions as his script's answer to an event at a point: a behaviour's own reason to go somewhere or look at something.</summary>
    /// <param name="eventPosition">Where the event was, in world units.</param>
    /// <param name="actions">The actions, as his script gives them.</param>
    public void ApplyActionsAt(Vector3d eventPosition, IReadOnlyList<AiProducedAction> actions) => _guard.ApplyActionsAt(eventPosition, actions);

    /// <summary>Has him run, or walk, to an event's position from now, as a script's run flag does.</summary>
    /// <param name="run">Whether he runs.</param>
    public void SetEventGait(bool run) => _guard.SetEventGait(run);

    /// <summary>Stands him idle where he is with no animation asked for: a pause of a behaviour's own.</summary>
    public void HoldStill() => _guard.HoldStill();

    /// <summary>Turns him toward a point by his own turn rate.</summary>
    /// <param name="point">The point, in world units.</param>
    /// <param name="tolerance">How near, in radians, counts as facing it.</param>
    /// <param name="level">Whether he turns about the vertical alone, his view kept level.</param>
    /// <returns>Whether he faces it within the tolerance.</returns>
    public bool TurnToward(Vector3d point, float tolerance, bool level = false) => _guard.TurnTowards(point, tolerance, level);

    /// <summary>Stops him where he is: his route and his destination dropped.</summary>
    public void StopRoute() => _guard.StopRoute();

    /// <summary>Steps him along his route at a fight's tense run this tick, as the game's own fight moves him.</summary>
    public void RunAlongRoute() => _guard.RunAlongRoute();

    /// <summary>Writes one of his combat channels for this tick, as his fight does: a stance, a lean, the trigger.</summary>
    /// <param name="channel">The channel.</param>
    /// <param name="value">Its value.</param>
    public void SetCombatChannel(AiCombatChannel channel, float value) => _guard.WriteChannel(channel, value);

    /// <summary>Holds his posture where it is this tick, as his fight does between moves.</summary>
    public void KeepPosture() => _guard.HoldPosture();

    /// <summary>Clears what his fight asked of his body this tick: no channel, no trigger, no turn.</summary>
    public void ClearFightCommand() => _guard.ClearFightCommand();

    /// <summary>Ends his fight, as the game's own does when it gives up on the enemy.</summary>
    public void EndFight() => _guard.EndCombat();

    /// <summary>Fires a burst at a point this tick, as his fight fires at an enemy he can see.</summary>
    /// <param name="point">Where he aims, in world units.</param>
    public void FireBurstAt(Vector3d point) => _guard.FireBurstAt(point);

    /// <summary>Runs his own fire at his target this tick: aim, bursts and reloads, as his fight fires.</summary>
    /// <param name="enemy">Where the enemy is, in world units.</param>
    public void FireAtEnemy(Vector3d enemy) => _guard.FireAtEnemy(enemy);

    /// <summary>Pulls his trigger this tick at his target with the hit chance given, as a soldier on the move fires.</summary>
    /// <param name="hitProbability">The chance each round hits.</param>
    public void PullTrigger(float hitProbability) => _guard.PullTrigger(hitProbability);

    /// <summary>Sets the chance each round now asked of his gun hits.</summary>
    /// <param name="value">The chance.</param>
    public void SetHitProbability(float value) => _guard.SetHitProbability(value);

    /// <summary>Drops the burst under way, and whatever move of the game's own fight he had latched, so he stands ready.</summary>
    public void CancelBurst() => _guard.CancelBurst();

    /// <summary>Has a patrol throw his grenade, as his fight's own one-shot does: the game's fight runs it while it lasts (<see cref="PatrolMoveUnderWay"/>).</summary>
    /// <returns>Whether he will: false for a soldier with no patrol fight or no grenade.</returns>
    public bool ThrowGrenade() => _guard.ThrowGrenade();

    /// <summary>Drops a grenade throw asked for that has not finished.</summary>
    public void ForgetGrenadeThrow() => _guard.ForgetGrenadeThrow();
}

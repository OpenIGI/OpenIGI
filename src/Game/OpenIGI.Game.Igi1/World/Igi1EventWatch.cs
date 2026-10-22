using OpenIGI.Engine.Events;
using OpenIGI.Game.Events;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.World;

/// <summary>Tells a mission's <see cref="GameEvents"/> what changed in each tick it runs.</summary>
/// <remarks>
/// <para>
/// It reads the mission after its tick -- the player's health, the soldiers' death latches, the alarm controls and the
/// objectives -- and raises an event for each change, in that order, then <see cref="LevelTicked"/>. It only reads, and an
/// event nobody asked for is not worked out, so a mission plays the same with it as without.
/// </para>
/// <para>
/// A soldier counts once, as its task's death latch (<c>Human+3317</c>) rises, whoever drives it: the game's own soldiers and
/// those a mod hands to another game alike.
/// </para>
/// </remarks>
internal sealed class Igi1EventWatch
{
    private readonly MissionWorld _mission;
    private readonly GameEvents _events;
    private readonly HashSet<HumanSoldierTask> _dead = [];
    private readonly List<MissionObjectiveState> _objectives = [];
    private float _damage;
    private bool _playerDead;
    private bool _alarm;
    private int _ticks;

    /// <summary>Initializes a new instance of the <see cref="Igi1EventWatch"/> class, as the mission stands when it is brought up.</summary>
    /// <param name="mission">The mission.</param>
    /// <param name="events">Where the events go.</param>
    public Igi1EventWatch(MissionWorld mission, GameEvents events)
    {
        _mission = mission;
        _events = events;
        _damage = mission.PlayerHealth?.Damage ?? 0f;
        _playerDead = mission.IsPlayerDead;
        _alarm = mission.Soldiers?.AnyAlarmOn ?? false;

        // A soldier the level places dead has not died in play.
        if (_mission.LevelRoot is { } level)
        {
            _dead.UnionWith(level.Descendants().OfType<HumanSoldierTask>().Where(static soldier => soldier.IsDead));
        }
    }

    /// <summary>Raises the events of the tick that has just run.</summary>
    public void Observe()
    {
        _ticks++;
        ObservePlayer();
        ObserveSoldiers();
        ObserveAlarm();
        ObserveObjectives();
        if (_events.IsHeard<LevelTicked>())
        {
            _events.Raise(new LevelTicked(_ticks));
        }
    }

    private void ObservePlayer()
    {
        if (_mission.PlayerHealth is { } health)
        {
            float damage = health.Damage;
            if (damage > _damage && _events.IsHeard<PlayerDamaged>())
            {
                _events.Raise(new PlayerDamaged(damage - _damage, health.RemainingFraction));
            }

            _damage = damage;
        }

        if (_mission.IsPlayerDead && !_playerDead && _events.IsHeard<PlayerKilled>())
        {
            _events.Raise(new PlayerKilled());
        }

        _playerDead = _mission.IsPlayerDead;
    }

    private void ObserveSoldiers()
    {
        if (!_events.IsHeard<HumanKilled>() || _mission.LevelRoot is not { } level)
        {
            return;
        }

        foreach (HumanSoldierTask soldier in level.Descendants().OfType<HumanSoldierTask>())
        {
            if (soldier.IsDead && _dead.Add(soldier))
            {
                _events.Raise(new HumanKilled(soldier.Id, soldier.Team));
            }
            else if (!soldier.IsDead)
            {
                // A generator's guard lives again in the same task.
                _dead.Remove(soldier);
            }
        }
    }

    private void ObserveAlarm()
    {
        bool on = _mission.Soldiers?.AnyAlarmOn ?? false;
        if (on != _alarm && _events.IsHeard<AlarmChanged>())
        {
            _events.Raise(new AlarmChanged(on));
        }

        _alarm = on;
    }

    private void ObserveObjectives()
    {
        if (_mission.Objectives is not { } runtime)
        {
            return;
        }

        IReadOnlyList<MissionObjective> objectives = runtime.Objectives;
        for (int slot = 0; slot < objectives.Count; slot++)
        {
            MissionObjectiveState state = objectives[slot].State;
            if (slot >= _objectives.Count)
            {
                _objectives.Add(state);
                continue;
            }

            if (state != _objectives[slot])
            {
                _objectives[slot] = state;
                if (_events.IsHeard<ObjectiveChanged>())
                {
                    _events.Raise(new ObjectiveChanged(slot, state, objectives[slot].Text));
                }
            }
        }
    }
}

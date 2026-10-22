using OpenIGI.Engine.Events;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Events;
using OpenIGI.Game.Igi2.Ai;
using OpenIGI.Game.Igi2.Computer;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Interaction;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.Player;

/// <summary>Tells a level's <see cref="GameEvents"/> what changed in each tick it runs.</summary>
/// <remarks>
/// It reads the level after its tick -- the player's damage, the soldiers' death latches, the alarm controls and the
/// computer's objectives -- and raises an event for each change, in that order, then <see cref="LevelTicked"/>. It only
/// reads, and an event nobody asked for is not worked out, so a level plays the same with it as without.
/// </remarks>
internal sealed class Igi2EventWatch
{
    private readonly Igi2TitlePlayer _player;
    private readonly GameTask _level;
    private readonly GameEvents _events;
    private readonly HashSet<Igi2HumanSoldierTask> _dead = [];
    private readonly Igi2AlarmControlTask[] _alarms;
    private readonly List<MissionObjectiveState> _objectives = [];
    private float _damage;
    private bool _playerDead;
    private bool _alarm;
    private int _ticks;

    /// <summary>Initializes a new instance of the <see cref="Igi2EventWatch"/> class, as the level stands when it is brought up.</summary>
    /// <param name="player">The level's player.</param>
    /// <param name="level">The level's root.</param>
    /// <param name="events">Where the events go.</param>
    public Igi2EventWatch(Igi2TitlePlayer player, GameTask level, GameEvents events)
    {
        _player = player;
        _level = level;
        _events = events;
        _alarms = [.. level.Descendants().OfType<Igi2AlarmControlTask>()];
        _damage = player.Human.Damage.Damage;
        _playerDead = player.Human.IsDead;
        _alarm = AnyAlarm();

        // A soldier the level places dead has not died in play.
        _dead.UnionWith(level.Descendants().OfType<Igi2HumanSoldierTask>().Where(static soldier => soldier.Body is { IsDead: true }));
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
        Igi2HumanDamage health = _player.Human.Damage;
        float damage = health.Damage;
        if (damage > _damage && _events.IsHeard<PlayerDamaged>())
        {
            float remaining = health.MaxDamage > 0f ? Math.Clamp(1f - (damage / health.MaxDamage), 0f, 1f) : 0f;
            _events.Raise(new PlayerDamaged(damage - _damage, remaining));
        }

        _damage = damage;
        bool dead = _player.Human.IsDead;
        if (dead && !_playerDead && _events.IsHeard<PlayerKilled>())
        {
            _events.Raise(new PlayerKilled());
        }

        _playerDead = dead;
    }

    private void ObserveSoldiers()
    {
        if (!_events.IsHeard<HumanKilled>())
        {
            return;
        }

        foreach (Igi2HumanSoldierTask soldier in _level.Descendants().OfType<Igi2HumanSoldierTask>())
        {
            bool dead = soldier.Body is { IsDead: true };
            if (dead && _dead.Add(soldier))
            {
                _events.Raise(new HumanKilled(soldier.Id, soldier.Team));
            }
            else if (!dead)
            {
                _dead.Remove(soldier);
            }
        }
    }

    private void ObserveAlarm()
    {
        bool on = AnyAlarm();
        if (on != _alarm && _events.IsHeard<AlarmChanged>())
        {
            _events.Raise(new AlarmChanged(on));
        }

        _alarm = on;
    }

    private void ObserveObjectives()
    {
        if (_player.Computer?.Objectives is not { } defined)
        {
            return;
        }

        IReadOnlyList<Igi2ComputerObjective> objectives = defined.Objectives;
        for (int slot = 0; slot < objectives.Count; slot++)
        {
            Igi2ComputerObjective objective = objectives[slot];
            MissionObjectiveState state = objective.IsFailed ? MissionObjectiveState.Failed
                : objective.IsComplete ? MissionObjectiveState.Complete
                : MissionObjectiveState.Incomplete;
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
                    string text = _player.Strings?.Get(objective.TextResource) ?? objective.TextResource;
                    _events.Raise(new ObjectiveChanged(slot, state, text));
                }
            }
        }
    }

    private bool AnyAlarm()
    {
        foreach (Igi2AlarmControlTask alarm in _alarms)
        {
            if (alarm.IsAlarm)
            {
                return true;
            }
        }

        return false;
    }
}

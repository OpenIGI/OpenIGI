using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Ai;

namespace OpenIGI.Game.Igi1.Ai;

/// <summary>
/// The behaviours attached to one guard (<see cref="IGuardBehaviour"/>), asked as one: each concern's members in the order they
/// were attached, combined as each interface member says, and each call contained, so one that throws is logged and counts as
/// having said nothing. <b>Not retail.</b>
/// </summary>
/// <param name="logger">Where a behaviour that throws is reported.</param>
internal sealed class GuardBehaviourSet(IOpenIgiLogger logger)
{
    private const string Category = "Ai.Behaviour";

    private readonly List<(object Source, IGuardBehaviour Behaviour)> _attached = [];

    // Rebuilt on each change, so a behaviour that attaches or detaches one while it is asked changes the next call, not this one.
    private IGuardLife[] _life = [];
    private IGuardSenses[] _senses = [];
    private IGuardMovement[] _movement = [];
    private IGuardCombat[] _combat = [];

    /// <summary>Gets a value indicating whether any behaviour is attached.</summary>
    public bool Any => _attached.Count > 0;

    /// <summary>Gets a value indicating whether a behaviour has a say in his senses.</summary>
    public bool HasSenses => _senses.Length > 0;

    /// <summary>Gets a value indicating whether a behaviour has a say in his movement.</summary>
    public bool HasMovement => _movement.Length > 0;

    /// <summary>Attaches a behaviour, under what made it.</summary>
    /// <param name="source">What made it, which <see cref="Remove"/> lets go of it by.</param>
    /// <param name="behaviour">The behaviour.</param>
    public void Add(object source, IGuardBehaviour behaviour)
    {
        _attached.Add((source, behaviour));
        Rebuild();
    }

    /// <summary>Lets go of what a source attached.</summary>
    /// <param name="source">The source.</param>
    public void Remove(object source)
    {
        if (_attached.RemoveAll(entry => ReferenceEquals(entry.Source, source)) > 0)
        {
            Rebuild();
        }
    }

    /// <summary>Gets the first behaviour attached that is a <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">What is looked for.</typeparam>
    /// <returns>It, or <see langword="null"/>.</returns>
    public T? Find<T>()
        where T : class
    {
        foreach ((_, IGuardBehaviour behaviour) in _attached)
        {
            if (behaviour is T found)
            {
                return found;
            }
        }

        return null;
    }

    public bool AllowsIdleAnimation
    {
        get
        {
            foreach (IGuardLife life in _life)
            {
                if (Contained.Run(logger, Category, "A guard behaviour's AllowsIdleAnimation", life, static b => b.AllowsIdleAnimation, out bool allows) && !allows)
                {
                    return false;
                }
            }

            return true;
        }
    }

    public bool KeepsUnseenBody
    {
        get
        {
            foreach (IGuardLife life in _life)
            {
                if (Contained.Run(logger, Category, "A guard behaviour's KeepsUnseenBody", life, static b => b.KeepsUnseenBody, out bool keeps) && keeps)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public void TickStarted(int tick)
    {
        foreach (IGuardLife life in _life)
        {
            Contained.Run(logger, Category, "A guard behaviour's TickStarted", (life, tick), static b => b.life.TickStarted(b.tick));
        }
    }

    public void BeforeIdle()
    {
        foreach (IGuardLife life in _life)
        {
            Contained.Run(logger, Category, "A guard behaviour's BeforeIdle", life, static b => b.BeforeIdle());
        }
    }

    public void TickEnded()
    {
        foreach (IGuardLife life in _life)
        {
            Contained.Run(logger, Category, "A guard behaviour's TickEnded", life, static b => b.TickEnded());
        }
    }

    public void ActionsApplied(AiEvent? cause, IReadOnlyList<AiProducedAction> actions)
    {
        foreach (IGuardLife life in _life)
        {
            Contained.Run(logger, Category, "A guard behaviour's ActionsApplied", (life, cause, actions), static b => b.life.ActionsApplied(b.cause, b.actions));
        }
    }

    public Vector3d EyeOffset
    {
        get
        {
            Vector3d offset = default;
            foreach (IGuardSenses senses in _senses)
            {
                if (Contained.Run(logger, Category, "A guard behaviour's EyeOffset", senses, static b => b.EyeOffset, out Vector3d own))
                {
                    offset += own;
                }
            }

            return offset;
        }
    }

    public float EyeTurn
    {
        get
        {
            float turn = 0f;
            foreach (IGuardSenses senses in _senses)
            {
                if (Contained.Run(logger, Category, "A guard behaviour's EyeTurn", senses, static b => b.EyeTurn, out float own))
                {
                    turn += own;
                }
            }

            return turn;
        }
    }

    public bool? Stimulus(ref GuardStimulus stimulus)
    {
        foreach (IGuardSenses senses in _senses)
        {
            if (Contained.Run(
                logger,
                Category,
                "A guard behaviour's Stimulus",
                (senses, stimulus),
                static b =>
                {
                    GuardStimulus moved = b.stimulus;
                    bool? answer = b.senses.Stimulus(ref moved);
                    return (answer, moved);
                },
                out (bool? Answer, GuardStimulus Moved) said))
            {
                stimulus = said.Moved;
                if (said.Answer is { } answered)
                {
                    return answered;
                }
            }
        }

        return null;
    }

    public bool Sighting(GuardSighting sighting)
    {
        foreach (IGuardSenses senses in _senses)
        {
            if (Contained.Run(logger, Category, "A guard behaviour's Sighting", (senses, sighting), static b => b.senses.Sighting(in b.sighting), out bool seen))
            {
                sighting = sighting with { Visible = seen };
            }
        }

        return sighting.Visible;
    }

    public void ShapeStep(ref GuardStep step)
    {
        foreach (IGuardMovement movement in _movement)
        {
            if (Contained.Run(
                logger,
                Category,
                "A guard behaviour's ShapeStep",
                (movement, step),
                static b =>
                {
                    GuardStep shaped = b.step;
                    b.movement.ShapeStep(ref shaped);
                    return shaped;
                },
                out GuardStep shaped))
            {
                step = shaped;
            }
        }
    }

    public bool PassesWaypoint(AiWaypoint waypoint, int remaining)
    {
        foreach (IGuardMovement movement in _movement)
        {
            if (Contained.Run(logger, Category, "A guard behaviour's PassesWaypoint", (movement, waypoint, remaining), static b => b.movement.PassesWaypoint(b.waypoint, b.remaining), out bool passes) && passes)
            {
                return true;
            }
        }

        return false;
    }

    public Vector3d SteerToward(AiWaypoint waypoint)
    {
        foreach (IGuardMovement movement in _movement)
        {
            if (Contained.Run(logger, Category, "A guard behaviour's SteerToward", (movement, waypoint), static b => b.movement.SteerToward(b.waypoint), out Vector3d point))
            {
                waypoint = waypoint with { Point = point };
            }
        }

        return waypoint.Point;
    }

    public bool HoldsPatrolStop(int tick)
    {
        foreach (IGuardMovement movement in _movement)
        {
            if (Contained.Run(logger, Category, "A guard behaviour's HoldsPatrolStop", (movement, tick), static b => b.movement.HoldsPatrolStop(b.tick), out bool holds) && holds)
            {
                return true;
            }
        }

        return false;
    }

    public void PatrolCommandFinished(int tick)
    {
        foreach (IGuardMovement movement in _movement)
        {
            Contained.Run(logger, Category, "A guard behaviour's PatrolCommandFinished", (movement, tick), static b => b.movement.PatrolCommandFinished(b.tick));
        }
    }

    // Every behaviour is asked, whatever the others said: one may count the ticks it holds a round.
    public bool MayFire
    {
        get
        {
            bool may = true;
            foreach (IGuardCombat combat in _combat)
            {
                if (Contained.Run(logger, Category, "A guard behaviour's MayFire", combat, static b => b.MayFire, out bool own) && !own)
                {
                    may = false;
                }
            }

            return may;
        }
    }

    public bool Fight(Vector3d enemy)
    {
        foreach (IGuardCombat combat in _combat)
        {
            if (Contained.Run(logger, Category, "A guard behaviour's Fight", (combat, enemy), static b => b.combat.Fight(b.enemy), out bool fought) && fought)
            {
                return true;
            }
        }

        return false;
    }

    public void CombatEnded()
    {
        foreach (IGuardCombat combat in _combat)
        {
            Contained.Run(logger, Category, "A guard behaviour's CombatEnded", combat, static b => b.CombatEnded());
        }
    }

    private void Rebuild()
    {
        _life = [.. _attached.Select(static entry => entry.Behaviour).OfType<IGuardLife>()];
        _senses = [.. _attached.Select(static entry => entry.Behaviour).OfType<IGuardSenses>()];
        _movement = [.. _attached.Select(static entry => entry.Behaviour).OfType<IGuardMovement>()];
        _combat = [.. _attached.Select(static entry => entry.Behaviour).OfType<IGuardCombat>()];
    }
}

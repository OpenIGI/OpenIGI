using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Saves;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.Ai;

/// <content>
/// What a save game keeps of the soldiers. Not retail: Project I.G.I. kept no save games; this follows I.G.I. 2's, which keeps a
/// human's body, its health and what it carries, and of its mind only where it stands and the patrol it was on
/// (<c>HumanAI_DeSerializeHandler</c> starts its detection, events, actions and movement afresh).
/// </content>
public sealed partial class SoldierSimulation : ISavedStateSource
{
    /// <inheritdoc/>
    /// <remarks>Each soldier by the task the level declares it with, then each guard generator's counts, then each alarm control.</remarks>
    public IEnumerable<KeyValuePair<GameTask, ISavedState>> SavedStates(ObjectCollisionIndex? solids)
    {
        foreach ((SoldierView view, Igi1HumanAi brain) in _context.Brains)
        {
            yield return new KeyValuePair<GameTask, ISavedState>(view.Soldier, new SavedSoldier(_context, _footsteps, view, brain));
        }

        foreach (GuardGeneratorTask generator in _generators.AllGenerators)
        {
            yield return new KeyValuePair<GameTask, ISavedState>(generator, generator);
        }

        foreach (KeyValuePair<GameTask, ISavedState> control in AlarmControls?.SavedStates ?? [])
        {
            yield return control;
        }
    }

    /// <summary>
    /// Makes the soldiers ready for a save game to be read onto the level they were played on: every one afresh, as the level
    /// made them, and nothing kept of what they were in the middle of -- the rounds in flight, the deaths not yet taken, who
    /// was seen through the binoculars. The save then puts each where it was, as it does on a level just come up.
    /// </summary>
    internal void RemakeForLoad()
    {
        // The soldiers are made again as the level made them: their guns' tasks stand where the level's did.
        _rounds.Clear();
        _rounds.LevelRunning = false;
        _damage.ClearForLoad();
        _footsteps.Clear();
        _combatClipMissing.Clear();
        _releasedThisEntry.Clear();
        _releasedState.Clear();
        _seenSince.Clear();
        _binoculars.Detected.Clear();
        _binoculars.Visibility.Clear();
        _generators.RemakeAll();

        // The alarm's edge is read again from the controls the save puts back.
        _alarmWasOn = false;
    }

    /// <summary>Kills a soldier where it stands, as a round through its chest would, for a test that needs one dead.</summary>
    /// <param name="view">The soldier.</param>
    internal void KillForTesting(SoldierView view)
    {
        _context.HealthOf(view).ApplyDirect(0, float.MaxValue);
        _damage.Die(view, default, view.Position, view.Position, default, default);
    }

    // One soldier: its body, its health, its gun's rounds, its mind's place, and whether its body has left the world.
    private sealed class SavedSoldier(SoldierContext context, SoldierFootsteps footsteps, SoldierView view, Igi1HumanAi brain) : IExchangedState
    {
        public void Exchange(StateExchange exchange)
        {
            bool wasDead = view.IsDead;
            ((IExchangedState)view).Exchange(exchange);
            ((IExchangedState)context.HealthOf(view)).Exchange(exchange);

            SoldierAgent? agent = context.FindAgent(view) ?? (context.GoneBodies.TryGetValue(view, out var gone) ? gone.Agent : null);
            bool armed = exchange.Bool(agent?.WeaponType is not null);
            int rounds = exchange.Int(agent?.AmmoInClip ?? 0);
            int cooldown = exchange.Int(agent?.FireCooldown ?? 0);
            int reload = exchange.Int(agent?.ReloadTicks ?? 0);
            ((IExchangedState)brain).Exchange(exchange);
            bool left = exchange.Bool(context.GoneBodies.ContainsKey(view));
            if (!exchange.IsReading)
            {
                return;
            }

            if (agent is not null)
            {
                // A gun its death dropped lies in the level as a pickup of its own, and is not in its hands again.
                if (!armed)
                {
                    agent.WeaponType = null;
                }

                agent.AmmoInClip = rounds;
                agent.FireCooldown = cooldown;
                agent.ReloadTicks = reload;
            }

            if (view.IsDead && !wasDead)
            {
                footsteps.Forget(view);
            }

            if (left && !context.GoneBodies.ContainsKey(view))
            {
                context.RemoveBody(view);
            }
        }
    }
}

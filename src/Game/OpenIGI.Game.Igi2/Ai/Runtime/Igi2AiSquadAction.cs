using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi2.Humans;

namespace OpenIGI.Game.Igi2.Ai.Runtime;

/// <summary>
/// A squad action, <c>AISquadAction_s</c>: its Init as it is pushed, its Run each tick it is on top, its DeInit as it is popped, and
/// its event handler (always the script's); the state it keeps lives on the subclass.
/// </summary>
public abstract class Igi2AiSquadAction
{
    /// <summary>Gets the kind.</summary>
    public abstract Igi2AiSquadActionKind Kind { get; }

    /// <summary>Gets or sets the line the switch to it was announced with, which it waits out.</summary>
    public IIgi2SoundInstance? SwitchStateSound { get; set; }

    /// <summary>Starts it.</summary>
    /// <param name="squad">The squad.</param>
    /// <param name="change">What the change carries.</param>
    /// <returns><see langword="false"/> when it popped itself at once.</returns>
    public abstract bool Init(Igi2AiSquad squad, Igi2AiSquadChange change);

    /// <summary>Runs it a tick.</summary>
    /// <param name="squad">The squad.</param>
    /// <returns><see langword="false"/> when it popped itself.</returns>
    public virtual bool Run(Igi2AiSquad squad) => true;

    /// <summary>
    /// Ends it (<c>AISquad_SquadActionDefaultDeInitHandler</c>): a hold, search or directed search ends with the "all clear" line
    /// from the leader; the coming action is handed what it needs from the current event; any action but idle and patrol pops the
    /// members' stacks when asked; and going idle clears them and drops their combat-idle events.
    /// </summary>
    /// <param name="squad">The squad.</param>
    /// <param name="change">What the change carries.</param>
    public virtual void DeInit(Igi2AiSquad squad, Igi2AiSquadChange change)
    {
        ArgumentNullException.ThrowIfNull(squad);
        ArgumentNullException.ThrowIfNull(change);
        change.PreviousAction = (int)Kind;
        if (Kind is Igi2AiSquadActionKind.HoldArea or Igi2AiSquadActionKind.SearchArea
            or Igi2AiSquadActionKind.MoveSearchInDirection or Igi2AiSquadActionKind.LookSearchInDirection && !change.Deserializing)
        {
            squad.SelectAndPlaySound(13, squad.FirstMember);
        }

        OnDeInit();
        if (squad.CurrentEvent is { } current)
        {
            switch ((Igi2AiSquadActionKind)change.NextAction)
            {
                case Igi2AiSquadActionKind.Inquire:
                    change.Position = current.Position;
                    change.Receiver = current.Receivers.Count > 0 ? current.Receivers[0] : null;
                    break;
                case Igi2AiSquadActionKind.SearchArea or Igi2AiSquadActionKind.Move:
                    change.Position = current.Position;
                    break;
                case Igi2AiSquadActionKind.Leapfrog:
                    change.Position = current.Position;
                    change.Target = current.Reference as IAiDetectable;
                    break;
                case Igi2AiSquadActionKind.MoveSearchInDirection or Igi2AiSquadActionKind.LookSearchInDirection or Igi2AiSquadActionKind.Danger:
                    change.Position = current.Position;
                    change.Direction = current.Direction;
                    break;
            }
        }

        if (Kind is not Igi2AiSquadActionKind.Patrol and not Igi2AiSquadActionKind.Idle && change.PopMembers)
        {
            squad.PopMembersActionStacks();
        }

        if (change.NextAction == (int)Igi2AiSquadActionKind.Idle)
        {
            if (change.PopMembers)
            {
                squad.ClearMembersActionStacks();
            }

            squad.RemoveEventFromMembersLists(30);
        }
    }

    /// <summary>Lets go of what the action holds as it ends.</summary>
    protected virtual void OnDeInit()
    {
    }

    /// <summary>Writes the action's own state for a save game, after its kind.</summary>
    /// <param name="words">Where it is written.</param>
    /// <param name="squad">Its squad.</param>
    public virtual void SaveState(Igi2AiSquadWords words, Igi2AiSquad squad)
    {
    }

    /// <summary>Reads the action's own state back, as <c>AISquad_DeSerializeHandler</c> rebinds each action by its kind.</summary>
    /// <param name="words">Where it is read from.</param>
    /// <param name="squad">Its squad.</param>
    public virtual void LoadState(Igi2AiSquadWords words, Igi2AiSquad squad)
    {
    }

    /// <summary>The run stream's difficulty test the searches make: a member runs unless the game is on its easiest setting.</summary>
    protected static bool RunsOn(Igi2AiSquad squad) => squad.World.Difficulty != 0;
}

using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Flow;

namespace OpenIGI.Game.Igi2.Interaction;

/// <summary>What a terminal is given by the task it is part of: its script, its name, its explosive and its activate settings.</summary>
public interface IIgi2TerminalOwner
{
    /// <summary>Gets the script that says whether it may be hacked, <c>Active</c>.</summary>
    string ActiveScript { get; }

    /// <summary>Gets the task's name, which its script is compiled under.</summary>
    string Name { get; }

    /// <summary>Gets the task's explosive part.</summary>
    Igi2Explosive Explosive { get; }

    /// <summary>Gets the task's position.</summary>
    Vector3d Position { get; }
}

/// <summary>
/// The terminal part of a task, I.G.I. 2's <c>Terminal</c> (terminal.c), itself an <c>ExplodeObject</c>: a thing a human hacks by
/// kneeling at its model's second magic vertex for its hack time, which the level's scripts read through <c>isInUse</c>,
/// <c>isLastInUse</c>, <c>isActive</c>, <c>isHacked</c>, <c>isHackedThisTick</c> and <c>nClientID</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>Terminal_RunHandler</c>: a hack finished last tick is made, and stands for one tick as hacked this tick, stamped with the level
/// flow's tick; it ticks as an <c>ExplodeObject</c>; exploded, it is no longer active, and otherwise its <c>Active</c> script, where it
/// has one, says whether it is; it stays in use while it was hacked at in the last two ticks.
/// </para>
/// <para>
/// <c>Terminal_EvaluateActivateHandler</c>: only while active, from its second magic vertex, to a view in front of it and within
/// three quarters of a metre to either side. <c>Terminal_ActivateHandler</c>: in use, as of this tick.
/// </para>
/// </remarks>
/// <param name="owner">The task it is part of.</param>
public sealed class Igi2Terminal(IIgi2TerminalOwner owner)
{
    /// <summary>The magic vertex a human hacks it from, <c>1</c>.</summary>
    public const int ActivateVertex = 1;

    /// <summary>How far to either side of its vertex a human may stand and still be offered it, <c>3072</c> (0.75 m).</summary>
    public const double ActivateHalfWidth = 3072.0;

    /// <summary>The ticks after its last use that it is still in use, <c>2</c>.</summary>
    public const int InUseTicks = 2;

    /// <summary>The seconds a hack takes unless the level says otherwise, <c>Terminal_CreateHandler</c>.</summary>
    public const float DefaultHackTime = 4f;

    /// <summary>The sound a hack makes unless the level says otherwise.</summary>
    public const string DefaultActivateSound = "tba_hackcomp";

    private byte _isInUse;
    private byte _isLastInUse;
    private int _lastInUseTick;
    private int _isSetHacked;
    private byte _isActive = 1;
    private byte _isHacked;
    private byte _isHackedThisTick;
    private int _hackedThisTickTick;
    private int _clientId;
    private Igi2LevelExpression? _activeExpression;
    private Igi2LevelFlowTask? _flow;

    /// <summary>Gets a value indicating whether it may be hacked, <c>isActive</c>.</summary>
    public bool IsActive => _isActive != 0;

    /// <summary>Gets a value indicating whether it is hacked at, <c>isInUse</c>.</summary>
    public bool IsInUse => _isInUse != 0;

    /// <summary>Gets a value indicating whether it was hacked at last tick, <c>isLastInUse</c>.</summary>
    public bool IsLastInUse => _isLastInUse != 0;

    /// <summary>Gets a value indicating whether it has been hacked, <c>isHacked</c>.</summary>
    public bool IsHacked => _isHacked != 0;

    /// <summary>Gets a value indicating whether it was hacked this tick, <c>isHackedThisTick</c>.</summary>
    public bool IsHackedThisTick => _isHackedThisTick != 0;

    /// <summary>Gets the level flow's tick it was hacked on, <c>nHackedThisFrameTick</c>.</summary>
    public int HackedTick => _hackedThisTickTick;

    /// <summary>Gets the client that hacked it, <c>nClientID</c>; nought for the single player.</summary>
    public int ClientId => _clientId;

    /// <summary>Gets where a human kneels to hack it, its model's second magic vertex.</summary>
    public Vector3d MagicVertex =>
        Igi2MagicVertex.Position(owner.Explosive.Models?.Invoke(owner.Explosive.Model), ActivateVertex, owner.Explosive.Frame, owner.Position);

    /// <summary>Gets the radius of its sphere: the model it wears, with its largest scale of one; with none, it has no sphere.</summary>
    public float Radius => owner.Explosive.Models?.Invoke(owner.Explosive.Model)?.NativeMarkerBound ?? 0f;

    /// <summary>Its tick, <c>Terminal_RunHandler</c> (single player), after its children have run as the task's.</summary>
    /// <param name="level">The level.</param>
    public void Run(Igi2LevelRun level)
    {
        ArgumentNullException.ThrowIfNull(level);
        _flow = level.LevelFlow;
        int flowTick = _flow?.Tick ?? 0;
        if (_isHackedThisTick != 0)
        {
            _isHackedThisTick = 0;
        }

        if (_isSetHacked != 0)
        {
            _isSetHacked = 0;
            _isHacked = 1;
            _isHackedThisTick = 1;
            _hackedThisTickTick = flowTick;
        }

        level.RunChildren((GameTask)owner);
        owner.Explosive.Run(level);
        if (owner.Explosive.IsExploded)
        {
            _isActive = 0;
        }
        else
        {
            _activeExpression ??= new Igi2LevelExpression(owner.ActiveScript, owner.Name, level.Natives);
            if (!_activeExpression.IsEmpty)
            {
                _isActive = (byte)Igi2LevelExpression.Truncate(level.Evaluate(_activeExpression));
            }
        }

        if (flowTick > _lastInUseTick + InUseTicks)
        {
            _isInUse = 0;
        }

        _isLastInUse = _isInUse;
    }

    /// <summary>Offers it to a human's view, <c>Terminal_EvaluateActivateHandler</c>.</summary>
    /// <param name="context">The view.</param>
    /// <param name="target">The activatable that stands for it.</param>
    public void EvaluateActivate(Igi2EvaluateActivate context, IIgi2Activatable target)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (_isActive != 0)
        {
            context.OfferFromFront(MagicVertex, owner.Explosive.Frame, ActivateHalfWidth, target);
        }
    }

    /// <summary>A tick of being hacked at, <c>Terminal_ActivateHandler</c>: in use, as of this tick.</summary>
    public void Activate()
    {
        _isInUse = 1;
        _lastInUseTick = _flow?.Tick ?? 0;
    }

    /// <summary>
    /// Hacked, from the next tick on: what <c>HumanPlayer_RunStateActivateTerminal</c> sets once the key has been held long enough
    /// (<c>Terminal_Hacked</c>).
    /// </summary>
    /// <param name="clientId">The client that hacked it; nought for the single player.</param>
    public void Hacked(int clientId = 0)
    {
        _isSetHacked = 1;
        _clientId = clientId;
    }

    /// <summary>Exports its variables, <c>Terminal_RegisterScriptSymbolsHandler</c>, after the <c>ExplodeObject</c>'s own.</summary>
    /// <param name="variables">The registry.</param>
    /// <param name="task">The task they are bound under.</param>
    public void ExportVars(ScriptVarRegistry variables, GameTask task)
    {
        ArgumentNullException.ThrowIfNull(variables);
        owner.Explosive.ExportVars(variables);
        variables.BindInt(task, "isInUse", () => _isInUse, value => _isInUse = (byte)value, sizeof(byte));
        variables.BindInt(task, "isLastInUse", () => _isLastInUse, value => _isLastInUse = (byte)value, sizeof(byte));
        variables.BindInt(task, "isActive", () => _isActive, value => _isActive = (byte)value, sizeof(byte));
        variables.BindInt(task, "isHacked", () => _isHacked, value => _isHacked = (byte)value, sizeof(byte));
        variables.BindInt(task, "isHackedThisTick", () => _isHackedThisTick, value => _isHackedThisTick = (byte)value, sizeof(byte));
        variables.BindInt(task, "nClientID", () => _clientId, value => _clientId = value);
    }

    /// <summary>
    /// Writes its save state, <c>Terminal_ptSerializeParameterList</c> past the exploding object's: whether it is in use and was, when it
    /// was last, whether it may be hacked, has been and was this tick, and when.
    /// </summary>
    /// <param name="writer">The writer.</param>
    public void WriteState(Saves.Igi2StateWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Bool(_isInUse != 0);
        writer.Bool(_isLastInUse != 0);
        writer.Int(_lastInUseTick);
        writer.Bool(_isActive != 0);
        writer.Bool(_isHacked != 0);
        writer.Bool(_isHackedThisTick != 0);
        writer.Int(_hackedThisTickTick);
    }

    /// <summary>Reads its save state back.</summary>
    /// <param name="reader">The reader.</param>
    public void ReadState(Saves.Igi2StateReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        _isInUse = reader.Bool() ? (byte)1 : (byte)0;
        _isLastInUse = reader.Bool() ? (byte)1 : (byte)0;
        _lastInUseTick = reader.Int();
        _isActive = reader.Bool() ? (byte)1 : (byte)0;
        _isHacked = reader.Bool() ? (byte)1 : (byte)0;
        _isHackedThisTick = reader.Bool() ? (byte)1 : (byte)0;
        _hackedThisTickTick = reader.Int();
    }
}

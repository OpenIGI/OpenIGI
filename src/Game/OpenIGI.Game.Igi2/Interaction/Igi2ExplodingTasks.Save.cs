using OpenIGI.Game.Igi2.Saves;

namespace OpenIGI.Game.Igi2.Interaction;

/// <content>Its save state, <c>ExplodeObject_ptSerializeParameterList</c>.</content>
public sealed partial class Igi2ExplodeObjectTask : IIgi2Serialized
{
    /// <inheritdoc/>
    public void WriteState(Igi2StateWriter writer) => Explosive.WriteState(writer);

    /// <inheritdoc/>
    public void ReadState(Igi2StateReader reader) => Explosive.ReadState(reader);
}

/// <content>Its save state, <c>Terminal_ptSerializeParameterList</c>: an exploding object's, then a terminal's.</content>
public sealed partial class Igi2TerminalTask : IIgi2Serialized
{
    /// <inheritdoc/>
    public void WriteState(Igi2StateWriter writer)
    {
        Explosive.WriteState(writer);
        Terminal.WriteState(writer);
    }

    /// <inheritdoc/>
    public void ReadState(Igi2StateReader reader)
    {
        Explosive.ReadState(reader);
        Terminal.ReadState(reader);
    }
}

/// <content>
/// Its save state, <c>AlarmControl_ptSerializeParameterList</c>: a terminal's, then whether it is triggered, sounds and sounded, and when
/// it was last triggered.
/// </content>
public sealed partial class Igi2AlarmControlTask : IIgi2Serialized
{
    /// <inheritdoc/>
    public void WriteState(Igi2StateWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        Explosive.WriteState(writer);
        Terminal.WriteState(writer);
        writer.Bool(_isTrigger != 0);
        writer.Bool(_isAlarm != 0);
        writer.Bool(_isLastAlarm != 0);
        writer.Int(_triggerLastTick);
    }

    /// <inheritdoc/>
    public void ReadState(Igi2StateReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        Explosive.ReadState(reader);
        Terminal.ReadState(reader);
        _isTrigger = reader.Bool() ? 1 : 0;
        _isAlarm = reader.Bool() ? 1 : 0;
        _isLastAlarm = reader.Bool() ? 1 : 0;
        _triggerLastTick = reader.Int();
    }
}

/// <content>
/// Its save state, <c>GenericTBA_ptSerializeParameterList</c>: an exploding object's, then whether it is in use and was, when it was
/// last, whether it may be worked, has finished, and finished this tick, and when.
/// </content>
public sealed partial class Igi2GenericTbaTask : IIgi2Serialized
{
    /// <inheritdoc/>
    public void WriteState(Igi2StateWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        Explosive.WriteState(writer);
        writer.Bool(_isInUse != 0);
        writer.Bool(_isLastInUse != 0);
        writer.Int(_lastInUseTick);
        writer.Bool(_isActive != 0);
        writer.Bool(_isFinished != 0);
        writer.Bool(_isFinishedThisTick != 0);
        writer.Int(_finishedThisTickTick);
    }

    /// <inheritdoc/>
    public void ReadState(Igi2StateReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        Explosive.ReadState(reader);
        _isInUse = reader.Bool() ? (byte)1 : (byte)0;
        _isLastInUse = reader.Bool() ? (byte)1 : (byte)0;
        _lastInUseTick = reader.Int();
        _isActive = reader.Bool() ? (byte)1 : (byte)0;
        _isFinished = reader.Bool() ? (byte)1 : (byte)0;
        _isFinishedThisTick = reader.Bool() ? (byte)1 : (byte)0;
        _finishedThisTickTick = reader.Int();
    }
}

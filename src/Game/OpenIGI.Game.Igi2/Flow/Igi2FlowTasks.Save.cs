using OpenIGI.Game.Igi2.Saves;

namespace OpenIGI.Game.Igi2.Flow;

/// <content>Its save state, <c>EditVariable_ptSerializeParameterList</c>: its value.</content>
public sealed partial class Igi2EditVariableTask : IIgi2Serialized
{
    /// <inheritdoc/>
    public void WriteState(Igi2StateWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Int(Value);
    }

    /// <inheritdoc/>
    public void ReadState(Igi2StateReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        Value = reader.Int();
    }
}

/// <content>Its save state, <c>LevelTimer_ptSerializeParameterList</c>: whether it runs, and its tick.</content>
public sealed partial class Igi2LevelTimerTask : IIgi2Serialized
{
    /// <inheritdoc/>
    public void WriteState(Igi2StateWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Bool(_isRun != 0);
        writer.Int(_tick);
    }

    /// <inheritdoc/>
    public void ReadState(Igi2StateReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        _isRun = reader.Bool() ? (byte)1 : (byte)0;
        _tick = reader.Int();
    }
}

/// <content>Its save state, <c>ConditionalContainer_ptSerializeParameterList</c>: whether its children are in the world, and whether it runs.</content>
public sealed partial class Igi2ConditionalContainerTask : IIgi2Serialized
{
    /// <inheritdoc/>
    public void WriteState(Igi2StateWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Bool(AreChildrenInWorld);
        writer.Bool(_isRun != 0);
    }

    /// <inheritdoc/>
    public void ReadState(Igi2StateReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        AreChildrenInWorld = reader.Bool();
        _isRun = reader.Bool() ? (byte)1 : (byte)0;
    }
}

/// <content>Its save state, <c>LevelFlow_ptSerializeParameterList</c>: its tick, then its time of day.</content>
public sealed partial class Igi2LevelFlowTask : IIgi2Serialized
{
    /// <inheritdoc/>
    public void WriteState(Igi2StateWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Int(Tick);
        writer.Int(TimeOfDayTick);
    }

    /// <inheritdoc/>
    public void ReadState(Igi2StateReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        Tick = reader.Int();
        TimeOfDayTick = reader.Int();
    }
}

/// <content>Its save state, <c>CutScene_ptSerializeParameterList</c>: whether it runs and has finished, and its clock.</content>
public sealed partial class Igi2CutSceneTask : IIgi2Serialized
{
    /// <inheritdoc/>
    public void WriteState(Igi2StateWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Bool(_isRun != 0);
        writer.Bool(_isFinished != 0);
        writer.Int(Tick);
    }

    /// <inheritdoc/>
    public void ReadState(Igi2StateReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        _isRun = reader.Bool() ? (byte)1 : (byte)0;
        _isFinished = reader.Bool() ? (byte)1 : (byte)0;
        Tick = reader.Int();
    }
}

/// <content>Its save state, <c>MissionTimer_ptSerializeParameterList</c>: the ticks left, and its status.</content>
public sealed partial class Igi2MissionTimerTask : IIgi2Serialized
{
    /// <inheritdoc/>
    public void WriteState(Igi2StateWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Int(TicksLeft);
        writer.Int((int)Status);
    }

    /// <inheritdoc/>
    public void ReadState(Igi2StateReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        TicksLeft = reader.Int();
        Status = (Igi2MissionTimerStatus)reader.Int();
    }
}

/// <content>
/// Its save state, <c>StatusMessage_ptSerializeParameterList</c>: the tick it was sent, when it finished showing and the ticks since,
/// whether it is forced, shown, sent and finished, whether its printing has finished, and its ticks.
/// </content>
public sealed partial class Igi2StatusMessageTask : IIgi2Serialized
{
    /// <inheritdoc/>
    public void WriteState(Igi2StateWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Int(_tickSent);
        writer.Int(_finishedDisplay);
        writer.Int(_ticksSinceFinishedDisplay);
        writer.Bool(IsForceSend);
        writer.Bool(_isDisplay != 0);
        writer.Bool(_isSent != 0);
        writer.Bool(_isFinishedDisplay != 0);
        writer.Bool(IsFinishedPrinting);
        writer.Int(_ticks);
    }

    /// <inheritdoc/>
    public void ReadState(Igi2StateReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        _tickSent = reader.Int();
        _finishedDisplay = reader.Int();
        _ticksSinceFinishedDisplay = reader.Int();
        IsForceSend = reader.Bool();
        _isDisplay = reader.Bool() ? (byte)1 : (byte)0;
        _isSent = reader.Bool() ? (byte)1 : (byte)0;
        _isFinishedDisplay = reader.Bool() ? (byte)1 : (byte)0;
        IsFinishedPrinting = reader.Bool();
        _ticks = reader.Int();
    }
}

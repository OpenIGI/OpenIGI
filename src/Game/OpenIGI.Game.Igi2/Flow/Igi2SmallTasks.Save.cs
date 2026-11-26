namespace OpenIGI.Game.Igi2.Flow;

/// <content>Its save state, <c>SoundGenerator_ptSerializeParameterList</c>: its sound, and whether it plays.</content>
public sealed partial class Igi2SoundGeneratorTask : ISavedState
{
    /// <inheritdoc/>
    /// <remarks>
    /// <c>SoundGenerator_SerializeHandler</c>: a sound that has played out is written as not playing, though <c>isPlaying</c> is
    /// left set as the level runs on.
    /// </remarks>
    public void WriteState(StateWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Text(Sound);
        writer.Bool(_isPlaying != 0 && _sound is { HasPlayedOut: false });
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <c>SoundGenerator_DeSerializeHandler</c>: the sound it has now is deleted, and one the save had playing is made again from
    /// its start -- a siren that rang when the game was saved rings, and one that started since falls silent.
    /// </remarks>
    public void ReadState(StateReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        Stop();
        string sound = reader.Text();
        if (reader.Bool())
        {
            Play(sound);
        }
        else
        {
            Sound = sound;
        }
    }
}

/// <content>Its save state, <c>SequenceCommand_ptSerializeParameterList</c>: whether its sequence has stopped, and the step it is on.</content>
public sealed partial class Igi2SequenceCommandTask : ISavedState
{
    /// <inheritdoc/>
    public void WriteState(StateWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Bool(IsStopped);
        writer.TaskReference(Current);
    }

    /// <inheritdoc/>
    public void ReadState(StateReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        IsStopped = reader.Bool();
        Current = reader.TaskReference() as Igi2SequenceCommandTask;
    }
}

/// <content>Its save state, <c>AnimQTask_ptSerializeParameterList</c>: its tick, then its programme's channels as they lie in memory.</content>
public sealed partial class Igi2AnimTask : ISavedState
{
    /// <inheritdoc/>
    public void WriteState(StateWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Int(_tick);
        writer.Array(_program is { } program ? program.Channels : new float[Engine.Tasks.AnimDataProgram.ChannelCount]);
    }

    /// <inheritdoc/>
    public void ReadState(StateReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        _tick = reader.Int();
        float[] channels = reader.FloatArray();
        if (_program is { } program)
        {
            for (int i = 0; i < channels.Length && i < Engine.Tasks.AnimDataProgram.ChannelCount; i++)
            {
                program.WriteChannel(i, channels[i]);
            }
        }
    }
}

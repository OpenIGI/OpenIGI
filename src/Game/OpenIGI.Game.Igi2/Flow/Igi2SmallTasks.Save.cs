using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi2.Saves;

namespace OpenIGI.Game.Igi2.Flow;

/// <content>Its save state, <c>SoundGenerator_ptSerializeParameterList</c>: its sound, and whether it plays.</content>
public sealed partial class Igi2SoundGeneratorTask : IIgi2Serialized
{
    /// <inheritdoc/>
    public void WriteState(Igi2StateWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Text(Sound);
        writer.Bool(_isPlaying != 0);
    }

    /// <inheritdoc/>
    public void ReadState(Igi2StateReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        Sound = reader.Text();
        _isPlaying = reader.Bool() ? (byte)1 : (byte)0;
    }
}

/// <content>Its save state, <c>SequenceCommand_ptSerializeParameterList</c>: whether its sequence has stopped, and the step it is on.</content>
public sealed partial class Igi2SequenceCommandTask : IIgi2Serialized
{
    /// <inheritdoc/>
    public void WriteState(Igi2StateWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Bool(IsStopped);
        writer.TaskReference(Current);
    }

    /// <inheritdoc/>
    public void ReadState(Igi2StateReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        IsStopped = reader.Bool();
        Current = reader.TaskReference() as Igi2SequenceCommandTask;
    }
}

/// <content>Its save state, <c>AnimQTask_ptSerializeParameterList</c>: its tick, then its programme's channels as they lie in memory.</content>
public sealed partial class Igi2AnimTask : IIgi2Serialized
{
    /// <inheritdoc/>
    public void WriteState(Igi2StateWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Int(_tick);
        writer.Array(_program is { } program ? program.Channels : new float[Engine.Tasks.AnimDataProgram.ChannelCount]);
    }

    /// <inheritdoc/>
    public void ReadState(Igi2StateReader reader)
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

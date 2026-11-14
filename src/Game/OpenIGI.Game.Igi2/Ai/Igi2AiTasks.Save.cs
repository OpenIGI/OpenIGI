namespace OpenIGI.Game.Igi2.Ai;

/// <content>
/// Its save state, <c>HumanSoldier_ptSerializeParameterList</c>: a human's, then the state that last set its main animation, which
/// squads have seen it dead and whether a camera has. Those three are not kept here, so they are written as a new soldier has them.
/// </content>
/// <remarks>
/// Its weapon and its view are not written: only the player's serialize handler writes those two children
/// (<c>HumanPlayer_SerializeHandler</c>), and retail's saves hold neither for a soldier. Its mind is a level child of its own.
/// </remarks>
public sealed partial class Igi2HumanSoldierTask : ISavedState
{
    /// <inheritdoc/>
    public void WriteState(StateWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        Body?.WriteSoldierState(writer);
        writer.Int(0);
        writer.Int(0);
        writer.Int(0);
        writer.Bool(false);
    }

    /// <inheritdoc/>
    public void ReadState(StateReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        Body?.ReadSoldierState(reader);
        reader.Int();
        reader.Int();
        reader.Int();
        reader.Bool();
    }
}

/// <content>Its save state: its mind's (<see cref="Runtime.Igi2HumanAi.WriteState"/>).</content>
public sealed partial class Igi2HumanAiTask : ISavedState
{
    /// <inheritdoc/>
    public void WriteState(StateWriter writer) => Mind?.WriteState(writer);

    /// <inheritdoc/>
    public void ReadState(StateReader reader) => Mind?.ReadState(reader);
}

/// <content>Its save state: its squad's (<see cref="Runtime.Igi2AiSquad.WriteState"/>).</content>
public sealed partial class Igi2AiSquadTask : ISavedState
{
    /// <inheritdoc/>
    public void WriteState(StateWriter writer) => Squad?.WriteState(writer);

    /// <inheritdoc/>
    public void ReadState(StateReader reader) => Squad?.ReadState(reader);
}

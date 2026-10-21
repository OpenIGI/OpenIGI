using OpenIGI.Game.Igi2.Saves;

namespace OpenIGI.Game.Igi2.Ai;

/// <content>
/// Its save state, <c>HumanSoldier_ptSerializeParameterList</c>: a human's, then the state that last set its main animation, which
/// squads have seen it dead and whether a camera has. Those three are not kept here, so they are written as a new soldier has them.
/// </content>
public sealed partial class Igi2HumanSoldierTask : IIgi2Serialized
{
    /// <inheritdoc/>
    /// <remarks>Its mind is a level child of its own, written before these.</remarks>
    public IReadOnlyList<Igi2SerializedPart> SerializedParts => Body is { } body ? body.SavedParts(body.Weapon.Inventory) : [];

    /// <inheritdoc/>
    public void WriteState(Igi2StateWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        Body?.WriteSoldierState(writer);
        writer.Int(0);
        writer.Int(0);
        writer.Int(0);
        writer.Bool(false);
    }

    /// <inheritdoc/>
    public void ReadState(Igi2StateReader reader)
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
public sealed partial class Igi2HumanAiTask : IIgi2Serialized
{
    /// <inheritdoc/>
    public void WriteState(Igi2StateWriter writer) => Mind?.WriteState(writer);

    /// <inheritdoc/>
    public void ReadState(Igi2StateReader reader) => Mind?.ReadState(reader);
}

/// <content>Its save state: its squad's (<see cref="Runtime.Igi2AiSquad.WriteState"/>).</content>
public sealed partial class Igi2AiSquadTask : IIgi2Serialized
{
    /// <inheritdoc/>
    public void WriteState(Igi2StateWriter writer) => Squad?.WriteState(writer);

    /// <inheritdoc/>
    public void ReadState(Igi2StateReader reader) => Squad?.ReadState(reader);
}

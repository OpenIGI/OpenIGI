using OpenIGI.Game.Igi2.Saves;

namespace OpenIGI.Game.Igi2.Interaction;

/// <content>Its save state, <c>EditRigidObj_ptSerializeParameterList</c>: a model object's, which is all it keeps.</content>
public sealed partial class Igi2EditRigidObjTask : IIgi2Serialized
{
    /// <inheritdoc/>
    public void WriteState(Igi2StateWriter writer) => Igi2ModelObjState.Write(writer, _position, _orientation);

    /// <inheritdoc/>
    /// <remarks>It is put where it stood, as a guide that rides it would put it.</remarks>
    public void ReadState(Igi2StateReader reader)
    {
        (OpenIGI.Core.Numerics.Vector3d position, float[] orientation, _) = Igi2ModelObjState.Read(reader);
        Ride(position, orientation, _movedTick);
    }
}

/// <content>Its save state, <c>MineField_ptSerializeParameterList</c>: the ticks before it may explode again.</content>
public sealed partial class Igi2MineFieldTask : IIgi2Serialized
{
    /// <inheritdoc/>
    public void WriteState(Igi2StateWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Int(Pause);
    }

    /// <inheritdoc/>
    public void ReadState(Igi2StateReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        Pause = reader.Int();
    }
}

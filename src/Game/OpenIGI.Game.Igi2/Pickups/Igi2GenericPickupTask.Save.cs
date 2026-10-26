using OpenIGI.Game.Igi2.Saves;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Game.Igi2.Pickups;

/// <content>Its save state, <c>GenericPickup_ptSerializeParameterList</c>: a model object's, then whether it is taken, and its model.</content>
public sealed partial class Igi2GenericPickupTask : IIgi2Serialized
{
    /// <inheritdoc/>
    public void WriteState(Igi2StateWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        Igi2ModelObjState.Write(writer, Position, EngineEuler.Rotation(Orientation.X, Orientation.Y, Orientation.Z), _isPickedUp == 0);
        writer.Bool(_isPickedUp != 0);
        writer.Text(Model);
    }

    /// <inheritdoc/>
    public void ReadState(Igi2StateReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        Igi2ModelObjState.Read(reader);
        _isPickedUp = reader.Bool() ? (byte)1 : (byte)0;
        Model = reader.Text();
    }
}

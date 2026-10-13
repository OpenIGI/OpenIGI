using OpenIGI.Game.Igi2.Saves;

namespace OpenIGI.Game.Igi2.Interaction;

/// <content>
/// Its save state, <c>SCamera_ptSerializeParameterList</c>: a model object's, then whether it saw a dead friend, is on, sees and saw, how
/// long it has seen, and whether it is destroyed. Its lens is its <c>SCameraLens</c> child in retail, a part here.
/// </content>
public sealed partial class Igi2SecurityCameraTask : IIgi2Serialized
{
    /// <inheritdoc/>
    public IReadOnlyList<Igi2SerializedPart> SerializedParts => [new("SCameraLens", new LensState(_lens))];

    /// <inheritdoc/>
    public void WriteState(Igi2StateWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        Igi2ModelObjState.Write(writer, _position, _holderFrame);
        writer.Bool(_isDeadFriend);
        writer.Bool(_isOn != 0);
        writer.Bool(_isDetection != 0);
        writer.Bool(_isLastDetection != 0);
        writer.Int(_detectionTime);
        writer.Bool(_isDestroyed != 0);
    }

    /// <inheritdoc/>
    public void ReadState(Igi2StateReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        Igi2ModelObjState.Read(reader);
        _isDeadFriend = reader.Bool();
        _isOn = reader.Bool() ? (byte)1 : (byte)0;
        _isDetection = reader.Bool() ? (byte)1 : (byte)0;
        _isLastDetection = reader.Bool() ? (byte)1 : (byte)0;
        _detectionTime = reader.Int();
        _isDestroyed = reader.Bool() ? (byte)1 : (byte)0;
    }

    /// <summary>
    /// The lens's save state, <c>SCameraLens_ptSerializeParameterList</c>: a model object's, then its tilt and turn, the turn it heads
    /// for, its delay and way round, its damage and limit, its side, how long it tracks, whether it explodes, and the timers and frame
    /// of its overlay, which the presentation keeps and are written as nought.
    /// </summary>
    private sealed class LensState(Lens lens) : IIgi2Serialized
    {
        public void WriteState(Igi2StateWriter writer)
        {
            Igi2ModelObjState.Write(writer, lens.Position, lens.Frame);
            writer.Real(lens.Alpha);
            writer.Real(lens.Gamma);
            writer.Real(lens.IdealGamma);
            writer.Int(lens.GammaDelay);
            writer.Int(lens.RotateDirection);
            writer.Real(lens.Damage);
            writer.Real(lens.MaxDamage);
            writer.Int(CameraTeam);
            writer.Int(lens.TrackTimeout);
            writer.Bool(lens.IsExplode);
            writer.Int(0);
            writer.Int(0);
            writer.Bool(false);
            writer.Bool(false);
            writer.Int(0);
        }

        public void ReadState(Igi2StateReader reader)
        {
            lens.Frame = Igi2ModelObjState.Read(reader).Orientation;
            lens.Alpha = reader.Real();
            lens.Gamma = reader.Real();
            lens.IdealGamma = reader.Real();
            lens.GammaDelay = reader.Int();
            lens.RotateDirection = reader.Int();
            lens.Damage = reader.Real();
            lens.MaxDamage = reader.Real();
            reader.Int();
            lens.TrackTimeout = reader.Int();
            lens.IsExplode = reader.Bool();
            reader.Int();
            reader.Int();
            reader.Bool();
            reader.Bool();
            reader.Int();
        }
    }
}

/// <content>Its save state, <c>Wire_ptSerializeParameterList</c>: a model object's, then whether it is cut at and was.</content>
public sealed partial class Igi2WireTask : IIgi2Serialized
{
    /// <inheritdoc/>
    public void WriteState(Igi2StateWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        Igi2ModelObjState.Write(writer, Position, _frame);
        writer.Bool(_isInUse != 0);
        writer.Bool(_isLastInUse != 0);
    }

    /// <inheritdoc/>
    public void ReadState(Igi2StateReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        Igi2ModelObjState.Read(reader);
        _isInUse = reader.Bool() ? (byte)1 : (byte)0;
        _isLastInUse = reader.Bool() ? (byte)1 : (byte)0;
    }
}

/// <content>
/// Its save state, <c>Cabinet_ptSerializeParameterList</c>: a model object's, then whether its drawer is opening or closing, whether it
/// is searched at and was, when it last was, and whether it has been searched.
/// </content>
public sealed partial class Igi2CabinetTask : IIgi2Serialized
{
    /// <inheritdoc/>
    public void WriteState(Igi2StateWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        Igi2ModelObjState.Write(writer, Position, _frame);
        writer.Bool(_isOpening);
        writer.Bool(_isClosing);
        writer.Bool(_isInUse != 0);
        writer.Bool(_isLastInUse != 0);
        writer.Int(_lastInUseTick);
        writer.Bool(_isSearched != 0);
    }

    /// <inheritdoc/>
    public void ReadState(Igi2StateReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        Igi2ModelObjState.Read(reader);
        _isOpening = reader.Bool();
        _isClosing = reader.Bool();
        _isInUse = reader.Bool() ? (byte)1 : (byte)0;
        _isLastInUse = reader.Bool() ? (byte)1 : (byte)0;
        _lastInUseTick = reader.Int();
        _isSearched = reader.Bool() ? (byte)1 : (byte)0;
    }
}

/// <content>Its save state, <c>ConveyorBelt_ptSerializeParameterList</c>: the speeds it carries along its X and its Y.</content>
public sealed partial class Igi2ConveyorBeltTask : IIgi2Serialized
{
    /// <inheritdoc/>
    public void WriteState(Igi2StateWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Real(XSpeed);
        writer.Real(YSpeed);
    }

    /// <inheritdoc/>
    public void ReadState(Igi2StateReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        XSpeed = reader.Real();
        YSpeed = reader.Real();
    }
}

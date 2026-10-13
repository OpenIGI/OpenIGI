using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Saves;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Game.Igi2.Interaction;

/// <content>
/// Its save state, <c>Door_ptSerializeParameterList</c>: its stop offsets and frame as authored, how far open it is, the angle it turns
/// to and is at, where it is told to be, how far it has travelled, its nine flags, its pick time (written as a byte, as retail's
/// boolean printer reads it), how long it has been open, and where it stood as the tick began.
/// </content>
public sealed partial class Igi2DoorTask : IIgi2Serialized
{
    /// <inheritdoc/>
    public void WriteState(Igi2StateWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Real(StopX);
        writer.Real(StopY);
        writer.Real9(_base);
        writer.Real(Slider);
        writer.Real((((Fpu)MaxAngle * _idealPos) * 0.017453292f).F);
        writer.Real((((Fpu)MaxAngle * Slider) * 0.017453292f).F);
        writer.Real(_idealPos);
        writer.Real(_distance);
        writer.Bool(_isLocked != 0);
        writer.Bool(_isOpen != 0);
        writer.Bool(_isClosed != 0);
        writer.Bool(_isLastOpen != 0);
        writer.Bool(_isLastClosed != 0);
        writer.Bool(_isPicked != 0);
        writer.Bool(StopOnCollision);
        writer.Bool(_isOpenDoorChanged != 0);
        writer.Bool(_isOpenDoor != 0);
        writer.Bool((BitConverter.SingleToInt32Bits(PickLockTime) & 0xFF) != 0);
        writer.Int(_openTicks);
        writer.Real9(_lastOrientation);
        writer.Real64x3(_lastPosition);
    }

    /// <inheritdoc/>
    /// <remarks>The angles follow from how far open it is; it is put where that leaves it.</remarks>
    public void ReadState(Igi2StateReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        StopX = reader.Real();
        StopY = reader.Real();
        _base = reader.Real9();
        Slider = reader.Real();
        reader.Real();
        reader.Real();
        _idealPos = reader.Real();
        _distance = reader.Real();
        _isLocked = Byte(reader);
        _isOpen = Byte(reader);
        _isClosed = Byte(reader);
        _isLastOpen = Byte(reader);
        _isLastClosed = Byte(reader);
        _isPicked = Byte(reader);
        StopOnCollision = reader.Bool();
        _isOpenDoorChanged = Byte(reader);
        _isOpenDoor = Byte(reader);
        reader.Bool();
        _openTicks = reader.Int();
        _lastOrientation = reader.Real9();
        _lastPosition = reader.Real64x3();
        SetPosition();
    }

    private static byte Byte(Igi2StateReader reader) => reader.Bool() ? (byte)1 : (byte)0;
}

/// <content>
/// Its save state, <c>Lift_ptSerializeParameterList</c>: the floor it heads for, where its way starts and stops and where it stood, its frames as authored, at
/// either end and as it stood, its speed and how far along it is, the ticks it has stood, its five flags and its floors.
/// </content>
public sealed partial class Igi2LiftTask : Saves.IIgi2Serialized
{
    /// <inheritdoc/>
    public void WriteState(Igi2StateWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.TaskReference(_activeFloor);
        writer.Real64x3(_startPosition);
        writer.Real64x3(_stopPosition);
        writer.Real64x3(_lastPosition);
        writer.Real9(Igi2Pose.Rotation(StartOrientation.X, StartOrientation.Y, StartOrientation.Z));
        writer.Real9(_startOrientation);
        writer.Real9(_stopOrientation);
        writer.Real9(_lastOrientation);
        writer.Real(_speed);
        writer.Real(_along);
        writer.Int(_inactive);
        writer.Bool(_isMovingLastFrame != 0);
        writer.Bool(_isMoving != 0);
        writer.Bool(_isStart != 0);
        writer.Bool(_isStop != 0);
        writer.Int(_currentFloor);
        writer.Int(_floorCache);
        writer.Bool(_isLastCanStart != 0);
    }

    /// <inheritdoc/>
    /// <remarks>It is put where it was along its way.</remarks>
    public void ReadState(Igi2StateReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        Igi2FloorTask? floor = reader.TaskReference() as Igi2FloorTask;
        _startPosition = reader.Real64x3();
        _stopPosition = reader.Real64x3();
        _lastPosition = reader.Real64x3();
        reader.Real9();
        _startOrientation = reader.Real9();
        _stopOrientation = reader.Real9();
        _lastOrientation = reader.Real9();
        _speed = reader.Real();
        _along = reader.Real();
        _inactive = reader.Int();
        _isMovingLastFrame = reader.Bool() ? (byte)1 : (byte)0;
        _isMoving = reader.Bool() ? (byte)1 : (byte)0;
        _isStart = reader.Bool() ? (byte)1 : (byte)0;
        _isStop = reader.Bool() ? (byte)1 : (byte)0;
        _currentFloor = reader.Int();
        _floorCache = reader.Int();
        _isLastCanStart = reader.Bool() ? (byte)1 : (byte)0;
        _activeFloor = floor ?? _activeFloor;
        UpdatePosition();
        UpdateAttachments();
    }
}

/// <content>
/// Its save state, <c>Switch_ptSerializeParameterList</c>: whether it is on and was, is and was destroyed, is and was pressed, when it was last
/// pressed, and whether it can be.
/// </content>
public sealed partial class Igi2SwitchTask : Saves.IIgi2Serialized
{
    /// <inheritdoc/>
    public void WriteState(Igi2StateWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Bool(_isOn != 0);
        writer.Bool(_isLastOn != 0);
        writer.Bool(_isDestroyed != 0);
        writer.Bool(_isLastDestroyed != 0);
        writer.Bool(_isPressed != 0);
        writer.Bool(_isLastPressed != 0);
        writer.Int(_lastPressedTick);
        writer.Bool(_isActive != 0);
    }

    /// <inheritdoc/>
    public void ReadState(Igi2StateReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        _isOn = reader.Bool() ? (byte)1 : (byte)0;
        _isLastOn = reader.Bool() ? (byte)1 : (byte)0;
        _isDestroyed = reader.Bool() ? (byte)1 : (byte)0;
        _isLastDestroyed = reader.Bool() ? (byte)1 : (byte)0;
        _isPressed = reader.Bool() ? (byte)1 : (byte)0;
        _isLastPressed = reader.Bool() ? (byte)1 : (byte)0;
        _lastPressedTick = reader.Int();
        _isActive = reader.Bool() ? (byte)1 : (byte)0;
    }
}

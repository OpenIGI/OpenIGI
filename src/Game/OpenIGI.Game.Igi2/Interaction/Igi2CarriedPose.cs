using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.Interaction;

/// <summary>A carrier that notes the tick it last moved in, which what it carries takes as the tick it moved in too.</summary>
public interface IIgi2MovedTick
{
    /// <summary>Gets the tick it last moved in.</summary>
    int MovedTick { get; }
}

/// <summary>
/// Where a model object stands as something carries it: where it stands now, where it stood at the start of the tick it last moved
/// in (for drawing between), and how it is held to its parent (<see cref="Igi2Attachment"/>, <c>ModelObj</c>'s relative pose).
/// </summary>
/// <remarks>
/// One object's bookkeeping, shared by every model object that rides another: a prop on a moving prop, a switch on a train.
/// <c>ModelObj_UpdateAttachment</c> (<c>igi2 0x56CB40</c>) puts it where its parent holds it in the tick its parent moved.
/// </remarks>
public sealed class Igi2CarriedPose
{
    private readonly Igi2Attachment _attachment = new();
    private float[] _orientation = EngineEuler.Rotation(0f, 0f, 0f);
    private float[] _lastOrientation = EngineEuler.Rotation(0f, 0f, 0f);
    private bool _isMoving;

    /// <summary>Gets where it stands now.</summary>
    public Vector3d Position { get; private set; }

    /// <summary>Gets which way it faces now, row by row.</summary>
    public IReadOnlyList<float> Orientation => _orientation;

    /// <summary>Gets where it stood at the start of the tick it last moved in.</summary>
    public Vector3d LastPosition { get; private set; }

    /// <summary>Gets which way it faced at the start of the tick it last moved in, row by row.</summary>
    public IReadOnlyList<float> LastOrientation => _lastOrientation;

    /// <summary>Gets the tick it last moved in.</summary>
    public int MovedTick { get; private set; } = int.MinValue;

    /// <summary>Gets what it is held to, or <see langword="null"/>.</summary>
    public IAttachParent? Parent => _attachment.Parent;

    /// <summary>Stands it where the level placed it, not moving.</summary>
    /// <param name="position">Where.</param>
    /// <param name="angles">Which way, as level data's three angles.</param>
    public void Place(Vector3d position, Vector3f angles) => Place(position, EngineEuler.Rotation(angles.X, angles.Y, angles.Z));

    /// <summary>Stands it where the level placed it, facing as a frame does, not moving.</summary>
    /// <param name="position">Where.</param>
    /// <param name="frame">Which way, row by row.</param>
    public void Place(Vector3d position, IReadOnlyList<float> frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        _orientation = [.. frame];
        Position = position;
        LastPosition = position;
        _lastOrientation = [.. _orientation];
        _isMoving = false;
    }

    /// <summary>
    /// A tick nothing moved it in starts where the last one left it: drawn between the two, one that has stopped stands still rather
    /// than being drawn over its last step again every tick.
    /// </summary>
    /// <param name="tick">The level's tick.</param>
    public void Settle(int tick)
    {
        if (_isMoving && tick != MovedTick)
        {
            _isMoving = false;
            LastPosition = Position;
            _lastOrientation = [.. _orientation];
        }
    }

    /// <summary>Moves it, keeping where it stood when first moved in a tick as where it stood at the tick's start.</summary>
    /// <param name="position">Where it now stands.</param>
    /// <param name="orientation">Which way it now faces, row by row.</param>
    /// <param name="tick">The tick it moves in.</param>
    public void Move(Vector3d position, IReadOnlyList<float> orientation, int tick)
    {
        ArgumentNullException.ThrowIfNull(orientation);
        _isMoving = true;
        if (tick != MovedTick)
        {
            MovedTick = tick;
            LastPosition = Position;
            _lastOrientation = [.. _orientation];
        }

        _orientation = [.. orientation];
        Position = position;
    }

    /// <summary>Holds it to a parent as the two now stand (<c>Attach_eAttachEvent</c>).</summary>
    /// <param name="parent">The parent.</param>
    public void AttachTo(IAttachParent parent)
    {
        ArgumentNullException.ThrowIfNull(parent);
        _attachment.Attach(parent, Position, _orientation);
    }

    /// <summary>Where its parent now holds it, and the tick the parent moved in.</summary>
    /// <param name="position">Receives where it stands.</param>
    /// <param name="frame">Receives which way it faces, row by row.</param>
    /// <param name="tick">Receives the tick to move it in: its parent's, or its own when the parent notes none.</param>
    /// <returns><see langword="false"/> while it is held to no parent.</returns>
    public bool TryFollow(out Vector3d position, out float[] frame, out int tick)
    {
        tick = (_attachment.Parent as IIgi2MovedTick)?.MovedTick ?? MovedTick;
        return _attachment.TryResolve(out position, out frame);
    }
}

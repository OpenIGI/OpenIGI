using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;

#pragma warning disable CS1591 // Public members are documented by the elevator registry contract.

namespace OpenIGI.Game.World;

/// <summary>One placed <c>Elevator</c> task and its authored spline floors.</summary>
/// <param name="Task">The <c>Elevator</c> task.</param>
/// <param name="State">Where the car is and where it is going.</param>
/// <param name="AuthoredOrientation">The task's own <c>Orientation</c> (<c>+288</c>).</param>
/// <param name="Model">The car's model.</param>
/// <param name="StartSound">The sound it starts with.</param>
/// <param name="StopSound">The sound it stops with.</param>
/// <param name="MoveSound">The sound it travels with.</param>
/// <param name="SplineFrame">
/// The frame its spline turns that orientation by, or <see langword="null"/> for a car that keeps it -- every
/// <c>Linear</c> or <c>Force orientation</c> car.
/// </param>
public sealed record PlacedElevator(
    GameTask Task,
    ElevatorState State,
    Matrix3f AuthoredOrientation,
    string Model,
    string StartSound,
    string StopSound,
    string MoveSound,
    ElevatorSplineFrame? SplineFrame = null)
{
    /// <summary>Gets the orientation the car is drawn and collides with: <c>0x43D960</c>'s authored times frame.</summary>
    public Matrix3f Orientation => SplineFrame is { } frame
        ? AuthoredOrientation * frame.At(State.FloorPosition)
        : AuthoredOrientation;

    /// <summary>Gets or sets whether the travelling sound is active.</summary>
    public bool IsSounding { get; set; }
    /// <summary>Gets the current platform position in renderer metres.</summary>
    public Vector3d DrawPosition => State.Position / WorldScene.UnitsPerMetre;

    /// <summary>Gets a presentation position between the previous and current simulation ticks.</summary>
    public Vector3d DrawPositionAt(float tickFraction, bool smooth)
    {
        double alpha = smooth ? Math.Clamp(tickFraction, 0f, 1f) : 1.0;
        return (State.PreviousPosition + ((State.Position - State.PreviousPosition) * alpha))
            / WorldScene.UnitsPerMetre;
    }
}

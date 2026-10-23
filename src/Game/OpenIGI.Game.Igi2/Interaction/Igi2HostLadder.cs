using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.Interaction;

/// <summary>
/// Another game's ladder, as I.G.I. 2's player finds and climbs one (<see cref="IIgi2Ladder"/>): the engine's placement of it
/// (<see cref="LadderPlacement"/>), its foot and top where that game's player climbs between them.
/// </summary>
/// <remarks>
/// Found as I.G.I. 2's ladder is (<see cref="Igi2LadderTask.EvaluateActivate"/>): from in front and below its top, facing it within
/// a quarter turn, or from its top's far side; without a clear sight, which that game's ladders do not ask.
/// </remarks>
/// <param name="ladder">The ladder.</param>
public sealed class Igi2HostLadder(LadderPlacement ladder) : IIgi2Ladder
{
    private readonly LadderPlacement _ladder = ladder ?? throw new ArgumentNullException(nameof(ladder));
    private readonly float[] _frame =
    [
        ladder.Orientation.M11, ladder.Orientation.M12, ladder.Orientation.M13,
        ladder.Orientation.M21, ladder.Orientation.M22, ladder.Orientation.M23,
        ladder.Orientation.M31, ladder.Orientation.M32, ladder.Orientation.M33,
    ];

    /// <summary>Gets the ladder.</summary>
    public LadderPlacement Ladder => _ladder;

    /// <inheritdoc/>
    public int Steps => (int)(Math.Abs(_ladder.Top.Z - _ladder.Bottom.Z) * Igi2LadderTask.StepsPerUnit);

    /// <inheritdoc/>
    public IReadOnlyList<float> Frame => _frame;

    /// <inheritdoc/>
    public Vector3d AttachPosition => _ladder.Origin;

    /// <inheritdoc/>
    public IReadOnlyList<float> AttachOrientation => _frame;

    /// <inheritdoc/>
    /// <remarks>Halfway up it, as a ladder's model is found round its middle, so it is found from its top as from its foot.</remarks>
    public Vector3d Centre => (_ladder.Bottom + _ladder.ActivationTop) * 0.5;

    /// <inheritdoc/>
    public float Radius => (float)(((_ladder.ActivationTop - _ladder.Bottom).Length * 0.5) + LadderPlacement.ActivateRadiusUnits);

    /// <inheritdoc/>
    public Igi2ActivateKind Kind => Igi2ActivateKind.Ladder;

    /// <inheritdoc/>
    public bool KeepsFooting => false;

    /// <inheritdoc/>
    /// <remarks>The second is where the view finds its top; the third and fourth its foot and top, as the climber stands.</remarks>
    public Vector3d MagicVertex(int index) => index switch
    {
        1 => _ladder.ActivationTop,
        3 => _ladder.Top,
        _ => _ladder.Bottom,
    };

    /// <inheritdoc/>
    public void EvaluateActivate(Igi2EvaluateActivate context)
    {
        ArgumentNullException.ThrowIfNull(context);
        float[] m = _frame;
        Vector3d at = _ladder.Origin;
        Vector3d view = context.ViewPosition;
        double dx = view.X - at.X, dy = view.Y - at.Y, dz = view.Z - at.Z;
        double across = (m[6] * dz) + (m[3] * dy) + (m[0] * dx);
        double ahead = (m[4] * dy) + (m[7] * dz) + (m[1] * dx);
        bool isNear = ahead >= 0.0
            ? ahead < 6144.0 && ahead < Igi2EvaluateActivate.Reach
            : ahead > -3072.0 && -ahead < Igi2EvaluateActivate.Reach;
        Vector3d high = MagicVertex(1);
        if (isNear && across > -LadderPlacement.ActivateHalfWidthUnits && across < LadderPlacement.ActivateHalfWidthUnits)
        {
            IReadOnlyList<float> v = context.ViewOrientation;
            float sign = ahead < 0.0 ? -1f : 1f;
            Vector3f facing = new(v[1] * sign, v[4] * sign, v[7] * sign);
            Vector3f toward = new(-m[1], -m[4], -m[7]);
            if (Igi2Pose.Angle(facing, toward) < 0.785398185f && view.Z < high.Z && view.Z > at.Z)
            {
                context.Take(this);
                if (context.Activator is IIgi2LadderClimber climber)
                {
                    climber.IsLadderActivateTop = false;
                }
            }
        }

        double side = (m[4] * (view.Y - high.Y)) + (m[7] * (view.Z - high.Z)) + (m[1] * (view.X - high.X));
        if (side < 0.0 && context.Resolve(high, this))
        {
            context.Take(this);
            if (context.Activator is IIgi2LadderClimber top)
            {
                top.IsLadderActivateTop = true;
            }
        }
    }
}

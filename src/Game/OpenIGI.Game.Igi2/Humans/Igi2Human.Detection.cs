using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Detection;

namespace OpenIGI.Game.Igi2.Humans;

/// <content>A human as detectors see it, <c>Human_AIDetectionGetInfoHandler</c> for the player and soldiers alike.</content>
public abstract partial class Igi2Human : IIgi2Detectable
{
    /// <summary>How far above the feet a detector looks for them, <c>409.6</c> (0.1 m).</summary>
    public const double DetectionFeetLift = 409.6000061035156;

    /// <inheritdoc/>
    public Vector3d DetectablePosition => Position;

    /// <inheritdoc/>
    /// <remarks>The body's radius as the world holds it.</remarks>
    public float DetectableRadius => ModelRadius;

    /// <inheritdoc/>
    float? IIgi2Detectable.VisibilityFactor => DetectionVisibility;

    /// <summary>Gets how far off it may be seen, as a share of a cone's length (<c>Human + 0x7C8</c>).</summary>
    protected virtual float DetectionVisibility => 1f;

    /// <summary>Gets a value indicating whether it takes part in the level, <c>Human.isActive</c>.</summary>
    protected virtual bool IsDetectionActive => true;

    /// <inheritdoc/>
    public bool IsGone => false;

    /// <summary>
    /// Gets where the eyes are by stance alone, <c>HumanView_GetOriginalViewPosition</c> (<c>igi2 0x4D7150</c>): above the feet by
    /// 6922.24 standing, 4096 crouching and 1228.8 lying flat.
    /// </summary>
    public Vector3d OriginalViewPosition
    {
        get
        {
            Vector3d p = Position;
            double lift = Pose switch
            {
                Igi2HumanPose.Crouching => 4096.0,
                Igi2HumanPose.LyingFlat => 1228.800048828125,
                _ => 6922.240234375,
            };
            return new Vector3d(p.X, p.Y, ((Fpu)p.Z + lift).Value);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <c>Human_AIDetectionGetInfoHandler</c> (<c>igi2 0x4FFBA0</c>): a human of its side, alive unless dead, active as the script
    /// left it; its points its eyes, a tenth of a metre above its feet, and halfway between.
    /// </remarks>
    public Igi2DetectionInfo DetectionInfo()
    {
        Vector3d head = OriginalViewPosition;
        Vector3d p = Position;
        Vector3d feet = new(p.X, p.Y, ((Fpu)p.Z + DetectionFeetLift).Value);
        Vector3d middle = new(
            (((Fpu)head.X + feet.X) * 0.5).Value,
            (((Fpu)head.Y + feet.Y) * 0.5).Value,
            (((Fpu)head.Z + feet.Z) * 0.5).Value);
        return new Igi2DetectionInfo(Igi2DetectableKind.Human, head, middle, feet, Team, !IsDead, IsDetectionActive);
    }
}

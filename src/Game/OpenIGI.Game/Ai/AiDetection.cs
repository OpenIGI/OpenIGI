using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Ai;

/// <summary>The kinds of thing a detector looks for, each its type's bit (I.G.I. 2's <c>AIDetection_AllocateQTaskMask</c>).</summary>
[Flags]
public enum AiDetectableKind
{
    /// <summary>Nothing.</summary>
    None = 0,

    /// <summary>A human, <c>Human_bAIDetectionQTaskMask</c>.</summary>
    Human = 1 << 0,

    /// <summary>A security camera, <c>SCamera_bAIDetectionQTaskMask</c>.</summary>
    SecurityCamera = 1 << 1,
}

/// <summary>
/// What a detectable says of itself when a detector asks: the engine's detection-info event's context (Project I.G.I.'s
/// <c>Human</c> answers it at <c>0x45FE30</c>, I.G.I. 2's at <c>Human_AIDetectionGetInfoHandler</c>).
/// </summary>
/// <param name="Kind">Its kind, <c>bQTaskMask</c>.</param>
/// <param name="Head">Its first point, a human's eyes.</param>
/// <param name="Middle">Its second, halfway between.</param>
/// <param name="Feet">Its third, just above a human's feet.</param>
/// <param name="Team">Its side, <c>eTeam</c> (<c>Human+252</c>).</param>
/// <param name="IsAlive">Whether it lives, <c>isAlive</c>; a camera lives until destroyed.</param>
/// <param name="IsActive">Whether it takes part at all, <c>isActive</c> (Project I.G.I.'s <c>Human+3318</c>).</param>
/// <remarks>
/// Both games fill the same record: the kind, the side and the two flags, and the points a detector looks at. Project I.G.I.
/// gives one point; I.G.I. 2 gives three, so a detector can see a human whose head alone shows. A one-point answer is the
/// same point three times (<see cref="OnePoint"/>).
/// </remarks>
public readonly record struct AiDetectionInfo(
    AiDetectableKind Kind, Vector3d Head, Vector3d Middle, Vector3d Feet, int Team, bool IsAlive, bool IsActive)
{
    /// <summary>A one-point answer, as Project I.G.I.'s detectables give it.</summary>
    /// <param name="kind">Its kind.</param>
    /// <param name="point">The point.</param>
    /// <param name="team">Its side.</param>
    /// <param name="isAlive">Whether it lives.</param>
    /// <param name="isActive">Whether it takes part.</param>
    /// <returns>The answer.</returns>
    public static AiDetectionInfo OnePoint(AiDetectableKind kind, Vector3d point, int team, bool isAlive, bool isActive) =>
        new(kind, point, point, point, team, isAlive, isActive);

    /// <summary>Gets one of its three points.</summary>
    /// <param name="index">Nought, one or two.</param>
    /// <returns>The point.</returns>
    public Vector3d Point(int index) => index switch
    {
        0 => Head,
        1 => Middle,
        _ => Feet,
    };
}

/// <summary>Something any game's detectors may see: a member of the engine's detection list.</summary>
public interface IAiDetectable
{
    /// <summary>Gets where it is, its object's position.</summary>
    Vector3d DetectablePosition { get; }

    /// <summary>Gets its radius.</summary>
    float DetectableRadius { get; }

    /// <summary>Gets how far off it may be seen, a share of a cone's length, or <see langword="null"/> for a thing that is not a human.</summary>
    float? VisibilityFactor { get; }

    /// <summary>Gets a value indicating whether it no longer exists, so a detector lets go of it.</summary>
    bool IsGone { get; }

    /// <summary>Says what it is and where: the detection-info event.</summary>
    /// <returns>Its kind, points, side and state.</returns>
    AiDetectionInfo DetectionInfo();
}

using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;

namespace OpenIGI.Game.Player;

/// <summary>A title player that holds something in front of its eyes for the host to draw: its arms and what they carry.</summary>
/// <remarks>
/// Optional beside <see cref="ITitlePlayer"/>: the host asks for the frame after the world is drawn and hands it to the
/// title's own <c>ITitleViewModelPresenter</c>, which knows the title's models. A title player without one draws nothing
/// in front of the eyes.
/// </remarks>
public interface ITitleFirstPerson
{
    /// <summary>Gets what the eyes hold as of the last tick, or <see langword="null"/> when nothing is drawn.</summary>
    TitleFirstPersonView? FirstPerson { get; }
}

/// <summary>The first-person model as one tick left it: where it stands, its pose and the parts it draws.</summary>
/// <param name="Origin">Where the model stands, in world units.</param>
/// <param name="Orientation">Its orientation, the engine's: its columns are the model's axes in the world.</param>
/// <param name="ObjectFieldOfView">The tangent of half the width it is drawn at, its own projection's.</param>
/// <param name="Tick">The tick it was posed on; a part's pose changes only when this does.</param>
/// <param name="Parts">The models drawn, the first-person model first.</param>
public sealed record TitleFirstPersonView(
    Vector3d Origin,
    Matrix3f Orientation,
    float ObjectFieldOfView,
    long Tick,
    IReadOnlyList<TitleViewModelPart> Parts)
{
    /// <summary>Gets the current tick's share of a skinned pose; one disables presentation interpolation.</summary>
    public float PoseInterpolation { get; init; } = 1f;
}

/// <summary>One model the first-person view draws.</summary>
/// <param name="Model">The model's name, as the title's packs resolve it.</param>
/// <param name="Pose">The skeleton a skinned model is posed by, in the view model's frame and units; null for a rigid one.</param>
/// <param name="Position">Where a rigid model stands in the view model's frame, in world units.</param>
/// <param name="Orientation">A rigid model's orientation in the view model's frame.</param>
public sealed record TitleViewModelPart(string Model, BonePose? Pose, Vector3f Position, Matrix3f Orientation)
{
    /// <summary>Gets the previous tick's skin pose, when this part is presented between ticks.</summary>
    public BonePose? PreviousPose { get; init; }
}

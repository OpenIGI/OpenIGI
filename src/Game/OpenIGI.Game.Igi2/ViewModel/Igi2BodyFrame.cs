using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Animation;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Player;

namespace OpenIGI.Game.Igi2.ViewModel;

/// <summary>A human body as one tick left it, for drawing: its skinned model posed by its clips, and what its clips hang on it.</summary>
/// <remarks>The pose is the body's own (<see cref="Igi2HumanAnimation.Pose"/>), the one its collision is traced against.</remarks>
public static class Igi2BodyFrame
{
    /// <summary>Builds the models a body draws as this tick.</summary>
    /// <param name="human">The body.</param>
    /// <param name="pose">The body's pose, or <see langword="null"/> when it has none.</param>
    /// <param name="model">The body's model name.</param>
    /// <param name="heldModel">The weapon in hand's fixed model, or <see langword="null"/>.</param>
    /// <param name="tick">The tick the frame is for.</param>
    /// <param name="flashes">The flashes at the weapon objects, or <see langword="null"/>.</param>
    /// <returns>The body and what hangs on it; empty when the body has no pose.</returns>
    public static IReadOnlyList<TitleWorldModel> Build(
        Igi2Human human,
        Igi2BodyPose? pose,
        string model,
        string? heldModel,
        long tick,
        Weapons.Igi2MuzzleFlashes? flashes = null)
    {
        ArgumentNullException.ThrowIfNull(human);
        if (pose is null)
        {
            return [];
        }

        Matrix3f orientation = human.Orientation;
        List<TitleWorldModel> models = [new TitleWorldModel(model, human.Position, orientation, pose.Pose, human, tick)];
        if (pose.AttachClip is not { } attachClip)
        {
            return models;
        }

        int weaponObject = 0;
        foreach (Igi2AttachedPart part in Igi2AttachedParts.Of(attachClip, pose.AttachTime, pose.Pose, heldModel))
        {
            Vector3f local = orientation.Transposed().Transform(part.Position);
            Vector3d at = new(human.Position.X + local.X, human.Position.Y + local.Y, human.Position.Z + local.Z);
            models.Add(new TitleWorldModel(part.Model, at, orientation * part.Orientation)
            {
                MuzzleFlashes = IsWeaponObject(part, heldModel) ? flashes?.Present(weaponObject++) : null,
            });
        }

        return models;
    }

    /// <summary>Whether a part is a weapon object, the weapon in hand drawn where the clip hangs it.</summary>
    /// <param name="part">The part.</param>
    /// <param name="heldModel">The weapon in hand's fixed model, or <see langword="null"/>.</param>
    /// <returns>Whether the part is one; weapon objects are numbered in the clip's order.</returns>
    internal static bool IsWeaponObject(Igi2AttachedPart part, string? heldModel) =>
        heldModel is not null && string.Equals(part.Model, heldModel, StringComparison.OrdinalIgnoreCase);
}

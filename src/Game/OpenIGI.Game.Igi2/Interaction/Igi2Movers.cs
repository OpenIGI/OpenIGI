using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Game.Igi2.Interaction;

/// <summary>A placed object that moves while the level runs, as a door or a lift does, and is drawn where it now stands.</summary>
public interface IIgi2Mover
{
    /// <summary>Gets the level id of the task that placed it.</summary>
    int Id { get; }

    /// <summary>Gets the pose the world scene placed it at, which every drawn part of it was built against.</summary>
    Igi2Pose Built { get; }

    /// <summary>Gets where it stands now.</summary>
    Vector3d CurrentPosition { get; }

    /// <summary>Gets which way it faces now, row by row.</summary>
    IReadOnlyList<float> CurrentOrientation { get; }

    /// <summary>Gets where it stood at the start of the tick.</summary>
    Vector3d LastPosition { get; }

    /// <summary>Gets which way it faced at the start of the tick, row by row.</summary>
    IReadOnlyList<float> LastOrientation { get; }
}

/// <summary>A child that follows its parent when the parent moves: its <c>Attach_eUpdateAttachmentEvent</c> handler.</summary>
public interface IIgi2Attached
{
    /// <summary>Moves to where its parent now holds it.</summary>
    void UpdateAttachment();
}

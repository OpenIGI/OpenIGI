using OpenIGI.Core.Graphics;

namespace OpenIGI.Game.Igi1.World;

/// <summary>
/// The player's view, taken by a mod for a while through <see cref="MissionWorld.ViewTakeover"/>: a scripted moment's
/// camera in place of the player's own eye. <b>Not retail.</b>
/// </summary>
/// <param name="Eye">
/// The eye a frame is drawn from, by how far the frame is between ticks (nought to one), in metres as the player's own
/// camera is.
/// </param>
public sealed record ViewTakeover(Func<float, Camera3D> Eye)
{
    /// <summary>Gets a value indicating whether the player's weapon in hand is hidden while the view is taken.</summary>
    public bool HidesViewModel { get; init; } = true;

    /// <summary>Gets a value indicating whether the first-person HUD (crosshair, weapon and ammunition) is hidden while the view is taken.</summary>
    public bool HidesHud { get; init; } = true;
}

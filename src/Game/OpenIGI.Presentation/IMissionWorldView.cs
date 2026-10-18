using OpenIGI.Core.Numerics;
using OpenIGI.Game.Weapons.Ballistics;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation;

/// <summary>What a title's player has put in the world, drawn among it: the projectiles in the air and the marks its rounds left.</summary>
public interface IMissionWorldView
{
    /// <summary>Gets the rounds in flight whose streaks are drawn, or <see langword="null"/> when there is no player to have fired any.</summary>
    BallisticSimulation? Ballistics { get; }

    /// <summary>Draws the grenades, rockets and launcher rounds in the air.</summary>
    /// <param name="grenades">The grenades' presenter, or <see langword="null"/>.</param>
    /// <param name="missiles">The rockets' presenter, or <see langword="null"/>.</param>
    /// <param name="eye">The eye, in metres.</param>
    /// <param name="projection">The world's projection.</param>
    /// <param name="tickBlend">How far the frame is between ticks.</param>
    void DrawProjectiles(IGrenadePresenter? grenades, IMissilePresenter? missiles, in Camera3D eye, in Matrix4f projection, float tickBlend);

    /// <summary>Draws the marks the player's rounds have left.</summary>
    /// <param name="marks">The marks' presenter.</param>
    /// <param name="eye">The eye, in metres.</param>
    /// <param name="projection">The world's projection.</param>
    /// <param name="poses">Where the world's placements are drawn this frame, which the marks ride.</param>
    void DrawMarks(IBulletMarkPresenter marks, in Camera3D eye, in Matrix4f projection, PlacementPose? poses);
}

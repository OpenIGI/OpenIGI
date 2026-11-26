using OpenIGI.Core.Numerics;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi2.Player;

namespace OpenIGI.Game.Igi2.Multiplayer;

/// <summary>Where an I.G.I. 2 player's name and a fallen one's mark stand, for <see cref="Game.Multiplayer.ICoopMarks"/>.</summary>
internal static class Igi2CoopMarks
{
    // A name stands a little above the eyes; a fallen body's mark a little above where it lies. Metres.
    private const double NameLift = 0.35;
    private const double ReviveLift = 0.7;
    private const double UnitsPerMetre = 4096.0;

    /// <summary>A living player's name over its head.</summary>
    /// <param name="human">The player.</param>
    /// <param name="name">Its name.</param>
    /// <param name="health">Its health, in <c>[0,1]</c>.</param>
    /// <returns>The tag.</returns>
    public static WorldNameTag Tag(Igi2HumanPlayer human, string name, float health)
    {
        Vector3d eye = human.View.Position / UnitsPerMetre;
        return new WorldNameTag(new Vector3d(eye.X, eye.Y, eye.Z + NameLift), name, health);
    }

    /// <summary>A fallen player's mark where it lies.</summary>
    /// <param name="human">The player.</param>
    /// <param name="name">Its name.</param>
    /// <param name="progress">How far its revival has gone, in <c>[0,1]</c>.</param>
    /// <returns>The mark.</returns>
    public static ReviveMarker Mark(Igi2HumanPlayer human, string name, float progress)
    {
        Vector3d feet = human.Position / UnitsPerMetre;
        return new ReviveMarker(new Vector3d(feet.X, feet.Y, feet.Z + ReviveLift), name, progress);
    }

    /// <summary>A human's health as a share of whole.</summary>
    /// <param name="human">The human.</param>
    /// <returns>The share, in <c>[0,1]</c>.</returns>
    public static float HealthOf(Igi2HumanPlayer human) =>
        human.Damage.MaxDamage > 0 ? Math.Clamp(1f - (human.Damage.Damage / human.Damage.MaxDamage), 0f, 1f) : 1f;
}

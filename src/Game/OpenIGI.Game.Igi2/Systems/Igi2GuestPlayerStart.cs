using OpenIGI.Core.Numerics;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Player;

namespace OpenIGI.Game.Igi2.Systems;

/// <summary>Who an <see cref="Igi2GuestLevel"/>'s player is: where the host stands it, what it looks like and what it carries.</summary>
/// <param name="Position">Where it stands, in world units.</param>
/// <param name="Gamma">The way it faces, in radians.</param>
/// <param name="Difficulty">The difficulty it plays at.</param>
public sealed record Igi2GuestPlayerStart(Vector3d Position, float Gamma, AiDifficulty Difficulty)
{
    /// <summary>The model I.G.I. 2 dresses its player in: David Jones.</summary>
    public const string JonesModel = "jones_1";

    /// <summary>Gets the model it wears, seen in the third person, by the shadow it casts and in a mirror.</summary>
    public string Model { get; init; } = JonesModel;

    /// <summary>Gets the model its first-person view carries, or empty for I.G.I. 2's own (<see cref="Player.Igi2Gameplay.FirstPersonFallbackModel"/>).</summary>
    public string FirstPersonModel { get; init; } = string.Empty;

    /// <summary>Gets what it carries, as a level's <c>Weapon</c> script gives it (<c>Human_AddWeapon("WEAPON_ID_AK47"), ...</c>).</summary>
    public string WeaponScript { get; init; } = string.Empty;

    /// <summary>Gets where its sounds are heard, or <see langword="null"/> where nothing is.</summary>
    public ITitleAudio? Audio { get; init; }
}

using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Weapons;

namespace OpenIGI.Game.Igi1.Multiplayer;

/// <summary>
/// The bomb the bomb modes plant, as a weapon its carrier carries: in the cycle, the number keys' reach and the weapon wheel
/// like any other, held up on the arms with I.G.I. 2's own C4 clips (<see cref="Igi1BombAnimations"/>), and planted by holding
/// fire or use at a site while it is in hand.
/// </summary>
public static class Igi1BombWeapon
{
    /// <summary>Its weapon id, past the retail roster's.</summary>
    public const int Id = 200;

    /// <summary>Its script id, which also picks its icon.</summary>
    public const string ScriptId = "WEAPON_ID_C4BOMB";

    /// <summary>Gets the weapon.</summary>
    public static WeaponType Type { get; } = new()
    {
        WeaponId = Id,
        ScriptId = ScriptId,
        DisplayName = "C4 BOMB",
        Description = "Plant it at a bomb site: hold fire or use there with it in hand.",
        Category = WeaponCategory.Bomb,
        AmmoDisplay = AmmoDisplayType.None,
        CalibreId = -1,
        StandAnimation = Igi1BombAnimations.FirstPersonHold,
        MoveAnimation = Igi1BombAnimations.FirstPersonWalk,
        FireAnimation1 = -1,
        FireAnimation2 = -1,
        FireAnimation3 = -1,
        ReloadAnimation = -1,
        UpperBodyStandAnimation = Igi1BombAnimations.Hold,
        UpperBodyWalkAnimation = Igi1BombAnimations.Hold,
        UpperBodyRunAnimation = Igi1BombAnimations.Hold,
        UpperBodyCrouchAnimation = Igi1BombAnimations.Hold,
        UpperBodyCrouchRunAnimation = Igi1BombAnimations.Hold,
        UpperBodyFireAnimation = -1,
        UpperBodyReloadAnimation = -1,
        Index = -1,
    };

    /// <summary>Gets the charge's shape for a thrown bomb's flight, from the kit's model; <see langword="null"/> if it cannot be read.</summary>
    public static OpenIGI.Game.Physics.DroppedGunCollider? Collider => Shape.Value;

    private static readonly Lazy<OpenIGI.Game.Physics.DroppedGunCollider?> Shape = new(() =>
    {
        try
        {
            return OpenIGI.Game.Multiplayer.Modes.BombKit.Model(OpenIGI.Game.Multiplayer.Modes.BombKit.HeldModel) is { } bytes
                && OpenIGI.Game.World.MefMeshBuilder.Build(OpenIGI.Formats.World.MefModel.Read(bytes, OpenIGI.Formats.AssetFormatProfile.Igi2)) is { Vertices.Length: > 0 } mesh
                ? new OpenIGI.Game.Physics.DroppedGunCollider(mesh.Vertices.Select(static v => v.Position))
                : null;
        }
        catch (Exception error) when (error is FormatException or ArgumentException or InvalidDataException)
        {
            return null;
        }
    });

    /// <summary>Gets whether a player holds the bomb in hand.</summary>
    /// <param name="player">The player.</param>
    /// <returns><see langword="true"/> when the bomb is the weapon in its hand.</returns>
    public static bool Holds(PlayerCamera player) => player?.Weapons?.InHand?.Weapon.WeaponId == Id;

    /// <summary>Gives a player the bomb, or takes it away, as the mode has it carrying it or not.</summary>
    /// <param name="player">The player.</param>
    /// <param name="carries">Whether it carries the bomb.</param>
    public static void Keep(PlayerCamera player, bool carries)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (player.Weapons is not { } weapons)
        {
            return;
        }

        bool has = weapons.HasWeapon(Id);
        if (carries && !has)
        {
            _ = player.Carry(Type);
            weapons.EnsureCycleContains(Id);
        }
        else if (!carries && has)
        {
            weapons.Drop(Id);
        }
    }
}

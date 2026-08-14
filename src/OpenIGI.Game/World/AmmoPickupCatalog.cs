using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;

namespace OpenIGI.Game.World;

/// <summary>Maps the retail ammunition pickup meshes to their inventory slots.</summary>
/// <remarks>
/// The original pickup task carries the ammunition identity in the model selected by its level script.
/// The model is presentation data in the port, but it is still the authoritative join: the shipped
/// QVM ammo table names these same instances when it creates the world pickup. Keeping the mapping here
/// lets every mission use the same collection path as a weapon pickup without guessing from the held gun.
/// </remarks>
public static class AmmoPickupCatalog
{
    private static readonly Dictionary<string, IReadOnlyList<string>> AmmoByModel =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["115_01_1"] = ["AMMO_ID_1000", "AMMO_ID_556"], // shared retail box mesh
            ["114_01_1"] = ["AMMO_ID_12"],
            ["118_01_1"] = ["AMMO_ID_357"],
            ["120_01_1"] = ["AMMO_ID_44"],
            ["119_01_1"] = ["AMMO_ID_762"],
            ["116_01_1"] = ["AMMO_ID_919"],
            ["137_01_1"] = ["AMMO_ID_DRAGUNOV", "AMMO_ID_FLASHBANG"],
            ["135_01_1"] = ["AMMO_ID_GRENADE"],
            ["108_05_1"] = ["AMMO_ID_M203"],
            ["104_01_1"] = ["AMMO_ID_MEDIPACK"],
            ["136_01_1"] = ["AMMO_ID_PROXIMITYMINE"],
        };

    /// <summary>Resolves a pickup using its explicit task ammo value before falling back to the model.</summary>
    public static bool TryResolve(GenericPickup pickup, AmmoTypeRegistry registry, out AmmoType ammo)
    {
        ArgumentNullException.ThrowIfNull(pickup);
        ArgumentNullException.ThrowIfNull(registry);

        if (pickup.Task.TryGetValue("Ammo", out object? rawAmmo))
        {
            if (rawAmmo is int id && registry.TryGet(id, out ammo!))
            {
                return true;
            }

            if (rawAmmo is string script && registry.TryGetByScriptId(script, out ammo!))
            {
                return true;
            }
        }

        if (pickup.ModelName is not null
            && AmmoByModel.TryGetValue(pickup.ModelName, out IReadOnlyList<string>? scripts))
        {
            foreach (string script in scripts)
            {
                if (registry.TryGetByScriptId(script, out ammo!))
                {
                    return true;
                }
            }
        }

        ammo = null!;
        return false;
    }

    /// <summary>Returns the conservative fallback payload used only when a task has no amount field.</summary>
    /// <remarks>Authored task values take precedence; this keeps malformed/synthetic tasks usable.</remarks>
    public static int DefaultAmount(int ammoId) => ammoId switch
    {
        PlayerWeapons.MedipackAmmoId => 1,
        0 or 1 or 5 or 6 => 50,
        _ => 1,
    };


    /// <summary>Reads the amount authored on a generic pickup, or returns its model's fallback payload.</summary>
    public static int Amount(GenericPickup pickup, int ammoId)
    {
        ArgumentNullException.ThrowIfNull(pickup);

        // "Ammo" identifies the inventory slot on the authored task; it is not the number of rounds.
        foreach (string parameter in new[] { "Amount", "Rounds", "Count" })
        {
            if (pickup.Task.TryGetValue(parameter, out object? raw))
            {
                int value = raw switch
                {
                    int integer => integer,
                    float real => (int)MathF.Round(real),
                    double doubleValue => (int)Math.Round(doubleValue),
                    _ => 0,
                };
                if (value > 0)
                {
                    return value;
                }
            }
        }

        return DefaultAmount(ammoId);
    }
}

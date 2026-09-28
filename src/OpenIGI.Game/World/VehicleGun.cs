using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;
using OpenIGI.Game.Weapons;

namespace OpenIGI.Game.World;

/// <summary>One gun an authored vehicle carries, resolved from its model's attachments.</summary>
/// <param name="Mount">The gun's authored limits (<c>magicobj.qvm</c>).</param>
/// <param name="Weapon">The weapon row the mount's weapon model names (<c>0x413C00</c>).</param>
/// <param name="Offset">Where the gun sits in the vehicle's frame, in world units.</param>
public sealed record VehicleGun(MagicGunMount Mount, WeaponType Weapon, Vector3d Offset)
{
    private const int MaximumNesting = 4;

    /// <summary>Gets whether the weapon row's projectile is an instant gunshot rather than a missile.</summary>
    public bool IsHitscan => string.Equals(Weapon.Projectile.TypeName, "TASKTYPE_GUNSHOT", StringComparison.Ordinal);

    /// <summary>Finds every gun a vehicle model carries, including guns mounted on guns.</summary>
    /// <param name="model">The vehicle's model name.</param>
    /// <param name="models">Resolves a model name to its data, or <see langword="null"/> when it is not loaded.</param>
    /// <param name="magic">The magic-object table the attachments are looked up in.</param>
    /// <param name="roster">The weapon rows, looked up by their model.</param>
    /// <param name="unresolved">Receives each gun whose weapon model no row uses, by attachment name.</param>
    /// <returns>The guns, outermost first; a gun on a gun sits at the sum of both attachment offsets.</returns>
    /// <remarks>
    /// A car spawns its model's attachments at <c>0x4F4800</c> through <c>0x4C4950</c>, and an
    /// <c>AIStationaryGun</c>'s constructor (<c>0x46EAE0</c>) spawns its own model's attachments the same
    /// way, which is how a T80's coaxial M2HB rides on its turret.
    /// </remarks>
    public static IReadOnlyList<VehicleGun> FromModel(
        string model,
        Func<string, MefModel?> models,
        MagicObjectRegistry magic,
        WeaponTypeRegistry roster,
        Action<string, string>? unresolved = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(models);
        ArgumentNullException.ThrowIfNull(magic);
        ArgumentNullException.ThrowIfNull(roster);

        List<VehicleGun> guns = [];
        Collect(model, Vector3d.Zero, 0);
        return guns;

        void Collect(string name, Vector3d offset, int depth)
        {
            if (depth > MaximumNesting || models(name) is not { } mef)
            {
                return;
            }

            foreach (MefAttachment attachment in mef.Attachments)
            {
                if (!magic.TryGet(attachment.Name, out MagicObjectDefinition definition) || definition.Gun is not { } mount)
                {
                    continue;
                }

                Vector3d at = offset + new Vector3d(attachment.Position.X, attachment.Position.Y, attachment.Position.Z);
                if (roster.TryGetByGunModel(mount.WeaponModel, out WeaponType row))
                {
                    guns.Add(new VehicleGun(mount, row, at));
                }
                else
                {
                    unresolved?.Invoke(attachment.Name, mount.WeaponModel);
                }

                Collect(definition.Model, at, depth + 1);
            }
        }
    }
}

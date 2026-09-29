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

    /// <summary>Gets the mount's own model, the placement the gun turns (<c>614_02_1</c> for a T80's turret).</summary>
    public string Model { get; init; } = string.Empty;

    /// <summary>Gets the index of the gun this one rides on, or -1 when it rides on the vehicle.</summary>
    public int Parent { get; init; } = -1;

    /// <summary>Gets where the mount is attached, in its parent's frame (<c>+240</c>).</summary>
    public Vector3f AttachmentPosition { get; init; }

    /// <summary>Gets the mount's attached orientation in its parent's frame, before its traverse (<c>+264</c>).</summary>
    public Matrix3f AttachmentOrientation { get; init; } = Matrix3f.Identity;

    /// <summary>
    /// Gets where the weapon model sits on the mount, in the mount's frame: its magic vertex 1, or the origin when
    /// the mount model has fewer than two (<c>0x414E20</c>).
    /// </summary>
    public Vector3f WeaponVertex { get; init; }

    /// <summary>Gets whether the weapon row's projectile is an instant gunshot rather than a missile.</summary>
    public bool IsHitscan => string.Equals(Weapon.Projectile.TypeName, "TASKTYPE_GUNSHOT", StringComparison.Ordinal);

    /// <summary>Finds every gun a vehicle model carries, including guns mounted on guns.</summary>
    /// <param name="model">The vehicle's model name.</param>
    /// <param name="models">Resolves a model name to its data, or <see langword="null"/> when it is not loaded.</param>
    /// <param name="magic">The magic-object table the attachments are looked up in.</param>
    /// <param name="roster">The weapon rows, looked up by their model.</param>
    /// <param name="unresolved">Receives each gun whose weapon model no row uses, by attachment name.</param>
    /// <returns>
    /// The guns, outermost first; a gun on a gun sits at the sum of both attachment offsets, and names the gun it
    /// rides on as its <see cref="Parent"/>.
    /// </returns>
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
        Collect(model, Vector3d.Zero, Matrix3f.Identity, 0, -1);
        return guns;

        // Each attachment sits in its parent's frame: a gun on a gun is rotated with the gun it rides on.
        // Mission 7's T80 turret is attached turned half a revolution, which puts its coaxial gun on the
        // other side of the barrel from where the coaxial attachment's own offset reads -- the side retail's
        // gun aims from (probed: the coaxial gun's first traverse is +0.064 rad, the cannon's -0.004).
        void Collect(string name, Vector3d offset, Matrix3f orientation, int depth, int parent)
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

                Vector3f local = orientation.Transposed().Transform(attachment.Position);
                Vector3d at = offset + new Vector3d(local.X, local.Y, local.Z);
                Matrix3f nested = orientation * attachment.Orientation;
                int index = -1;
                if (roster.TryGetByGunModel(mount.WeaponModel, out WeaponType row))
                {
                    // A gun whose parent gun has no weapon row rides on the vehicle, at the attachments' sum.
                    bool onGun = parent >= 0;
                    Vector3f weaponVertex = default;
                    if (models(definition.Model) is { MagicVertexCount: > 1 } mountModel)
                    {
                        mountModel.TryGetMagicVertex(1, out weaponVertex);
                    }

                    index = guns.Count;
                    guns.Add(new VehicleGun(mount, row, at)
                    {
                        Model = definition.Model,
                        Parent = parent,
                        AttachmentPosition = onGun ? attachment.Position : new Vector3f((float)at.X, (float)at.Y, (float)at.Z),
                        AttachmentOrientation = onGun ? attachment.Orientation : nested,
                        WeaponVertex = weaponVertex,
                    });
                }
                else
                {
                    unresolved?.Invoke(attachment.Name, mount.WeaponModel);
                }

                Collect(definition.Model, at, nested, depth + 1, index);
            }
        }
    }
}

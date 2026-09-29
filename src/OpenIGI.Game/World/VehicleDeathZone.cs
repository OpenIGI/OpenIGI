using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;

namespace OpenIGI.Game.World;

/// <summary>The volume a vehicle kills a human in by driving over them, in the vehicle's frame.</summary>
/// <remarks>
/// <para>
/// Every vehicle model carries a <c>TASKTYPE_DEATHZONE</c> attachment (the T80's <c>614_07_1</c>, the
/// APC's <c>601_07_1</c>, the truck's <c>610_04_1</c>). The class (<c>0x465BC0</c>) registers the zone's
/// model into the collision world and re-seats it on its vehicle every tick (<c>0x465F50</c>). A human it
/// touches runs <c>0x45FA80</c>: once inside the zone's bounding sphere, a segment from the human to the
/// zone's centre is cast against the zone's own model, and a human with no zone surface in between --
/// one inside the zone -- takes 10000 damage to zone 13 if the vehicle is moving at 50 units a tick or
/// more. Probed in Mission 7: APC 352 killed a player standing in its road on contact.
/// </para>
/// <para>
/// The port takes the zone's volume as the bounds of its model's collision mesh, placed at the
/// attachment's offset.
/// </para>
/// </remarks>
/// <param name="Minimum">The volume's lower corner in the vehicle's frame, in world units.</param>
/// <param name="Maximum">The volume's upper corner in the vehicle's frame, in world units.</param>
public sealed record VehicleDeathZone(Vector3d Minimum, Vector3d Maximum)
{
    /// <summary>The damage a run-over deals (<c>0x45FA80</c>: 10000, far past any human's maximum).</summary>
    public const float Damage = 10000f;

    /// <summary>The damage zone a run-over is booked to (<c>0x45FA80</c> writes <c>+744 = 13</c>).</summary>
    public const int Zone = 13;

    /// <summary>The speed below which a zone touches without harming, in world units per tick.</summary>
    public const double LethalSpeedUnitsPerTick = 50d;

    /// <summary>Gets whether a point in the vehicle's frame lies inside the volume.</summary>
    public bool Contains(Vector3d local) =>
        local.X >= Minimum.X && local.X <= Maximum.X
        && local.Y >= Minimum.Y && local.Y <= Maximum.Y
        && local.Z >= Minimum.Z && local.Z <= Maximum.Z;

    /// <summary>Finds the death zones a vehicle model carries.</summary>
    /// <param name="model">The vehicle's model name.</param>
    /// <param name="models">Resolves a model name to its data, or <see langword="null"/> when it is not loaded.</param>
    /// <param name="magic">The magic-object table the attachments are looked up in.</param>
    /// <returns>Each zone's volume in the vehicle's frame.</returns>
    public static IReadOnlyList<VehicleDeathZone> FromModel(
        string model,
        Func<string, MefModel?> models,
        MagicObjectRegistry magic)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(models);
        ArgumentNullException.ThrowIfNull(magic);

        List<VehicleDeathZone> zones = [];
        if (models(model) is not { } vehicle)
        {
            return zones;
        }

        foreach (MefAttachment attachment in vehicle.Attachments)
        {
            if (!magic.TryGet(attachment.Name, out MagicObjectDefinition definition)
                || !string.Equals(magic.TaskTypeName(definition.TaskType), "TASKTYPE_DEATHZONE", StringComparison.Ordinal)
                || models(definition.Model) is not { } zoneModel
                || !MefCollisionMesh.TryRead(zoneModel.Container, out MefCollisionMesh? mesh)
                || mesh is null
                || mesh.Vertices.Count == 0)
            {
                continue;
            }

            Vector3d offset = new(attachment.Position.X, attachment.Position.Y, attachment.Position.Z);
            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
            foreach (Vector3f vertex in mesh.Vertices)
            {
                minX = Math.Min(minX, vertex.X); minY = Math.Min(minY, vertex.Y); minZ = Math.Min(minZ, vertex.Z);
                maxX = Math.Max(maxX, vertex.X); maxY = Math.Max(maxY, vertex.Y); maxZ = Math.Max(maxZ, vertex.Z);
            }

            zones.Add(new VehicleDeathZone(
                offset + new Vector3d(minX, minY, minZ),
                offset + new Vector3d(maxX, maxY, maxZ)));
        }

        return zones;
    }
}

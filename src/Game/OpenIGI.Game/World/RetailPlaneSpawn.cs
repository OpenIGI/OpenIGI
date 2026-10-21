using OpenIGI.Game.Physics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.World;

namespace OpenIGI.Game.World;

/// <summary>A plane's placement in the level: the Plane task's declared parameters its physics reads.</summary>
/// <param name="Position">"Position", world units.</param>
/// <param name="Orientation">"Orientation": α, β, γ.</param>
/// <param name="OriginalThrust">"Original Thrust" (<c>+1856</c>).</param>
/// <param name="Speed">"Speed" (<c>+384</c>): the velocity the reset gives it, or none for the take-off speed.</param>
/// <param name="Gear">"Gear" (<c>+2097</c>): the gear starts down.</param>
/// <param name="Canopy">"Canopy" (<c>+2096</c>).</param>
public readonly record struct RetailPlanePlacement(
    Vector3d Position,
    Vector3f Orientation,
    float OriginalThrust,
    Vector3f Speed,
    bool Gear,
    bool Canopy);

/// <summary>Builds a <see cref="RetailPlaneBody"/> the way the original creates a plane and resets it.</summary>
public static class RetailPlaneSpawn
{
    /// <summary>
    /// The fixed definition from the parsed Plane type: the shared header (mass, box, sleep thresholds with no body
    /// points), the record from argument 12 and its surfaces from argument 23, then the elevons <c>0x433580</c> derives.
    /// </summary>
    /// <param name="physics">The parsed type.</param>
    /// <returns>The definition.</returns>
    public static RetailPlaneDefinition Definition(in PlanePhysicsDefinition physics)
    {
        PlanePhysicsDefinition type = physics;
        float F(int index) => type.ArgumentAt(index).ToSingle();
        // The vehicle's part of the type (VehicleType_DefineVehicleType) from argument 12: its wings readied by 0x4EC4D0.
        int next = 12;
        Wing[] surfaces = [.. VehicleTypeDefinition.Parse<X87>(physics.Arguments, ref next).Wings];
        int count = surfaces.Length;

        // 0x433580: surfaces 3 and 4 become half of surface 2, 4 m either side and mirrored fore and aft, and are
        // written back into the type's record, where the tick reads their coefficients and normals.
        if (count >= 5)
        {
            Wing lift = surfaces[2];
            float half = ((X87)lift.Area * 0.5).F;
            float y = -lift.Position.Y;
            surfaces[3] = new Wing(half, new Vector3f(16384.0f, y, lift.Position.Z), lift.Normal);
            surfaces[4] = new Wing(half, new Vector3f(-16384.0f, y, lift.Position.Z), lift.Normal);
        }

        float[] record = new float[10];
        for (int k = 0; k < record.Length; k++)
        {
            record[k] = F(12 + k);
        }

        return new RetailPlaneDefinition
        {
            Mass = F(2),
            SleepSpeed = F(10),
            SleepSpin = F(11),
            Record = record,
            Surfaces = surfaces,
        };
    }

    /// <summary>Reads a Plane task's physics parameters.</summary>
    /// <param name="task">The Plane task.</param>
    /// <returns>The placement.</returns>
    public static RetailPlanePlacement Placement(DeclaredTask task)
    {
        ArgumentNullException.ThrowIfNull(task);
        object? Named(string compact)
        {
            string wanted = compact.Replace(" ", string.Empty, StringComparison.Ordinal);
            for (int i = 0; i < task.Type.Parameters.Count; i++)
            {
                if (task.Type.Parameters[i].Name.Replace(" ", string.Empty, StringComparison.Ordinal)
                    .Equals(wanted, StringComparison.OrdinalIgnoreCase))
                {
                    return task.GetValue(i);
                }
            }

            return null;
        }

        static bool Flag(object? value) => value switch
        {
            bool b => b,
            int i => i != 0,
            float f => f != 0f,
            _ => false,
        };

        return new RetailPlanePlacement(
            Named("Position") is Vector3d position ? position : default,
            Named("Orientation") is Vector3f orientation ? orientation : default,
            Named("Original thrust") is float thrust ? thrust : 0.0f,
            Named("Speed") is Vector3f speed ? speed : default,
            Flag(Named("Gear")),
            Flag(Named("Canopy")));
    }

    /// <summary>Creates a plane with its gear legs from its model's attachments, reset at its placement.</summary>
    /// <param name="physics">The parsed Plane type.</param>
    /// <param name="model">The airframe model.</param>
    /// <param name="placement">The level parameters.</param>
    /// <param name="magic">Resolves an attachment's magic-object definition.</param>
    /// <param name="models">Resolves a leg's model, for its wheel vertex.</param>
    /// <returns>The body, as the mode-0 packet leaves it.</returns>
    public static RetailPlaneBody Create(
        in PlanePhysicsDefinition physics,
        MefModel model,
        in RetailPlanePlacement placement,
        Func<string, MagicObjectDefinition?> magic,
        Func<string, MefModel?> models)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(magic);
        ArgumentNullException.ThrowIfNull(models);
        RetailPlaneBody plane = new(Definition(physics))
        {
            ModelRadius = model.NativeMarkerBound,
        };
        plane.Dimensions[0] = plane.Dimensions[1] = plane.Dimensions[2] = 1f;
        plane.RunCollide = true;

        // The model's attachments, in ATTA order: each Gear becomes a leg, touching at its model's magic vertex 0.
        foreach (MefAttachment attachment in model.Attachments)
        {
            if (magic(attachment.Name) is not { Gear: { } record } definition
                || models(definition.Model) is not { } legModel
                || !legModel.TryGetMagicVertex(0, out Vector3f vertex))
            {
                continue;
            }

            plane.Gears.Add(new RetailPlaneGear(
                new Vector3d(attachment.Position.X, attachment.Position.Y, attachment.Position.Z),
                AttachmentBasis(attachment.Orientation),
                record,
                vertex));
        }

        Reset(plane, physics, placement);
        foreach (RetailPlaneGear gear in plane.Gears)
        {
            gear.Seat(plane.GearDown != 0);
        }

        return plane;
    }

    /// <summary>
    /// The frame <c>0x4DFA50</c> rebuilds from an attachment's stored matrix before it spawns the child: its first column
    /// normalised, then <c>(c × d) × c</c> from its first two columns, then the cross of those two, each normalised. A
    /// mirrored attachment comes out a proper rotation -- the SU-27's left main leg is stored as <c>diag(−1, 1, 1)</c>
    /// and hangs as <c>diag(−1, 1, −1)</c>.
    /// </summary>
    /// <param name="o">The stored matrix.</param>
    /// <returns>The rebuilt frame, row-major.</returns>
    internal static float[] AttachmentBasis(Matrix3f o)
    {
        float cx = o.M11, cy = o.M21, cz = o.M31;
        float dx = o.M12, dy = o.M22, dz = o.M32;
        X87 ex = (X87)dz * cy - (X87)dy * cz;
        X87 ey = -((X87)dz * cx - (X87)dx * cz);
        X87 ez = (X87)dy * cx - (X87)dx * cy;
        float[] n = new float[9];
        n[0] = cx;
        n[1] = cy;
        n[2] = cz;
        if (!(cx == 0f && cy == 0f && cz == 0f))
        {
            X87 inverse = (X87)1.0 / X87.Sqrt((X87)cx * cx + (X87)cy * cy + (X87)cz * cz);
            n[0] = ((X87)cx * inverse).F;
            n[1] = ((X87)n[1] * inverse).F;
            n[2] = ((X87)n[2] * inverse).F;
        }

        float r1x = (ey * cz - ez * cy).F;
        float r1y = (-(ex * cz - ez * cx)).F;
        float r1z = (ex * cy - ey * cx).F;
        n[3] = r1x;
        n[4] = r1y;
        n[5] = r1z;
        float r2x = ((X87)r1z * n[1] - (X87)r1y * n[2]).F;
        float r2y = (-((X87)r1z * n[0] - (X87)r1x * n[2])).F;
        float r2z = ((X87)r1y * n[0] - (X87)r1x * n[1]).F;
        n[6] = r2x;
        n[7] = r2y;
        n[8] = r2z;
        if (!(r1x == 0f && r1y == 0f && r1z == 0f))
        {
            X87 inverse = (X87)1.0 / X87.Sqrt((X87)r1z * r1z + (X87)r1y * r1y + (X87)r1x * r1x);
            n[3] = ((X87)r1x * inverse).F;
            n[4] = ((X87)n[4] * inverse).F;
            n[5] = ((X87)n[5] * inverse).F;
        }

        if (!(r2x == 0f && r2y == 0f && r2z == 0f))
        {
            X87 inverse = (X87)1.0 / X87.Sqrt((X87)r2x * r2x + (X87)r2y * r2y + (X87)r2z * r2z);
            n[6] = ((X87)r2x * inverse).F;
            n[7] = ((X87)n[7] * inverse).F;
            n[8] = ((X87)n[8] * inverse).F;
        }

        return n;
    }

    /// <summary>The mode-0 reset at the placement.</summary>
    /// <param name="plane">The plane.</param>
    /// <param name="physics">Its parsed type.</param>
    /// <param name="placement">Its level parameters.</param>
    public static void Reset(RetailPlaneBody plane, in PlanePhysicsDefinition physics, in RetailPlanePlacement placement)
    {
        ArgumentNullException.ThrowIfNull(plane);
        plane.OriginalThrust = placement.OriginalThrust;
        plane.InitialGear = placement.Gear ? (byte)1 : (byte)0;
        plane.InitialCanopy = placement.Canopy ? (byte)1 : (byte)0;
        Vector3f dimensions = new(
            physics.ArgumentAt(3).ToSingle(),
            physics.ArgumentAt(4).ToSingle(),
            physics.ArgumentAt(5).ToSingle());
        plane.Reset(
            placement.Position,
            RetailSplineSegment.AnglesToMatrix(placement.Orientation.X, placement.Orientation.Y, placement.Orientation.Z),
            dimensions,
            placement.Speed);
    }
}

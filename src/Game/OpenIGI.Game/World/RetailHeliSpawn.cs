using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.World;

namespace OpenIGI.Game.World;

/// <summary>A helicopter's placement in the level: the Heli task's declared parameters its physics reads.</summary>
/// <param name="Position">"Position", world units.</param>
/// <param name="Orientation">"Orientation": α, β, γ.</param>
/// <param name="OriginalThrust">"Original Thrust": the collective the reset starts at (1.0 when absent).</param>
/// <param name="Speed">"Speed": the body-frame velocity the reset gives it.</param>
public readonly record struct RetailHeliPlacement(Vector3d Position, Vector3f Orientation, float OriginalThrust, Vector3f Speed);

/// <summary>Builds a <see cref="RetailHeliBody"/> the way the original creates a helicopter and resets it.</summary>
public static class RetailHeliSpawn
{
    /// <summary>The fixed definition from the parsed Heli type (<c>0x433030</c>: arg 12 is <c>R + 0</c> with no body points).</summary>
    /// <param name="physics">The parsed type.</param>
    /// <returns>The definition.</returns>
    public static RetailHeliDefinition Definition(in HelicopterPhysicsDefinition physics)
    {

        return new RetailHeliDefinition
        {
            Mass = physics.Mass,
            SleepSpeed = physics.SleepSpeed,
            SleepSpin = physics.SleepSpin,
            Record =
            [
                physics.MaximumSpeed,
                physics.Torque.X,
                physics.Smoothing.X,
                physics.Torque.Y,
                physics.Smoothing.Y,
                physics.Torque.Z,
                physics.Smoothing.Z,
                physics.MaximumThrust,
                physics.HighCollectiveStep,
                physics.LowCollectiveStep,
            ],
            Aero = [.. physics.Wings],
        };
    }

    /// <summary>Reads a Heli task's physics parameters.</summary>
    /// <param name="task">The Heli task.</param>
    /// <returns>The placement.</returns>
    public static RetailHeliPlacement Placement(DeclaredTask task)
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

        return new RetailHeliPlacement(
            Named("Position") is Vector3d position ? position : default,
            Named("Orientation") is Vector3f orientation ? orientation : default,
            Named("Original thrust") is float thrust ? thrust : 1.0f,
            Named("Speed") is Vector3f speed ? speed : default);
    }

    /// <summary>Creates a helicopter with its skids and rotors from its model's attachments, reset at its placement.</summary>
    /// <param name="physics">The parsed Heli type.</param>
    /// <param name="model">The airframe model.</param>
    /// <param name="placement">The level parameters.</param>
    /// <param name="magic">Resolves an attachment's magic-object definition.</param>
    /// <param name="rotor">Resolves a rotor model's record.</param>
    /// <returns>The body, as the second mode-0 packet leaves it.</returns>
    public static RetailHeliBody Create(
        in HelicopterPhysicsDefinition physics,
        MefModel model,
        in RetailHeliPlacement placement,
        Func<string, MagicObjectDefinition?> magic,
        Func<string, RotorPhysicsDefinition?> rotor)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(magic);
        ArgumentNullException.ThrowIfNull(rotor);
        RetailHeliBody heli = new(Definition(physics))
        {
            ModelRadius = model.NativeMarkerBound,
            OriginalThrust = placement.OriginalThrust,
        };
        heli.Dimensions[0] = heli.Dimensions[1] = heli.Dimensions[2] = 1f;
        heli.RunCollide = true;

        // The model's attachments, in ATTA order: HeliWheels become skids, rotor objects rotors.
        foreach (MefAttachment attachment in model.Attachments)
        {
            if (magic(attachment.Name) is not { } definition)
            {
                continue;
            }

            if (definition.HeliWheel is { } wheel)
            {
                heli.Skids.Add(new RetailHeliSkid(
                    new Vector3d(attachment.Position.X, attachment.Position.Y, attachment.Position.Z),
                    wheel.Travel,
                    wheel.BottomOut));
            }
            else if (rotor(definition.Model) is { } record)
            {
                heli.Rotors.Add(new RetailHeliRotor(record.ProducesLift, record.IsTailRotor));
            }
        }

        Reset(heli, physics, placement);
        return heli;
    }

    /// <summary>The mode-0 reset at the placement.</summary>
    /// <param name="heli">The helicopter.</param>
    /// <param name="physics">Its parsed type.</param>
    /// <param name="placement">Its level parameters.</param>
    public static void Reset(RetailHeliBody heli, in HelicopterPhysicsDefinition physics, in RetailHeliPlacement placement)
    {
        ArgumentNullException.ThrowIfNull(heli);
        heli.OriginalThrust = placement.OriginalThrust;
        heli.Reset(
            placement.Position,
            RetailSplineSegment.AnglesToMatrix(placement.Orientation.X, placement.Orientation.Y, placement.Orientation.Z),
            physics.Dimensions,
            placement.Speed);
    }
}

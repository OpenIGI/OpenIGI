using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Game.Igi2.Vehicles;

/// <summary>
/// A vehicle's kill box, I.G.I. 2's <c>DeathZone</c> (deathzone.c): a magic object on the vehicle's attach point that kills the humans
/// inside it while the vehicle moves.
/// </summary>
/// <remarks>
/// <para>
/// <c>DeathZone_RunHandler</c>, single player only: while its car is not at rest, or its train has speed, every human is tested
/// (<c>Human_DeathZoneHitTestHandler</c>).
/// </para>
/// <para>
/// An active human whose root bone is within the box's radius of its centre, and from whose root bone a line to the centre meets
/// nothing of the box, is inside it: it takes ten thousand damage to the chest when the vehicle moves at fifty units a tick or more,
/// none slower, and dies if that is enough.
/// </para>
/// </remarks>
public sealed class Igi2DeathZone
{
    /// <summary>The magic task type a kill box is.</summary>
    public const string TypeName = "DeathZone";

    /// <summary>The damage a kill box does.</summary>
    public const float KillDamage = 10000f;

    /// <summary>The speed a vehicle must move at for its kill box to do damage, units a tick.</summary>
    public const double KillSpeed = 50.0;

    private readonly Igi2LineTrace _line = new(1);

    /// <summary>Initializes a new instance of the <see cref="Igi2DeathZone"/> class.</summary>
    /// <param name="attachment">Its attach point on the vehicle's model.</param>
    /// <param name="model">Its model.</param>
    /// <param name="solid">The solid it was built as, which a line from inside it does not meet; <see langword="null"/> for none.</param>
    public Igi2DeathZone(MefAttachment attachment, MefModel? model, Igi2RigidObject? solid)
    {
        Attachment = attachment;
        Radius = model?.NativeMarkerBound ?? 1f;
        Solid = solid;
    }

    /// <summary>Gets its attach point on the vehicle's model.</summary>
    public MefAttachment Attachment { get; }

    /// <summary>Gets its radius: its model's.</summary>
    public float Radius { get; }

    /// <summary>Gets the solid it was built as.</summary>
    public Igi2RigidObject? Solid { get; }

    /// <summary>Finds a vehicle model's kill boxes, each with the solid its vehicle's attach point made of it.</summary>
    /// <param name="model">The vehicle's model.</param>
    /// <param name="magic">The magic object types.</param>
    /// <param name="models">The level's models.</param>
    /// <param name="solids">The vehicle's solids.</param>
    /// <returns>Its kill boxes, in attach point order.</returns>
    public static IReadOnlyList<Igi2DeathZone> Find(
        MefModel model, Igi2MagicObjTypes magic, Func<string, MefModel?> models, IReadOnlyList<Igi2RigidObject> solids)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(magic);
        ArgumentNullException.ThrowIfNull(models);
        ArgumentNullException.ThrowIfNull(solids);
        List<Igi2DeathZone> zones = [];
        foreach (MefAttachment attachment in model.Attachments)
        {
            if (magic.Find(attachment.Name) is { TaskType: TypeName } type)
            {
                Igi2RigidObject? solid = solids.FirstOrDefault(
                    s => s.Owner is Igi2CollisionOwner { Depth: 1 } owner && string.Equals(owner.Model, type.Model, StringComparison.Ordinal));
                zones.Add(new Igi2DeathZone(attachment, models(type.Model), solid));
            }
        }

        return zones;
    }

    /// <summary>Tests every human against it, <c>Human_DeathZoneHitTestHandler</c>.</summary>
    /// <param name="vehicle">Where its vehicle stands now.</param>
    /// <param name="velocity">Its vehicle's velocity, <c>tCMVel</c>.</param>
    /// <param name="humans">The humans.</param>
    public void HitTest(Igi2Pose vehicle, Vector3f velocity, IEnumerable<Igi2Human> humans)
    {
        ArgumentNullException.ThrowIfNull(vehicle);
        ArgumentNullException.ThrowIfNull(humans);
        Vector3d centre = vehicle.Attach(Attachment).Position;
        float damage = Math.Sqrt((velocity.X * (double)velocity.X) + (velocity.Y * (double)velocity.Y) + (velocity.Z * (double)velocity.Z))
            >= KillSpeed ? KillDamage : 0f;
        foreach (Igi2Human human in humans)
        {
            if (!human.IsActive)
            {
                continue;
            }

            Vector3d root = Root(human);
            double dx = centre.X - root.X, dy = centre.Y - root.Y, dz = centre.Z - root.Z;
            if (Math.Sqrt((dy * dy) + (dz * dz) + (dx * dx)) > Radius || !IsInside(root, centre))
            {
                continue;
            }

            human.Damage.LastPart = Igi2DamagePart.Chest;
            human.Damage.Add(Igi2DamagePart.Chest, damage);
            human.UpdateDie();
        }
    }

    // A line from the human's root bone to the box's centre meets nothing of the box.
    private bool IsInside(Vector3d from, Vector3d centre)
    {
        if (Solid is null)
        {
            return true;
        }

        _line.Count = 1;
        _line.Starts[0] = from;
        _line.Ends[0] = centre;
        _line.Intersections[0].IsHit = false;
        Igi2RigidTrace.Trace(Solid, _line);
        return !_line.Intersections[0].IsHit;
    }

    // The human's root bone, turned by its rows and set on its position.
    private static Vector3d Root(Igi2Human human)
    {
        Matrix3f o = human.Orientation;
        Vector3f r = human.RootTranslation;
        Vector3d p = human.Position;
        return new Vector3d(
            (((((Fpu)o.M12 * r.Y) + ((Fpu)o.M11 * r.X)) + ((Fpu)o.M13 * r.Z)) + p.X).Value,
            (((((Fpu)o.M22 * r.Y) + ((Fpu)o.M21 * r.X)) + ((Fpu)o.M23 * r.Z)) + p.Y).Value,
            (((((Fpu)o.M32 * r.Y) + ((Fpu)o.M31 * r.X)) + ((Fpu)o.M33 * r.Z)) + p.Z).Value);
    }
}

using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Game.Igi2.World;

namespace OpenIGI.Game.Igi2.Vehicles;

/// <summary>
/// A car's door, <c>CarDoor</c> (cardoor.c): a magic object at one of the car's attach points that swings about one of its own axes
/// while its car says it is open (<c>Car_OpenDoor</c>), toward its open angle at its speed, and back to shut when it is not. It stands
/// where its point puts it, turned by its angle (<c>CarDoor_UpdateMagicObjHandler</c>); a bullet that strikes it strikes its car.
/// </summary>
public sealed class Igi2CarDoor : IIgi2WeaponHitTarget
{
    /// <summary>The magic type a door is made as.</summary>
    public const string TypeName = "CarDoor";

    /// <summary>The tag its axis is read from, <c>CARDOOR_AXIS_X</c>, <c>_Y</c> or <c>_Z</c>; X when it has none.</summary>
    public const string AxisTag = "CARDOOR - Rotation axis";

    /// <summary>The tag its speed is read from, in degrees a second; 30 when it has none.</summary>
    public const string SpeedTag = "CARDOOR - Rotation speed (deg/sec)";

    /// <summary>The tag its open angle is read from, in degrees; -118 when it has none.</summary>
    public const string MaxAngleTag = "CARDOOR - Max angle";

    /// <summary>Initializes a new instance of the <see cref="Igi2CarDoor"/> class, shut (<c>CarDoor_CreateHandler</c>).</summary>
    /// <param name="attachment">Its point on the car.</param>
    /// <param name="model">Its model.</param>
    /// <param name="axis">The axis it swings about: 0 X, 1 Y, 2 Z.</param>
    /// <param name="degreesPerSecond">How fast it swings.</param>
    /// <param name="maxDegrees">The angle it swings open to.</param>
    /// <param name="solid">Its solid, or <see langword="null"/>.</param>
    /// <remarks>
    /// <c>CarDoor_ParseMagicObjParametersHandler</c>: the speed is taken to radians a tick (times π/180 and a thirtieth), the angle to
    /// radians.
    /// </remarks>
    public Igi2CarDoor(MefAttachment attachment, MefModel? model, int axis, float degreesPerSecond, float maxDegrees, Igi2RigidObject? solid)
    {
        Attachment = attachment;
        Model = model;
        Axis = axis;
        Speed = ((Fpu)degreesPerSecond * 0.01745329238474369 * 0.03333333507180214).F;
        MaxAngle = ((Fpu)maxDegrees * 0.01745329238474369).F;
        Solid = solid;
        if (solid is not null)
        {
            solid.Handler = this;
        }
    }

    /// <summary>Gets its point on the car.</summary>
    public MefAttachment Attachment { get; }

    /// <summary>Gets its model.</summary>
    public MefModel? Model { get; }

    /// <summary>Gets the axis it swings about.</summary>
    public int Axis { get; }

    /// <summary>Gets how far it swings a tick, in radians.</summary>
    public float Speed { get; }

    /// <summary>Gets the angle it swings open to, in radians.</summary>
    public float MaxAngle { get; }

    /// <summary>Gets how far open it is, in radians.</summary>
    public float Angle { get; private set; }

    /// <summary>Gets its solid, or <see langword="null"/>.</summary>
    public Igi2RigidObject? Solid { get; }

    /// <summary>Gets or sets the car a bullet that strikes it strikes (<c>CarDoor_WeaponHitHandler</c>).</summary>
    public IIgi2WeaponHitTarget? Car { get; set; }

    /// <summary>The doors a car's model carries, each with the settings its magic type's tags give.</summary>
    /// <param name="car">The car's model.</param>
    /// <param name="magic">The magic object types.</param>
    /// <param name="models">Resolves a model.</param>
    /// <param name="solids">The car's solids, a door's being the one built of its model at its point.</param>
    /// <returns>The doors, in the order of their points.</returns>
    public static IReadOnlyList<Igi2CarDoor> Find(MefModel car, Igi2MagicObjTypes magic, Func<string, MefModel?> models, IReadOnlyList<Igi2RigidObject> solids)
    {
        ArgumentNullException.ThrowIfNull(car);
        ArgumentNullException.ThrowIfNull(magic);
        ArgumentNullException.ThrowIfNull(models);
        ArgumentNullException.ThrowIfNull(solids);
        List<Igi2CarDoor> doors = [];
        foreach (MefAttachment attachment in car.Attachments)
        {
            if (magic.Find(attachment.Name) is not { TaskType: TypeName } type)
            {
                continue;
            }

            Igi2RigidObject? solid = solids.FirstOrDefault(
                s => s.Owner is Igi2CollisionOwner { Depth: 1 } owner && string.Equals(owner.Model, type.Model, StringComparison.Ordinal)
                    && s.Handler is null);
            doors.Add(new Igi2CarDoor(
                attachment,
                models(type.Model),
                Int(type, AxisTag, 0),
                Real(type, SpeedTag, 30f),
                Real(type, MaxAngleTag, -118f),
                solid));
        }

        return doors;

        static int Int(Igi2MagicObjType type, string tag, int fallback) =>
            type.Settings.OfType<Igi2TagItemInt32Task>().FirstOrDefault(t => t.TagName == tag)?.Value ?? fallback;

        static float Real(Igi2MagicObjType type, string tag, float fallback) =>
            type.Settings.OfType<Igi2TagItemReal32Task>().FirstOrDefault(t => t.TagName == tag)?.Value ?? fallback;
    }

    /// <summary>
    /// Its tick, <c>CarDoor_RunHandler</c>: open, it swings by its speed toward its open angle and stops there; shut, it swings by its
    /// speed back toward nought and stops there.
    /// </summary>
    /// <param name="isOpen">Whether its car says it is open.</param>
    public void Run(bool isOpen)
    {
        float target = isOpen ? MaxAngle : 0f;
        if (Angle == target)
        {
            return;
        }

        if (Angle < target)
        {
            Angle = ((Fpu)Speed + Angle).F;
            if (Angle > target)
            {
                Angle = target;
            }
        }
        else
        {
            Angle = ((Fpu)Angle - Speed).F;
            if (Angle < target)
            {
                Angle = target;
            }
        }
    }

    /// <summary>
    /// Where it stands, <c>CarDoor_UpdateMagicObjHandler</c>: where its point puts it on the car, turned about its axis by its angle
    /// (the point's frame times the swing).
    /// </summary>
    /// <param name="car">Where the car stands.</param>
    /// <returns>Its pose.</returns>
    public Igi2Pose Pose(Igi2Pose car)
    {
        ArgumentNullException.ThrowIfNull(car);
        Igi2Pose at = car.Attach(Attachment);
        float[] swing = Swing();
        float[] m = at.Orientation is float[] rows ? rows : [.. at.Orientation];
        float[] turned = new float[9];
        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 3; column++)
            {
                turned[(row * 3) + column] = (((Fpu)m[row * 3] * swing[column]) + ((Fpu)m[(row * 3) + 1] * swing[3 + column])
                    + ((Fpu)m[(row * 3) + 2] * swing[6 + column])).F;
            }
        }

        return Igi2Pose.FromMatrix(at.Position, new Matrix3f(turned[0], turned[1], turned[2], turned[3], turned[4], turned[5], turned[6], turned[7], turned[8]));
    }

    /// <inheritdoc/>
    /// <remarks>The hit is its car's.</remarks>
    public void WeaponHit(in Igi2WeaponHit hit) => Car?.WeaponHit(hit);

    // The swing about the axis, rows: X turns Y toward Z, Y turns Z toward X, Z by its gamma.
    private float[] Swing()
    {
        float sin = (float)Math.Sin(Angle), cos = (float)Math.Cos(Angle);
        return Axis switch
        {
            0 => [1f, 0f, 0f, 0f, cos, -sin, 0f, sin, cos],
            1 => [cos, 0f, sin, 0f, 1f, 0f, -sin, 0f, cos],
            2 => Igi2Pose.Rotation(0f, 0f, Angle),
            _ => [1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f],
        };
    }
}

using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Player;

namespace OpenIGI.Game.Igi2.Weapons;

/// <summary>
/// A spent casing thrown from a weapon object, I.G.I. 2's <c>GunCasing</c> (<c>guncasing.c</c>): a model that flies for nineteen
/// ticks, falling and turning, and is then deleted. It touches nothing.
/// </summary>
public sealed class Igi2GunCasing
{
    /// <summary>The ticks a casing flies, <c>GunCasing_New</c>'s count.</summary>
    public const int Lifetime = 19;

    /// <summary>What gravity takes off its upward speed each tick, in world units.</summary>
    public const float Gravity = 44.600887298583984f;

    private Matrix3f _orientation;
    private int _ticksLeft = Lifetime;

    /// <summary>Initializes a new instance of the <see cref="Igi2GunCasing"/> class.</summary>
    /// <param name="model">Its model.</param>
    /// <param name="position">Where it starts.</param>
    /// <param name="speed">Its speed, in world units a tick.</param>
    /// <param name="orientation">Its orientation, the weapon object's.</param>
    /// <param name="spin">The turn it makes each tick.</param>
    public Igi2GunCasing(string model, Vector3d position, Vector3f speed, Matrix3f orientation, Matrix3f spin)
    {
        Model = model;
        Position = position;
        Speed = speed;
        _orientation = orientation;
        Spin = spin;
    }

    /// <summary>Gets its model.</summary>
    public string Model { get; }

    /// <summary>Gets where it is.</summary>
    public Vector3d Position { get; private set; }

    /// <summary>Gets its speed, in world units a tick.</summary>
    public Vector3f Speed { get; private set; }

    /// <summary>Gets its orientation.</summary>
    public Matrix3f Orientation => _orientation;

    /// <summary>Gets the turn it makes each tick.</summary>
    public Matrix3f Spin { get; }

    /// <summary>
    /// One tick, <c>GunCasing_RunHandler</c> (<c>igi2 0x533380</c>): gravity off its upward speed, its speed onto its position, its
    /// orientation turned by its spin; deleted once its ticks are spent.
    /// </summary>
    /// <returns>Whether it is still there.</returns>
    public bool Run()
    {
        if (_ticksLeft == 0)
        {
            return false;
        }

        _ticksLeft--;
        float up = ((Fpu)Speed.Z - Gravity).F;
        Speed = new Vector3f(Speed.X, Speed.Y, up);
        Position = new Vector3d(((Fpu)Speed.X + Position.X).Value, ((Fpu)Speed.Y + Position.Y).Value, ((Fpu)up + Position.Z).Value);
        _orientation *= Spin;
        return true;
    }
}

/// <summary>The casings in flight in a level, run once a tick and drawn where they are.</summary>
/// <param name="models">Resolves a model, whose fourth magic vertex is a weapon object's ejection port.</param>
public sealed class Igi2GunCasings(Func<string, MefModel?> models)
{
    /// <summary>The magic vertex a casing leaves a weapon object from, <c>RigidDynCubeObj_GetMagicVertexPos(…, 3, …)</c>.</summary>
    public const int EjectionMagicVertex = 3;

    /// <summary>The speed a casing leaves at, in world units a tick.</summary>
    public const float EjectionSpeed = 273.0666809082031f;

    private readonly List<Igi2GunCasing> _casings = [];

    /// <summary>Gets the casings in flight.</summary>
    public IReadOnlyList<Igi2GunCasing> Flying => _casings;

    /// <summary>
    /// Throws a casing from a weapon object (<c>Gun_FireHandler</c>'s <c>GunCasing_New</c>): from the object's ejection port, in its
    /// frame, at the drawn angle out to its side and up (a gun's also forward), with its holder's speed added; turning each tick by
    /// the drawn spin, whole degrees about each axis (<c>Real32x9_Create</c>).
    /// </summary>
    /// <param name="ejection">What the shot drew for it.</param>
    /// <param name="objectModel">The weapon object's model.</param>
    /// <param name="objectPosition">Where the weapon object stands.</param>
    /// <param name="objectOrientation">The weapon object's orientation.</param>
    /// <param name="holderSpeed">The holder's speed, added to the casing's.</param>
    public void Eject(in Igi2CasingEjection ejection, string objectModel, Vector3d objectPosition, Matrix3f objectOrientation, Vector3f holderSpeed)
    {
        Vector3f port = models(objectModel)?.TryGetMagicVertex(EjectionMagicVertex, out Vector3f vertex) == true ? vertex : default;
        Vector3f offset = objectOrientation.Transposed().Transform(port);
        Vector3d at = new(objectPosition.X + offset.X, objectPosition.Y + offset.Y, objectPosition.Z + offset.Z);

        double angle = ejection.Angle;
        Vector3f local = new((float)(Math.Cos(angle) * EjectionSpeed), ejection.IsGun ? EjectionSpeed : 0f, (float)(Math.Sin(angle) * EjectionSpeed));
        Vector3f speed = objectOrientation.Transposed().Transform(local);
        speed = new Vector3f(speed.X + holderSpeed.X, speed.Y + holderSpeed.Y, speed.Z + holderSpeed.Z);

        const float degree = 0.01745329238474369f;
        float[] spin = Igi2Pose.Rotation(ejection.Spin.Alpha * degree, ejection.Spin.Beta * degree, ejection.Spin.Gamma * degree);
        _casings.Add(new Igi2GunCasing(
            ejection.Model,
            at,
            speed,
            objectOrientation,
            new Matrix3f(spin[0], spin[1], spin[2], spin[3], spin[4], spin[5], spin[6], spin[7], spin[8])));
    }

    /// <summary>Runs each casing, dropping those whose flight is over.</summary>
    public void Run() => _casings.RemoveAll(static c => !c.Run());

    /// <summary>The casings as models to draw.</summary>
    /// <returns>One rigid model a casing.</returns>
    public IEnumerable<TitleWorldModel> Models() =>
        _casings.Select(static c => new TitleWorldModel(c.Model, c.Position, c.Orientation));
}

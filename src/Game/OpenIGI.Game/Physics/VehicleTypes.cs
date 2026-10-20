using OpenIGI.Core.Numerics;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.Physics;

/// <summary>A surface a vehicle meets the air with, <c>Wing_t</c>: its area, where it is on the vehicle, and its unit normal.</summary>
/// <param name="Area">Its area, <c>vArea</c>; nought for a wing that does nothing.</param>
/// <param name="Position">Where its force acts, in world units in the vehicle's frame, <c>tPos</c>.</param>
/// <param name="Normal">Its unit normal, <c>tNormal</c>.</param>
public readonly record struct Wing(float Area, Vector3f Position, Vector3f Normal)
{
    /// <summary>Makes a wing (<c>Wing_Init</c>, <c>igi2 0x5A4A10</c>): its normal made a unit vector unless it is nought.</summary>
    /// <typeparam name="TFpu">The precision: <see cref="X87"/> for Project I.G.I., <see cref="X87Single"/> for I.G.I. 2.</typeparam>
    /// <param name="area">Its area.</param>
    /// <param name="position">Where it acts, world units.</param>
    /// <param name="normal">Its normal, any length.</param>
    /// <returns>The wing.</returns>
    public static Wing Create<TFpu>(float area, Vector3f position, Vector3f normal)
        where TFpu : struct, IX87<TFpu>
    {
        if (normal.X != 0f || normal.Y != 0f || normal.Z != 0f)
        {
            TFpu scale = (TFpu)1.0 / TFpu.Sqrt((TFpu)normal.X * normal.X + (TFpu)normal.Y * normal.Y + (TFpu)normal.Z * normal.Z);
            normal = new Vector3f((scale * normal.X).F, (scale * normal.Y).F, (scale * normal.Z).F);
        }

        return new Wing(area, position, normal);
    }
}

/// <summary>
/// What every vehicle's physics type says of it, <c>VehicleType_s</c>, as <c>VehicleType_DefineVehicleType</c> reads it from the type's
/// own arguments: its limits and their inertias, its wings, and the cone it watches through.
/// </summary>
/// <remarks>
/// One engine reader in both games: the first game's base physics-object parse fills the same fourteen leading fields (a car's
/// steering retention at <c>+24</c>, its engine at <c>+28</c>, its wing count at <c>+40</c>; a missile's handling likewise).
/// </remarks>
/// <param name="MaxSpeed">Its top speed, <c>vMaxSpeed</c>.</param>
/// <param name="MaxDeltaAlpha">Its most pitch a tick, <c>vMaxDeltaAlpha</c>.</param>
/// <param name="DeltaAlphaInertia">How much pitch it keeps a tick, <c>vDeltaAlphaInertia</c>.</param>
/// <param name="MaxDeltaBeta">Its most roll a tick, <c>vMaxDeltaBeta</c>.</param>
/// <param name="DeltaBetaInertia">How much roll it keeps a tick, <c>vDeltaBetaInertia</c>.</param>
/// <param name="MaxDeltaGamma">Its most turn a tick, <c>vMaxDeltaGamma</c>.</param>
/// <param name="DeltaGammaInertia">How much of its steering it keeps a tick, <c>vDeltaGammaInertia</c>.</param>
/// <param name="MaxThrust">Its most thrust, <c>vMaxThrust</c>.</param>
/// <param name="ThrustInertia">How much of its thrust it keeps a tick, <c>vThrustInertia</c>.</param>
/// <param name="ThrustStartStopInertia">Its thrust's inertia starting and stopping, <c>vThrustStartStopInertia</c>.</param>
/// <param name="Wings">Its wings, <c>atWing</c>.</param>
/// <param name="ViewconeLength">Its cone's reach, in world units, <c>vViewconeLength</c>.</param>
/// <param name="ViewconeAlpha">Its cone's half height, in radians, <c>vViewconeAlpha</c>.</param>
/// <param name="ViewconeGamma">Its cone's half width, in radians, <c>vViewconeGamma</c>.</param>
public sealed record VehicleTypeDefinition(
    float MaxSpeed,
    float MaxDeltaAlpha,
    float DeltaAlphaInertia,
    float MaxDeltaBeta,
    float DeltaBetaInertia,
    float MaxDeltaGamma,
    float DeltaGammaInertia,
    float MaxThrust,
    float ThrustInertia,
    float ThrustStartStopInertia,
    IReadOnlyList<Wing> Wings,
    float ViewconeLength,
    float ViewconeAlpha,
    float ViewconeGamma)
{
    /// <summary>The most wings a vehicle has, <c>atWing[8]</c>.</summary>
    public const int MaxWings = 8;

    /// <summary>Reads a vehicle's part of its type's arguments, <c>VehicleType_DefineVehicleType</c>.</summary>
    /// <typeparam name="TFpu">The precision its wings' normals are made unit at.</typeparam>
    /// <param name="arguments">The type's own arguments (<see cref="PhysicsObjectType.Parameters"/>).</param>
    /// <param name="next">The argument to read from; moved past what is read.</param>
    /// <returns>The vehicle type.</returns>
    /// <remarks>
    /// A wing is its area, its place in metres and its normal, which is made a unit vector (<c>Wing_Init</c>); the cone's reach is in
    /// metres and its angles are whole widths in degrees, halved. A scaling by a power of two, or one product stored, rounds alike at
    /// either precision.
    /// </remarks>
    public static VehicleTypeDefinition Parse<TFpu>(IReadOnlyList<QvmValue> arguments, ref int next)
        where TFpu : struct, IX87<TFpu>
    {
        ArgumentNullException.ThrowIfNull(arguments);
        float maxSpeed = Single(arguments, ref next), maxDeltaAlpha = Single(arguments, ref next), deltaAlphaInertia = Single(arguments, ref next);
        float maxDeltaBeta = Single(arguments, ref next), deltaBetaInertia = Single(arguments, ref next);
        float maxDeltaGamma = Single(arguments, ref next), deltaGammaInertia = Single(arguments, ref next);
        float maxThrust = Single(arguments, ref next), thrustInertia = Single(arguments, ref next), startStop = Single(arguments, ref next);
        int count = Math.Clamp(Int(arguments, ref next), 0, MaxWings);
        Wing[] wings = new Wing[count];
        for (int i = 0; i < count; i++)
        {
            float area = Single(arguments, ref next);
            Vector3f at = new(
                ((TFpu)Single(arguments, ref next) * 4096.0).F,
                ((TFpu)Single(arguments, ref next) * 4096.0).F,
                ((TFpu)Single(arguments, ref next) * 4096.0).F);
            Vector3f normal = new(Single(arguments, ref next), Single(arguments, ref next), Single(arguments, ref next));
            wings[i] = Wing.Create<TFpu>(area, at, normal);
        }

        float length = ((TFpu)Single(arguments, ref next) * 4096.0).F;
        float alpha = ((TFpu)Single(arguments, ref next) * 0.5 * 0.01745329238474369).F;
        float gamma = ((TFpu)Single(arguments, ref next) * 0.5 * 0.01745329238474369).F;
        return new VehicleTypeDefinition(
            maxSpeed, maxDeltaAlpha, deltaAlphaInertia, maxDeltaBeta, deltaBetaInertia, maxDeltaGamma, deltaGammaInertia,
            maxThrust, thrustInertia, startStop, wings, length, alpha, gamma);
    }

    /// <summary>Reads a float argument and moves past it.</summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="next">The argument to read.</param>
    /// <returns>Its value, or nought past the end.</returns>
    public static float Single(IReadOnlyList<QvmValue> arguments, ref int next)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return next < arguments.Count ? arguments[next++].ToSingle() : 0f;
    }

    /// <summary>Reads an integer argument and moves past it.</summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="next">The argument to read.</param>
    /// <returns>Its value, or nought past the end.</returns>
    public static int Int(IReadOnlyList<QvmValue> arguments, ref int next)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return next < arguments.Count ? arguments[next++].ToInt32() : 0;
    }

    /// <summary>Reads a string argument and moves past it.</summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="next">The argument to read.</param>
    /// <returns>Its value, or empty past the end.</returns>
    public static string Text(IReadOnlyList<QvmValue> arguments, ref int next)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return next < arguments.Count ? arguments[next++].ToText() : string.Empty;
    }
}

/// <summary>One wheel of a car's type, <c>CarWheelType_s</c>.</summary>
/// <param name="IsSteering">Whether it steers, <c>isSteeringWheel</c>.</param>
/// <param name="IsEngine">Whether the engine drives it, <c>isEngineWheel</c>.</param>
/// <param name="MaxAngle">The most it steers, in degrees, <c>vMaxAngle</c>.</param>
/// <param name="ContactPoint">The physics contact point it is, <c>iCollPoint</c>; negative for none.</param>
/// <param name="MaxSpringDistance">The furthest its spring compresses, in world units, <c>vMaxSpringDistance</c>.</param>
/// <param name="SpringStiffness">Its spring's stiffness, <c>vSpringStiffness</c>.</param>
public readonly record struct CarWheelTypeDefinition(bool IsSteering, bool IsEngine, float MaxAngle, int ContactPoint, float MaxSpringDistance, float SpringStiffness)
{
    /// <summary>Gets a value indicating whether it hangs from a contact point: a wheel with none still draws but has no spring.</summary>
    public bool HasContactPoint => ContactPoint >= 0;

    /// <summary>Gets the most it steers, in radians.</summary>
    public float MaxAngleRadians => MaxAngle * (MathF.PI / 180f);
}

/// <summary>
/// A car's physics type, <c>CarType_s</c>, as <c>CarType_ParsePhysicsObjTypeHandler</c> (Project I.G.I. <c>0x42DB00</c>) reads it: a
/// vehicle's, then its wheels, how far its wheels' contact points are lowered, whether its wheels are attach objects and whether it is
/// a crawler, its engine's sound, the magic vertices it explodes at and the model it leaves.
/// </summary>
/// <param name="Vehicle">Its vehicle part.</param>
/// <param name="Wheels">Its wheels, <c>atCarWheelType</c>.</param>
/// <param name="WheelContactLowering">How far below its magic vertex each wheel's contact point is, in world units.</param>
/// <param name="IsWheelsAttachObject">Whether its wheels are attach objects of their own, <c>isWheelsAttachObj</c>.</param>
/// <param name="IsCrawler">Whether it runs on tracks, <c>isCrawler</c>.</param>
/// <param name="EngineSound">Its engine's sound, <c>zEngineSound</c>.</param>
/// <param name="ExplodeVertices">The magic vertices it explodes at, <c>anExplodeMagicVertex</c>; none for one blast at its centre.</param>
/// <param name="DestroyedModel">The model it leaves when it has exploded, <c>zDestroyedModel</c>.</param>
public sealed record CarTypeDefinition(
    VehicleTypeDefinition Vehicle,
    IReadOnlyList<CarWheelTypeDefinition> Wheels,
    float WheelContactLowering,
    bool IsWheelsAttachObject,
    bool IsCrawler,
    string EngineSound,
    IReadOnlyList<int> ExplodeVertices,
    string DestroyedModel)
{
    /// <summary>The most wheels a car has, <c>atCarWheelType[16]</c>.</summary>
    public const int MaxWheels = 16;

    /// <summary>The most magic vertices it explodes at, <c>anExplodeMagicVertex[10]</c>.</summary>
    public const int MaxExplodeVertices = 10;

    /// <summary>Reads a car's type from its physics type's own arguments.</summary>
    /// <typeparam name="TFpu">The precision its wings' normals are made unit at.</typeparam>
    /// <param name="arguments">The arguments.</param>
    /// <returns>The car type.</returns>
    public static CarTypeDefinition Parse<TFpu>(IReadOnlyList<QvmValue> arguments)
        where TFpu : struct, IX87<TFpu>
    {
        int next = 0;
        return Parse<TFpu>(arguments, ref next);
    }

    /// <summary>Reads a car's type from a call's arguments, from a given one on.</summary>
    /// <typeparam name="TFpu">The precision its wings' normals are made unit at.</typeparam>
    /// <param name="arguments">The arguments.</param>
    /// <param name="next">The argument its vehicle part starts at; moved past what is read.</param>
    /// <returns>The car type.</returns>
    public static CarTypeDefinition Parse<TFpu>(IReadOnlyList<QvmValue> arguments, ref int next)
        where TFpu : struct, IX87<TFpu>
    {
        ArgumentNullException.ThrowIfNull(arguments);
        VehicleTypeDefinition vehicle = VehicleTypeDefinition.Parse<TFpu>(arguments, ref next);
        int count = Math.Clamp(VehicleTypeDefinition.Int(arguments, ref next), 0, MaxWheels);
        CarWheelTypeDefinition[] wheels = new CarWheelTypeDefinition[count];
        for (int i = 0; i < count; i++)
        {
            bool steering = VehicleTypeDefinition.Int(arguments, ref next) != 0;
            bool engine = VehicleTypeDefinition.Int(arguments, ref next) != 0;
            float maxAngle = VehicleTypeDefinition.Single(arguments, ref next);
            int point = VehicleTypeDefinition.Int(arguments, ref next);
            float spring = ((TFpu)VehicleTypeDefinition.Single(arguments, ref next) * 4096.0).F;
            float stiffness = VehicleTypeDefinition.Single(arguments, ref next);
            wheels[i] = new CarWheelTypeDefinition(steering, engine, maxAngle, point, spring, stiffness);
        }

        float lowering = ((TFpu)VehicleTypeDefinition.Single(arguments, ref next) * 4096.0).F;
        bool attach = (sbyte)VehicleTypeDefinition.Int(arguments, ref next) != 0;
        bool crawler = (sbyte)VehicleTypeDefinition.Int(arguments, ref next) != 0;
        string sound = VehicleTypeDefinition.Text(arguments, ref next);
        int explodeCount = Math.Clamp(VehicleTypeDefinition.Int(arguments, ref next), 0, MaxExplodeVertices);
        int[] explode = new int[explodeCount];
        for (int i = 0; i < explodeCount; i++)
        {
            explode[i] = VehicleTypeDefinition.Int(arguments, ref next);
        }

        string destroyed = VehicleTypeDefinition.Text(arguments, ref next);
        return new CarTypeDefinition(vehicle, wheels, lowering, attach, crawler, sound, explode, destroyed);
    }
}

/// <summary>
/// What a missile's physics type says of it: the vehicle's handling, then the missile's own (<c>Missile_ParsePhysicsObjTypeHandler</c>,
/// <c>igi2 0x52EE70</c>; Project I.G.I. <c>0x429730</c>): smoke, guidance, fuel and its burn, and the smoke trail.
/// </summary>
public sealed class MissileTypeDefinition
{
    private MissileTypeDefinition(VehicleTypeDefinition vehicle) => Vehicle = vehicle;

    /// <summary>Gets its vehicle part: its pitch and turn limits and inertias, its thrust and its wings.</summary>
    public VehicleTypeDefinition Vehicle { get; }

    /// <summary>Gets whether it smokes, <c>MISSILE_SMOKE_*</c> (<c>+280</c>).</summary>
    public int Smoke { get; private init; }

    /// <summary>Gets how it steers, <c>MISSILE_TYPE_*</c> (<c>+284</c>).</summary>
    public int Guidance { get; private init; }

    /// <summary>Gets its fuel when fired (<c>+288</c>).</summary>
    public float Fuel { get; private init; }

    /// <summary>Gets the fuel it burns a tick: its rate a second over thirty (<c>+292</c>).</summary>
    public float Burn { get; private init; }

    /// <summary>Gets its smoke's size when made, units (<c>+300</c>).</summary>
    public float SmokeSize { get; private init; }

    /// <summary>Gets its smoke's size when gone, units (<c>+304</c>).</summary>
    public float SmokeEndSize { get; private init; }

    /// <summary>Gets the ticks between its puffs of smoke (<c>+308</c>).</summary>
    public float SmokeDensity { get; private init; }

    /// <summary>Gets a puff's life, seconds (<c>+312</c>).</summary>
    public float SmokeLifeTime { get; private init; }

    /// <summary>Gets its smoke's sprite (<c>+316</c>).</summary>
    public string SmokeSprite { get; private init; } = string.Empty;

    /// <summary>Gets the sound it flies with (<c>+572</c>).</summary>
    public string Sound { get; private init; } = string.Empty;

    /// <summary>Reads a physics type's arguments as a missile's.</summary>
    /// <typeparam name="TFpu">The precision its wings' normals are made unit at.</typeparam>
    /// <param name="type">The physics type.</param>
    /// <returns>The missile type, or <see langword="null"/> when its arguments are too few.</returns>
    public static MissileTypeDefinition? Of<TFpu>(PhysicsObjectType type)
        where TFpu : struct, IX87<TFpu>
    {
        ArgumentNullException.ThrowIfNull(type);
        IReadOnlyList<QvmValue> p = type.Parameters;
        if (p.Count < 11 || p.Count < 11 + (7 * Math.Max(0, p[10].ToInt32())) + 3 + 11)
        {
            return null;
        }

        int next = 0;
        VehicleTypeDefinition vehicle = VehicleTypeDefinition.Parse<TFpu>(p, ref next);
        int smoke = VehicleTypeDefinition.Int(p, ref next);
        int guidance = VehicleTypeDefinition.Int(p, ref next);
        float fuel = VehicleTypeDefinition.Single(p, ref next);
        float burn = ((TFpu)VehicleTypeDefinition.Single(p, ref next) * 0.03333333507180214).F;
        next++;
        return new MissileTypeDefinition(vehicle)
        {
            Smoke = smoke,
            Guidance = guidance,
            Fuel = fuel,
            Burn = burn,
            SmokeSize = ((TFpu)VehicleTypeDefinition.Single(p, ref next) * 4096.0).F,
            SmokeEndSize = ((TFpu)VehicleTypeDefinition.Single(p, ref next) * 4096.0).F,
            SmokeDensity = VehicleTypeDefinition.Single(p, ref next),
            SmokeLifeTime = VehicleTypeDefinition.Single(p, ref next),
            SmokeSprite = VehicleTypeDefinition.Text(p, ref next),
            Sound = VehicleTypeDefinition.Text(p, ref next),
        };
    }
}

using OpenIGI.Core.Numerics;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.Igi2.Vehicles;

/// <summary>A surface a vehicle meets the air with, <c>Wing_t</c>: its area, where it is on the vehicle, and its unit normal.</summary>
/// <param name="Area">Its area, <c>vArea</c>.</param>
/// <param name="Position">Where it is, in world units in the vehicle's frame, <c>tPos</c>.</param>
/// <param name="Normal">Its unit normal, <c>tNormal</c>.</param>
public readonly record struct Igi2Wing(float Area, Vector3f Position, Vector3f Normal);

/// <summary>One wheel of a car's type, <c>CarWheelType_s</c>.</summary>
/// <param name="IsSteering">Whether it steers, <c>isSteeringWheel</c>.</param>
/// <param name="IsEngine">Whether the engine drives it, <c>isEngineWheel</c>.</param>
/// <param name="MaxAngle">The most it steers, in degrees, <c>vMaxAngle</c>.</param>
/// <param name="ContactPoint">The physics contact point it is, <c>iCollPoint</c>; negative for none.</param>
/// <param name="MaxSpringDistance">The furthest its spring compresses, in world units, <c>vMaxSpringDistance</c>.</param>
/// <param name="SpringStiffness">Its spring's stiffness, <c>vSpringStiffness</c>.</param>
public readonly record struct Igi2CarWheelType(bool IsSteering, bool IsEngine, float MaxAngle, int ContactPoint, float MaxSpringDistance, float SpringStiffness);

/// <summary>
/// What every vehicle's physics type says of it, <c>VehicleType_s</c>, as <c>VehicleType_DefineVehicleType</c> reads it from the type's
/// own arguments: its limits and their inertias, its wings, and the cone it watches through.
/// </summary>
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
public sealed record Igi2VehicleType(
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
    IReadOnlyList<Igi2Wing> Wings,
    float ViewconeLength,
    float ViewconeAlpha,
    float ViewconeGamma)
{
    /// <summary>The most wings a vehicle has, <c>atWing[8]</c>.</summary>
    public const int MaxWings = 8;

    /// <summary>Reads a vehicle's part of its type's arguments, <c>VehicleType_DefineVehicleType</c>.</summary>
    /// <param name="arguments">The type's own arguments.</param>
    /// <param name="next">The argument to read from; moved past what is read.</param>
    /// <returns>The vehicle type.</returns>
    /// <remarks>
    /// A wing is its area, its place in metres and its normal, which is made a unit vector (<c>Wing_Init</c>); the cone's reach is in
    /// metres and its angles are whole widths in degrees, halved.
    /// </remarks>
    public static Igi2VehicleType Parse(IReadOnlyList<QvmValue> arguments, ref int next)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        float maxSpeed = Single(arguments, ref next), maxDeltaAlpha = Single(arguments, ref next), deltaAlphaInertia = Single(arguments, ref next);
        float maxDeltaBeta = Single(arguments, ref next), deltaBetaInertia = Single(arguments, ref next);
        float maxDeltaGamma = Single(arguments, ref next), deltaGammaInertia = Single(arguments, ref next);
        float maxThrust = Single(arguments, ref next), thrustInertia = Single(arguments, ref next), startStop = Single(arguments, ref next);
        int count = Math.Clamp(Int(arguments, ref next), 0, MaxWings);
        Igi2Wing[] wings = new Igi2Wing[count];
        for (int i = 0; i < count; i++)
        {
            float area = Single(arguments, ref next);
            Vector3f at = new(
                ((Fpu)Single(arguments, ref next) * 4096.0).F,
                ((Fpu)Single(arguments, ref next) * 4096.0).F,
                ((Fpu)Single(arguments, ref next) * 4096.0).F);
            Vector3f normal = new(Single(arguments, ref next), Single(arguments, ref next), Single(arguments, ref next));
            if (normal.X != 0f || normal.Y != 0f || normal.Z != 0f)
            {
                Fpu scale = (Fpu)1.0 / Fpu.Sqrt(((Fpu)normal.X * normal.X) + ((Fpu)normal.Y * normal.Y) + ((Fpu)normal.Z * normal.Z));
                normal = new Vector3f((scale * normal.X).F, (scale * normal.Y).F, (scale * normal.Z).F);
            }

            wings[i] = new Igi2Wing(area, at, normal);
        }

        float length = ((Fpu)Single(arguments, ref next) * 4096.0).F;
        float alpha = ((Fpu)Single(arguments, ref next) * 0.5 * 0.01745329238474369).F;
        float gamma = ((Fpu)Single(arguments, ref next) * 0.5 * 0.01745329238474369).F;
        return new Igi2VehicleType(
            maxSpeed, maxDeltaAlpha, deltaAlphaInertia, maxDeltaBeta, deltaBetaInertia, maxDeltaGamma, deltaGammaInertia,
            maxThrust, thrustInertia, startStop, wings, length, alpha, gamma);
    }

    /// <summary>Reads a float argument and moves past it.</summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="next">The argument to read.</param>
    /// <returns>Its value, or nought past the end.</returns>
    internal static float Single(IReadOnlyList<QvmValue> arguments, ref int next) =>
        next < arguments.Count ? arguments[next++].ToSingle() : 0f;

    /// <summary>Reads an integer argument and moves past it.</summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="next">The argument to read.</param>
    /// <returns>Its value, or nought past the end.</returns>
    internal static int Int(IReadOnlyList<QvmValue> arguments, ref int next) =>
        next < arguments.Count ? arguments[next++].ToInt32() : 0;

    /// <summary>Reads a string argument and moves past it.</summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="next">The argument to read.</param>
    /// <returns>Its value, or empty past the end.</returns>
    internal static string Text(IReadOnlyList<QvmValue> arguments, ref int next) =>
        next < arguments.Count ? arguments[next++].ToText() : string.Empty;
}

/// <summary>
/// A car's physics type, <c>CarType_s</c>, as <c>CarType_ParsePhysicsObjTypeHandler</c> reads it: a vehicle's, then its wheels, how far
/// its wheels' contact points are lowered, whether its wheels are attach objects and whether it is a crawler, its engine's sound, the
/// magic vertices it explodes at and the model it leaves.
/// </summary>
/// <param name="Vehicle">Its vehicle part.</param>
/// <param name="Wheels">Its wheels, <c>atCarWheelType</c>.</param>
/// <param name="WheelContactLowering">How far below its magic vertex each wheel's contact point is, in world units.</param>
/// <param name="IsWheelsAttachObject">Whether its wheels are attach objects of their own, <c>isWheelsAttachObj</c>.</param>
/// <param name="IsCrawler">Whether it runs on tracks, <c>isCrawler</c>.</param>
/// <param name="EngineSound">Its engine's sound, <c>zEngineSound</c>.</param>
/// <param name="ExplodeVertices">The magic vertices it explodes at, <c>anExplodeMagicVertex</c>; none for one blast at its centre.</param>
/// <param name="DestroyedModel">The model it leaves when it has exploded, <c>zDestroyedModel</c>.</param>
public sealed record Igi2CarType(
    Igi2VehicleType Vehicle,
    IReadOnlyList<Igi2CarWheelType> Wheels,
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
    /// <param name="arguments">The arguments.</param>
    /// <returns>The car type.</returns>
    public static Igi2CarType Parse(IReadOnlyList<QvmValue> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        int next = 0;
        Igi2VehicleType vehicle = Igi2VehicleType.Parse(arguments, ref next);
        int count = Math.Clamp(Igi2VehicleType.Int(arguments, ref next), 0, MaxWheels);
        Igi2CarWheelType[] wheels = new Igi2CarWheelType[count];
        for (int i = 0; i < count; i++)
        {
            bool steering = Igi2VehicleType.Int(arguments, ref next) != 0;
            bool engine = Igi2VehicleType.Int(arguments, ref next) != 0;
            float maxAngle = Igi2VehicleType.Single(arguments, ref next);
            int point = Igi2VehicleType.Int(arguments, ref next);
            float spring = ((Fpu)Igi2VehicleType.Single(arguments, ref next) * 4096.0).F;
            float stiffness = Igi2VehicleType.Single(arguments, ref next);
            wheels[i] = new Igi2CarWheelType(steering, engine, maxAngle, point, spring, stiffness);
        }

        float lowering = ((Fpu)Igi2VehicleType.Single(arguments, ref next) * 4096.0).F;
        bool attach = (sbyte)Igi2VehicleType.Int(arguments, ref next) != 0;
        bool crawler = (sbyte)Igi2VehicleType.Int(arguments, ref next) != 0;
        string sound = Igi2VehicleType.Text(arguments, ref next);
        int explodeCount = Math.Clamp(Igi2VehicleType.Int(arguments, ref next), 0, MaxExplodeVertices);
        int[] explode = new int[explodeCount];
        for (int i = 0; i < explodeCount; i++)
        {
            explode[i] = Igi2VehicleType.Int(arguments, ref next);
        }

        string destroyed = Igi2VehicleType.Text(arguments, ref next);
        return new Igi2CarType(vehicle, wheels, lowering, attach, crawler, sound, explode, destroyed);
    }
}

using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.World;

/// <summary>A locomotive and the carriages behind it, placed along the spline they ride.</summary>
/// <remarks>
/// <para>
/// <b>A train has no position.</b> Its <c>Position</c> parameter is a single <c>Real32</c> — a distance
/// along the spline its <c>RailroadQTaskID</c> names — and everything else follows from walking that
/// curve. The carriages are child <c>Train</c> tasks and carry no distance of their own: each hangs one
/// carriage-length behind the one in front.
/// </para>
/// <para>
/// <b>A carriage's length is not authored either.</b>
/// <c>Train_UpdateInternalDataHandler</c> (<c>igi2ed 0x47F8E0</c>) measures it as the distance between
/// the model's first two magic vertices, which are its couplings. That is the only reason this needs the
/// models at all, and the reason a world walk given none places no trains.
/// </para>
/// <para>
/// <b>Only the locomotive moves.</b> That handler calls <c>Train_Move</c> (<c>0x47F9D0</c>) only when
/// the task's parent is not itself a Train, and <c>Train_Move</c> then places the whole rake:
/// <c>Train_GetPositions</c> (<c>0x47FA70</c>) walks one coupling point per carriage and
/// <c>Train_MoveCarriage</c> (<c>0x47FB20</c>) turns each carriage to span the two points it sits
/// between. Every step uses the LOCOMOTIVE's <c>Flip Direction</c>, not each carriage's own.
/// </para>
/// </remarks>
public static class Igi2Train
{
    /// <summary>The task type of a locomotive and of every carriage behind it.</summary>
    public const string TrainType = "Train";

    /// <summary>The parameter naming the spline the train rides.</summary>
    public const string RailroadParameter = "RailroadQTaskID";

    /// <summary>The parameter giving how far along that spline the locomotive's front coupling sits.</summary>
    /// <remarks>
    /// A <c>Real32</c>, in engine units of distance — not a point. The type also declares
    /// <c>Displacement X</c> and <c>Displacement Z</c>, which are in METRES and are multiplied by
    /// <c>WORLD_METER</c> before use, unlike every other length a task carries.
    /// </remarks>
    public const string DistanceParameter = "Position";

    /// <summary>Places a locomotive and its carriages along the spline they ride, where the level starts them.</summary>
    /// <param name="lead">The locomotive: a <c>Train</c> whose parent is not one.</param>
    /// <param name="path">The spline its <c>RailroadQTaskID</c> names.</param>
    /// <param name="models">Resolves a model name to the mesh, for the coupling measurement.</param>
    /// <returns>One placement per carriage, the locomotive first, or nothing when a model is missing.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static IReadOnlyList<Igi2Placement> Carriages(
        Vehicles.Igi2TrainTask lead,
        Igi2SplinePath path,
        Func<string, MefModel?> models)
    {
        ArgumentNullException.ThrowIfNull(lead);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(models);

        List<Vehicles.Igi2TrainTask> rake = [lead, .. lead.Children.OfType<Vehicles.Igi2TrainTask>()];

        // Every step takes the locomotive's flag: Train_GetPositions negates the whole array on the
        // locomotive's isFlipDirection, reading the train it was called for rather than each carriage.
        float[] steps = new float[rake.Count];
        for (int i = 0; i < rake.Count; i++)
        {
            if (!TryMeasure(rake[i].Model, models, out float length))
            {
                return [];
            }

            steps[i] = lead.FlipDirection ? length : -length;
        }

        if (!path.TryWalk(lead.OriginalPosition, steps, out Vector3d[] points))
        {
            return [];
        }

        List<Igi2Placement> placed = [];
        for (int i = 0; i < rake.Count; i++)
        {
            Matrix3f facing = Facing(points[i], points[i + 1]);
            placed.Add(new Igi2Placement(
                rake[i].Id,
                TrainType,
                rake[i].Model,
                Offset(points[i], facing, rake[i].DisplacementX, rake[i].DisplacementZ),
                default,
                0,
                ModelVariant: 0,
                MountedModel: string.Empty,
                MountedOrientation: default,
                Frame: facing));
        }

        return placed;
    }

    /// <summary>
    /// Measures a carriage from coupling to coupling: the distance between its model's first two magic vertices
    /// (<c>Train_UpdateInternalDataHandler</c>).
    /// </summary>
    /// <param name="model">The carriage's model.</param>
    /// <param name="models">Resolves a model name to the mesh.</param>
    /// <param name="length">Receives the length, in world units.</param>
    /// <returns>Whether the model is there with both couplings, a length apart.</returns>
    public static bool TryMeasure(string model, Func<string, MefModel?> models, out float length)
    {
        ArgumentNullException.ThrowIfNull(models);
        length = 0f;
        MefModel? mesh = string.IsNullOrWhiteSpace(model) ? null : models(model);
        if (mesh is null
            || !mesh.TryGetMagicVertex(0, out Vector3f head)
            || !mesh.TryGetMagicVertex(1, out Vector3f tail))
        {
            return false;
        }

        float x = head.X - tail.X, y = head.Y - tail.Y, z = head.Z - tail.Z;
        length = Fpu.Sqrt(((Fpu)x * x) + ((Fpu)y * y) + ((Fpu)z * z)).F;
        return length > 0f;
    }

    /// <summary>Builds the rotation a carriage takes from the two couplings it spans.</summary>
    /// <param name="front">The forward coupling, which is where the carriage stands.</param>
    /// <param name="back">The coupling behind it.</param>
    /// <returns>The rotation, with the direction of travel in its second column.</returns>
    /// <remarks>
    /// <c>Train_MoveCarriage</c> builds it from the chord alone, not from the curve: the carriage points
    /// at the coupling ahead of it however the track bends in between, which is what a rigid carriage
    /// does. The side axis starts horizontal, so a carriage rolls only where the track is banked.
    /// Two couplings at the same point leave it unturned.
    /// </remarks>
    public static Matrix3f Facing(Vector3d front, Vector3d back)
    {
        Vector3f along = Normalize(new Vector3f(
            (float)(front.X - back.X),
            (float)(front.Y - back.Y),
            (float)(front.Z - back.Z)));
        if (along.X == 0f && along.Y == 0f && along.Z == 0f)
        {
            return Matrix3f.Identity;
        }

        Vector3f side = Normalize(new Vector3f(along.Y, -along.X, 0f));
        Vector3f up = Normalize(Cross(side, along));
        return new Matrix3f(
            side.X, along.X, up.X,
            side.Y, along.Y, up.Y,
            side.Z, along.Z, up.Z);
    }

    /// <summary>Shifts a carriage off the rail by its displacement.</summary>
    /// <param name="point">The forward coupling.</param>
    /// <param name="facing">The carriage's rotation.</param>
    /// <param name="across">Its displacement across, in metres.</param>
    /// <param name="up">Its displacement up, in metres.</param>
    /// <returns>Where it stands.</returns>
    /// <remarks>
    /// <c>Train_MoveCarriage</c> multiplies both by <c>WORLD_METER</c> and turns them through the
    /// carriage's own rotation, across and up -- never along. It is what sets a carriage on its bogies
    /// rather than with the rail through its middle.
    /// </remarks>
    public static Vector3d Offset(Vector3d point, Matrix3f facing, float across, float up)
    {
        float x = across * (float)WorldScene.UnitsPerMetre;
        float z = up * (float)WorldScene.UnitsPerMetre;
        return new Vector3d(
            point.X + (facing.M11 * x) + (facing.M13 * z),
            point.Y + (facing.M21 * x) + (facing.M23 * z),
            point.Z + (facing.M31 * x) + (facing.M33 * z));
    }

    private static Vector3f Normalize(Vector3f value)
    {
        float length = MathF.Sqrt((value.X * value.X) + (value.Y * value.Y) + (value.Z * value.Z));
        return length > 0f
            ? new Vector3f(value.X / length, value.Y / length, value.Z / length)
            : value;
    }

    private static Vector3f Cross(Vector3f left, Vector3f right) => new(
        (left.Y * right.Z) - (left.Z * right.Y),
        (left.Z * right.X) - (left.X * right.Z),
        (left.X * right.Y) - (left.Y * right.X));
}

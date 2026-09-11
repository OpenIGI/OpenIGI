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

    /// <summary>Places a locomotive and its carriages along the spline they ride.</summary>
    /// <param name="lead">The locomotive: a <c>Train</c> whose parent is not one.</param>
    /// <param name="path">The spline its <c>RailroadQTaskID</c> names.</param>
    /// <param name="models">Resolves a model name to the mesh, for the coupling measurement.</param>
    /// <returns>One placement per carriage, the locomotive first, or nothing when a model is missing.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public static IReadOnlyList<Igi2Placement> Carriages(
        DeclaredTask lead,
        Igi2SplinePath path,
        Func<string, MefModel?> models)
    {
        ArgumentNullException.ThrowIfNull(lead);
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(models);

        List<DeclaredTask> rake = [lead];
        foreach (GameTask child in lead.Children)
        {
            if (child is DeclaredTask carriage
                && string.Equals(carriage.Type.Name, TrainType, StringComparison.Ordinal))
            {
                rake.Add(carriage);
            }
        }

        // Every step takes the locomotive's flag: Train_GetPositions negates the whole array on
        // `!train[+2260]`, reading the train it was called for rather than each carriage.
        bool flipped = Flag(lead, "Flip Direction");
        float[] steps = new float[rake.Count];
        string[] named = new string[rake.Count];
        for (int i = 0; i < rake.Count; i++)
        {
            if (!TryModel(rake[i], models, out string model, out float length))
            {
                return [];
            }

            named[i] = model;
            steps[i] = flipped ? length : -length;
        }

        if (!path.TryWalk(Distance(lead), steps, out Vector3d[] points))
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
                named[i],
                Offset(points[i], facing, rake[i]),
                default,
                Number(rake[i], Igi2WorldScene.RenderGroupParameter, 0),
                ModelVariant: 0,
                MountedModel: string.Empty,
                MountedOrientation: default,
                Frame: facing));
        }

        return placed;
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

    /// <summary>Gets where the train's front coupling is, in engine units along the spline.</summary>
    private static double Distance(DeclaredTask lead) =>
        lead.TryGetValue(DistanceParameter, out object? raw) && raw is float value ? value : 0.0;

    /// <summary>Shifts a carriage off the rail by its authored displacement.</summary>
    /// <remarks>
    /// <c>Train_MoveCarriage</c> multiplies both by <c>WORLD_METER</c> and turns them through the
    /// carriage's own rotation, across and up — never along. It is what sets a carriage on its bogies
    /// rather than with the rail through its middle.
    /// </remarks>
    private static Vector3d Offset(Vector3d point, Matrix3f facing, DeclaredTask carriage)
    {
        float across = Real(carriage, "Displacement X") * (float)WorldScene.UnitsPerMetre;
        float up = Real(carriage, "Displacement Z") * (float)WorldScene.UnitsPerMetre;
        return new Vector3d(
            point.X + (facing.M11 * across) + (facing.M13 * up),
            point.Y + (facing.M21 * across) + (facing.M23 * up),
            point.Z + (facing.M31 * across) + (facing.M33 * up));
    }

    /// <summary>Reads a carriage's model and the coupling-to-coupling length of it.</summary>
    private static bool TryModel(
        DeclaredTask carriage,
        Func<string, MefModel?> models,
        out string name,
        out float length)
    {
        name = string.Empty;
        length = 0f;
        if (!carriage.TryGetValue(Igi2WorldScene.ModelParameter, out object? raw)
            || raw is not string model
            || string.IsNullOrWhiteSpace(model))
        {
            return false;
        }

        name = model;
        MefModel? mesh = models(model);
        if (mesh is null
            || !mesh.TryGetMagicVertex(0, out Vector3f head)
            || !mesh.TryGetMagicVertex(1, out Vector3f tail))
        {
            return false;
        }

        length = MathF.Sqrt(
            ((head.X - tail.X) * (head.X - tail.X))
            + ((head.Y - tail.Y) * (head.Y - tail.Y))
            + ((head.Z - tail.Z) * (head.Z - tail.Z)));
        return length > 0f;
    }

    private static bool Flag(DeclaredTask task, string name) =>
        task.TryGetValue(name, out object? raw) && raw is bool value && value;

    private static float Real(DeclaredTask task, string name) =>
        task.TryGetValue(name, out object? raw) && raw is float value ? value : 0f;

    private static int Number(DeclaredTask task, string name, int fallback) =>
        task.TryGetValue(name, out object? raw) && raw is int value ? value : fallback;

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

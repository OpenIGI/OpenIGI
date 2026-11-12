using OpenIGI.Core.Numerics;
using OpenIGI.Editor.Core.Missions;
using OpenIGI.Engine.Tasks;

namespace OpenIGI.Editor.Viewport;

/// <summary>
/// The box a volume task covers, and the parameter that sizes it, in whichever of the two ways the games store a box.
/// </summary>
/// <remarks>
/// <para>
/// Trigger and play areas (<c>AreaActivate</c>, I.G.I. 2's <c>SpawnArea</c> and <c>C4BombArea</c>) keep their full size along
/// each axis in world units, a <c>Real32x3</c> called <c>Dimensions</c> (<c>Dimentions</c> on <c>SpawnArea</c>, as retail spells
/// it); <c>AreaActivation</c> and <c>Igi2OrientedBox</c> halve it. An <c>AmbientArea</c> keeps half its size in metres, a
/// <c>Real64x3</c> called <c>Size</c>.
/// </para>
/// <para>
/// The box is centred on the task's position and turned by its orientation, a local corner standing at the transpose of
/// the orientation applied to it, as <c>AreaActivation</c> builds its corners.
/// </para>
/// </remarks>
internal readonly record struct TaskVolume(MissionParameter Parameter, bool StoresHalf, double MetresPerStored)
{
    /// <summary>Gets the box a task covers, if it is a volume with a constant size.</summary>
    /// <param name="task">The task.</param>
    /// <returns>The volume, or <see langword="null"/>.</returns>
    public static TaskVolume? Of(MissionTask task)
    {
        if (task.FindParameter("Size") is { Declaration.Type: ScriptParamType.Real64x3 } size
            && task.TypeName.Equals("AmbientArea", StringComparison.OrdinalIgnoreCase))
        {
            return new TaskVolume(size, StoresHalf: true, MetresPerStored: 1.0);
        }

        MissionParameter? dimensions = task.FindParameter("Dimensions") ?? task.FindParameter("Dimentions");
        return dimensions is { Declaration.Type: ScriptParamType.Real32x3 }
            ? new TaskVolume(dimensions, StoresHalf: false, MetresPerStored: 1.0 / WorldUnits.PerMetre)
            : null;
    }

    /// <summary>Gets half the box's size along each of its axes, in metres.</summary>
    /// <returns>The half sizes, or <see langword="null"/> when the size is not a constant.</returns>
    public Vector3d? HalfMetres()
    {
        if (!Parameter.TryGetNumbers(out double[] values) || values.Length != 3)
        {
            return null;
        }

        double factor = MetresPerStored * (StoresHalf ? 1.0 : 0.5);
        return new Vector3d(Math.Abs(values[0]) * factor, Math.Abs(values[1]) * factor, Math.Abs(values[2]) * factor);
    }

    /// <summary>Gets the values that store a box of the given half sizes.</summary>
    /// <param name="halfMetres">Half the size along each axis, in metres.</param>
    /// <returns>The parameter's three values.</returns>
    public double[] Stored(Vector3d halfMetres)
    {
        double factor = 1.0 / (MetresPerStored * (StoresHalf ? 1.0 : 0.5));
        return [halfMetres.X * factor, halfMetres.Y * factor, halfMetres.Z * factor];
    }

    /// <summary>Gets where a point of the box's own frame stands in the world.</summary>
    /// <param name="centre">The box's centre, in metres.</param>
    /// <param name="orientation">The task's orientation.</param>
    /// <param name="local">The point, in metres along the box's axes.</param>
    /// <returns>The world point, in metres.</returns>
    public static Vector3d ToWorld(Vector3d centre, Matrix3f orientation, Vector3d local) =>
        centre + orientation.Transposed().Transform(local);
}

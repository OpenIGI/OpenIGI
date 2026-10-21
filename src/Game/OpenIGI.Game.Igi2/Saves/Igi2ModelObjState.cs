using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Igi2.Saves;

/// <summary>
/// The fields every model object keeps first, <c>ModelObjParameters_ptSerializeParameterList</c> (modelobjparameters.c): where it is,
/// its orientation, its orientation and position relative to what it is attached to, and whether it is in the world.
/// </summary>
public static class Igi2ModelObjState
{
    private static readonly float[] Identity = [1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f];

    /// <summary>Writes them: a placed object is attached to nothing, so its relative frame is the identity and its offset nought.</summary>
    /// <param name="writer">The writer.</param>
    /// <param name="position">Where it is, <c>tPos</c>.</param>
    /// <param name="orientation">Its orientation, row by row.</param>
    /// <param name="isInWorld">Whether it is in the world, <c>isRegisteredInWorldHack</c>.</param>
    public static void Write(Igi2StateWriter writer, Vector3d position, IReadOnlyList<float> orientation, bool isInWorld = true)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.Real64x3(position);
        writer.Real9(orientation);
        writer.Real9(Identity);
        writer.Real3(default);
        writer.Int(isInWorld ? 1 : 0);
    }

    /// <summary>Reads them back.</summary>
    /// <param name="reader">The reader.</param>
    /// <returns>Where it is, its orientation row by row, and whether it is in the world.</returns>
    public static (Vector3d Position, float[] Orientation, bool IsInWorld) Read(Igi2StateReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        Vector3d position = reader.Real64x3();
        float[] orientation = reader.Real9();
        reader.Real9();
        reader.Real3();
        return (position, orientation, reader.Int() != 0);
    }
}

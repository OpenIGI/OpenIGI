using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;

namespace OpenIGI.Game.Igi2.Interaction;

/// <summary>A model's magic vertex in the world: <c>RigidDynCubeObj_GetMagicVertexPos</c> (<c>igi2 0x58E430</c>).</summary>
internal static class Igi2MagicVertex
{
    /// <summary>One of a model's magic vertices, turned by an object's frame and added to its position.</summary>
    /// <param name="model">The object's model, or <see langword="null"/> for none, whose vertex is the origin.</param>
    /// <param name="index">Which vertex.</param>
    /// <param name="m">The object's frame, row by row.</param>
    /// <param name="position">The object's position.</param>
    /// <returns>The vertex in the world.</returns>
    public static Vector3d Position(MefModel? model, int index, IReadOnlyList<float> m, Vector3d position)
    {
        Vector3f v = model is not null && model.TryGetMagicVertex(index, out Vector3f vertex) ? vertex : default;
        Fpu x = (((Fpu)v.Z * m[2]) + ((Fpu)v.Y * m[1])) + ((Fpu)v.X * m[0]);
        Fpu y = (((Fpu)v.Z * m[5]) + ((Fpu)v.Y * m[4])) + ((Fpu)v.X * m[3]);
        Fpu z = (((Fpu)v.Z * m[8]) + ((Fpu)v.Y * m[7])) + ((Fpu)v.X * m[6]);
        return new Vector3d((x + position.X).Value, (y + position.Y).Value, (z + position.Z).Value);
    }
}

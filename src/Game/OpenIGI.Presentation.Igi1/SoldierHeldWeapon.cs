using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Igi1;

/// <summary>
/// One weapon model, uploaded once and drawn by every soldier carrying that weapon.
/// </summary>
/// <remarks>
/// Unlike a body, a gun is a rigid model: it is not skinned and its pose never changes, so a single
/// mesh serves every soldier and only the transform differs. That transform is the hand bone's, which
/// is why the body mesh has to be posed but this one does not.
/// </remarks>
/// <param name="geometry">The model's geometry.</param>
/// <param name="mesh">Its one rigid mesh.</param>
/// <param name="instance">The model's instance in its pack, which names its textures.</param>
/// <param name="pack">The pack the model came from.</param>
/// <param name="presentation">The multipart retail object, or <see langword="null"/> where none was built.</param>
internal sealed class SoldierHeldWeapon(
    MeshGeometry geometry,
    MeshHandle mesh,
    MtpInstance? instance,
    LoadedPack? pack,
    IHeldWeaponPresenter? presentation)
{
    public MeshGeometry Geometry { get; } = geometry;

    public MeshHandle Mesh { get; } = mesh;

    public MtpInstance? Instance { get; } = instance;

    public LoadedPack? Pack { get; } = pack;

    /// <summary>The multipart retail object, including its transient TASKTYPE_GUNFLAME child.</summary>
    public IHeldWeaponPresenter? Presentation { get; } = presentation;

    /// <summary>The middle of the model's far end along its barrel (its +Y), in its own frame and units. <b>Not retail.</b></summary>
    public Vector3f Muzzle => _muzzle ??= FarEnd(Geometry.Vertices);

    private Vector3f? _muzzle;

    // The vertices within 2 cm of the furthest along +Y, averaged across.
    private static Vector3f FarEnd(Vertex3D[] vertices)
    {
        if (vertices.Length == 0)
        {
            return default;
        }

        float far = vertices.Max(v => v.Position.Y);
        Vertex3D[] end = [.. vertices.Where(v => v.Position.Y > far - 0.02f)];
        return new Vector3f(end.Average(v => v.Position.X), far, end.Average(v => v.Position.Z));
    }
}

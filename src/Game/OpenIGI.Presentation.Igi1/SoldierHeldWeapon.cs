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
/// <param name="magicMuzzle">The model's magic vertex 2 in metres, the muzzle the original fires a mounted gun from; or <see langword="null"/> for none.</param>
internal sealed class SoldierHeldWeapon(
    MeshGeometry geometry,
    MeshHandle mesh,
    MtpInstance? instance,
    LoadedPack? pack,
    IHeldWeaponPresenter? presentation,
    Vector3f? magicMuzzle = null)
{
    public MeshGeometry Geometry { get; } = geometry;

    public MeshHandle Mesh { get; } = mesh;

    public MtpInstance? Instance { get; } = instance;

    public LoadedPack? Pack { get; } = pack;

    /// <summary>The multipart retail object, including its transient TASKTYPE_GUNFLAME child.</summary>
    public IHeldWeaponPresenter? Presentation { get; } = presentation;

    /// <summary>
    /// Gets where the model's muzzle is, in its own frame and in metres: where its gun flame is drawn, or else its magic vertex 2;
    /// <see langword="null"/> when it has neither. <b>Not retail.</b>
    /// </summary>
    public Vector3f? Muzzle => Presentation?.MuzzleMetres ?? magicMuzzle;
}

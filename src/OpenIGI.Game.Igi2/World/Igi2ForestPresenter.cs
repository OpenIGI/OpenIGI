using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Game.Igi2.World;

/// <summary>
/// Draws I.G.I. 2's forests: one mesh per stand, one draw per tree.
/// </summary>
/// <remarks>
/// <para>
/// The state is the original's, from <c>igi2ed.exe</c>. <c>ForestRender_SetGenericState_Trunk</c>
/// (<c>0x57C5A0</c>) draws opaque with <c>CULLMODE = CCW</c> — <b>the opposite winding to the
/// terrain</b>, which is <c>CW</c> — and with the fog density at
/// <c>DrawTerrainMesh_vFogDensity * 0.8</c>, so a wood fogs a fifth less than the ground it stands on.
/// </para>
/// <para>
/// The foliage is a second pass. <c>ForestRender_SetStates_Foliage</c> (<c>0x57BDF0</c>) chooses between
/// an alpha-tested and an alpha-blended state per face group, and its setup path sets
/// <c>ALPHAREF = 32</c> — so a leaf texel below an eighth opacity is cut away rather than blended.
/// </para>
/// <para>
/// <b>One draw per tree, and no level of detail.</b> The original walks a BSP tree per stand
/// (<c>Forest_DrawTrees1_Optimized</c>) and picks a level of detail per tree
/// (<c>ForestRender_GetLOD</c>, <c>0x57D680</c>); this draws every tree of every stand at full detail.
/// 54,537 trees is more draws than a frame should carry, and the fix is the cull rather than anything
/// about the geometry — which is why the trees are placed correctly first and culled later.
/// </para>
/// </remarks>
public sealed class Igi2ForestPresenter : IDisposable
{
    /// <summary>The category load diagnostics are logged under.</summary>
    public const string LogCategory = "igi2.forest";

    /// <summary>The alpha below which a foliage texel is cut away, out of 255.</summary>
    /// <remarks><c>ForestRender_SetStates_Foliage</c> sets <c>D3DRS_ALPHAREF</c> to this.</remarks>
    public const int FoliageAlphaReference = 32;

    private readonly IGraphicsDevice _device;
    private readonly List<Stand> _stands = [];
    private bool _disposed;

    /// <summary>Builds the forests of one level.</summary>
    /// <param name="device">The device to upload through.</param>
    /// <param name="forest">The level's stands, already placed.</param>
    /// <param name="models">Resolves a stand's model name to its mesh, or null when the level lacks it.</param>
    /// <param name="textures">Resolves a texture name to a handle.</param>
    /// <param name="logger">Where load diagnostics go.</param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    public Igi2ForestPresenter(
        IGraphicsDevice device,
        Igi2Forest forest,
        Func<string, MefModel?> models,
        Func<string, TextureHandle> textures,
        IIgiLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(forest);
        ArgumentNullException.ThrowIfNull(models);
        ArgumentNullException.ThrowIfNull(textures);

        _device = device;
        IIgiLogger log = logger ?? NullLogger.Instance;

        foreach (Igi2ForestStand stand in forest.Stands)
        {
            if (models(stand.Model) is not { } model)
            {
                log.Warning(LogCategory, $"Forest {stand.TaskId} draws '{stand.Model}', which the level does not hold.");
                continue;
            }

            if (MefMeshBuilder.Build(model) is not { } geometry)
            {
                log.Warning(LogCategory, $"Forest {stand.TaskId}'s model '{stand.Model}' has no geometry.");
                continue;
            }

            _stands.Add(new Stand(
                _device.CreateMesh(geometry.Vertices, geometry.Indices),
                geometry.Indices.Length,
                stand.Trees,
                TextureHandle.None));
        }

        TreeCount = _stands.Sum(static s => s.Trees.Count);
        log.Information(LogCategory, $"Forest: {_stands.Count} stand(s), {TreeCount} trees.");
    }

    /// <summary>Gets how many trees the presenter draws.</summary>
    public int TreeCount { get; }

    /// <summary>Gets how many stands were uploaded.</summary>
    public int StandCount => _stands.Count;

    /// <summary>Draws every tree of every stand.</summary>
    /// <param name="camera">Where the world is seen from.</param>
    /// <exception cref="ObjectDisposedException">The presenter has been disposed.</exception>
    /// <remarks>
    /// Opaque, because that is what the trunk state is; the foliage's alpha test belongs to the
    /// sub-meshes whose texture carries a cut-out, and separating them needs the per-face-group flag the
    /// original reads and this does not yet carry.
    /// </remarks>
    public void Draw(in Camera3D camera)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // CCW, which is the opposite of the terrain's winding and has to be set per pass rather than
        // once for the frame.
        _device.SetCullMode(CullMode.CounterClockwise);

        foreach (Stand stand in _stands)
        {
            foreach (Igi2Tree tree in stand.Trees)
            {
                _device.DrawMesh(
                    stand.Mesh,
                    0,
                    stand.IndexCount,
                    camera.CreateModelView(tree.Position, OrientationOf(tree)),
                    stand.Texture,
                    TextureHandle.None,
                    BlendMode.Opaque);
            }
        }
    }

    /// <summary>Builds a tree's orientation, with its per-axis scale folded in.</summary>
    /// <param name="tree">The tree.</param>
    /// <returns>The matrix to draw it with.</returns>
    /// <remarks>
    /// Scale rides on the orientation because that is the only transform a draw carries. A quarter of
    /// the corpus's trees are tilted rather than upright — the bake leans them to the ground they stand
    /// on — so the angles are not decoration.
    /// </remarks>
    public static Matrix3f OrientationOf(Igi2Tree tree)
    {
        Matrix3f rotation = WorldScene.ToOrientation(tree.Angles);
        return new Matrix3f(
            rotation.M11 * tree.Scale.X,
            rotation.M12 * tree.Scale.X,
            rotation.M13 * tree.Scale.X,
            rotation.M21 * tree.Scale.Y,
            rotation.M22 * tree.Scale.Y,
            rotation.M23 * tree.Scale.Y,
            rotation.M31 * tree.Scale.Z,
            rotation.M32 * tree.Scale.Z,
            rotation.M33 * tree.Scale.Z);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (Stand stand in _stands)
        {
            _device.DestroyMesh(stand.Mesh);
        }

        _stands.Clear();
    }

    /// <inheritdoc/>
    public override string ToString() => $"I.G.I. 2 forest, {_stands.Count} stand(s), {TreeCount} trees";

    private readonly record struct Stand(
        MeshHandle Mesh,
        int IndexCount,
        IReadOnlyList<Igi2Tree> Trees,
        TextureHandle Texture);
}

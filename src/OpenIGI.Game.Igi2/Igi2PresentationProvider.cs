using OpenIGI.Formats;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.Ilff;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Presentation;
using OpenIGI.Rendering;
using OpenIGI.Presentation.Rendering;

namespace OpenIGI.Game.Igi2;

/// <summary>Builds the renderers I.G.I. 2 draws with.</summary>
/// <remarks>
/// <para>
/// <b>Three members, and that is the honest surface.</b> The sequel draws its placed world, its ground
/// and its sky; it has no first-person arms, no held weapon, no HUD and no map computer, because none of
/// those systems exist for it yet. Every other factory is left at the interface's default of null, which
/// the host reads as "this title does not present that pass" and skips.
/// </para>
/// <para>
/// <b>Not here yet, and for one shared reason.</b> The forest and the water are read and culled and
/// tessellated, and neither is wired: both need the mission's model packs and its own archives, and a
/// <see cref="GroundPresentationRequest"/> carries the level tree and the resource registry alone. That
/// is a seam to widen deliberately once, with both passes in front of it, rather than twice by halves.
/// </para>
/// <para>
/// The world presenter itself is <c>WorldRenderer</c>, shared with Project I.G.I. It named no task type,
/// no model id and no asset path, so it moved into the presentation layer and both titles draw through
/// it; what differs is only how a level becomes placements, which is
/// <see cref="World.Igi2DrawableScene"/>'s job.
/// </para>
/// <para>
/// The ground arrives through <see cref="CreateGround"/> rather than
/// <see cref="ITitlePresentationProvider.CreateTerrain"/>, because the latter carries a
/// <c>TerrainSurface</c> built from Project I.G.I.'s chunked octree and the sequel's flat grid of
/// <c>.thm</c> tiles produces no such thing. Given the level and a registry, this reads its own.
/// </para>
/// </remarks>
public sealed class Igi2PresentationProvider : ITitlePresentationProvider
{
    /// <summary>Gets the shared instance.</summary>
    public static Igi2PresentationProvider Instance { get; } = new();

    /// <inheritdoc/>
    public IWorldPresenter CreateWorld(in WorldPresentationRequest request) =>
        new WorldRenderer(request.Device, request.Resources, AssetFormatProfile.Igi2, request.Logger);

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// The sequel's sky is a typed <see cref="World.FlatSkyTask"/> on the level tree, which is exactly
    /// why this had to come behind the seam: the shared <c>SkyRenderer</c> finds its own by walking for a
    /// <c>DeclaredTask</c> named <c>FlatSky</c>, and a title with a class for the type has no such node.
    /// The host built that renderer directly, so until now I.G.I. 2 drew no sky at all and nothing said
    /// so.
    /// </para>
    /// <para>
    /// A level that declares no sky gets no presenter, so the host skips the pass rather than clearing
    /// to a colour nobody authored.
    /// </para>
    /// </remarks>
    public ISkyPresenter? CreateSky(in SkyPresentationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request.LevelRoot);
        ArgumentNullException.ThrowIfNull(request.Resources);

        if (FindSky(request.LevelRoot) is not { } sky)
        {
            return null;
        }

        Igi2TextureLoader textures = new(request.Device, request.Resources, request.Logger);
        return new Igi2SkyPresenter(request.Device, sky, textures.Resolve, request.Logger);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The level's <c>TerrainMap</c> tasks say which tiles it has; <c>heightmaps.res</c> beside them
    /// holds the three maps of each. A level that declares no terrain gets no presenter rather than an
    /// empty one, so the host skips the pass entirely instead of issuing nothing every frame.
    /// </remarks>
    public ITerrainPresenter? CreateGround(in GroundPresentationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request.LevelRoot);
        ArgumentNullException.ThrowIfNull(request.Resources);

        Igi2TerrainSurface surface = Igi2TerrainSurface.Build(
            request.LevelRoot, request.Resources, request.Logger);
        if (surface.IsEmpty)
        {
            return null;
        }

        Igi2TextureLoader textures = new(request.Device, request.Resources, request.Logger);

        // The level's own Terrain task carries Detail and Adaption, which are what the original's split
        // test is scaled by. A level that declares them gets the quadtree; one that does not gets every
        // tile at full detail, which is the same ground and 70 times the triangles.
        return Igi2TerrainLodSettings.Read(request.LevelRoot) is { } settings
            ? new Igi2TerrainPresenter(request.Device, surface, settings, textures.Resolve, request.Logger)
            : new Igi2TerrainPresenter(request.Device, surface, textures.Resolve, request.Logger);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// The stands come from the level's <c>Forest</c> tasks and the trees from the <c>forest_&lt;id&gt;.dat</c>
    /// beside them; the model each stand plants comes from the mission's packs, which is the one thing a
    /// ground or a sky never needs and the reason this pass has a request of its own.
    /// </para>
    /// <para>
    /// A host that cannot resolve models yet supplies a lookup that declines, and the presenter then
    /// finds no geometry for any stand — so nothing is built rather than 54,537 invisible trees being
    /// culled every frame.
    /// </para>
    /// </remarks>
    public IForestPresenter? CreateForest(in ScenePresentationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request.LevelRoot);
        ArgumentNullException.ThrowIfNull(request.Resources);
        ArgumentNullException.ThrowIfNull(request.Models);

        Igi2Forest forest = Igi2Forest.Build(request.LevelRoot, request.Resources, request.Logger);
        if (forest.IsEmpty)
        {
            return null;
        }

        Igi2TextureLoader textures = new(request.Device, request.Resources, request.Logger);
        Func<string, int, string?> materials = request.MaterialTextures;

        return new Igi2ForestPresenter(
            request.Device,
            forest,
            request.Models,
            (model, slot) => materials(model, slot) is { } name
                ? textures.Resolve(Igi2ModelTable.TextureResourceName(name))
                : TextureHandle.None,
            request.Logger);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// Water needs nothing from the model packs — a sheet is a surface, not an instance of anything — so
    /// the only part of the request it reads beyond the level is the registry, which is where its
    /// texture and its baked reflection both live.
    /// </para>
    /// <para>
    /// The reflection is the first cube map in the sheet's own <c>water_&lt;id&gt;.res</c>. A level lists
    /// several and the original picks between them by which the camera is nearest; picking the first is
    /// this port's simplification, and a sheet whose archive is absent simply does not reflect.
    /// </para>
    /// </remarks>
    public IWaterPresenter? CreateWater(in ScenePresentationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request.LevelRoot);
        ArgumentNullException.ThrowIfNull(request.Resources);

        Igi2WaterSurface surface = Igi2WaterSurface.Build(request.LevelRoot, request.Logger);
        if (surface.IsEmpty)
        {
            return null;
        }

        Dictionary<int, WaterTask> tasks = [];
        foreach (WaterTask water in Waters(request.LevelRoot))
        {
            tasks[water.Id] = water;
        }

        IGraphicsDevice device = request.Device;
        ResourceRegistry resources = request.Resources;
        IIgiLogger logger = request.Logger;

        Igi2TextureLoader textures = new(device, resources, logger);
        return new Igi2WaterPresenter(
            device,
            surface,
            textures.Resolve,
            sheet => LoadCube(device, resources, tasks, sheet, logger),
            logger);
    }

    /// <summary>Loads one sheet's baked reflection, or hands back nothing.</summary>
    private static TextureHandle LoadCube(
        IGraphicsDevice device,
        ResourceRegistry resources,
        Dictionary<int, WaterTask> tasks,
        Igi2WaterSheet sheet,
        IIgiLogger logger)
    {
        if (sheet.EnvmapIndices.Count == 0 || !tasks.TryGetValue(sheet.TaskId, out WaterTask? water))
        {
            return TextureHandle.None;
        }

        string name = "MISSION:" + Igi2EnvironmentMap.ArchiveDirectory + "/" + Igi2EnvironmentMap.ArchiveName(sheet.TaskId);
        if (!resources.TryLoad(name, out ReadOnlyMemory<byte> bytes))
        {
            return TextureHandle.None;
        }

        try
        {
            IresArchive archive = IresArchive.Read(bytes);
            return Igi2EnvironmentMap.Load(water, sheet.EnvmapIndices[0], archive).Upload(device);
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or GraphicsDeviceException)
        {
            logger.Warning(
                Igi2WaterPresenter.LogCategory,
                $"Water {sheet.TaskId}'s reflection did not load: {ex.Message}");
            return TextureHandle.None;
        }
    }

    /// <summary>Walks the level's water tasks.</summary>
    private static IEnumerable<WaterTask> Waters(GameTask task)
    {
        foreach (GameTask child in task.Children)
        {
            if (child is WaterTask water)
            {
                yield return water;
            }

            foreach (WaterTask descendant in Waters(child))
            {
                yield return descendant;
            }
        }
    }



    /// <summary>Finds the level's sky, if it declares one.</summary>
    /// <remarks>
    /// The first in the tree. No shipped level declares two, and a second would be a second backdrop
    /// drawn over the first rather than anything meaningful.
    /// </remarks>
    private static World.FlatSkyTask? FindSky(GameTask task)
    {
        foreach (GameTask child in task.Children)
        {
            if (child is World.FlatSkyTask sky)
            {
                return sky;
            }

            if (FindSky(child) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}

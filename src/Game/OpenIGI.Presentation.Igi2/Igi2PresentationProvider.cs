using OpenIGI.Formats;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats.Ilff;
using OpenIGI.Game.Igi2.ViewModel;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Presentation;
using OpenIGI.Rendering;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Game.Igi2;
using OpenIGI.Presentation.Igi2.ViewModel;
using OpenIGI.Presentation.Igi2.World;

namespace OpenIGI.Presentation.Igi2;

/// <summary>Builds the renderers I.G.I. 2 draws with.</summary>
/// <remarks>
/// <para>
/// <b>Only what exists.</b> The sequel draws its placed world, its ground, its sky, its forests and water,
/// and what its own player holds in front of its eyes (<see cref="CreateTitleViewModel"/>). Its title HUD draws weapon sights;
/// the remaining HUD and map computer are pending. Every other factory is left at the interface's default of null, which the host
/// reads as "this title does not present that pass" and skips.
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
/// <see cref="OpenIGI.Game.Igi2.World.Igi2DrawableScene"/>'s job.
/// </para>
/// <para>
/// The ground is registered for a <see cref="GroundPresentationRequest"/> rather than a
/// <see cref="TerrainPresentationRequest"/>, because the latter carries a <c>TerrainSurface</c> built from
/// Project I.G.I.'s chunked octree and the sequel's flat grid of <c>.thm</c> tiles produces no such thing.
/// Given the level and a registry, this reads its own.
/// </para>
/// </remarks>
public sealed class Igi2PresentationProvider : ITitlePresentationProvider
{
    /// <summary>Initializes a new instance of the <see cref="Igi2PresentationProvider"/> class.</summary>
    public Igi2PresentationProvider() =>
        Presenters = new PresenterRegistry()
            .Add<HudPresentationRequest, ITitleHudPresenter>(CreateTitleHud)
            .Add<ViewModelPresentationRequest, ITitleViewModelPresenter>(CreateTitleViewModel)
            .Add<WorldPresentationRequest, IWorldPresenter>(CreateWorld)
            .Add<SkyPresentationRequest, ISkyPresenter>(CreateSky)
            .Add<GroundPresentationRequest, ITerrainPresenter>(CreateGround)
            .Add<ScenePresentationRequest, IForestPresenter>(CreateForest)
            .Add<ScenePresentationRequest, IWaterPresenter>(CreateWater)

            // The remastered computer is both games' own: the shared display, fed whichever mission's objectives there are.
            .Add<MapComputerPresentationRequest, ITacticalComputerPresenter>(static (in MapComputerPresentationRequest request) =>
                new TacticalComputerRenderer(request.Device, request.Resources, request.Logger, request.ResolveText));

    /// <inheritdoc/>
    public PresenterRegistry Presenters { get; }

    /// <inheritdoc/>
    private ITitleHudPresenter? CreateTitleHud(in HudPresentationRequest request) => new OpenIGI.Presentation.Igi2.Hud.Igi2HudRenderer(request.Device, request.Logger);

    /// <inheritdoc/>
    /// <remarks>The arms and the weapon in them, posed by the player's view; see <see cref="Igi2ViewModelRenderer"/>.</remarks>
    private ITitleViewModelPresenter? CreateTitleViewModel(in ViewModelPresentationRequest request) =>
        new Igi2ViewModelRenderer(request.Device, request.Resources, request.Logger, request.Work);

    /// <inheritdoc/>
    private IWorldPresenter CreateWorld(in WorldPresentationRequest request) =>
        new WorldRenderer(request.Device, request.Resources, AssetFormatProfile.Igi2, request.Logger);

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// The sequel's sky is a typed <see cref="OpenIGI.Game.Igi2.World.FlatSkyTask"/> on the level tree, which is exactly
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
    private ISkyPresenter? CreateSky(in SkyPresentationRequest request)
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
    /// <inheritdoc/>
    /// <remarks>
    /// The sequel's levels carry a <c>GlobalLight</c> task; see <see cref="Igi2SceneAtmosphere"/> for
    /// what the original does with it and why a level without the haze does not read as night.
    /// </remarks>
    public SceneAtmosphere? ReadAtmosphere(GameTask levelRoot) =>
        Igi2SceneAtmosphere.TryRead(levelRoot, out Igi2SceneAtmosphere air)
            ? new SceneAtmosphere(air.FogColour, air.FogDensity, air.Ambient)
            : null;

    /// <summary>What a sampled object lightmap is multiplied by: the sequel's lightmap stage is <c>MODULATE2X</c>.</summary>
    /// <remarks>
    /// <c>Direct3DRender_SetTextureStateLightmap</c> (<c>igi2ed 0x56D140</c>) sets stage 0 to
    /// <c>SELECTARG1(TEXTURE)</c> and stage 1 to <c>COLOROP = MODULATE2X</c> with <c>COLORARG1 = TEXTURE</c>
    /// (the bake) and <c>COLORARG2 = CURRENT</c>. The vertex colour never enters; the product is doubled.
    /// A lightmapped object drawn at a plain multiply is half the brightness the game shipped at.
    /// </remarks>
    public const float LightmapGain = 2f;

    /// <inheritdoc/>
    /// <remarks>
    /// The gain is a property of the renderer and holds for every level; the sun is read off the level and
    /// may be absent, in which case an unlightmapped object is left as the port has always drawn it.
    /// </remarks>
    public ObjectShading? ReadObjectShading(GameTask levelRoot) =>
        new ObjectShading(
            LightmapGain,
            Igi2FixedFunctionSun.TryRead(levelRoot, out Igi2FixedFunctionSun sun)
                ? new FixedFunctionSun(sun.Ambient, sun.Colour, sun.Direction)
                : null);

    /// <summary>How dark a sun has to be before the picture is shown as night.</summary>
    /// <remarks>
    /// <para>
    /// The band is fitted to what the sequel actually authors, which is the only thing available: the
    /// engine has no notion of exposure and nothing in a level says how bright to show it. Over the
    /// twenty-four levels the sun's luminance is either under 0.45 or over 0.55, with nothing in
    /// between — three night levels at 0.00, 0.11 and 0.15, a dusk one at 0.32, a dim mine at 0.41, and
    /// everything else from 0.56 to 0.99. So the band brackets a gap in the data rather than splitting
    /// a continuum, and every level lands firmly on one side of it.
    /// </para>
    /// </remarks>
    public const float NightLuminance = 0.05f;

    /// <summary>The luminance above which a level is shown as full day.</summary>
    public const float DayLuminance = 0.45f;

    /// <summary>How dark a level has to read before the cycle binds it by its night end.</summary>
    /// <remarks>
    /// Three of the twenty-four are past this: <c>location1/level1</c> at 0.85, <c>location2/LEVEL6</c> at
    /// 0.94 and <c>location3/LEVEL4</c> at 1.00, whose sun is literally black. The two below it are a dusk
    /// map at 0.24 and a dim one at 0.03, and both are far nearer a dim DAY than a night -- bound by their
    /// night end they would open at midnight, which is not what they are.
    /// </remarks>
    public const float NightBoundNightfall = 0.5f;

    /// <summary>The hour a night-bound level opens at: the darkest point of the cycle.</summary>
    /// <remarks>
    /// <para>
    /// Midnight rather than an hour fitted per level, because the sequel gives no hour to fit one from --
    /// every campaign level writes <c>Time 12</c> and every multiplayer map writes <c>Time 0</c>, so the
    /// field is a constant and says nothing. Midnight is where the cycle's own handover is complete: the
    /// moon is fully risen and the sun fully faded, so the authored night is reproduced exactly rather
    /// than crossfaded with a sun on its way down.
    /// </para>
    /// <para>
    /// Written as twenty-four rather than nought, which is the same instant: <c>DayNightCycle.Apply</c>
    /// reads a reference hour as <c>&gt; 0 and &lt;= 24</c> -- nought is its "this level said nothing" --
    /// and <c>Reset</c> wraps twenty-four back to nought for the clock itself. So this is midnight said
    /// in the one way both of them hear.
    /// </para>
    /// </remarks>
    public const float NightReferenceHour = 24f;

    /// <summary>The daylight sun a night level would have authored, taken from the ones that did.</summary>
    /// <remarks>
    /// The median over the nineteen levels the corpus reads as day, which is the only evidence available:
    /// a level that authors a night authors no day, and the cycle has to sweep it somewhere. Median rather
    /// than mean so <c>location3/LEVEL4</c>'s pure black and <c>location2/LEVEL2</c>'s over-unity red
    /// pull nothing.
    /// </remarks>
    public static readonly Vector3f DaylightColour = new(0.923077f, 0.788462f, 0.58f);

    /// <summary>And the fill, measured the same way over the same nineteen.</summary>
    public static readonly Vector3f DaylightAmbient = new(0.28308f, 0.288462f, 0.29f);

    /// <inheritdoc/>
    public DeferredLighting? ReadDeferredLighting(GameTask levelRoot)
    {
        ArgumentNullException.ThrowIfNull(levelRoot);

        // The fill is the GlobalLight's object ambient -- the same one the fixed-function path shines on
        // an object with no bake -- and not the Dirlight's back colour, which nineteen of the sequel's
        // levels leave at black.
        Vector3f ambient = Igi2SceneAtmosphere.TryRead(levelRoot, out Igi2SceneAtmosphere air)
            ? air.ObjectAmbient
            : Vector3f.Zero;

        // And the darkness is the sun's own, because that is where the sequel puts it. A level with no
        // Dirlight at all has no sun to be dark, so it reads as day rather than as pitch black.
        float nightfall = 0f;
        if (DirectionalLightSettings.TryRead(levelRoot, out DirectionalLightSettings sun))
        {
            float luminance = (sun.Colour.X * 0.2126f) + (sun.Colour.Y * 0.7152f) + (sun.Colour.Z * 0.0722f);
            float day = Math.Clamp((luminance - NightLuminance) / (DayLuminance - NightLuminance), 0f, 1f);
            nightfall = 1f - (day * day * (3f - (2f * day)));
        }

        // A level the sequel authors as a night is bound to the cycle by its NIGHT end, so the clock
        // sweeps it UP to a day rather than down from one it never had. See AuthoredNight.
        AuthoredNight? night = nightfall > NightBoundNightfall
            ? new AuthoredNight(NightReferenceHour, DaylightColour, DaylightAmbient)
            : null;

        // And the sequel's bakes are WHOLE -- the ground's third pass is a complete lighting solution and
        // a lightmapped object carries one too, which is why the fixed-function sun reaches only the
        // objects that have none. So nothing is to add a sun on top of them. See DeferredLighting.
        return new DeferredLighting(ambient, nightfall, night, CompleteBake: true);
    }

    /// <inheritdoc/>
    private ITerrainPresenter? CreateGround(in GroundPresentationRequest request)
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
    private IForestPresenter? CreateForest(in ScenePresentationRequest request)
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
    /// the only part of the request it reads beyond the level is the registry, which is where its baked
    /// reflections and the ground under it live.
    /// </para>
    /// <para>
    /// The reflections are the cube maps in the sheet's own <c>water_&lt;id&gt;.res</c>, one a slot of its
    /// resource list, and the slot drawn is the one its index expression picks. A sheet whose archive is
    /// absent is not drawn, as the original returns before drawing one with no valid reflection.
    /// </para>
    /// </remarks>
    private IWaterPresenter? CreateWater(in ScenePresentationRequest request)
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
        IOpenIgiLogger logger = request.Logger;

        // How shallow the water is reads the ground under it (Water_VertexFunction); the light is the level's sun
        // and the objects' ambient, as WaterRender_Draw hands them to the shader.
        Igi2TerrainSurface ground = Igi2TerrainSurface.Build(request.LevelRoot, resources, logger);
        Vector3f ambient = Igi2SceneAtmosphere.TryRead(request.LevelRoot, out Igi2SceneAtmosphere air) ? air.ObjectAmbient : Vector3f.Zero;
        Igi2WaterLight light = Igi2FixedFunctionSun.TryRead(request.LevelRoot, out Igi2FixedFunctionSun sun)
            ? new Igi2WaterLight(sun.Direction, sun.Colour, ambient)
            : new Igi2WaterLight(Vector3f.Zero, Vector3f.Zero, ambient);

        return new Igi2WaterPresenter(
            device,
            surface,
            sheet => LoadCubes(device, resources, tasks, sheet, logger),
            logger,
            taskId => tasks.TryGetValue(taskId, out WaterTask? water) ? water.EnvmapSlot : 0,
            ground.WaterGroundAt,
            light);
    }

    /// <summary>Loads one sheet's baked reflections, one a slot of its resource list (<c>Water_LoadEnvmapData</c>).</summary>
    private static List<TextureHandle> LoadCubes(
        IGraphicsDevice device,
        ResourceRegistry resources,
        Dictionary<int, WaterTask> tasks,
        Igi2WaterSheet sheet,
        IOpenIgiLogger logger)
    {
        if (sheet.EnvmapIndices.Count == 0 || !tasks.TryGetValue(sheet.TaskId, out WaterTask? water))
        {
            return [];
        }

        string name = "MISSION:" + Igi2EnvironmentMap.ArchiveDirectory + "/" + Igi2EnvironmentMap.ArchiveName(sheet.TaskId);
        if (!resources.TryLoad(name, out ReadOnlyMemory<byte> bytes))
        {
            return [];
        }

        IresArchive archive;
        try
        {
            archive = IresArchive.Read(bytes);
        }
        catch (InvalidDataException ex)
        {
            logger.Warning(Igi2WaterPresenter.LogCategory, $"Water {sheet.TaskId}'s reflections did not load: {ex.Message}");
            return [];
        }

        List<TextureHandle> cubes = [];
        foreach (int index in sheet.EnvmapIndices)
        {
            try
            {
                cubes.Add(Igi2EnvironmentMap.Load(water, index, archive).Upload(device));
            }
            catch (Exception ex) when (ex is InvalidDataException or ArgumentException or GraphicsDeviceException)
            {
                logger.Warning(
                    Igi2WaterPresenter.LogCategory,
                    $"Water {sheet.TaskId}'s reflection {index} did not load: {ex.Message}");
                cubes.Add(TextureHandle.None);
            }
        }

        return cubes;
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
    private static OpenIGI.Game.Igi2.World.FlatSkyTask? FindSky(GameTask task)
    {
        foreach (GameTask child in task.Children)
        {
            if (child is OpenIGI.Game.Igi2.World.FlatSkyTask sky)
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

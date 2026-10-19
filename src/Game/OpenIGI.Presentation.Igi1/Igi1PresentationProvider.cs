using OpenIGI.Game.Ai;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Formats;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Presentation.Igi1.Menus;
using OpenIGI.Game.Igi1.Menus;
using OpenIGI.Game.World;
using OpenIGI.Presentation;
using OpenIGI.Presentation.Lighting;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Presentation.Igi1.World;

namespace OpenIGI.Presentation.Igi1;

/// <summary>Builds the renderers Project I.G.I. draws with.</summary>
/// <remarks>
/// The ground, the placed world, and the first-person and projectile passes. Each arrived when the host
/// stopped naming its renderer, which is what lets the renderer live here; the census on
/// <c>TitleContentTests</c> records what is holding the rest.
/// </remarks>
public sealed class Igi1PresentationProvider : ITitlePresentationProvider
{
    /// <summary>Initializes a new instance of the <see cref="Igi1PresentationProvider"/> class.</summary>
    public Igi1PresentationProvider() =>
        Presenters = new PresenterRegistry()
            .Add<RetailMenusRequest, IRetailMenus>(static (in RetailMenusRequest request) =>
                new Igi1RetailMenus(request))
            .Add<MissionEffectsRequest, IMissionImpactView>(static (in MissionEffectsRequest request) =>
                request.Gameplay is Igi1Gameplay gameplay
                    ? new Igi1ImpactView(new RetailImpactRenderer(request.Device, request.Resources, request.FileSystem), gameplay)
                    : null)
            .Add<MissionPresentationRequest, IMissionHudOverlay>(static (in MissionPresentationRequest request) =>
                request.Gameplay is Igi1Gameplay gameplay ? new Igi1HudOverlay(request.Device, gameplay) : null)
            .Add<MissionPresentationRequest, IMissionFirstPersonView>(static (in MissionPresentationRequest request) =>
                request.Gameplay is Igi1Gameplay gameplay ? new Igi1FirstPersonView(request.Device, gameplay, request.Logger) : null)
            .Add<MissionPresentationRequest, IMissionWorldView>(static (in MissionPresentationRequest request) =>
                request.Gameplay is Igi1Gameplay gameplay ? new Igi1WorldView(gameplay) : null)
            .Add<MissionPresentationRequest, IMissionViewpoint>(static (in MissionPresentationRequest request) =>
                request.Gameplay is Igi1Gameplay gameplay ? new Igi1Viewpoint(gameplay) : null)
            .Add<SoldierPresentationRequest, ISoldierBodies>(static (in SoldierPresentationRequest request) =>
                new SoldierRenderer(request.Device, request.Resources, request.Logger, request.Magic) { DetailMaps = request.DetailMaps })
            .Add<WorldPresentationRequest, IWorldPresenter>(CreateWorld)
            .Add<SkyPresentationRequest, ISkyPresenter>(CreateSky)
            .Add<ViewModelPresentationRequest, IViewModelPresenter>(CreateViewModel)
            .Add<ViewModelPresentationRequest, IHeldWeaponPresenter>(CreateHeldWeapon)
            .Add<ViewModelPresentationRequest, IGrenadePresenter>(CreateGrenades)
            .Add<ViewModelPresentationRequest, IMissilePresenter>(CreateMissiles)
            .Add<ViewModelPresentationRequest, IGunPickupPresenter>(CreateGunPickups)
            .Add<EffectsPresentationRequest, IBulletMarkPresenter>(CreateBulletMarks)
            .Add<EffectsPresentationRequest, ICameraStatusLightPresenter>(CreateCameraStatusLights)
            .Add<HudPresentationRequest, IHudPresenter>(CreateHud)
            .Add<HudPresentationRequest, IRemasteredHudPresenter>(CreateRemasteredHud)
            .Add<MapComputerPresentationRequest, IMapComputerPresenter>(CreateMapComputer)
            .Add<MapComputerPresentationRequest, ITacticalComputerPresenter>(CreateTacticalComputer)
            .Add<ComputerHighlightRequest, IComputerHighlightCatalogue>(CreateComputerHighlights)
            .Add<TerrainPresentationRequest, ITerrainPresenter>(CreateTerrain);

    /// <inheritdoc/>
    public PresenterRegistry Presenters { get; }

    /// <summary>The dome luminance at or under which a level is drawn as night.</summary>
    /// <remarks>
    /// <para>
    /// Fitted to a gap in the data rather than to a continuum. Measured as
    /// <see cref="SkyDomeColours.MeanLuminance"/> over the thirteen missions that declare a sky, the
    /// daylit ones read 0.47 (1, 4), 0.50 (8), 0.52 (5) and 0.86 (2, 6, 10); the dark ones read 0.23
    /// (12), 0.26 (11), 0.28 (3, 9), 0.30 (13) and 0.35 (7). Nothing falls between 0.35 and 0.47.
    /// </para>
    /// <para>
    /// The dark six are what vanilla draws as night -- a slate or navy dome over ground lit by its bake --
    /// and they are exactly the six the dynamic path drew as a bright noon, because their <c>Dirlight</c>
    /// is authored at the same 12 o'clock as everyone else's. Mission 14 is indoors and declares no sky.
    /// </para>
    /// </remarks>
    public const float NightSkyLuminance = 0.4f;

    /// <summary>The hour a night level is bound to the cycle at: midnight, said as 24.</summary>
    /// <remarks>
    /// Twenty-four rather than nought because <c>DayNightCycle.Apply</c> hears nought as "the level said
    /// nothing". See the sequel's provider, which binds its night levels at the same hour.
    /// </remarks>
    public const float NightReferenceHour = 24f;

    /// <summary>The daylight sun a night level sweeps up to, taken from the levels that author a day.</summary>
    /// <remarks>
    /// The per-channel median of the <c>Front Color</c> over the seven daylit missions (1, 2, 4, 5, 6, 8,
    /// 10). A night level authors no day of its own, and its own sun cannot stand in: it is the light the
    /// original shines on unbaked models under that night sky, which the cycle uses as the moon.
    /// </remarks>
    public static readonly Vector3f DaylightColour = new(0.923077f, 0.769231f, 0.615385f);

    /// <summary>And the fill, the median <c>Back Color</c> over the same seven.</summary>
    public static readonly Vector3f DaylightAmbient = new(0.230769f, 0.288462f, 0.326923f);

    /// <summary>How much exposure a night level takes off at full night, on top of the cycle's own.</summary>
    /// <remarks>
    /// <para>
    /// Far less than the sequel's 0.632. That figure answers a sun left UP and painted nearly black, which
    /// the eye adaptation reads as a correctly exposed dark subject and lifts back to grey. These levels are
    /// lit by the cycle's own moon under the horizon-bound sun, so the lighting already computes their
    /// darkness and the adaptation meets a scene that is dark for the reason a real night is.
    /// </para>
    /// <para>
    /// Measured on mission 3's ground from the spawn view, ultra against vanilla: 0.072 against 0.415
    /// with the sequel's drop, 0.140 with this one and the brighter moon, sky and dark-adapted eye that
    /// came with it. The original lights its nights as a grey dusk; the remaster's is meant to read as
    /// night and still be playable, and this is where that was judged to sit.
    /// </para>
    /// </remarks>
    public const float NightExposureDrop = 0.2f;

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// Nothing for a daylit level, which keeps the sweep exactly as it was: the authored sun is a noon
    /// sun and the cycle's arc is fitted through it.
    /// </para>
    /// <para>
    /// A level whose dome is painted as night is bound to the cycle by its NIGHT end, as the sequel's
    /// night levels are: it opens at midnight with its authored sun as the moon and its authored model
    /// ambient -- the <c>GlobalLight</c> band the original lights models with, equal to the
    /// <c>Dirlight</c>'s back colour in every mission -- as the night fill, and the clock sweeps it up to
    /// a day it never authored. Its bakes are not whole, so the hybrid sun stays.
    /// </para>
    /// </remarks>
    public DeferredLighting? ReadDeferredLighting(GameTask levelRoot)
    {
        ArgumentNullException.ThrowIfNull(levelRoot);

        if (!SkyDomeColours.TryRead(levelRoot, out SkyDomeColours dome)
            || dome.MeanLuminance > NightSkyLuminance)
        {
            return null;
        }

        return new DeferredLighting(
            GlobalLightSettings.Read(levelRoot).ObjectAmbient,
            Nightfall: 1f,
            new AuthoredNight(NightReferenceHour, DaylightColour, DaylightAmbient, SunIsMoon: false),
            NightExposureDrop: NightExposureDrop);
    }

    /// <inheritdoc/>
    private IWorldPresenter CreateWorld(in WorldPresentationRequest request) =>
        new WorldRenderer(request.Device, request.Resources, AssetFormatProfile.Igi1, request.Logger);

    /// <inheritdoc/>
    /// <remarks>
    /// The dome is sized to the far plane, which is this game's rule and not the sequel's; a layer's
    /// texture is a path under <c>LOCAL:</c>, so the file system is the half of the request it reads.
    /// </remarks>
    private ISkyPresenter CreateSky(in SkyPresentationRequest request) =>
        new SkyRenderer(request.Device, request.FileSystem, request.LevelRoot, request.FarPlane, request.Logger);

    /// <inheritdoc/>
    private IViewModelPresenter CreateViewModel(in ViewModelPresentationRequest request) =>
        new ViewModelRenderer(request.Device, request.Resources, request.Logger);

    /// <inheritdoc/>
    private IHeldWeaponPresenter CreateHeldWeapon(in ViewModelPresentationRequest request) =>
        new HeldWeaponRenderer(request.Device, request.Resources, MagicOf(request), request.Logger);

    /// <inheritdoc/>
    private IGrenadePresenter CreateGrenades(in ViewModelPresentationRequest request) =>
        new GrenadeRenderer(request.Device, request.Resources, MagicOf(request), request.Logger);

    /// <inheritdoc/>
    private IMissilePresenter CreateMissiles(in ViewModelPresentationRequest request) =>
        new MissileRenderer(request.Device, request.Resources, MagicOf(request), request.Logger);

    /// <inheritdoc/>
    private IGunPickupPresenter CreateGunPickups(in ViewModelPresentationRequest request) =>
        new GunPickupRenderer(request.Device, request.Resources, MagicOf(request), request.Logger);

    /// <summary>Takes the magic-object table these renderers cannot do without.</summary>
    /// <remarks>
    /// The request carries it optionally, because a title may have no table of that shape — I.G.I. 2's
    /// magic objects are QTask data rather than a <c>DefineMagicObj</c> table, so its host publishes
    /// none. This game always has one, and every renderer below resolves its model through it, so a
    /// missing one is a host that wired something wrong rather than a case to degrade around.
    /// </remarks>
    private static MagicObjectRegistry MagicOf(in ViewModelPresentationRequest request) =>
        request.Magic
        ?? throw new InvalidOperationException(
            "Project I.G.I.'s view model needs its magic-object table, and the request carries none.");

    /// <inheritdoc/>
    private IBulletMarkPresenter CreateBulletMarks(in EffectsPresentationRequest request) =>
        new BulletMarkRenderer(request.Device, request.Resources, request.Logger);

    /// <inheritdoc/>
    private ICameraStatusLightPresenter CreateCameraStatusLights(in EffectsPresentationRequest request) =>
        new CameraStatusLightRenderer(request.Device, request.Logger);

    /// <inheritdoc/>
    private IHudPresenter CreateHud(in HudPresentationRequest request) =>
        new StatusScreenRenderer(request.Device, request.Logger);

    /// <inheritdoc/>
    private IRemasteredHudPresenter CreateRemasteredHud(in HudPresentationRequest request) =>
        new RemasteredHudRenderer(request.Device, request.Logger);

    /// <inheritdoc/>
    private IMapComputerPresenter CreateMapComputer(in MapComputerPresentationRequest request) =>
        new MapComputerRenderer(request.Device, request.Resources, request.Logger, request.ResolveText);

    /// <inheritdoc/>
    private ITacticalComputerPresenter CreateTacticalComputer(in MapComputerPresentationRequest request) =>
        new TacticalComputerRenderer(request.Device, request.Resources, request.Logger, request.ResolveText);

    /// <inheritdoc/>
    private IComputerHighlightCatalogue CreateComputerHighlights(in ComputerHighlightRequest request) =>
        new ComputerHighlightCatalog(request.LevelRoot, request.Variables);

    /// <inheritdoc/>
    private ITerrainPresenter CreateTerrain(in TerrainPresentationRequest request) =>
        new TerrainRenderer(
            request.Device,
            request.Surface,
            request.Logger,
            request.DetailMaps,
            request.DerivedDetail);
}

using OpenIGI.Game.Igi1.World;
using OpenIGI.Game.World;
using OpenIGI.Presentation;
using OpenIGI.Presentation.Rendering;

namespace OpenIGI.Game.Igi1;

/// <summary>Builds the renderers Project I.G.I. draws with.</summary>
/// <remarks>
/// The ground, the placed world, and the first-person and projectile passes. Each arrived when the host
/// stopped naming its renderer, which is what lets the renderer live here; the census on
/// <c>TitleContentTests</c> records what is holding the rest.
/// </remarks>
public sealed class Igi1PresentationProvider : ITitlePresentationProvider
{
    /// <summary>Gets the shared instance.</summary>
    public static Igi1PresentationProvider Instance { get; } = new();

    /// <inheritdoc/>
    public IWorldPresenter CreateWorld(in WorldPresentationRequest request) =>
        new WorldRenderer(request.Device, request.Resources, request.Logger);

    /// <inheritdoc/>
    /// <remarks>
    /// The dome is sized to the far plane, which is this game's rule and not the sequel's; a layer's
    /// texture is a path under <c>LOCAL:</c>, so the file system is the half of the request it reads.
    /// </remarks>
    public ISkyPresenter CreateSky(in SkyPresentationRequest request) =>
        new SkyRenderer(request.Device, request.FileSystem, request.LevelRoot, request.FarPlane, request.Logger);

    /// <inheritdoc/>
    public IViewModelPresenter CreateViewModel(in ViewModelPresentationRequest request) =>
        new ViewModelRenderer(request.Device, request.Resources, request.Logger);

    /// <inheritdoc/>
    public IHeldWeaponPresenter CreateHeldWeapon(in ViewModelPresentationRequest request) =>
        new HeldWeaponRenderer(request.Device, request.Resources, MagicOf(request), request.Logger);

    /// <inheritdoc/>
    public IGrenadePresenter CreateGrenades(in ViewModelPresentationRequest request) =>
        new GrenadeRenderer(request.Device, request.Resources, MagicOf(request), request.Logger);

    /// <inheritdoc/>
    public IMissilePresenter CreateMissiles(in ViewModelPresentationRequest request) =>
        new MissileRenderer(request.Device, request.Resources, MagicOf(request), request.Logger);

    /// <inheritdoc/>
    public IGunPickupPresenter CreateGunPickups(in ViewModelPresentationRequest request) =>
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
    public IBulletMarkPresenter CreateBulletMarks(in EffectsPresentationRequest request) =>
        new BulletMarkRenderer(request.Device, request.Resources, request.Logger);

    /// <inheritdoc/>
    public ICameraStatusLightPresenter CreateCameraStatusLights(in EffectsPresentationRequest request) =>
        new CameraStatusLightRenderer(request.Device, request.Logger);

    /// <inheritdoc/>
    public IHudPresenter CreateHud(in HudPresentationRequest request) =>
        new StatusScreenRenderer(request.Device, request.Logger);

    /// <inheritdoc/>
    public IRemasteredHudPresenter CreateRemasteredHud(in HudPresentationRequest request) =>
        new RemasteredHudRenderer(request.Device, request.Logger);

    /// <inheritdoc/>
    public IMapComputerPresenter CreateMapComputer(in MapComputerPresentationRequest request) =>
        new MapComputerRenderer(request.Device, request.Resources, request.Logger, request.ResolveText);

    /// <inheritdoc/>
    public ITacticalComputerPresenter CreateTacticalComputer(in MapComputerPresentationRequest request) =>
        new TacticalComputerRenderer(request.Device, request.Resources, request.Logger, request.ResolveText);

    /// <inheritdoc/>
    public IComputerHighlightCatalogue CreateComputerHighlights(in ComputerHighlightRequest request) =>
        new ComputerHighlightCatalog(request.LevelRoot, request.Variables);

    /// <inheritdoc/>
    public ITerrainPresenter CreateTerrain(in TerrainPresentationRequest request) =>
        new TerrainRenderer(
            request.Device,
            request.Surface,
            request.Logger,
            request.DetailMaps,
            request.DerivedDetail);
}

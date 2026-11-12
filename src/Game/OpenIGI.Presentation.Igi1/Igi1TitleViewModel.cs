using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;
using OpenIGI.Presentation.Igi1.World;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Igi1;

/// <summary>
/// Project I.G.I.'s arms and the weapon in hand, drawn for its player wherever it stands (<see cref="Igi1TitlePlayer"/>): the game's own
/// view model, posed from the player's rig as the game's own missions draw it.
/// </summary>
/// <remarks>
/// The player says when there is anything to draw and at what width (<see cref="Igi1TitlePlayer.FirstPerson"/>); the arms' pose, the
/// weapon's bones, its magazine and moving part and the shot it fired are the rig's, which this reads itself, as
/// <see cref="Igi1FirstPersonView"/> does for a mission's player. The models are the game's: from its own mission's packs where the
/// player stands in another game's level, else from the mission the host loads.
/// </remarks>
public sealed class Igi1TitleViewModel : ITitleViewModelPresenter
{
    private readonly ViewModelRenderer _arms;
    private readonly HeldWeaponRenderer _held;
    private readonly Igi1FirstPersonView _view;
    private readonly LoadedMission? _own;
    private LoadedMission? _mission;

    /// <summary>Initializes a new instance of the <see cref="Igi1TitleViewModel"/> class.</summary>
    /// <param name="device">The device to draw with.</param>
    /// <param name="resources">Where the game's models are read from.</param>
    /// <param name="magic">The game's magic objects, which place a weapon's parts.</param>
    /// <param name="player">The player it draws; <see langword="null"/> while none stands.</param>
    /// <param name="logger">Where it reports.</param>
    /// <param name="own">
    /// The game's own mission whose packs the models are read from where the player stands in another game's level; <see langword="null"/>
    /// to read them from the mission the host loads.
    /// </param>
    public Igi1TitleViewModel(
        IGraphicsDevice device,
        ResourceRegistry resources,
        MagicObjectRegistry magic,
        Func<Igi1TitlePlayer?> player,
        IOpenIgiLogger logger,
        LoadedMission? own = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(magic);
        ArgumentNullException.ThrowIfNull(player);
        _arms = new ViewModelRenderer(device, resources, logger);
        _held = new HeldWeaponRenderer(device, resources, magic, logger);
        _view = new Igi1FirstPersonView(device, () => player()?.Human.Rig, static () => null, logger);
        _own = own;
    }

    /// <inheritdoc/>
    public ObjectShading Shading { get; set; } = ObjectShading.Default;

    /// <inheritdoc/>
    public bool Load(LoadedMission mission)
    {
        ArgumentNullException.ThrowIfNull(mission);
        _mission = _own ?? mission;
        return _arms.Load(_mission);
    }

    /// <inheritdoc/>
    public void Draw(TitleFirstPersonView view, in Camera3D camera, in Matrix4f projection)
    {
        ArgumentNullException.ThrowIfNull(view);
        _view.Draw(
            camera,
            new FirstPersonFrame(
                ShowViewModel: true,
                view.ObjectFieldOfView,
                Smooth: view.PoseInterpolation < 1f,
                view.PoseInterpolation,
                FarPlane: 0f,
                Terrain: null,
                _arms,
                _held,
                _mission,
                Shading.Sun,
                Projection: projection));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _held.Dispose();
        _arms.Dispose();
    }
}

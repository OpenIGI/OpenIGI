using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Presentation.Igi1.Hud;
using OpenIGI.Presentation.Igi1.World;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Igi1;

/// <summary>
/// Project I.G.I.'s retail HUD over its player wherever it stands (<see cref="Igi1TitlePlayer"/>): the weapon readout or the optics,
/// the player's status and the washes, as the game's own missions draw them (<see cref="Igi1HudSections"/>), and the mission's status
/// messages in the game's look.
/// </summary>
/// <remarks>
/// The binoculars pick out nothing and range nothing here: those are the game's own mission's detectors and solids. A mission of
/// another game times nothing on the game's clock either.
/// </remarks>
public sealed class Igi1TitleHud : ITitleHudPresenter
{
    private readonly StatusScreenRenderer _hud;
    private readonly Func<ITitlePlayer, Igi1TitlePlayer?> _resolve;
    private readonly AmmoTypeRegistry? _ammoTypes;
    private readonly VirtualFileSystem? _own;
    private IReadOnlyList<StatusMessageDisplay> _messages = [];

    /// <summary>Initializes a new instance of the <see cref="Igi1TitleHud"/> class.</summary>
    /// <param name="device">The device to draw with.</param>
    /// <param name="logger">Where it reports.</param>
    /// <param name="ammoTypes">The game's ammunition table, which names the launcher's grenades; <see langword="null"/> for none.</param>
    /// <param name="resolve">
    /// Finds the game's player in the host's title player: the host's player itself by default, or the game's player standing in
    /// another game's.
    /// </param>
    /// <param name="own">
    /// The game's own files the HUD's sprites and fonts are read from where the player stands in another game's level;
    /// <see langword="null"/> to read them from the files the host gives.
    /// </param>
    public Igi1TitleHud(
        IGraphicsDevice device,
        IOpenIgiLogger logger,
        AmmoTypeRegistry? ammoTypes = null,
        Func<ITitlePlayer, Igi1TitlePlayer?>? resolve = null,
        VirtualFileSystem? own = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        _hud = new StatusScreenRenderer(device, logger);
        _ammoTypes = ammoTypes;
        _resolve = resolve ?? (static player => player as Igi1TitlePlayer);
        _own = own;
    }

    /// <inheritdoc/>
    public void Load(VirtualFileSystem files)
    {
        ArgumentNullException.ThrowIfNull(files);
        _hud.Load(_own ?? files);
    }

    /// <inheritdoc/>
    public void ShowMissionMessages(IReadOnlyList<StatusMessageDisplay> messages) => _messages = messages ?? [];

    /// <inheritdoc/>
    public void Draw(SpriteBatch batch, ITitlePlayer player, int screenWidth, int screenHeight, int requestedScale = UiScale.Automatic)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(player);
        if (!_hud.IsReady || _resolve(player) is not { } igi1)
        {
            return;
        }

        int scale = UiScale.Resolve(requestedScale, screenWidth, screenHeight);
        PlayerCamera rig = igi1.Human.Rig;
        if (!igi1.Human.Life.IsDead)
        {
            if (rig.Weapons is { } carried)
            {
                Igi1HudSections.Weapon(batch, _hud, rig, carried, _ammoTypes, screenWidth, screenHeight, scale, (int)igi1.Ticks, [], null);
            }

            _hud.DrawStatusMessages(batch, _messages.Where(static message => !message.IsCutscene), screenWidth, screenHeight, scale);
            Igi1HudSections.Status(batch, _hud, rig, igi1.Human.Life.Health.RemainingFraction, null, screenWidth, screenHeight, scale);
        }

        Igi1HudSections.Washes(batch, igi1.Human.Life, screenWidth, screenHeight, Math.Clamp(HudCVars.DamageFlash.Value, 0f, 1f));
    }

    /// <inheritdoc/>
    public void Dispose() => _hud.Dispose();
}

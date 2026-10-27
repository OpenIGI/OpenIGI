using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Presentation.Igi1.Hud;
using OpenIGI.Presentation.Hud.Remastered;
using OpenIGI.Presentation.Igi1.World;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Igi1;

/// <summary>
/// Project I.G.I.'s HUD over its player wherever it stands (<see cref="Igi1TitlePlayer"/>), as the game's own missions draw it: the
/// retail display -- the weapon readout or the optics, the player's status (<see cref="Igi1HudSections"/>) -- or, with the remastered
/// display chosen, that one in the game's theme (<see cref="Igi1HudTheme"/>); the mission's status messages in the game's look; and
/// the washes over the screen.
/// </summary>
/// <remarks>
/// The binoculars pick out the game's soldiers about the player as its own missions' do, but range nothing: that is the game's own
/// mission's solids. A mission of another game times nothing on the game's clock either.
/// </remarks>
public sealed class Igi1TitleHud : ITitleHudPresenter
{
    private readonly StatusScreenRenderer _hud;
    private readonly Func<ITitlePlayer, Igi1TitlePlayer?> _resolve;
    private readonly AmmoTypeRegistry? _ammoTypes;
    private readonly VirtualFileSystem? _own;
    private readonly Func<Game.Igi1.Ai.SoldierSimulation?> _soldiers;
    private readonly IGraphicsDevice _device;
    private readonly HudModel _model = new();
    private RemasteredHud? _remastered;
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
    /// <param name="soldiers">The game's soldiers about the player, whom the remastered display shows seeing it; <see langword="null"/> for none.</param>
    public Igi1TitleHud(
        IGraphicsDevice device,
        IOpenIgiLogger logger,
        AmmoTypeRegistry? ammoTypes = null,
        Func<ITitlePlayer, Igi1TitlePlayer?>? resolve = null,
        VirtualFileSystem? own = null,
        Func<Game.Igi1.Ai.SoldierSimulation?>? soldiers = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        _device = device;
        _soldiers = soldiers ?? (static () => null);
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
        // The binoculars have no enhanced optics here, which need the game's tactical computer: the retail device draws them.
        bool retail = RemasteredHudPresentation.UsesRetailWeaponHud(
            RemasteredHudPresentation.UsesRemasteredHud(HudCVars.RemasteredHud.Value), rig.IsBinocularsOpen, remasteredOpticsEnabled: false);
        if (!igi1.Human.Life.IsDead && !retail)
        {
            DrawRemastered(batch, igi1, rig, screenWidth, screenHeight, scale);
        }
        else if (!igi1.Human.Life.IsDead)
        {
            if (rig.Weapons is { } carried)
            {
                Igi1HudSections.Weapon(
                    batch, _hud, rig, carried, _ammoTypes, screenWidth, screenHeight, scale, (int)igi1.Ticks,
                    BinocularTargets(igi1, rig, screenWidth, screenHeight), null);
            }

            _hud.DrawStatusMessages(batch, _messages.Where(static message => !message.IsCutscene), screenWidth, screenHeight, scale);
            Igi1HudSections.Status(batch, _hud, rig, igi1.Human.Life.Health.RemainingFraction, null, screenWidth, screenHeight, scale);
        }

        Igi1HudSections.Washes(batch, igi1.Human.Life, screenWidth, screenHeight, Math.Clamp(HudCVars.DamageFlash.Value, 0f, 1f));
    }

    /// <inheritdoc/>
    public void Dispose() => _hud.Dispose();

    // What the binoculars pick out of the soldiers about the player, through its eye, timed on the player's own ticks in the
    // milliseconds the original's detector counts.
    private IReadOnlyList<EnemyOverlayBox> BinocularTargets(Igi1TitlePlayer igi1, PlayerCamera rig, int width, int height)
    {
        if (!rig.IsBinocularsOpen || _soldiers() is not { } soldiers || rig.View is not { } view)
        {
            return [];
        }

        Camera3D eye = new(igi1.EyePosition / WorldScene.UnitsPerMetre, igi1.ViewOrientation);
        int milliseconds = (int)(igi1.Ticks * 1000 / 30);
        return soldiers.BinocularTargets(eye, view.TanX, width, height, milliseconds, float.PositiveInfinity, (int)igi1.Ticks);
    }

    // The remastered display in the game's theme, the scope's picture under it as the game's own missions draw it.
    private void DrawRemastered(SpriteBatch batch, Igi1TitlePlayer igi1, PlayerCamera rig, int width, int height, int scale)
    {
        if (rig.IsSniperScoped)
        {
            _hud.DrawSniperScope(batch, width, height);
        }

        if ((_remastered ??= CreateRemastered()).IsReady)
        {
            Igi1HudTheme.Build(
                rig, igi1.Human.Life.Health.RemainingFraction, _soldiers(), _messages.Where(static message => !message.IsCutscene), null,
                worldPrompt: false, StatusScreenRenderer.IconFor, _model);
            HudProjector? projector = rig.View is { } view
                ? HudProjector.FromYawPitch(rig.Body.Eye, rig.Yaw, rig.Pitch, view.TanX, width, height)
                : null;
            _remastered.Draw(batch, _model, Igi1HudTheme.Theme, _hud, HudFrame.FromSettings(width, height, scale, igi1.Ticks / 30f, crosshairSpread: 8f) with
            {
                Projector = projector,
            });
        }
    }

    private RemasteredHud CreateRemastered()
    {
        RemasteredHud hud = new(_device, NullLogger.Instance);
        hud.Load();
        return hud;
    }
}

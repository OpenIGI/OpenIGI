using OpenIGI.Core.Configuration;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;

namespace OpenIGI.Host;

/// <summary>
/// A title's thermal optics -- I.G.I. 2's goggles and its thermal scope -- seen through the remastered sensor where it is chosen
/// (<see cref="RemasteredHudPresentation.UsesRemasteredTitleThermal"/>), or the original's infrared view where it is not.
/// </summary>
public sealed partial class GameHost
{
    /// <summary>The command the palette key runs.</summary>
    public const string ThermalPaletteCommand = "thermal_palette";

    private readonly ThermalOptics _thermalOptics = new();
    private RemasteredThermal? _thermalHud;
    private bool _thermalHudTried;

    // The palette the key chose this session, or null to follow the setting. Kept off r_thermal_palette, which the player sets
    // on the settings page: a key pressed while looking must not decide what the game starts as tomorrow (see DefaultBinds).
    private ThermalPalette? _thermalPaletteChosen;

    /// <summary>Gets a value indicating whether the title's player sees in infrared now: goggles raised, or a thermal scope up.</summary>
    private bool TitleInfraredUp =>
        _playerMode && TitleOptics is { IsInfraredView: true } && !_computerOpen && _mission.Scenes?.IsPlaying != true;

    /// <summary>Gets a value indicating whether the remastered sensor is raised now.</summary>
    private bool RemasteredThermalUp => TitleInfraredUp && RemasteredHudPresentation.UsesRemasteredTitleThermal;

    /// <summary>Gets the palette the sensor colours heat with: the one the key chose, or the setting's.</summary>
    private ThermalPalette ThermalPaletteShown => _thermalPaletteChosen ?? HudCVars.ThermalPalette.Value;

    /// <summary>Gets the remastered eyepiece, made on first ask; null where its faces could not load.</summary>
    private RemasteredThermal? ThermalHud
    {
        get
        {
            if (!_thermalHudTried)
            {
                _thermalHudTried = true;
                RemasteredThermal hud = new(_device, _logger);
                _thermalHud = hud.Load() ? hud : null;
            }

            return _thermalHud;
        }
    }

    /// <summary>Advances the sensor's envelope one rendered frame and returns what the resolve is to do with it.</summary>
    /// <param name="elapsedSeconds">Real seconds since the last frame.</param>
    /// <returns>The sensor's settings; inactive while it is down and faded out.</returns>
    private ThermalDisplaySettings UpdateThermal(float elapsedSeconds)
    {
        _thermalOptics.Update(RemasteredThermalUp, elapsedSeconds);
        return _thermalOptics.Display(ThermalPaletteShown, HudCVars.ThermalNoise.Value);
    }

    /// <summary>
    /// Hands the title's people to whichever infrared view is up: the remastered sensor, every body each frame it is raised or
    /// fading; or the original's, its bodies in the heat ramp and through walls over the finished scene.
    /// </summary>
    /// <param name="eye">The camera the frame is drawn from.</param>
    /// <param name="projection">The world's projection.</param>
    private void DrawTitleInfrared(in Camera3D eye, in Matrix4f projection)
    {
        if (!_playerMode || _computerOpen
            || _titleFeatures?.WorldModels is not { } owned || TitleWorldModelPresenter is not { } models)
        {
            return;
        }

        if (RemasteredHudPresentation.UsesRemasteredTitleThermal)
        {
            if (_thermalOptics.Amount > 0f)
            {
                models.DrawWorldThermal(owned.WorldModels, eye, projection, _titlePresentationBlend);
            }

            return;
        }

        if (TitleOptics is { IsInfraredView: true })
        {
            models.DrawWorldInfrared(owned.WorldModels, eye, projection, _titlePresentationBlend);
        }
    }

    /// <summary>Draws the remastered thermal goggles' eyepiece over the frame; a thermal scope keeps its own reticle.</summary>
    /// <param name="state">The frame loop's state, whose batch it draws into.</param>
    private void DrawRemasteredThermal(FrameLoopState state)
    {
        if (!RemasteredThermalUp || TitleOptics is not { IsThermalGoggles: true } optics || ThermalHud is not { } hud)
        {
            return;
        }

        RemasteredThermalView shown = new(
            optics.BinocularHeadingRadians,
            optics.BinocularMagnification,
            ThermalPaletteShown,
            _thermalOptics.Calibration,
            _thermalOptics.PaletteNotice,
            _thermalOptics.Seconds);
        _device.Set2DProjection(_window.Width, _window.Height);
        state.Batch.Begin();
        hud.Draw(state.Batch, shown, _window.Width, _window.Height);
        state.Batch.End();
    }

    /// <summary>Registers the command the palette key runs.</summary>
    /// <param name="console">The console to register with.</param>
    private void RegisterThermalCommands(ConsoleSystem console) =>
        console.Commands.Add(
            ThermalPaletteCommand,
            "Steps the remastered thermal sensor to its next palette -- white-hot, black-hot, ironbow, rainbow -- while it is up, for this session. Not in the original.",
            context =>
            {
                if (!RemasteredThermalUp)
                {
                    context.Print("The remastered thermal sensor is not up.");
                    return;
                }

                _thermalPaletteChosen = ThermalPaletteRamp.Next(ThermalPaletteShown);
                _thermalOptics.NotePaletteChanged();
                context.Print($"Thermal palette: {ThermalPaletteRamp.Name(ThermalPaletteShown).ToLowerInvariant()}.");
            });
}

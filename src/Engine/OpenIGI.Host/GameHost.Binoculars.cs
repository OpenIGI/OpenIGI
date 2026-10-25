using OpenIGI.Core.Numerics;
using OpenIGI.Core.Graphics;
using OpenIGI.Game.Player;
using OpenIGI.Presentation;
using OpenIGI.Presentation.Hud.Remastered;
using OpenIGI.Presentation.Rendering;
using OpenIGI.UI.Layout;

namespace OpenIGI.Host;

/// <summary>
/// The remastered binoculars, for whichever player looks through them: Project I.G.I.'s through its viewpoint, or a title's
/// player through <see cref="ITitlePlayerOptics"/>. One lens, one interface (<see cref="RemasteredBinoculars"/>) and one
/// highlight (<see cref="BinocularMarking"/>), whichever title's player it is.
/// </summary>
public sealed partial class GameHost
{
    // A contact's box as wide as this share of its height.
    private const float ContactBoxAspect = 0.45f;

    private readonly List<EnemyOverlayBox> _binocularContacts = [];
    private readonly Dictionary<object, BinocularMarkerColor> _titleMarked = [];
    private RemasteredBinoculars? _binocularHud;
    private bool _binocularHudTried;

    /// <summary>Gets the title's player's binoculars, where it carries its own.</summary>
    private ITitlePlayerOptics? TitleOptics => TitlePlayer as ITitlePlayerOptics;

    /// <summary>Gets a value indicating whether the title's player is looking through its binoculars.</summary>
    private bool TitleBinocularsOpen => _playerMode && TitleOptics?.IsBinocularsOpen == true;

    /// <summary>Gets a value indicating whether any player is looking through binoculars.</summary>
    private bool BinocularsOpen => Viewpoint?.IsBinocularsOpen == true || TitleBinocularsOpen;

    /// <summary>Gets the remastered binoculars' interface, made on first ask; null where its faces could not load.</summary>
    private RemasteredBinoculars? BinocularHud
    {
        get
        {
            if (!_binocularHudTried)
            {
                _binocularHudTried = true;
                RemasteredBinoculars hud = new(_device, _logger);
                _binocularHud = hud.Load() ? hud : null;
            }

            return _binocularHud;
        }
    }

    /// <summary>Whether the binoculars up now are presented in the remastered style.</summary>
    private bool RemasteredOpticsUp() => TitleBinocularsOpen
        ? RemasteredHudPresentation.UsesRemasteredTitleOptics && BinocularHud is { IsReady: true }
        : RemasteredHudPresentation.UsesRemasteredOptics(TacticalComputer is not null, BinocularHud is { IsReady: true });

    /// <summary>Highlights whom the title's player's binoculars picked out, as the level's own marked soldiers are.</summary>
    /// <param name="eye">The camera the frame is drawn from.</param>
    private void DrawTitleBinocularMarks(in Camera3D eye)
    {
        if (!TitleBinocularsOpen || TitleOptics is not { } optics
            || _titleFeatures?.WorldModels is not { } owned || TitleWorldModelPresenter is not { } models)
        {
            return;
        }

        _titleMarked.Clear();
        foreach (TitleBinocularContact contact in optics.BinocularContacts)
        {
            _titleMarked[contact.Body] = contact.Marker;
        }

        if (_titleMarked.Count > 0)
        {
            models.DrawWorldOutlines(owned.WorldModels, _titleMarked, eye, radiusPixels: 8f, _titlePresentationBlend);
        }
    }

    /// <summary>Draws the remastered binoculars' interface over the frame, for whichever player is looking.</summary>
    /// <param name="state">The frame loop's state, whose batch it draws into.</param>
    /// <param name="view">The frame's view.</param>
    /// <param name="rangeMetres">The range Project I.G.I.'s view measured to what its centre rests on.</param>
    private void DrawRemasteredBinoculars(FrameLoopState state, in FrameView view, float? rangeMetres)
    {
        if (!view.RemasteredOptics || !BinocularsOpen || BinocularHud is not { } hud || _computerOpen
            || _mission.Cutscenes?.IsPlaying == true || (!TitleBinocularsOpen && !view.ShowFirstPersonHud))
        {
            return;
        }

        _binocularContacts.Clear();
        _binocularContacts.AddRange(view.BinocularTargets);
        RemasteredBinocularView shown;
        if (TitleBinocularsOpen && TitlePlayer is { } player && TitleOptics is { } optics)
        {
            AddTitleContacts(player, optics);
            shown = new RemasteredBinocularView(
                optics.BinocularHeadingRadians, optics.BinocularMagnification, _binocularContacts, _binocularOptics.Seconds, optics.BinocularRangeMetres);
        }
        else
        {
            float tanX = view.Sight.TanX;
            shown = new RemasteredBinocularView(
                HostedPlayer?.Body.Yaw ?? 0f,
                tanX > 1e-4f ? HumanView.DefaultTanX / tanX : 1f,
                _binocularContacts,
                _binocularOptics.Seconds,
                rangeMetres);
        }

        _device.Set2DProjection(_window.Width, _window.Height);
        state.Batch.Begin();
        hud.Draw(state.Batch, shown, _window.Width, _window.Height);
        state.Batch.End();
    }

    // The title's contacts as boxes on the screen: the person's height either side of their middle, through the title's view.
    private void AddTitleContacts(ITitlePlayer player, ITitlePlayerOptics optics)
    {
        Matrix3f rows = player.ViewOrientation;
        HudProjector projector = new(player.EyePosition, rows.Row1, rows.Row2, rows.Row3, player.FieldOfViewScale, _window.Width, _window.Height);
        foreach (TitleBinocularContact contact in optics.BinocularContacts)
        {
            Vector3d top = new(contact.Centre.X, contact.Centre.Y, contact.Centre.Z + contact.HalfHeight);
            Vector3d bottom = new(contact.Centre.X, contact.Centre.Y, contact.Centre.Z - contact.HalfHeight);
            if (!projector.TryProject(top, out UiVec2 high) || !projector.TryProject(bottom, out UiVec2 low))
            {
                continue;
            }

            float halfWidth = MathF.Abs(low.Y - high.Y) * ContactBoxAspect * 0.5f;
            Vector3d eye = player.EyePosition;
            double dx = contact.Centre.X - eye.X, dy = contact.Centre.Y - eye.Y, dz = contact.Centre.Z - eye.Z;
            float metres = (float)(Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz)) / TitleUnitsPerMetre);
            _binocularContacts.Add(new EnemyOverlayBox(
                contact.Id,
                high.X - halfWidth,
                MathF.Min(high.Y, low.Y),
                high.X + halfWidth,
                MathF.Max(high.Y, low.Y),
                metres,
                contact.Alpha,
                PulseScale: contact.PulseScale,
                Marker: contact.Marker));
        }
    }
}

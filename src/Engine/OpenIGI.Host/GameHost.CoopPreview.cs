using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Game.World;
using OpenIGI.Host.Menu;
using OpenIGI.Presentation;
using OpenIGI.Rendering;

namespace OpenIGI.Host;

/// <summary>The look a co-op player picks, shown standing in the level behind the menu, and looks borrowed from other levels.</summary>
public sealed partial class GameHost
{
    // How far ahead of the menu's eye the look stands, how far to the eye's right, and how high above its feet the middle of
    // the body is: the point it is centred on in the picture when there is no floor near enough to stand it on. Metres.
    private const double PreviewDistance = 3.4;
    private const double PreviewAside = 0.0;
    private const double PreviewMiddle = 0.9;

    // How far above and below that point a floor is looked for to stand the look on, in metres.
    private const double PreviewFloorAbove = 1.0;
    private const double PreviewFloorBelow = 2.5;

    // How quickly the look's feet follow the floor under them as the camera moves on, a rate a second; and how far they
    // may have to go before they are put there at once, as the camera cuts to another place. Metres.
    private const float PreviewFloorFollow = 8f;
    private const double PreviewFloorCut = 3.0;

    // A tick of the look's idle, as the game's own ticks run.
    private const float PreviewTickSeconds = 1f / 30f;

    // Levels opened for a model the level being drawn doesn't ship, by the mission they were opened beside.
    private readonly Dictionary<(LoadedMission Into, int Level), LoadedMission?> _borrowed = [];

    private string? _previewLook;
    private ISoldierBodies? _previewBodies;
    private MissionPreview? _previewFor;
    private double? _previewFeetZ;
    private float _previewFrameElapsed;
    private float _previewElapsed;
    private float _previewTurn;
    private int _previewClock;

    /// <summary>Shows a look standing in the level behind the menu, or none.</summary>
    /// <param name="skinId">The look, a <see cref="PlayerSkins"/> id, or <see langword="null"/> to stop.</param>
    internal void PreviewLook(string? skinId) => _previewLook = skinId;

    /// <summary>Turns the look being shown about its feet.</summary>
    /// <param name="radians">How far, positive to its left.</param>
    internal void TurnLook(float radians) => _previewTurn = (_previewTurn + radians) % MathF.Tau;

    // Once a frame: the look stood in the backdrop while the co-op page asks for it, and put away otherwise.
    private void UpdateLookPreview()
    {
        int now = _loop.TimeSource.Milliseconds;
        float elapsed = Math.Clamp((now - _previewClock) / 1000f, 0f, 0.1f);
        _previewClock = now;

        MissionPreview? backdrop = _menuFlow.OverShowcase && _session.Mission is null ? _menuBackdrop.Preview : null;
        if (_previewLook is null || backdrop is null)
        {
            EndLookPreview();
            return;
        }

        if (!ReferenceEquals(_previewFor, backdrop))
        {
            EndLookPreview();
            _previewFor = backdrop;

            // A renderer of the backdrop level's humans of its own: a lite backdrop builds none, and a full one's are kept
            // for Play to take over, never drawn.
            _previewBodies = _presentation.Presenters.Create<SoldierPresentationRequest, ISoldierBodies>(
                new SoldierPresentationRequest(_device, backdrop.Resources, _logger, _mission.MagicObjects, _detailMaps));
            _previewBodies?.Load(
                backdrop.Mission,
                backdrop.LevelRoot,
                backdrop.Mission.Terrain is { IsUsable: true } standing ? standing : null,
                backdrop.Solids.Caster());
            if (_previewBodies is not null)
            {
                _previewBodies.BorrowLevel = level => Borrow(backdrop.Mission, backdrop.Resources, level);
            }
        }

        if (_previewBodies is not { } bodies)
        {
            return;
        }

        _previewFrameElapsed = elapsed;
        for (_previewElapsed += elapsed; _previewElapsed >= PreviewTickSeconds; _previewElapsed -= PreviewTickSeconds)
        {
            bodies.TickPreviewBody();
        }
    }

    // Once the frame's eye is settled: the look stood in front of it. The backdrop keeps the menu's own camera -- its
    // orbits, rails and cuts, as behind every other page -- and the look goes where that camera looks.
    private void PlaceLook(in Camera3D eye)
    {
        if (_previewLook is null || _previewFor is not { } backdrop || _previewBodies is not { } bodies)
        {
            return;
        }

        (Vector3d feet, float facing) = StandLook(backdrop, eye);
        bodies.ShowPreviewBody(backdrop.Mission, PlayerSkins.Of(_previewLook).Model, feet, facing + _previewTurn);

        // Drawn between its last two ticks, as the game's own bodies are.
        bodies.BeginFrame(_previewElapsed / PreviewTickSeconds, smooth: true);
    }

    private void EndLookPreview()
    {
        _previewBodies?.Dispose();
        _previewBodies = null;
        _previewFor = null;
        _previewFeetZ = null;
        _previewElapsed = 0f;
        _previewTurn = 0f;
    }

    // Where the look stands: a few metres along the eye's line of sight, facing the eye. On the floor when there is one near
    // that point, which the feet follow as the camera moves on; centred on the point when there is none -- the camera high
    // over a yard, or out over the last mission's shafts -- so the look is always in the picture.
    private (Vector3d Feet, float Facing) StandLook(MissionPreview backdrop, in Camera3D eye)
    {
        double metre = WorldScene.UnitsPerMetre;
        Vector3f look = eye.Orientation.Row3;
        Vector3d from = eye.Position * metre;
        Vector3d middle = from + (new Vector3d(look.X, look.Y, look.Z) * (PreviewDistance * metre));
        double flat = Math.Sqrt((look.X * look.X) + (look.Y * look.Y));
        (double ax, double ay) = flat > 1e-4 ? (look.X / flat, look.Y / flat) : (0.0, 1.0);

        // The camera's right, as Camera3D.LookRotation builds it for a look along (ax, ay).
        double x = middle.X + (ay * PreviewAside * metre);
        double y = middle.Y - (ax * PreviewAside * metre);
        double top = middle.Z + (PreviewFloorAbove * metre);
        double bottom = middle.Z - (PreviewFloorBelow * metre);
        double? floor = null;
        if (backdrop.Ground?.TryGetGround(x, y, top, aboveUnits: 0.0, out TerrainGround terrain) == true
            && terrain.Height <= top && terrain.Height >= bottom)
        {
            floor = terrain.Height;
        }

        if (backdrop.Solids.Caster()(new Vector3d(x, y, top), new Vector3d(x, y, bottom), out WorldRayHit hit)
            && (floor is null || hit.Position.Z > floor))
        {
            floor = hit.Position.Z;
        }

        double wanted = floor ?? (middle.Z - (PreviewMiddle * metre));
        double feetZ = _previewFeetZ is double was && Math.Abs(wanted - was) < PreviewFloorCut * metre
            ? was + ((wanted - was) * (1.0 - Math.Exp(-PreviewFloorFollow * _previewFrameElapsed)))
            : wanted;
        _previewFeetZ = feetZ;

        // The body's heading is a gamma, the negated yaw of the way it looks: here, back at the camera.
        float facing = -(float)Math.Atan2(from.X - x, from.Y - y);
        return (new Vector3d(x, y, feetZ), facing);
    }

    // A view of a mission with another level's own pack as its level pack, for a model only that level ships: opened once
    // beside each mission, and left closed when the level won't open. A browser holds only the levels it has fetched, so
    // until the load gate has that level's files there is nothing yet -- and asking it is what fetches them; the look is
    // asked for again each frame, and stands once they are there.
    private LoadedMission? Borrow(LoadedMission into, ResourceRegistry resources, int level)
    {
        if (_borrowed.TryGetValue((into, level), out LoadedMission? held))
        {
            return held;
        }

        LoadedMission? view = null;
        if (_missions.TryGet(level, out MissionDescriptor? descriptor) && descriptor is not null)
        {
            if (!_loadGate.IsReady(descriptor, out _))
            {
                return null;
            }

            PackReference home = descriptor.PacksToOpen[^1];
            try
            {
                view = into with { LevelPack = LoadedPack.Open(home.Directory, home.Name, resources, _logger), Packs = null };
                _logger.Information("Coop", $"Opened level {level}'s pack for a co-op look this level doesn't ship.");
            }
            catch (Exception ex) when (ex is EngineFatalException or IOException or InvalidDataException)
            {
                _logger.Warning("Coop", $"Level {level}'s pack would not open for a co-op look: {ex.Message}");
            }
        }

        _borrowed[(into, level)] = view;
        return view;
    }
}

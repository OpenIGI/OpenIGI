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
    // How far ahead of the menu's eye the look stands, how far back the camera then stands from it, how high, and how far
    // the camera aims to its side so it stands in the right of the picture, clear of the co-op page's panels. Metres.
    private const double PreviewAhead = 4.0;
    private const double PreviewBack = 3.4;
    private const double PreviewEyeHeight = 1.5;
    private const double PreviewAim = 0.75;
    private const double PreviewAside = 1.15;

    // A tick of the look's idle, as the game's own ticks run.
    private const float PreviewTickSeconds = 1f / 30f;

    // Levels opened for a model the level being drawn doesn't ship, by the mission they were opened beside.
    private readonly Dictionary<(LoadedMission Into, int Level), LoadedMission?> _borrowed = [];

    private string? _previewLook;
    private ISoldierBodies? _previewBodies;
    private MissionPreview? _previewFor;
    private (Camera3D Eye, Vector3d Feet, float Facing)? _previewShot;
    private float _previewElapsed;
    private int _previewClock;

    /// <summary>Shows a look standing in the level behind the menu, or none.</summary>
    /// <param name="skinId">The look, a <see cref="PlayerSkins"/> id, or <see langword="null"/> to stop.</param>
    internal void PreviewLook(string? skinId) => _previewLook = skinId;

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
            _previewShot = FrameLook(backdrop, _earCamera ?? Camera3D.Default);

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

        if (_previewShot is not { } shot || _previewBodies is not { } bodies)
        {
            return;
        }

        bodies.ShowPreviewBody(backdrop.Mission, PlayerSkins.Of(_previewLook).Model, shot.Feet, shot.Facing);
        for (_previewElapsed += elapsed; _previewElapsed >= PreviewTickSeconds; _previewElapsed -= PreviewTickSeconds)
        {
            bodies.TickPreviewBody();
        }
    }

    private void EndLookPreview()
    {
        _previewBodies?.Dispose();
        _previewBodies = null;
        _previewFor = null;
        _previewShot = null;
        _previewElapsed = 0f;
    }

    // Where the look stands and how it is seen: on the ground a few metres ahead of the menu's eye, facing a camera stood
    // back from it at head height, which aims a little to the look's left so it stands in the right of the picture.
    private static (Camera3D Eye, Vector3d Feet, float Facing) FrameLook(MissionPreview backdrop, Camera3D menuEye)
    {
        double metre = WorldScene.UnitsPerMetre;
        Vector3f look = menuEye.Orientation.Row3;
        double flat = Math.Sqrt((look.X * look.X) + (look.Y * look.Y));
        (double ax, double ay) = flat > 1e-4 ? (look.X / flat, look.Y / flat) : (0.0, 1.0);

        Vector3d eye = menuEye.Position * metre;
        double x = eye.X + (ax * PreviewAhead * metre);
        double y = eye.Y + (ay * PreviewAhead * metre);
        double ground = eye.Z - (1.7 * metre);
        if (backdrop.Ground?.TryGetGround(x, y, eye.Z, aboveUnits: 0.0, out TerrainGround terrain) == true)
        {
            ground = terrain.Height;
        }

        if (backdrop.Solids.Caster()(new Vector3d(x, y, eye.Z), new Vector3d(x, y, eye.Z - (60 * metre)), out WorldRayHit floor)
            && floor.Position.Z > ground)
        {
            ground = floor.Position.Z;
        }

        Vector3d feet = new(x, y, ground);

        // Back towards where the menu looked from, on the level, at head height.
        Vector3d camera = new(x - (ax * PreviewBack * metre), y - (ay * PreviewBack * metre), ground + (PreviewEyeHeight * metre));

        // The camera's right, as Camera3D.LookRotation builds it for a look along (ax, ay).
        (double rx, double ry) = (ay, -ax);
        Vector3d aim = new(x - (rx * PreviewAside * metre), y - (ry * PreviewAside * metre), ground + (PreviewAim * metre));
        Vector3d along = aim - camera;
        float yaw = (float)Math.Atan2(along.X, along.Y);
        float pitch = (float)Math.Atan2(along.Z, Math.Sqrt((along.X * along.X) + (along.Y * along.Y)));

        // The body's heading is a gamma, the negated yaw of the way it looks: here, back at the camera.
        float facing = -(float)Math.Atan2(camera.X - x, camera.Y - y);
        return (new Camera3D(camera / metre, Camera3D.LookRotation(yaw, pitch)), feet, facing);
    }

    // A view of a mission with another level's own pack as its level pack, for a model only that level ships: opened once
    // beside each mission, and left closed when the level won't open.
    private LoadedMission? Borrow(LoadedMission into, ResourceRegistry resources, int level)
    {
        if (_borrowed.TryGetValue((into, level), out LoadedMission? held))
        {
            return held;
        }

        LoadedMission? view = null;
        if (_missions.TryGet(level, out MissionDescriptor? descriptor) && descriptor is not null)
        {
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

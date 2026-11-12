using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi1.World;
using OpenIGI.Engine.Vfs;
using OpenIGI.Formats.World;
using OpenIGI.Game.Ai;
using OpenIGI.Game.World;
using OpenIGI.Presentation;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Igi1;

/// <summary>What Project I.G.I.'s player has put in the world: the weapons lying about, its grenades, rockets and launcher rounds, the vehicles' rockets, its marks, and the lamps of the cameras.</summary>
/// <param name="gameplay">The first game's mission.</param>
internal sealed class Igi1WorldView(Igi1Gameplay gameplay) : IMissionWorldView, IDisposable
{
    // Made for the level being played, from the title's own presenters (which a mod may have replaced).
    private IGunPickupPresenter? _pickups;
    private IGrenadePresenter? _grenades;
    private IMissilePresenter? _missiles;
    private IBulletMarkPresenter? _marks;
    private ICameraStatusLightPresenter? _lamps;

    /// <inheritdoc/>
    public bool TryHeldWeapon(out int shotSequence)
    {
        if (gameplay.World.Player is { } player && player.Weapons?.HideFirstPersonWeapon != true)
        {
            shotSequence = player.ShotSequence;
            return true;
        }

        shotSequence = 0;
        return false;
    }

    /// <inheritdoc/>
    public void LoadLooseObjects(in WorldObjectsRequest request, IReadOnlyList<GunPickup>? lying)
    {
        if (lying is not null)
        {
            _pickups = request.Presenters.Create<ViewModelPresentationRequest, IGunPickupPresenter>(request.Models);
            if (_pickups is not null)
            {
                _pickups.Materials = request.Materials;
                _pickups.Load(request.Mission, lying);
            }
        }

        // The grenades in the air, drawn with their own retail models: the same model machinery as the pickups, at a
        // transform the simulation owns.
        _grenades?.Dispose();
        _grenades = request.Presenters.Create<ViewModelPresentationRequest, IGrenadePresenter>(request.Models);
        _grenades?.Load(request.Mission);
        _missiles?.Dispose();
        _missiles = request.Presenters.Create<ViewModelPresentationRequest, IMissilePresenter>(request.Models);
        _missiles?.Load(request.Mission);
    }

    /// <inheritdoc/>
    public void LoadMarks(in WorldObjectsRequest request)
    {
        _marks?.Dispose();
        _marks = request.Presenters.Create<EffectsPresentationRequest, IBulletMarkPresenter>(request.Effects);
    }

    /// <inheritdoc/>
    public void LoadLamps(in WorldObjectsRequest request, VirtualFileSystem fileSystem)
    {
        _lamps?.Dispose();
        _lamps = request.Presenters.Create<EffectsPresentationRequest, ICameraStatusLightPresenter>(request.Effects);
        if (_lamps is not null && !_lamps.Load(fileSystem))
        {
            _lamps.Dispose();
            _lamps = null;
        }
    }

    /// <inheritdoc/>
    public void ReleaseLyingWeaponsAndMarks()
    {
        _pickups?.Dispose();
        _pickups = null;
        _marks?.Dispose();
        _marks = null;
    }

    /// <inheritdoc/>
    public void ReleaseLevel()
    {
        ReleaseLyingWeaponsAndMarks();
        _lamps?.Dispose();
        _lamps = null;
        _grenades?.Dispose();
        _grenades = null;
        _missiles?.Dispose();
        _missiles = null;
    }

    /// <inheritdoc/>
    public void Dispose() => ReleaseLevel();

    /// <inheritdoc/>
    public void Light(FixedFunctionSun? sun)
    {
        if (_pickups is { } pickups)
        {
            pickups.Sun = sun;
        }

        if (_grenades is { } grenades)
        {
            grenades.Sun = sun;
        }

        if (_missiles is { } missiles)
        {
            missiles.Sun = sun;
        }
    }

    /// <inheritdoc/>
    public void BeginFrame(float tickFraction, bool smooth) => _pickups?.BeginFrame(tickFraction, smooth);

    /// <inheritdoc/>
    public void WeaponDropped(LoadedMission mission, GunPickup pickup) => _pickups?.Add(mission, pickup);

    /// <inheritdoc/>
    public void WeaponRemoved(GunPickup pickup) => _pickups?.Remove(pickup);

    /// <inheritdoc/>
    public void DrawLyingWeapons(in Camera3D eye, in Matrix4f projection) => _pickups?.Draw(eye, projection);

    /// <inheritdoc/>
    public void DrawProjectiles(in Camera3D eye, in Matrix4f projection, float tickBlend)
    {
        IGrenadePresenter? grenades = _grenades;
        IMissilePresenter? missiles = _missiles;
        MissionWorld mission = gameplay.World;
        if (mission.Player is not { } player)
        {
            return;
        }

        grenades?.Draw(player.Grenades, eye, projection, tickBlend);
        if (missiles is not null)
        {
            missiles.Draw(player.Missiles, eye, projection, tickBlend);
            missiles.Draw(mission.VehicleMissiles, eye, projection, tickBlend);
            missiles.Draw(player.M203, eye, projection, tickBlend);
        }

        // A co-op server's partners throw, lay and fire in its own simulation; a client draws the others' as the server's
        // snapshots have them.
        foreach (Game.Igi1.Multiplayer.Igi1NetworkPlayer partner in mission.NetworkPlayers)
        {
            grenades?.Draw(partner.Player.Grenades, eye, projection, tickBlend);
            missiles?.Draw(partner.Player.Missiles, eye, projection, tickBlend);
            missiles?.Draw(partner.Player.M203, eye, projection, tickBlend);
        }

        if (mission.ShownProjectiles.Count > 0)
        {
            missiles?.Draw(mission.ShownProjectiles, eye, projection, tickBlend);
        }
    }

    /// <inheritdoc/>
    public void DrawMarks(in Camera3D eye, in Matrix4f projection, PlacementPose? poses)
    {
        if (_marks is { } marks && gameplay.World.Player is { } player)
        {
            marks.Draw(player.Marks, eye, projection, poses);
        }
    }

    /// <inheritdoc/>
    public void DrawCameraLamps(IReadOnlyList<SecurityCamera> cameras, in Camera3D eye, in Matrix4f projection, float tickFraction, bool smooth) =>
        _lamps?.Draw(cameras, eye, projection, tickFraction, smooth);

    /// <inheritdoc/>
    public void DrawGlows(IReadOnlyList<WorldGlow> glows, in Camera3D eye, in Matrix4f projection) =>
        _lamps?.DrawGlows(glows, eye, projection);
}

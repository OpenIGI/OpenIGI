using OpenIGI.Core.Graphics;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Game.Weapons;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Igi1;

/// <summary>
/// The ghost a race shows: a body built as a co-op partner's is, from the level's own player's look, posed each tick from the
/// runner's track and handed to the picture's ghost pass, which lays it over the lit scene as a runner under a cloak. It is never
/// handed to the simulation, as a partner's body is: no round meets it, no guard sees it, it casts no shadow. Its gun is in its
/// hand, through the same pass, and its flame is drawn as a partner's is on the tick its runner fired.
/// </summary>
public sealed partial class SoldierRenderer
{
    // What the ghost's body follows, and the body; null until a race first shows it.
    private static readonly object GhostKey = new();
    private PlayerBodyRenderer? _ghost;
    private bool _ghostShown;
    private bool _ghostFallen;
    private readonly List<MeshDrawRange> _ghostRanges = [];

    // The weapon types its gun is named by, its gun, its shots as last seen, and how many it has fired and whether it did now.
    private WeaponTypeRegistry? _ghostWeapons;
    private SoldierHeldWeapon? _ghostGun;
    private byte? _ghostShots;
    private int _ghostShotSequence;
    private bool _ghostFired;

    /// <inheritdoc/>
    public void UseGhostWeapons(WeaponTypeRegistry? weapons) => _ghostWeapons = weapons;

    /// <inheritdoc/>
    public void FollowGhost(ReplicatedHuman? frame)
    {
        if (frame is not { } human || _partnerLoad is not { } load)
        {
            _ghostShown = false;
            return;
        }

        // Fired on the tick its shot count moved, as a client's partner is.
        _ghostFired = _ghostShots is { } before && before != human.Shots;
        _ghostShots = human.Shots;
        _ghostShotSequence += _ghostFired ? 1 : 0;
        WeaponType? weapon = Game.Igi1.Multiplayer.Igi1ReplicatedWorld.HeldWeapon(human.Weapon, _ghostWeapons);
        _ghostGun = weapon?.GunModel is { Length: > 0 } model ? _armoury.HeldWeaponFor(load.Mission, model) : null;
        PartnerBody ghost = PartnerBody.FromReplicated(GhostKey, human, weapon, _ghostFired);
        if (_ghost is null)
        {
            PlayerBodyRenderer body = new(this, _device, _sim, _logger, new SoldierPlayerBody());
            if (!body.Load(load.Mission, load.Model, load.Animations, ghost.Feet, ghost.Facing))
            {
                _ghostShown = false;
                return;
            }

            _ghost = body;
        }

        _ghostShown = true;
        if (ghost.Dead)
        {
            // Fallen once, it lies there.
            if (!_ghostFallen)
            {
                _ghostFallen = true;
                _ghost.Kill(Game.Igi1.Player.HumanPlayerDeath.Choose(0, Game.Player.HumanHitZoneTable.Player, 0), ghost.Feet, ghost.Facing);
            }

            _ghost.Tick();
            return;
        }

        if (_ghostFallen)
        {
            _ghostFallen = false;
            _ghost.Revive();
        }

        if (ghost.ClipTime is { } time)
        {
            _ghost.PoseClip(ghost.Animation, time, ghost.Feet, ghost.Facing);
            return;
        }

        _ghost.StopClimbing();
        PlayerBodyPose pose = ghost.Pose;
        _ghost.Follow(ghost.Feet, ghost.Facing, ghost.Animation, ghost.Loops, in pose);
        _ghost.Tick();
    }

    /// <inheritdoc/>
    public void DrawGhost(in Camera3D camera, in GhostLook look)
    {
        if (_ghostShown && !_partnersHidden && _ghost is { } ghost)
        {
            ghost.DrawGhost(camera, look, _ghostRanges);
            if (_ghostGun is { } gun && !_ghostFallen && ghost.HandAttachment() is { } hand)
            {
                _drawer.DrawHeldGhost(gun, hand, camera, look, _ghostRanges);
            }
        }
    }

    // The ghost's flame on the tick its runner fired, in the forward pass with the soldiers' and the partners'.
    private void DrawGhostMuzzleFlash(in Camera3D camera, in Matrix4f projection)
    {
        if (_ghostShown && _ghostFired && !_ghostFallen && !_partnersHidden && _ghostGun?.Presentation is { } presentation
            && _ghost?.HandAttachment() is { } hand)
        {
            presentation.Draw(hand, camera, projection, firedThisTick: true, _ghostShotSequence, WeaponDrawParts.FlameOnly);
        }
    }

    // The ghost let go of with the level.
    private void ReleaseGhost()
    {
        _ghost?.Release();
        _ghost = null;
        _ghostShown = false;
        _ghostFallen = false;
        _ghostGun = null;
        _ghostShots = null;
        _ghostFired = false;
    }
}

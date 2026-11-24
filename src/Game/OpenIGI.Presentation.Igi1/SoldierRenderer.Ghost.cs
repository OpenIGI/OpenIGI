using OpenIGI.Core.Graphics;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Igi1;

/// <summary>
/// The ghost a race shows: a body built as a co-op partner's is, from the level's own player's look, posed each tick from the
/// runner's track and drawn as a silhouette over the finished scene. It is never handed to the simulation, as a partner's body
/// is: no round meets it, no guard sees it.
/// </summary>
public sealed partial class SoldierRenderer
{
    // What the ghost's body follows, and the body; null until a race first shows it.
    private static readonly object GhostKey = new();
    private PlayerBodyRenderer? _ghost;
    private bool _ghostShown;
    private bool _ghostFallen;
    private readonly List<MeshDrawRange> _ghostRanges = [];

    /// <inheritdoc/>
    public void FollowGhost(ReplicatedHuman? frame)
    {
        if (frame is not { } human || _partnerLoad is not { } load)
        {
            _ghostShown = false;
            return;
        }

        PartnerBody ghost = PartnerBody.FromReplicated(GhostKey, human, weapon: null, fired: false);
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
    public void DrawGhost(in Camera3D camera)
    {
        if (_ghostShown && !_partnersHidden && _ghost is { } ghost)
        {
            ghost.DrawSilhouette(camera, SilhouetteGroup.Disabled, _ghostRanges);
        }
    }

    // The ghost let go of with the level.
    private void ReleaseGhost()
    {
        _ghost?.Release();
        _ghost = null;
        _ghostShown = false;
        _ghostFallen = false;
    }
}

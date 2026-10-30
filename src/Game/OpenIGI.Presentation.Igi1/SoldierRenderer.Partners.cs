using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.World;

namespace OpenIGI.Presentation.Igi1;

/// <summary>A co-op session's partners, each drawn as the player's own body is drawn.</summary>
public sealed partial class SoldierRenderer
{
    private readonly Dictionary<object, PlayerBodyRenderer> _partners = [];
    private readonly Dictionary<object, string> _partnerNames = [];

    // The fallen partners: what lies where, and how far each one's revival has gone.
    private readonly Dictionary<object, (Vector3d Feet, float Progress)> _fallen = [];

    // A body lying a little above its feet: where a revive mark stands over it, in metres.
    private const double ReviveMarkLift = 0.7;

    /// <inheritdoc/>
    public IReadOnlyList<WorldNameTag> NameTags()
    {
        List<WorldNameTag> tags = [];
        foreach ((object key, PlayerBodyRenderer body) in _partners)
        {
            if (!_fallen.ContainsKey(key) && _partnerNames.TryGetValue(key, out string? name) && body.HeadAnchorMetres() is { } head)
            {
                tags.Add(new WorldNameTag(head, name));
            }
        }

        return tags;
    }
    /// <inheritdoc/>
    public IReadOnlyList<ReviveMarker> ReviveMarkers()
    {
        List<ReviveMarker> marks = [];
        foreach ((object key, (Vector3d feet, float progress)) in _fallen)
        {
            Vector3d metres = feet / OpenIGI.Game.Ai.AiViewCone.UnitsPerMetre;
            marks.Add(new ReviveMarker(
                new Vector3d(metres.X, metres.Y, metres.Z + ReviveMarkLift), _partnerNames.GetValueOrDefault(key) ?? "Teammate", progress));
        }

        return marks;
    }

    private readonly Dictionary<object, (SoldierHeldWeapon? Gun, int ShotSequence, bool Fired)> _partnerGuns = [];
    private (LoadedMission Mission, string Model, BoneAnimationSet Animations)? _partnerLoad;

    /// <inheritdoc/>
    public void FollowPartnerBodies(IReadOnlyList<PartnerBody> partners)
    {
        ArgumentNullException.ThrowIfNull(partners);
        if (_partnerLoad is not { } load)
        {
            return;
        }

        HashSet<object> present = [];
        foreach (PartnerBody partner in partners)
        {
            present.Add(partner.Key);
            if (!_partners.TryGetValue(partner.Key, out PlayerBodyRenderer? body))
            {
                body = new PlayerBodyRenderer(this, _device, _sim, _logger, new SoldierPlayerBody());
                if (!body.Load(load.Mission, load.Model, load.Animations, partner.Feet, partner.Facing))
                {
                    continue;
                }

                _partners[partner.Key] = body;
            }

            if (partner.Name is { Length: > 0 } fallenName)
            {
                _partnerNames[partner.Key] = fallenName;
            }

            // A fallen partner falls once and lies there; stood up again, it walks on from where it lay.
            if (partner.Dead)
            {
                if (!_fallen.ContainsKey(partner.Key))
                {
                    body.Kill(OpenIGI.Game.Igi1.Player.HumanPlayerDeath.Choose(0, OpenIGI.Game.Player.HumanHitZoneTable.Player, 0), partner.Feet, partner.Facing);
                }

                _fallen[partner.Key] = (partner.Feet, partner.Revive ?? 0f);
                body.Tick();
                continue;
            }

            if (_fallen.Remove(partner.Key))
            {
                body.Revive();
            }

            // A climber is posed where its own ladder body is, on the climbing path the player's is drawn on: the clip
            // held at its time, its root height the ladder's. Anyone else walks as the player's body follows the player.
            if (partner.LadderTime is { } time)
            {
                body.PoseClimbing(partner.Animation, time, partner.Feet, partner.Facing);
            }
            else
            {
                body.StopClimbing();
                PlayerBodyPose pose = partner.Pose;
                body.Follow(partner.Feet, partner.Facing, partner.Animation, partner.Loops, in pose);
                body.Tick();
            }

            if (partner.Name is { Length: > 0 } name)
            {
                _partnerNames[partner.Key] = name;
            }

            int shots = _partnerGuns.TryGetValue(partner.Key, out (SoldierHeldWeapon? Gun, int ShotSequence, bool Fired) before)
                ? before.ShotSequence
                : 0;
            _partnerGuns[partner.Key] = (
                partner.Weapon?.GunModel is { Length: > 0 } model ? _armoury.HeldWeaponFor(load.Mission, model) : null,
                partner.Fired ? shots + 1 : shots,
                partner.Fired);
        }

        foreach (object gone in _partners.Keys.Where(key => !present.Contains(key)).ToArray())
        {
            _partners[gone].Release();
            _partners.Remove(gone);
            _partnerGuns.Remove(gone);
            _partnerNames.Remove(gone);
            _fallen.Remove(gone);
        }
    }

    // The player's body was built from these; each partner's is built the same way.
    private void RememberPartnerLoad(LoadedMission mission, string model, BoneAnimationSet animations) =>
        _partnerLoad = (mission, model, animations);

    private void ResolvePartnerFrameStates()
    {
        foreach (PlayerBodyRenderer body in _partners.Values)
        {
            body.ResolveFrameState();
        }
    }

    private void DrawPartners(in Camera3D camera, in Matrix4f projection)
    {
        foreach ((object key, PlayerBodyRenderer body) in _partners)
        {
            body.Draw(in camera, in projection);

            // The weapon in its hand, drawn as a soldier's is: the gun's body only, its flame being light.
            if (_partnerGuns.TryGetValue(key, out (SoldierHeldWeapon? Gun, int ShotSequence, bool Fired) gun)
                && gun.Gun is { } held
                && body.HandAttachment() is { } hand)
            {
                _drawer.DrawHeld(held, hand, camera, projection, gun.ShotSequence);
            }
        }
    }

    // The flames of the partners that fired this tick, in the forward pass with the soldiers'.
    private void DrawPartnerMuzzleFlashes(in Camera3D camera, in Matrix4f projection)
    {
        foreach ((object key, PlayerBodyRenderer body) in _partners)
        {
            if (_partnerGuns.TryGetValue(key, out (SoldierHeldWeapon? Gun, int ShotSequence, bool Fired) gun)
                && gun.Fired
                && gun.Gun?.Presentation is { } presentation
                && body.HandAttachment() is { } hand)
            {
                presentation.Draw(hand, camera, projection, firedThisTick: true, gun.ShotSequence, WeaponDrawParts.FlameOnly);
            }
        }
    }

    private void ReleasePartners()
    {
        foreach (PlayerBodyRenderer body in _partners.Values)
        {
            body.Release();
        }

        _partners.Clear();
        _partnerGuns.Clear();
        _partnerNames.Clear();
        _partnerLoad = null;
    }
}

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

    // The model each partner's body was built from, so a change of look rebuilds it.
    private readonly Dictionary<object, string> _partnerModels = [];

    // The player's clips on each look's own skeleton, by skeleton id: built once, shared by every body of that skeleton.
    private readonly Dictionary<int, BoneAnimationSet> _lookAnimations = [];

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

            // Its look's model where the level ships it; the level's own player's otherwise. A look changed (the
            // scoreboard naming it a moment after the body first showed) builds the body again.
            string look = partner.Model is { Length: > 0 } wanted && load.Mission.TryGetModel(wanted, out _) ? wanted : load.Model;
            BoneAnimationSet animations = AnimationsFor(look, load.Mission, load.Animations);
            if (_partners.TryGetValue(partner.Key, out PlayerBodyRenderer? body) && _partnerModels.GetValueOrDefault(partner.Key) != look)
            {
                body.Release();
                _partners.Remove(partner.Key);
                _fallen.Remove(partner.Key);
                body = null;
            }

            if (body is null)
            {
                body = new PlayerBodyRenderer(this, _device, _sim, _logger, new SoldierPlayerBody());
                if (!body.Load(load.Mission, look, animations, partner.Feet, partner.Facing))
                {
                    continue;
                }

                _partners[partner.Key] = body;
                _partnerModels[partner.Key] = look;
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
            _partnerModels.Remove(gone);
        }
    }

    // The clips a look's body plays: the player's own, on the look's own skeleton. Every human skeleton but one shares the
    // player's 33 bones and their order, and a clip is a root track and a rotation per bone, so the player's motion poses
    // any of them -- each on its own proportions, which its skeleton's rest pose carries. A look whose skeleton the
    // mission hasn't loaded, or one of another shape, keeps the player's set.
    private BoneAnimationSet AnimationsFor(string model, LoadedMission mission, BoneAnimationSet player)
    {
        if (OpenIGI.Game.Multiplayer.PlayerSkins.All.FirstOrDefault(s => s.Model == model) is not { } skin || skin.Skeleton == player.Skeleton.Id)
        {
            return player;
        }

        if (_lookAnimations.TryGetValue(skin.Skeleton, out BoneAnimationSet? built))
        {
            return built;
        }

        BoneAnimationSet? own = mission.PacksInLoadOrder.SelectMany(static p => p.AnimationSets).FirstOrDefault(s => s.Skeleton.Id == skin.Skeleton);
        if (own is null || own.Skeleton.BoneCount != player.Skeleton.BoneCount || !own.Skeleton.Parents.SequenceEqual(player.Skeleton.Parents))
        {
            return player;
        }

        built = BoneAnimationSet.Create(own.Skeleton, [.. player.Animations]);
        _lookAnimations[skin.Skeleton] = built;
        return built;
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

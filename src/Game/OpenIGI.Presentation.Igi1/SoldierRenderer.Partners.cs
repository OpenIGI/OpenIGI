using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.World;

namespace OpenIGI.Presentation.Igi1;

/// <summary>A co-op session's partners, each drawn as the player's own body is drawn.</summary>
public sealed partial class SoldierRenderer
{
    private readonly Dictionary<object, PlayerBodyRenderer> _partners = [];
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

            PlayerBodyPose pose = partner.Pose;
            body.Follow(partner.Feet, partner.Facing, partner.Animation, partner.Loops, in pose);
            body.Tick();
        }

        foreach (object gone in _partners.Keys.Where(key => !present.Contains(key)).ToArray())
        {
            _partners[gone].Release();
            _partners.Remove(gone);
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
        foreach (PlayerBodyRenderer body in _partners.Values)
        {
            body.Draw(in camera, in projection);
        }
    }

    private void ReleasePartners()
    {
        foreach (PlayerBodyRenderer body in _partners.Values)
        {
            body.Release();
        }

        _partners.Clear();
        _partnerLoad = null;
    }
}

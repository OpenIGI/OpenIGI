using OpenIGI.Game.Ai;
using OpenIGI.Game.Multiplayer;

namespace OpenIGI.Game.Igi2.Multiplayer;

/// <content>The partners as the host's own display shows them: each name over its head, each fallen one's mark.</content>
public sealed partial class Igi2ReplicatedWorld : ICoopMarks
{
    private readonly Dictionary<int, string> _names = [];

    /// <inheritdoc/>
    public IReadOnlyList<WorldNameTag> NameTags()
    {
        List<WorldNameTag> tags = [];
        int? ownTeam = IncludesHost ? _level.Human.Team : null;
        foreach ((int slot, Igi2NetworkHuman partner) in _bySlot)
        {
            if (!partner.Human.IsDead && Game.Multiplayer.Modes.PlayerSides.ShowsName(mode: null, ownTeam, partner.Human.Team))
            {
                tags.Add(Igi2CoopMarks.Tag(partner.Human, _names.GetValueOrDefault(slot) ?? "Teammate", Igi2CoopMarks.HealthOf(partner.Human)));
            }
        }

        return tags;
    }

    /// <inheritdoc/>
    public IReadOnlyList<ReviveMarker> ReviveMarkers()
    {
        List<ReviveMarker> marks = [];
        foreach ((int slot, Igi2NetworkHuman partner) in _bySlot)
        {
            if (ReviveProgressOf(slot + 1) is { } progress)
            {
                marks.Add(Igi2CoopMarks.Mark(partner.Human, _names.GetValueOrDefault(slot) ?? "Teammate", progress));
            }
        }

        return marks;
    }
}

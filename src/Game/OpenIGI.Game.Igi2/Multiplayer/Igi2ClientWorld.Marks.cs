using OpenIGI.Game.Ai;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Game.Multiplayer.Modes;

namespace OpenIGI.Game.Igi2.Multiplayer;

/// <content>The other players as this client's display shows them: each name over its head, each fallen one's mark.</content>
public sealed partial class Igi2ClientWorld : ICoopMarks
{
    private readonly Dictionary<int, string> _names = [];
    private string? _mode;
    private Func<int, int?> _teamOf = static _ => null;
    private int? _ownTeam;

    /// <inheritdoc/>
    public void Name(int entity, string name) => _names[entity] = name;

    /// <inheritdoc/>
    public void Sides(string mode, Func<int, int?> teamOf, int? ownTeam) =>
        (_mode, _teamOf, _ownTeam) = (mode, teamOf ?? throw new ArgumentNullException(nameof(teamOf)), ownTeam);

    /// <inheritdoc/>
    public IReadOnlyList<WorldNameTag> NameTags()
    {
        List<WorldNameTag> tags = [];
        foreach ((int entity, Igi2NetworkHuman puppet) in _puppets)
        {
            if (puppet.PuppetTarget is { } seen && (seen.Flags & ReplicatedHumanCondition.Alive) != 0
                && PlayerSides.ShowsName(_mode, _ownTeam, _teamOf(entity) ?? seen.Team))
            {
                tags.Add(Igi2CoopMarks.Tag(puppet.Human, _names.GetValueOrDefault(entity) ?? "Teammate", seen.Health / 255f));
            }
        }

        return tags;
    }

    /// <inheritdoc/>
    /// <remarks>A fallen player's health byte carries its revival, as the server sends it.</remarks>
    public IReadOnlyList<ReviveMarker> ReviveMarkers()
    {
        List<ReviveMarker> marks = [];
        if (!PlayerSides.Revivable(_mode))
        {
            return marks;
        }

        foreach ((int entity, Igi2NetworkHuman puppet) in _puppets)
        {
            if (puppet.PuppetTarget is { } seen && (seen.Flags & ReplicatedHumanCondition.Alive) == 0)
            {
                marks.Add(Igi2CoopMarks.Mark(puppet.Human, _names.GetValueOrDefault(entity) ?? "Teammate", seen.Health / 255f));
            }
        }

        return marks;
    }
}

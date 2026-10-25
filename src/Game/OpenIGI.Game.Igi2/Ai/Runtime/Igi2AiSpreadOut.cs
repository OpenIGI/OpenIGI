using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Igi2.Ai.Runtime;

/// <summary>
/// Where a covering team spreads to (<c>AISquadSpreadOutContext_s</c>): the spread-out center, eight places to either side of it,
/// the turn of the next place, and the wait before another member moves.
/// </summary>
public sealed class Igi2AiSpreadOut
{
    private readonly Vector3d[] _places = new Vector3d[8];

    /// <summary>Gets or sets the spread-out center.</summary>
    public Vector3d Center { get; set; }

    /// <summary>Writes its state for a save game: its center, its places, its timer and its turn.</summary>
    /// <param name="words">Where it is written.</param>
    public void SaveState(Igi2AiSquadWords words)
    {
        ArgumentNullException.ThrowIfNull(words);
        words.Add(Center);
        foreach (Vector3d place in _places)
        {
            words.Add(place);
        }

        words.Add(UpdateTimer);
        words.Add(Turn);
    }

    /// <summary>Reads its state back.</summary>
    /// <param name="words">Where it is read from.</param>
    public void LoadState(Igi2AiSquadWords words)
    {
        ArgumentNullException.ThrowIfNull(words);
        Center = words.Double3();
        for (int i = 0; i < _places.Length; i++)
        {
            _places[i] = words.Double3();
        }

        UpdateTimer = words.Int();
        Turn = words.Int();
    }

    /// <summary>Gets or sets the ticks before another member moves.</summary>
    public int UpdateTimer { get; set; }

    /// <summary>Gets or sets which place is next.</summary>
    public int Turn { get; set; }

    /// <summary>
    /// The squad's center moved away from the other squads it crowds (<c>AISquad_FindSpreadOutCenter</c>): its first member, pushed
    /// by the average of how far each other live squad (over four metres off) overlaps it.
    /// </summary>
    /// <param name="squad">The squad.</param>
    /// <returns>The center.</returns>
    public static Vector3d FindCenter(Igi2AiSquad squad)
    {
        ArgumentNullException.ThrowIfNull(squad);
        Vector3d center = squad.FirstMember?.Soldier.Position ?? default;
        float radius = squad.AreaRadius(center);
        float dx = 0f, dy = 0f, dz = 0f;
        int count = 0;
        foreach (Igi2AiSquad other in squad.World.Squads)
        {
            if (ReferenceEquals(other, squad) || other.IsDead)
            {
                continue;
            }

            Vector3d at = other.FirstMember?.Soldier.Position ?? default;
            float otherRadius = other.AreaRadius(at);
            float x = ((Fpu)center.X - at.X).F, y = ((Fpu)center.Y - at.Y).F, z = ((Fpu)center.Z - at.Z).F;
            float length = Fpu.Sqrt((((Fpu)x * x) + ((Fpu)y * y)) + (((Fpu)center.Z - at.Z) * ((Fpu)center.Z - at.Z))).F;
            if (length > 4096f)
            {
                Fpu reach = (Fpu)otherRadius + radius;
                if (length < reach.Value)
                {
                    Fpu push = reach / length;
                    count++;
                    y = (push * y).F;
                    z = (push * z).F;
                    dx = (((Fpu)x * push) + dx).F;
                    dy = ((Fpu)y + dy).F;
                    dz = ((Fpu)z + dz).F;
                }
            }
        }

        if (count == 0)
        {
            return center;
        }

        Fpu share = 1.0 / count;
        dy = (share * dy).F;
        dz = (share * dz).F;
        return new Vector3d((((Fpu)dx * share) + center.X).Value, ((Fpu)dy + center.Y).Value, ((Fpu)dz + center.Z).Value);
    }

    /// <summary>
    /// Moves one covering member sideways (<c>AISquad_SpreadTeamSideways</c>): every five seconds at most, half the time, a member
    /// settled for five seconds, off cover, not activating or moving, with no shotgun and at least thirty percent of a clip, moves to
    /// the next place across the line to the target, when two of its team see the target and no one stands there.
    /// </summary>
    /// <param name="squad">The squad.</param>
    /// <param name="target">The target's position.</param>
    /// <param name="team">The covering team.</param>
    public void SpreadSideways(Igi2AiSquad squad, Vector3d target, int team)
    {
        ArgumentNullException.ThrowIfNull(squad);
        int turn = squad.FireIndex;
        Igi2HumanAi? member = turn < squad.Teams[team].Members.Count ? squad.Teams[team].Members[turn] : null;
        if (member is null)
        {
            return;
        }

        if (UpdateTimer > 0)
        {
            UpdateTimer--;
            return;
        }

        if (member.MemberTimer < 150 || squad.World.Random.NextSingle() < 0.5f || member.IsOnCoverNode() || member.IsOnTheWayToCoverNode()
            || member.IsTop(Igi2AiContent.ActivateInit) || member.IsTop(Igi2AiContent.MoveToNodeSquadOrderInit) || member.IsShotgunSoldier()
            || !member.IsLessThanPercentageAmmoLeftInWeapon(30f))
        {
            return;
        }

        int seeing = squad.Teams[team].Members.Count(static m => m.GetTarget(visibleOnly: true) is not null);
        if (seeing < 2)
        {
            return;
        }

        double ax = target.X - Center.X;
        float ay = ((Fpu)target.Y - Center.Y).F;
        if (ax != 0.0 || ay != 0.0)
        {
            double bx = -ax;
            Fpu length = Fpu.Sqrt(((Fpu)ay * ay) + ((Fpu)bx * bx));
            float across = ((Fpu)ay * ((Fpu)1.0 / length)).F;
            Fpu along = (Fpu)bx * ((Fpu)1.0 / length);
            float distance = squad.Task.FormationDistance;
            for (int i = 0; i < 8; i++)
            {
                double side = (i & 1) == 0 ? -4096.0 : 4096.0;
                _places[i] = new Vector3d(
                    ((((Fpu)across * distance) * side) + Center.X).Value, (((along * distance) * side) + Center.Y).Value, Center.Z);
            }
        }

        if (member.Graph is not { } graph || graph.NodeAt(member.CurrentNode) is not { } start)
        {
            return;
        }

        if (graph.ClosestNodeFromStartingNode(start, _places[squad.FireIndex], 50) is not { } node)
        {
            return;
        }

        foreach (Igi2HumanAi other in squad.Teams[team].Members)
        {
            if (other.CurrentNode == node.Id)
            {
                return;
            }
        }

        member.DestinationNode = node.Id;
        member.MemberTimer = 0;
        UpdateTimer = 150;
    }
}

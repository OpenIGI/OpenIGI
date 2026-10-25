namespace OpenIGI.Game.Igi2.Ai.Runtime;

/// <summary>One of a squad's two teams, <c>AISquadTeam_s</c>: up to eight minds, the first its leader.</summary>
public sealed class Igi2AiSquadTeam
{
    /// <summary>The most minds a team holds.</summary>
    public const int Capacity = 8;

    /// <summary>Gets its minds, the leader first.</summary>
    public List<Igi2HumanAi> Members { get; } = [];

    /// <summary>Gets or sets how it fires, <c>eFiringMode</c>: one fires on the squad's order.</summary>
    public int FiringMode { get; set; } = 1;

    /// <summary>Gets its leader, or <see langword="null"/>.</summary>
    public Igi2HumanAi? Leader => Members.Count > 0 ? Members[0] : null;

    /// <summary>Makes the member at an index the leader, the old leader taking its place (<c>AISquad_MakeTeamLeader</c>).</summary>
    /// <param name="index">The index.</param>
    public void MakeLeader(int index) => (Members[0], Members[index]) = (Members[index], Members[0]);
}

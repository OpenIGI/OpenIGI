namespace OpenIGI.Game.Multiplayer.Modes;

/// <summary>
/// A sample of every kind of kill feed row, for seeing the feed without a fight: the <c>killfeed_sample</c> development
/// command lists it through the session as real kills go. Its weapons are the title's armoury's, so a title with none
/// shows the marks alone.
/// </summary>
public static class KillFeedSample
{
    // The made-up players the sample's kills are between, by entity, out of the way of any real one.
    private const int Kim = 61;
    private const int Anya = 62;
    private const int Dmitri = 63;
    private const int Viktor = 65;
    private const int Boris = 66;

    /// <summary>Gets a sample's kills.</summary>
    /// <param name="set">Which: 1 the player's own kills and the weapons; 2 the marks -- team kill, suicide, fall, guard, the player killed.</param>
    /// <param name="title">The title played, whose armoury the weapons are from.</param>
    /// <param name="own">The player's name.</param>
    /// <param name="ownEntity">The player's entity.</param>
    /// <param name="ownTeam">The player's team.</param>
    /// <returns>The kills, in the order they are listed.</returns>
    public static IReadOnlyList<KillNote> Notes(int set, string? title, string own, int ownEntity, int ownTeam)
    {
        IReadOnlyList<ArmouryItem> armoury = Armoury.For(title);
        string Weapon(string id) => armoury.FirstOrDefault(i => i.Id == id)?.Weapon ?? string.Empty;
        int enemy = ownTeam == TeamRoster.Good ? TeamRoster.Bad : TeamRoster.Good;
        return set == 2
            ?
            [
                new("Kim", ownTeam, "Anya", ownTeam, Kim, Anya, Weapon("uzi"), Marks: KillMarks.TeamKill),
                new(string.Empty, -1, "Dmitri", enemy, -1, Dmitri, Weapon("grenade"), KillCause.Suicide),
                new(string.Empty, -1, "Kim", ownTeam, -1, Kim, string.Empty, KillCause.Fall),
                new("Anya", ownTeam, KillNote.GuardName, -1, Anya, -1, Weapon("mp5sd"), Marks: KillMarks.Guard | KillMarks.Headshot),
                new("Viktor_Sniper_Longname", enemy, own, ownTeam, Viktor, ownEntity, Weapon("dragunov"), Marks: KillMarks.Headshot),
            ]
            :
            [
                new("Anya", ownTeam, "Viktor", enemy, Anya, Viktor, Weapon("grenade"), KillCause.Explosive),
                new("Dmitri", enemy, "Kim", ownTeam, Dmitri, Kim, string.Empty, KillCause.Melee, KillMarks.Backstab),
                new("Viktor", enemy, "Anya", ownTeam, Viktor, Anya, Weapon("spas")),
                new(own, ownTeam, "Dmitri", enemy, ownEntity, Dmitri, Weapon("ak47")),
                new(own, ownTeam, "Boris", enemy, ownEntity, Boris, Weapon("m16"), Marks: KillMarks.Headshot),
            ];
    }
}

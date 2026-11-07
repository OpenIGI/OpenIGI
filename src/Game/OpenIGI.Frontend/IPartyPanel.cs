namespace OpenIGI.Frontend;

/// <summary>A member of the party, as the menu lists it.</summary>
/// <param name="Id">Its number in the party, which the leader's kick and promote name it by.</param>
/// <param name="Name">Its name.</param>
/// <param name="Leads">Whether it leads the party.</param>
/// <param name="IsYou">Whether it is this player.</param>
/// <param name="Where">Where it plays, as the menu says it: <c>with the leader</c>, a server or room, or <c>in the menus</c>.</param>
public sealed record PartyMemberView(int Id, string Name, bool Leads, bool IsYou, string Where);

/// <summary>The party the player is in, as the menu shows it.</summary>
/// <param name="Code">Its code, which friends join it by; <see langword="null"/> while it is still being made or joined.</param>
/// <param name="Leader">Who leads it.</param>
/// <param name="Leading">Whether this player leads it.</param>
/// <param name="Members">Who is in it, the leader first.</param>
/// <param name="Playing">Where its leader plays, as the menu says it, or <see langword="null"/> for nowhere yet.</param>
public sealed record PartyView(string? Code, string Leader, bool Leading, IReadOnlyList<PartyMemberView> Members, string? Playing)
{
    /// <summary>Gets a key that changes whenever anything shown changes, for a page to rebuild on.</summary>
    public string Key => $"{Code}|{Leader}|{Leading}|{string.Join(',', Members.Select(m => $"{m.Id}:{m.Name}:{m.Where}"))}|{Playing}";
}

/// <summary>
/// Parties: friends who play together. One makes a party and hands its code or link round; wherever its leader then plays,
/// the others' games follow. The host supplies it where the build can reach the master server.
/// </summary>
public interface IPartyPanel
{
    /// <summary>Gets the party the player is in, or <see langword="null"/> for none.</summary>
    PartyView? Party { get; }

    /// <summary>Gets why the last party was lost, or <see langword="null"/>.</summary>
    string? Failure { get; }

    /// <summary>Gets why the leader's last kick or hand-over was turned down, or <see langword="null"/>.</summary>
    string? OrderError { get; }

    /// <summary>Makes a party this player leads, leaving any it was in.</summary>
    void Create();

    /// <summary>Joins a friend's party by its code, leaving any it was in.</summary>
    /// <param name="code">The code; case does not matter.</param>
    void Join(string code);

    /// <summary>Leaves the party; the next oldest member leads it.</summary>
    void Leave();

    /// <summary>Puts a member out of the party; only its leader may.</summary>
    /// <param name="member">The member's <see cref="PartyMemberView.Id"/>.</param>
    void Kick(int member);

    /// <summary>Hands the lead to another member; only the leader may.</summary>
    /// <param name="member">The member's <see cref="PartyMemberView.Id"/>.</param>
    void Promote(int member);

    /// <summary>Copies the party's invite link for friends, when the build has a clipboard.</summary>
    /// <returns>The link copied, or <see langword="null"/> when there is no party yet.</returns>
    string? CopyInvite();
}

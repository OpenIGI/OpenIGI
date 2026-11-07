namespace OpenIGI.Frontend;

/// <summary>The party the player is in, as the menu shows it.</summary>
/// <param name="Code">Its code, which friends join it by; <see langword="null"/> while it is still being made or joined.</param>
/// <param name="Leader">Who leads it.</param>
/// <param name="Leading">Whether this player leads it.</param>
/// <param name="Members">Who is in it, the leader first.</param>
/// <param name="Playing">Where its leader plays, as the menu says it, or <see langword="null"/> for nowhere yet.</param>
public sealed record PartyView(string? Code, string Leader, bool Leading, IReadOnlyList<string> Members, string? Playing)
{
    /// <summary>Gets a key that changes whenever anything shown changes, for a page to rebuild on.</summary>
    public string Key => $"{Code}|{Leader}|{Leading}|{string.Join(',', Members)}|{Playing}";
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

    /// <summary>Makes a party this player leads, leaving any it was in.</summary>
    void Create();

    /// <summary>Joins a friend's party by its code, leaving any it was in.</summary>
    /// <param name="code">The code; case does not matter.</param>
    void Join(string code);

    /// <summary>Leaves the party; the next oldest member leads it.</summary>
    void Leave();

    /// <summary>Copies the party's invite link for friends, when the build has a clipboard.</summary>
    /// <returns>The link copied, or <see langword="null"/> when there is no party yet.</returns>
    string? CopyInvite();
}

using OpenIGI.Game.Multiplayer.Modes;

namespace OpenIGI.Host.Multiplayer;

/// <summary>
/// What a server does when a match is decided, the same on a dedicated server and a listen host: the next maps offered to vote
/// for while the match's card is up, and when the card's clock runs out the map the players picked -- or, with no pick, the one
/// the server would play anyway -- gone on to, once.
/// </summary>
/// <param name="session">The session whose matches it follows.</param>
/// <param name="offer">The maps to offer when a match is decided; none for no vote.</param>
/// <param name="goOn">Goes on to the map picked, or to the server's own next with <see langword="null"/>.</param>
public sealed class NextMapFlow(HostedSession session, Func<IReadOnlyList<MapChoice>> offer, Action<MapChoice?> goOn)
{
    private readonly HostedSession _session = session ?? throw new ArgumentNullException(nameof(session));
    private readonly Func<IReadOnlyList<MapChoice>> _offer = offer ?? throw new ArgumentNullException(nameof(offer));
    private readonly Action<MapChoice?> _goOn = goOn ?? throw new ArgumentNullException(nameof(goOn));
    private bool _offered;
    private bool _changing;

    /// <summary>Runs between the host's steps.</summary>
    public void Tick()
    {
        if (_session.IsMatchDecided && !_offered)
        {
            _offered = true;
            _session.OfferMaps(_offer());
        }

        if (_session.IsMapOver && !_changing)
        {
            _changing = true;
            _goOn(_session.TakeChosenMap());
        }
        else if (!_session.IsMapOver)
        {
            _changing = false;
            _offered = _session.IsMatchDecided && _offered;
        }
    }

    /// <summary>The maps a listen host offers: this one again, the next mission, and two others under other modes.</summary>
    /// <param name="missions">The missions there are, in campaign order, with their names.</param>
    /// <param name="mission">The mission played.</param>
    /// <param name="mode">The mode played.</param>
    /// <param name="random">What picks the others.</param>
    /// <returns>The maps, the one played first.</returns>
    public static IReadOnlyList<MapChoice> Around(IReadOnlyList<(int Id, string Name)> missions, int mission, string mode, Random random)
    {
        ArgumentNullException.ThrowIfNull(missions);
        ArgumentNullException.ThrowIfNull(random);
        string NameOf(int id) => missions.FirstOrDefault(m => m.Id == id).Name ?? string.Empty;
        List<MapChoice> choices = [new MapChoice(mission, mode, NameOf(mission), Again: true)];
        if (missions.Count < 2 || !GameModes.IsVersus(mode))
        {
            return choices;
        }

        int at = Math.Max(0, missions.ToList().FindIndex(m => m.Id == mission));
        (int Id, string Name) next = missions[(at + 1) % missions.Count];
        choices.Add(new MapChoice(next.Id, mode, next.Name));

        // Two more: other missions, each under another versus mode.
        string[] others = [.. VersusModes.Where(m => m != mode)];
        random.Shuffle(others);
        List<(int Id, string Name)> rest = [.. missions.Where(m => m.Id != mission && m.Id != next.Id)];
        random.Shuffle(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(rest));
        for (int i = 0; i < 2 && i < rest.Count && i < others.Length; i++)
        {
            choices.Add(new MapChoice(rest[i].Id, others[i], rest[i].Name));
        }

        return choices;
    }

    // The modes a listen host's vote moves between.
    private static readonly string[] VersusModes = [GameModes.TeamDeathmatch, GameModes.FreeForAll, GameModes.Elimination, GameModes.SearchAndDestroy];
}

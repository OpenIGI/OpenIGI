using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace OpenIGI.Net.Rooms;

/// <summary>A member of a party, as every member sees it.</summary>
/// <param name="Id">Its number in the party.</param>
/// <param name="Name">Its name.</param>
/// <param name="At">Where it plays, as <see cref="PartyState.Target"/> says a place, or <see langword="null"/> for nowhere.</param>
public sealed record PartyMember(int Id, string Name, string? At = null);

/// <summary>A party as the master server last told it.</summary>
/// <param name="Code">Its code, which friends join it by.</param>
/// <param name="Leader">The member who leads it.</param>
/// <param name="Members">Who is in it, the leader first.</param>
/// <param name="Target">Where its leader plays: a room's code, a server's browser key (<c>udp:host:port</c>), or nowhere.</param>
/// <param name="Version">How many times the leader has moved it.</param>
public sealed record PartyState(string Code, int Leader, IReadOnlyList<PartyMember> Members, string? Target, int Version);

/// <summary>
/// A party on the master server, as one member plays in it: friends who follow their leader from server to server. The
/// leader's game tells the party where it plays; every other member's asks every couple of seconds and follows.
/// </summary>
/// <remarks>
/// Nothing here blocks a frame: each call to the master runs in the background, one at a time, and
/// <see cref="Update"/>, on the game's thread, acts on what came back.
/// </remarks>
public sealed class Party : IDisposable
{
    /// <summary>How often a member asks where its leader plays, in seconds.</summary>
    public const double AskSeconds = 2;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    private readonly Uri _master;
    private readonly HttpMessageInvoker _http;
    private readonly CancellationTokenSource _cancel = new();
    private Task<(HttpStatusCode Status, JsonObject? Body)>? _call;
    private bool _joining = true;
    private double _askedAt = double.NegativeInfinity;
    private int _followed = -1;
    private string? _told;
    private bool _telling;
    private string? _toTell;

    // What the leader asked of the party (put a member out, hand the lead over), sent when no other call is in flight.
    private (string Action, int Member)? _order;
    private bool _ordering;

    private Party(Uri master, string path, string name, HttpMessageInvoker? http)
    {
        _master = master;
        _http = http ?? Http;
        _call = Send(HttpMethod.Post, path, new JsonObject { ["name"] = name });
    }

    /// <summary>Gets the link a friend opens to join a party, in the browser or the game.</summary>
    /// <param name="code">The party's code.</param>
    /// <returns>The link.</returns>
    public static string InviteLink(string code) => $"https://openigi.com/party/{Uri.EscapeDataString(code)}";

    /// <summary>Gets the party's code, once it is made or joined.</summary>
    public string? Code => State?.Code;

    /// <summary>Gets the party as last told, or <see langword="null"/> until it is made or joined.</summary>
    public PartyState? State { get; private set; }

    /// <summary>Gets this member's number in the party.</summary>
    public int Member { get; private set; }

    /// <summary>Gets a value indicating whether this member leads the party.</summary>
    public bool IsLeader => State is { } state && state.Leader == Member;

    /// <summary>Gets why the party was lost (could not be made or joined, or this member is no longer in it), or <see langword="null"/>.</summary>
    public string? Failure { get; private set; }

    /// <summary>Gets why the leader's last order (a kick, the lead handed over) was turned down, or <see langword="null"/>.</summary>
    public string? OrderError { get; private set; }

    /// <summary>Gets a value indicating whether the party is still being made or joined.</summary>
    public bool IsJoining => _joining && Failure is null;

    /// <summary>Gets a value indicating whether a call to the master server is in flight.</summary>
    internal bool IsBusy => _call is not null;

    private string Token { get; set; } = string.Empty;

    /// <summary>Makes a party, led by this member.</summary>
    /// <param name="master">The master server.</param>
    /// <param name="name">This member's name.</param>
    /// <returns>The party, made in the background.</returns>
    public static Party Create(Uri master, string name) => Create(master, name, null);

    /// <summary>Makes a party through a given HTTP line, as tests answer for the master server.</summary>
    /// <param name="master">The master server.</param>
    /// <param name="name">This member's name.</param>
    /// <param name="http">What sends the requests; <see langword="null"/> for the network.</param>
    /// <returns>The party, made in the background.</returns>
    internal static Party Create(Uri master, string name, HttpMessageInvoker? http)
    {
        ArgumentNullException.ThrowIfNull(master);
        return new Party(master, "/v1/parties", name ?? string.Empty, http);
    }

    /// <summary>Joins a party by its code.</summary>
    /// <param name="master">The master server.</param>
    /// <param name="code">The party's code; case does not matter.</param>
    /// <param name="name">This member's name.</param>
    /// <returns>The party, joined in the background.</returns>
    public static Party Join(Uri master, string code, string name) => Join(master, code, name, null);

    /// <summary>Joins a party through a given HTTP line, as tests answer for the master server.</summary>
    /// <param name="master">The master server.</param>
    /// <param name="code">The party's code; case does not matter.</param>
    /// <param name="name">This member's name.</param>
    /// <param name="http">What sends the requests; <see langword="null"/> for the network.</param>
    /// <returns>The party, joined in the background.</returns>
    internal static Party Join(Uri master, string code, string name, HttpMessageInvoker? http)
    {
        ArgumentNullException.ThrowIfNull(master);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        return new Party(master, $"/v1/parties/{Uri.EscapeDataString(code.Trim().ToUpperInvariant())}/join", name ?? string.Empty, http);
    }

    /// <summary>Puts a member out of the party; only its leader may, and the member is told why.</summary>
    /// <param name="member">The member's number.</param>
    public void Kick(int member) => _order = ("kick", member);

    /// <summary>Hands the lead to another member; only the leader may.</summary>
    /// <param name="member">The member's number.</param>
    public void Promote(int member) => _order = ("promote", member);

    /// <summary>Acts on what the master server said, and asks it again when it is time.</summary>
    /// <param name="now">The caller's clock, in seconds.</param>
    /// <param name="playing">Where this member plays now, as <see cref="PartyState.Target"/> says it; <see langword="null"/> for nowhere.</param>
    /// <returns>Where to go to follow the leader, once each time it moves; <see langword="null"/> when there is nowhere new.</returns>
    public string? Update(double now, string? playing)
    {
        if (_call is { IsCompleted: true } done)
        {
            _call = null;
            Take(done);
        }

        if (Failure is not null || _joining || _call is not null)
        {
            return Follow(playing);
        }

        // The leader's order first; then where it plays, whenever that changes; everyone asks, the leader too, to stay in.
        if (_order is { } order)
        {
            _order = null;
            _ordering = true;
            OrderError = null;
            _call = Send(HttpMethod.Post, $"/v1/parties/{State!.Code}/{order.Action}", new JsonObject { ["member"] = Member, ["token"] = Token, ["target"] = order.Member });
        }
        else if (IsLeader && playing != _told)
        {
            _telling = true;
            _toTell = playing;
            _call = Send(HttpMethod.Post, $"/v1/parties/{State!.Code}/target", new JsonObject { ["member"] = Member, ["token"] = Token, ["target"] = playing });
        }
        else if (now - _askedAt >= AskSeconds)
        {
            _askedAt = now;
            string at = playing is null ? string.Empty : $"&at={Uri.EscapeDataString(playing)}";
            // The token in a header, out of the address asked, which is what a log or a proxy keeps. A master from before it
            // read the header knows the token only in the address: asked that way once the header has been turned down.
            string member = Member.ToString(CultureInfo.InvariantCulture);
            _askedByHeader = !_tokenInAddress;
            _call = _tokenInAddress
                ? Send(HttpMethod.Get, $"/v1/parties/{State!.Code}?member={member}&token={Token}{at}", null)
                : Send(HttpMethod.Get, $"/v1/parties/{State!.Code}?member={member}{at}", null, Token);
        }

        return Follow(playing);
    }

    /// <summary>Leaves the party; the next oldest member leads it.</summary>
    public void Dispose()
    {
        if (State is { } state && Failure is null)
        {
            // Told in the background, and not waited for: a member that never says goodbye is let go when it stops asking.
            _ = Send(HttpMethod.Post, $"/v1/parties/{state.Code}/leave", new JsonObject { ["member"] = Member, ["token"] = Token });
        }

        _cancel.Cancel();
        Failure ??= "left";
    }

    // Where to go: the leader's place, once each time it moves, when this member isn't there already.
    private string? Follow(string? playing)
    {
        if (IsLeader || State is not { Target: { } target } state || state.Version == _followed)
        {
            return null;
        }

        _followed = state.Version;
        return string.Equals(target, playing, StringComparison.OrdinalIgnoreCase) ? null : target;
    }

    // Whether the last ask carried the token in a header, whether a master has yet answered one that did, and whether this
    // party's master has turned the header down and is asked the old way.
    private bool _askedByHeader;
    private bool _headerAnswered;
    private bool _tokenInAddress;

    private void Take(Task<(HttpStatusCode Status, JsonObject? Body)> call)
    {
        bool byHeader = _askedByHeader;
        _askedByHeader = false;
        if (byHeader && !_headerAnswered)
        {
            // A master that doesn't read the header says this member isn't in the party, or -- to a browser -- refuses the
            // header itself: not the party's end, only an older master.
            if (!call.IsCompletedSuccessfully
                || (call.Result.Status == HttpStatusCode.Forbidden && call.Result.Body?["error"]?.GetValue<string>() == "not in the party"))
            {
                _tokenInAddress = true;
                _askedAt = double.NegativeInfinity;
                return;
            }

            _headerAnswered = call.Result.Status == HttpStatusCode.OK;
        }

        if (!call.IsCompletedSuccessfully)
        {
            // The master didn't answer: tried again at the next ask, except a party not yet made or joined, which is lost.
            if (_joining)
            {
                Failure = "the master server could not be reached";
            }

            _telling = false;
            _ordering = false;
            return;
        }

        (HttpStatusCode status, JsonObject? body) = call.Result;

        // An order turned down (not the leader any more, the member gone) is said, and the party plays on.
        if (_ordering)
        {
            _ordering = false;
            if (status != HttpStatusCode.OK || body is null)
            {
                OrderError = body?["error"]?.GetValue<string>() ?? "the party's master server turned that down";
                return;
            }
        }

        if (status != HttpStatusCode.OK || body is null)
        {
            Failure = body?["error"]?.GetValue<string>() is { Length: > 0 } error ? error
                : _joining ? "no such party" : "no longer in the party";
            return;
        }

        if (_joining)
        {
            _joining = false;
            Member = body["member"]?.GetValue<int>() ?? 0;
            Token = body["token"]?.GetValue<string>() ?? string.Empty;
        }

        if (_telling)
        {
            _telling = false;
            _told = _toTell;
        }

        State = Read(body);

        // A member that joins takes the leader as it moved last; one that has led and leads again starts from where it plays.
        if (IsLeader)
        {
            _followed = State.Version;
        }
    }

    /// <summary>Reads a party as the master server writes it.</summary>
    /// <param name="body">The answer.</param>
    /// <returns>The party.</returns>
    public static PartyState Read(JsonObject body)
    {
        ArgumentNullException.ThrowIfNull(body);
        List<PartyMember> members = [];
        if (body["members"] is JsonArray list)
        {
            foreach (JsonNode? member in list)
            {
                if (member is JsonObject one)
                {
                    members.Add(new PartyMember(one["id"]?.GetValue<int>() ?? 0, one["name"]?.GetValue<string>() ?? string.Empty, one["at"]?.GetValue<string>()));
                }
            }
        }

        return new PartyState(
            body["code"]?.GetValue<string>() ?? string.Empty,
            body["leader"]?.GetValue<int>() ?? 0,
            members,
            body["target"]?.GetValue<string>(),
            body["version"]?.GetValue<int>() ?? 0);
    }

    private async Task<(HttpStatusCode Status, JsonObject? Body)> Send(HttpMethod method, string path, JsonObject? body, string? token = null)
    {
        using HttpRequestMessage request = new(method, new Uri(_master, path));
        if (token is not null)
        {
            request.Headers.TryAddWithoutValidation("x-openigi-token", token);
        }

        if (body is not null)
        {
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        }

        using HttpResponseMessage response = await _http.SendAsync(request, _cancel.Token).ConfigureAwait(false);
        string text = await response.Content.ReadAsStringAsync(_cancel.Token).ConfigureAwait(false);
        JsonObject? answer = null;
        try
        {
            answer = JsonNode.Parse(text) as JsonObject;
        }
        catch (System.Text.Json.JsonException)
        {
            // Nothing to do: the failure is expected here and leaves nothing to undo.
        }

        return (response.StatusCode, answer);
    }
}

using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using OpenIGI.Net.Rooms;
using Xunit;

namespace OpenIGI.Net.Tests;

/// <summary>Parties as a member's game plays in them, against a master server answered in-process.</summary>
public sealed class PartyTests
{
    private static readonly Uri Master = new("https://master.test/");

    [Fact]
    public void A_member_follows_its_leader_once_each_time_the_leader_moves_and_not_where_it_already_is()
    {
        FakeMaster master = new();
        using Party leader = Party.Create(Master, "Ann", master.Line);
        double now = 0;
        Settle(leader, now, null);
        Assert.True(leader.IsLeader);
        Assert.NotNull(leader.Code);

        using Party member = Party.Join(Master, leader.Code.ToLowerInvariant(), "Bob", master.Line);
        Settle(member, now, null);
        Assert.False(member.IsLeader);
        Assert.Equal(["Ann", "Bob"], member.State!.Members.Select(m => m.Name));

        // The leader joins a server: the member, asking next, is sent there once.
        Settle(leader, now, "udp:203.0.113.5:26001");
        now += Party.AskSeconds;
        Assert.Equal("udp:203.0.113.5:26001", Settle(member, now, null));
        now += Party.AskSeconds;
        Assert.Null(Settle(member, now, null));

        // On to a room; a member already there isn't sent again.
        Settle(leader, now, "QWERTY");
        now += Party.AskSeconds;
        Assert.Null(Settle(member, now, "qwerty"));
        Assert.Equal("QWERTY", member.State.Target);
    }

    [Fact]
    public void A_member_who_joins_late_goes_where_the_leader_already_plays()
    {
        FakeMaster master = new();
        using Party leader = Party.Create(Master, "Ann", master.Line);
        Settle(leader, 0, null);
        Settle(leader, 0, "ZXCVBN");

        using Party late = Party.Join(Master, leader.Code!, "Cat", master.Line);
        Assert.Equal("ZXCVBN", Settle(late, 0, null));
    }

    [Fact]
    public void The_next_member_leads_when_the_leader_leaves_and_an_unknown_code_is_lost()
    {
        FakeMaster master = new();
        Party leader = Party.Create(Master, "Ann", master.Line);
        Settle(leader, 0, null);
        using Party member = Party.Join(Master, leader.Code!, "Bob", master.Line);
        Settle(member, 0, null);

        leader.Dispose();
        Settle(member, Party.AskSeconds, null);
        Assert.True(member.IsLeader);
        Assert.Equal(["Bob"], member.State!.Members.Select(m => m.Name));

        using Party nowhere = Party.Join(Master, "AAAAAA", "Dan", master.Line);
        Settle(nowhere, 0, null);
        Assert.NotNull(nowhere.Failure);
    }

    [Fact]
    public void The_leader_hands_the_lead_over_and_a_member_put_out_is_told_so()
    {
        FakeMaster master = new();
        using Party ann = Party.Create(Master, "Ann", master.Line);
        Settle(ann, 0, null);
        using Party bob = Party.Join(Master, ann.Code!, "Bob", master.Line);
        Settle(bob, 0, null);
        using Party cat = Party.Join(Master, ann.Code!, "Cat", master.Line);
        Settle(cat, 0, null);

        // Bob is no leader: his kick is turned down, and he stays in.
        bob.Kick(cat.Member);
        Settle(bob, Party.AskSeconds, null);
        Assert.NotNull(bob.OrderError);
        Assert.Null(bob.Failure);

        ann.Kick(cat.Member);
        Settle(ann, Party.AskSeconds, null);
        Assert.Equal(["Ann", "Bob"], ann.State!.Members.Select(m => m.Name));
        Settle(cat, Party.AskSeconds * 2, null);
        Assert.Equal("the party's leader removed you from it", cat.Failure);

        ann.Promote(bob.Member);
        Settle(ann, Party.AskSeconds * 3, "QWERTY");
        Assert.False(ann.IsLeader);
        Settle(bob, Party.AskSeconds * 3, null);
        Assert.True(bob.IsLeader);
    }

    // Updates until the call in flight has come back, keeping the first place it says to go.
    private static string? Settle(Party party, double now, string? playing)
    {
        string? follow = null;
        for (int i = 0; i < 5000; i++)
        {
            // Updated every time: once there is somewhere to go, the call it started must still be taken back.
            string? go = party.Update(now, playing);
            follow ??= go;
            if (!party.IsBusy)
            {
                return follow;
            }

            Thread.Sleep(1);
        }

        throw new TimeoutException(
            $"member {party.Member} (leader {party.IsLeader}, joining {party.IsJoining}, failure {party.Failure ?? "none"}) never settled at {now} playing {playing ?? "nowhere"}");
    }

    // The master's party routes, as tools/master-server/src/parties.js answers them, over one party table.
    private sealed class FakeMaster : HttpMessageHandler
    {
        private readonly Dictionary<string, (List<(int Id, string Name, string Token)> Members, string? Target, int Version)> _parties = [];
        private readonly HashSet<string> _kicked = [];
        private int _codes;

        public HttpMessageInvoker Line => new(this, disposeHandler: false);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string[] parts = request.RequestUri!.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            JsonObject body = request.Content is null ? [] : JsonNode.Parse(await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)) as JsonObject ?? [];
            lock (_parties)
            {
                if (parts.Length == 2)
                {
                    string made = $"PARTY{++_codes}";
                    _parties[made] = ([], null, 0);
                    return Admit(made, body);
                }

                string code = parts[2].ToUpperInvariant();
                if (!_parties.TryGetValue(code, out var party))
                {
                    return Answer(HttpStatusCode.NotFound, new JsonObject { ["error"] = "no such party" });
                }

                string action = parts.Length > 3 ? parts[3] : "state";
                if (action == "join")
                {
                    return Admit(code, body);
                }

                System.Collections.Specialized.NameValueCollection query = System.Web.HttpUtility.ParseQueryString(request.RequestUri.Query);
                int id = action == "state" ? int.Parse(query["member"]!, System.Globalization.CultureInfo.InvariantCulture) : body["member"]!.GetValue<int>();
                string token = action == "state" ? query["token"]! : body["token"]!.GetValue<string>();
                int at = party.Members.FindIndex(m => m.Id == id && m.Token == token);
                if (at < 0)
                {
                    return Answer(HttpStatusCode.Forbidden, new JsonObject { ["error"] = _kicked.Contains(token) ? "the party's leader removed you from it" : "not in the party" });
                }

                if (action is "kick" or "promote")
                {
                    int other = party.Members.FindIndex(m => m.Id == body["target"]!.GetValue<int>());
                    if (at != 0 || other <= 0)
                    {
                        return Answer(HttpStatusCode.Forbidden, new JsonObject { ["error"] = "only the party's leader does that" });
                    }

                    var moved = party.Members[other];
                    party.Members.RemoveAt(other);
                    if (action == "kick")
                    {
                        _kicked.Add(moved.Token);
                    }
                    else
                    {
                        party.Members.Insert(0, moved);
                    }

                    return Answer(HttpStatusCode.OK, View(code));
                }

                if (action == "leave")
                {
                    party.Members.RemoveAt(at);
                    _parties[code] = party;
                    return Answer(HttpStatusCode.OK, new JsonObject { ["left"] = true });
                }

                if (action == "target")
                {
                    string? target = body["target"]?.GetValue<string>()?.ToUpperInvariant() is { } upper && !upper.StartsWith("UDP:", StringComparison.Ordinal)
                        ? upper
                        : body["target"]?.GetValue<string>();
                    if (at != 0)
                    {
                        return Answer(HttpStatusCode.Forbidden, new JsonObject { ["error"] = "only the party's leader moves it" });
                    }

                    if (target != party.Target)
                    {
                        party = (party.Members, target, party.Version + 1);
                        _parties[code] = party;
                    }
                }

                return Answer(HttpStatusCode.OK, View(code));
            }
        }

        private HttpResponseMessage Admit(string code, JsonObject body)
        {
            var party = _parties[code];
            int id = party.Members.Count == 0 ? 1 : party.Members.Max(m => m.Id) + 1;
            string token = Guid.NewGuid().ToString("N");
            party.Members.Add((id, body["name"]?.GetValue<string>() ?? $"Player {id}", token));
            JsonObject view = View(code);
            view["member"] = id;
            view["token"] = token;
            return Answer(HttpStatusCode.OK, view);
        }

        private JsonObject View(string code)
        {
            var party = _parties[code];
            JsonArray members = [];
            foreach ((int id, string name, _) in party.Members)
            {
                members.Add(new JsonObject { ["id"] = id, ["name"] = name });
            }

            return new JsonObject
            {
                ["code"] = code,
                ["leader"] = party.Members.Count > 0 ? party.Members[0].Id : 0,
                ["members"] = members,
                ["target"] = party.Target,
                ["version"] = party.Version,
            };
        }

        private static HttpResponseMessage Answer(HttpStatusCode status, JsonObject body) =>
            new(status) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
    }
}

using System.Net;
using System.Security.Cryptography;
using System.Text;
using OpenIGI.Releases.Accounts;
using Xunit;

namespace OpenIGI.Releases.Tests;

/// <summary>
/// The account client: every request about one's own account goes signed by the key the player plays under (a raw P-256
/// point, as game servers know it), checks out as the master checks it, and reads what the master answers.
/// </summary>
public sealed class AccountServiceTests
{
    private static readonly Uri Master = new("https://master.test");

    /// <summary>A link begun, asked about and seen linked, each request signed by the player's own key over its path alone.</summary>
    [Fact]
    public async Task A_link_is_asked_for_and_followed_under_the_players_key()
    {
        using PointSigner key = new();
        List<(HttpMethod Method, string Path, bool Signed)> seen = [];
        using HttpClient http = new(new Answering(async request =>
        {
            byte[] body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync();
            seen.Add((request.Method, request.RequestUri!.PathAndQuery, Checks(request, body)));
            return request.RequestUri.AbsolutePath switch
            {
                "/v1/link" => Json(HttpStatusCode.Created, """{"code":"ABCD2345","url":"https://master.test/v1/auth/discord/start?code=ABCD2345","expiresIn":900}"""),
                "/v1/link/ABCD2345" => Json(HttpStatusCode.OK, """{"state":"linked","account":{"id":"111","name":"Ana","avatar":"https://cdn/a.png","since":5}}"""),
                "/v1/account" => Json(HttpStatusCode.OK, """{"linked":true,"player":"0123456789abcdef","account":{"id":"111","name":"Ana","avatar":"https://cdn/a.png","since":5},"keys":[{"id":"0123456789abcdef","label":"Desktop","added":7,"current":true}]}"""),
                _ => Json(HttpStatusCode.NotFound, """{"error":"Not found."}"""),
            };
        }));
        AccountService accounts = new(http, Master);

        LinkStart start = await accounts.StartLinkAsync(key, "Desktop");
        Assert.Equal("ABCD2345", start.Code);
        Assert.Equal(900, start.ExpiresIn);

        LinkState state = await accounts.LinkStateAsync(key, start.Code);
        Assert.True(state.IsLinked);
        Assert.Equal("Ana", state.Account!.Name);

        OwnAccount own = await accounts.OwnAsync(key);
        Assert.True(own.Linked);
        Assert.Equal("Desktop", Assert.Single(own.Keys!).Label);

        Assert.Equal(new (HttpMethod, string, bool)[]
        {
            (HttpMethod.Post, "/v1/link", true),
            (HttpMethod.Get, "/v1/link/ABCD2345", true),
            (HttpMethod.Get, "/v1/account", true),
        }, seen);
    }

    /// <summary>A code the master doesn't know reads as unknown; any other refusal comes back with the master's reason.</summary>
    [Fact]
    public async Task Refusals_come_back_as_the_master_put_them()
    {
        using PointSigner key = new();
        using HttpClient http = new(new Answering(request => Task.FromResult(request.RequestUri!.AbsolutePath == "/v1/link"
            ? Json(HttpStatusCode.Unauthorized, """{"error":"Sign with the key you play under."}""")
            : Json(HttpStatusCode.NotFound, """{"state":"unknown"}"""))));
        AccountService accounts = new(http, Master);

        Assert.Equal("unknown", (await accounts.LinkStateAsync(key, "NOPE")).State);
        AccountRefusedException refused = await Assert.ThrowsAsync<AccountRefusedException>(() => accounts.StartLinkAsync(key, "Desktop"));
        Assert.Equal(HttpStatusCode.Unauthorized, refused.Status);
        Assert.Equal("Sign with the key you play under.", refused.Message);
    }

    /// <summary>The servers' lookup asks once for every id, unsigned, and reads the accounts it is given.</summary>
    [Fact]
    public async Task Players_are_looked_up_together()
    {
        string? asked = null;
        using HttpClient http = new(new Answering(request =>
        {
            asked = request.RequestUri!.PathAndQuery;
            Assert.False(request.Headers.Contains(SignedRequest.KeyHeader));
            return Task.FromResult(Json(HttpStatusCode.OK, """{"aaaaaaaaaaaaaaaa":{"id":"111","name":"Ana","avatar":"x","since":1}}"""));
        }));
        AccountService accounts = new(http, Master);

        IReadOnlyDictionary<string, PlayerAccount> found = await accounts.LookupAsync(["aaaaaaaaaaaaaaaa", "bbbbbbbbbbbbbbbb", "aaaaaaaaaaaaaaaa"]);
        Assert.Equal("/v1/players?ids=aaaaaaaaaaaaaaaa,bbbbbbbbbbbbbbbb", asked);
        Assert.Equal("Ana", found["aaaaaaaaaaaaaaaa"].Name);
        Assert.Empty(await accounts.LookupAsync([]));
    }

    // Whether a request checks out as signed.js checks it: the key's signature over the path alone, the body as sent.
    private static bool Checks(HttpRequestMessage request, byte[] body)
    {
        string Header(string name) => request.Headers.GetValues(name).Single();
        return SignedRequest.Verify(
            Header(SignedRequest.KeyHeader), Header(SignedRequest.SignatureHeader), request.Method.Method,
            request.RequestUri!.AbsolutePath, long.Parse(Header(SignedRequest.TimeHeader), System.Globalization.CultureInfo.InvariantCulture), body);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    // A player's key as IPlayerIdentity holds one: its public key an uncompressed point, its signatures r then s.
    private sealed class PointSigner : IRequestSigner, IDisposable
    {
        private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        public string PublicKey
        {
            get
            {
                ECParameters p = _key.ExportParameters(false);
                return Convert.ToBase64String([4, .. p.Q.X!, .. p.Q.Y!]);
            }
        }

        public Task<byte[]> SignAsync(byte[] text) =>
            Task.FromResult(_key.SignData(text, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));

        public void Dispose() => _key.Dispose();
    }

    private sealed class Answering(Func<HttpRequestMessage, Task<HttpResponseMessage>> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => answer(request);
    }
}

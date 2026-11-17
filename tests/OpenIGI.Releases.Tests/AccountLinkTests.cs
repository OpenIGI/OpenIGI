using System.Net;
using System.Security.Cryptography;
using System.Text;
using OpenIGI.Releases.Accounts;
using Xunit;

namespace OpenIGI.Releases.Tests;

/// <summary>
/// The account as the profile page drives it, frame by frame: asked of the master first, then linked through Discord (the
/// browser opened once, the link asked about only every few seconds until approved), unlinked; offline kept and retried,
/// and a link that is never approved given up.
/// </summary>
public sealed class AccountLinkTests
{
    private const string Ana = """{"id":"111","name":"Ana","avatar":"https://cdn/a.png","since":5}""";

    /// <summary>Unlinked at first; LINK opens the browser once; the link is asked about every two seconds until it is approved.</summary>
    [Fact]
    public async Task A_link_is_opened_once_and_followed_until_approved()
    {
        Master master = new();
        List<string> opened = [];
        using PointSigner key = new();
        DateTimeOffset now = DateTimeOffset.UnixEpoch;
        AccountLink link = new(new AccountService(master.Client, new Uri("https://master.test")), () => key, opened.Add, "Desktop", () => now);
        List<string?> changes = [];
        link.AccountChanged += account => changes.Add(account?.Name);

        Assert.Equal(AccountLinkState.Checking, link.State);
        await Settle(link);
        Assert.Equal(AccountLinkState.Unlinked, link.State);

        link.Link();
        Assert.Equal(AccountLinkState.Starting, link.State);
        await Settle(link);
        Assert.Equal(AccountLinkState.Waiting, link.State);
        Assert.Equal(["https://master.test/v1/auth/discord/start?code=ABCD2345"], opened);
        Assert.Equal(opened[0], link.LinkUrl);

        // Not asked again before two seconds have passed.
        await Settle(link);
        Assert.Equal(0, master.Polls);
        now += AccountLink.PollEvery;
        await Settle(link);
        Assert.Equal(1, master.Polls);
        Assert.Equal(AccountLinkState.Waiting, link.State);

        master.Approved = true;
        now += AccountLink.PollEvery;
        await Settle(link);
        Assert.Equal(AccountLinkState.Linked, link.State);
        Assert.Equal("Ana", link.Account!.Name);
        Assert.Null(link.LinkUrl);
        Assert.Single(opened);

        link.Unlink();
        await Settle(link);
        Assert.Equal(AccountLinkState.Unlinked, link.State);
        Assert.Null(link.Account);
        Assert.Equal([null, "Ana", null], changes);
    }

    /// <summary>The master unreachable: the account remembered is kept, said to be offline, and asked again after a while.</summary>
    [Fact]
    public async Task Offline_keeps_the_account_known_and_tries_again()
    {
        Master master = new() { Down = true, Linked = true };
        using PointSigner key = new();
        DateTimeOffset now = DateTimeOffset.UnixEpoch;
        AccountLink link = new(new AccountService(master.Client, new Uri("https://master.test")), () => key, null, "Desktop", () => now);
        link.Remember(new PlayerAccount("111", "Ana", "x", 1));

        await Settle(link);
        Assert.Equal(AccountLinkState.Offline, link.State);
        Assert.Equal("Ana", link.Account!.Name);
        Assert.NotNull(link.Error);

        master.Down = false;
        await Settle(link);
        Assert.Equal(AccountLinkState.Offline, link.State);
        now += AccountLink.RetryOfflineEvery;
        await Settle(link);
        Assert.Equal(AccountLinkState.Linked, link.State);
        Assert.Null(link.Error);
    }

    /// <summary>A link never approved is given up after its time, and the player told; one cancelled simply stops.</summary>
    [Fact]
    public async Task A_link_never_approved_is_given_up()
    {
        Master master = new();
        using PointSigner key = new();
        DateTimeOffset now = DateTimeOffset.UnixEpoch;
        AccountLink link = new(new AccountService(master.Client, new Uri("https://master.test")), () => key, null, "Desktop", () => now);
        await Settle(link);

        link.Link();
        await Settle(link);
        now += AccountLink.GiveUpAfter;
        await Settle(link);
        Assert.Equal(AccountLinkState.Unlinked, link.State);
        Assert.Contains("in time", link.Error, StringComparison.Ordinal);

        link.Link();
        await Settle(link);
        Assert.Equal(AccountLinkState.Waiting, link.State);
        link.CancelLink();
        Assert.Equal(AccountLinkState.Unlinked, link.State);
        Assert.Null(link.LinkUrl);
    }

    /// <summary>Nothing is asked while the player's key isn't loaded; it is asked as soon as it is.</summary>
    [Fact]
    public async Task Nothing_is_asked_until_the_key_is_loaded()
    {
        Master master = new() { Linked = true };
        using PointSigner key = new();
        PointSigner? loaded = null;
        AccountLink link = new(new AccountService(master.Client, new Uri("https://master.test")), () => loaded, null, "Browser");

        await Settle(link);
        link.Link();
        Assert.Equal(AccountLinkState.Checking, link.State);
        Assert.Equal(0, master.Requests);

        loaded = key;
        await Settle(link);
        Assert.Equal(AccountLinkState.Linked, link.State);
    }

    // Runs frames until nothing is on its way.
    private static async Task Settle(AccountLink link)
    {
        for (int frame = 0; frame < 50; frame++)
        {
            link.Update();
            if (!link.IsBusy)
            {
                return;
            }

            await Task.Delay(1);
        }

        Assert.Fail("A request never came back.");
    }

    // The master's accounts, as far as these tests need them.
    private sealed class Master
    {
        public Master() => Client = new HttpClient(new Answering(Answer));

        public HttpClient Client { get; }

        public bool Down { get; set; }

        public bool Linked { get; set; }

        public bool Approved { get; set; }

        public int Polls { get; private set; }

        public int Requests { get; private set; }

        private Task<HttpResponseMessage> Answer(HttpRequestMessage request)
        {
            Requests++;
            if (Down)
            {
                throw new HttpRequestException("down");
            }

            string path = request.RequestUri!.AbsolutePath;
            (HttpStatusCode status, string body) = (request.Method.Method, path) switch
            {
                ("GET", "/v1/account") => (HttpStatusCode.OK, Linked ? $$"""{"linked":true,"player":"p","account":{{Ana}},"keys":[]}""" : """{"linked":false,"player":"p"}"""),
                ("POST", "/v1/link") => (HttpStatusCode.Created, """{"code":"ABCD2345","url":"https://master.test/v1/auth/discord/start?code=ABCD2345","expiresIn":900}"""),
                ("GET", "/v1/link/ABCD2345") => Poll(),
                ("DELETE", "/v1/link") => (HttpStatusCode.OK, """{"unlinked":true}"""),
                _ => (HttpStatusCode.NotFound, """{"error":"Not found."}"""),
            };
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }

        private (HttpStatusCode, string) Poll()
        {
            Polls++;
            return (HttpStatusCode.OK, Approved ? $$"""{"state":"linked","account":{{Ana}}}""" : """{"state":"pending"}""");
        }
    }

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

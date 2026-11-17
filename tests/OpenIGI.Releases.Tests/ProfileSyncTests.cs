using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using OpenIGI.Releases.Accounts;
using Xunit;

namespace OpenIGI.Releases.Tests;

/// <summary>
/// The profile kept in step with the account: written up when the account has none, merged when it has, a change here
/// written a few seconds later, a change made offline counted as newer, and a second computer brought up to date.
/// </summary>
public sealed class ProfileSyncTests
{
    /// <summary>The account's first profile is this computer's; a change later goes up a few seconds after it is kept.</summary>
    [Fact]
    public async Task The_first_profile_goes_up_and_changes_follow_it()
    {
        Master master = new();
        using PointSigner key = new();
        DateTimeOffset now = DateTimeOffset.UnixEpoch;
        Profile desk = new() { Mission = 3, Difficulty = 1 };
        ProfileSync sync = new(new AccountService(master.Client, new Uri("https://master.test")), () => key, desk, null, () => now);

        sync.SignedIn("111");
        await Settle(sync);
        await Settle(sync);
        Assert.Equal(ProfileSyncState.Synced, sync.State);
        Assert.Equal(3, master.Kept!.Value.GetProperty("mission").GetInt32());

        desk.Difficulty = 2;
        sync.Changed();
        await Settle(sync);
        Assert.Equal(1, master.Kept!.Value.GetProperty("difficulty").GetInt32());
        now += ProfileSync.WriteAfter;
        await Settle(sync);
        Assert.Equal(2, master.Kept!.Value.GetProperty("difficulty").GetInt32());

        // Kept unchanged: nothing goes up.
        int writes = master.Writes;
        sync.Changed();
        now += ProfileSync.WriteAfter;
        await Settle(sync);
        Assert.Equal(writes, master.Writes);
    }

    /// <summary>A second computer takes the account's settings and the furthest progress, and puts the merge back up.</summary>
    [Fact]
    public async Task A_second_computer_catches_up_and_gives_its_progress_back()
    {
        Master master = new() { Kept = Profile.Document(mission: 9, difficulty: 2) };
        using PointSigner key = new();
        Profile browser = new() { Mission = 4, Difficulty = 0, Other = 5 };
        ProfileSync sync = new(new AccountService(master.Client, new Uri("https://master.test")), () => key, browser, null);

        sync.SignedIn("111");
        await Settle(sync);
        await Settle(sync);

        Assert.Equal(9, browser.Mission);
        Assert.Equal(2, browser.Difficulty);
        Assert.Equal(5, master.Kept!.Value.GetProperty("other").GetInt32());
        Assert.Equal(ProfileSyncState.Synced, sync.State);
    }

    /// <summary>A change made here since the last sync is newer than the account's copy; one made before ever syncing is not.</summary>
    [Fact]
    public async Task A_change_made_offline_wins_over_the_accounts_settings()
    {
        string memory = Path.Combine(Path.GetTempPath(), "openigi-sync-tests", Guid.NewGuid().ToString("N"), "sync.json");
        try
        {
            Master master = new();
            using PointSigner key = new();
            Profile desk = new() { Mission = 3, Difficulty = 1 };
            ProfileSync first = new(new AccountService(master.Client, new Uri("https://master.test")), () => key, desk, memory);
            first.SignedIn("111");
            await Settle(first);
            await Settle(first);

            // Offline, the player turns the difficulty up; meanwhile another computer finished a mission.
            desk.Difficulty = 2;
            master.Kept = Profile.Document(mission: 4, difficulty: 1);

            ProfileSync next = new(new AccountService(master.Client, new Uri("https://master.test")), () => key, desk, memory);
            Assert.True(next.WasSignedIn);
            next.SignedIn("111");
            await Settle(next);
            await Settle(next);

            Assert.Equal(2, desk.Difficulty);
            Assert.Equal(4, desk.Mission);
            Assert.Equal(2, master.Kept!.Value.GetProperty("difficulty").GetInt32());

            next.SignedOut();
            Assert.False(new ProfileSync(new AccountService(master.Client, new Uri("https://master.test")), () => key, desk, memory).WasSignedIn);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(Path.GetDirectoryName(memory))!, recursive: true);
        }
    }

    /// <summary>The master down: it says so, and reads again a minute later.</summary>
    [Fact]
    public async Task A_failed_read_is_tried_again()
    {
        Master master = new() { Down = true };
        using PointSigner key = new();
        DateTimeOffset now = DateTimeOffset.UnixEpoch;
        Profile desk = new() { Mission = 3 };
        ProfileSync sync = new(new AccountService(master.Client, new Uri("https://master.test")), () => key, desk, null, () => now);

        sync.SignedIn("111");
        await Settle(sync);
        Assert.Equal(ProfileSyncState.Failed, sync.State);

        master.Down = false;
        await Settle(sync);
        Assert.Equal(ProfileSyncState.Failed, sync.State);
        now += ProfileSync.RetryAfter;
        await Settle(sync);
        await Settle(sync);
        Assert.Equal(ProfileSyncState.Synced, sync.State);
    }

    private static async Task Settle(ProfileSync sync)
    {
        for (int frame = 0; frame < 50; frame++)
        {
            sync.Update();
            if (!sync.IsBusy)
            {
                return;
            }

            await Task.Delay(1);
        }

        Assert.Fail("A request never came back.");
    }

    // A profile of three numbers: the furthest mission (merged to the furthest), another game's, and a setting.
    private sealed class Profile : ISyncedProfile
    {
        public int Mission { get; set; }

        public int Other { get; set; }

        public int Difficulty { get; set; }

        public static JsonElement Document(int mission, int difficulty, int other = 0) =>
            JsonSerializer.SerializeToElement(new Dictionary<string, int> { ["mission"] = mission, ["other"] = other, ["difficulty"] = difficulty });

        public string? Fingerprint() => $"{Mission}/{Other}/{Difficulty}";

        public JsonElement? Take() => Document(Mission, Difficulty, Other);

        public JsonElement? Adopt(JsonElement remote, bool localIsNewer)
        {
            Mission = Math.Max(Mission, remote.GetProperty("mission").GetInt32());
            Other = Math.Max(Other, remote.GetProperty("other").GetInt32());
            if (!localIsNewer)
            {
                Difficulty = remote.GetProperty("difficulty").GetInt32();
            }

            return Take();
        }
    }

    private sealed class Master
    {
        public Master() => Client = new HttpClient(new Answering(Answer));

        public HttpClient Client { get; }

        public JsonElement? Kept { get; set; }

        public bool Down { get; set; }

        public int Writes { get; private set; }

        private async Task<HttpResponseMessage> Answer(HttpRequestMessage request)
        {
            if (Down)
            {
                throw new HttpRequestException("down");
            }

            if (request.Method == HttpMethod.Put)
            {
                Writes++;
                using JsonDocument body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                Kept = body.RootElement.GetProperty("data").Clone();
                return Json("""{"updated":5}""");
            }

            return Json(Kept is { } kept ? $$"""{"data":{{kept.GetRawText()}},"updated":4}""" : """{"data":null,"updated":0}""");
        }

        private static HttpResponseMessage Json(string json) =>
            new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
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

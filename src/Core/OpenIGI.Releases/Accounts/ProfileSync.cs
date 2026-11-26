using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenIGI.Releases.Accounts;

/// <summary>The player's profile as the account keeps it in step: taken, fingerprinted, and merged with the account's.</summary>
public interface ISyncedProfile
{
    /// <summary>Gets a fingerprint of what of the profile is kept in step, to tell whether it changed since it last was.</summary>
    /// <returns>The fingerprint, or <see langword="null"/> while there is no profile.</returns>
    string? Fingerprint();

    /// <summary>Takes what of the profile is kept in step.</summary>
    /// <returns>Its document, or <see langword="null"/> while there is no profile.</returns>
    JsonElement? Take();

    /// <summary>Merges the account's profile into this one, keeps the result, and says what it came to.</summary>
    /// <param name="remote">The account's document.</param>
    /// <param name="localIsNewer">Whether this computer's profile changed since the two were last in step.</param>
    /// <returns>The merged document, or <see langword="null"/> for a document this build can't read.</returns>
    JsonElement? Adopt(JsonElement remote, bool localIsNewer);
}

/// <summary>How the profile stands with the account.</summary>
public enum ProfileSyncState
{
    /// <summary>Not signed in, or not yet asked.</summary>
    Idle,

    /// <summary>Reading or writing.</summary>
    Syncing,

    /// <summary>In step with the account.</summary>
    Synced,

    /// <summary>The last attempt failed; it is tried again.</summary>
    Failed,
}

/// <summary>
/// Keeps the player's profile in step with their account. Signed in, the account's copy is read and merged with this
/// computer's (the furthest progress in each game; the settings of whichever changed last), and the result kept on both;
/// after that, every change here is written up a few seconds later. What was last in step is remembered on this computer,
/// so a change made offline is told from one made elsewhere. Frame by frame like <see cref="AccountLink"/>.
/// </summary>
public sealed class ProfileSync
{
    /// <summary>How long after a change it is written, so a run of changes goes up as one.</summary>
    public static readonly TimeSpan WriteAfter = TimeSpan.FromSeconds(3);

    /// <summary>How long after a failure it is tried again.</summary>
    public static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(60);

    private readonly AccountService _service;
    private readonly Func<IRequestSigner?> _signer;
    private readonly ISyncedProfile _profile;
    private readonly string? _memoryPath;
    private readonly Func<DateTimeOffset> _clock;

    private SyncMemory _memory;
    private string? _account;
    private Task? _pending;
    private Action<Task>? _collect;
    private bool _readDone;
    private DateTimeOffset? _writeAt;
    private DateTimeOffset? _retryAt;

    /// <summary>Initializes a new instance of the <see cref="ProfileSync"/> class.</summary>
    /// <param name="service">The master's accounts.</param>
    /// <param name="signer">The key the player plays under, or <see langword="null"/> while it isn't loaded.</param>
    /// <param name="profile">The profile.</param>
    /// <param name="memoryPath">The file what was last in step is remembered in, or <see langword="null"/> to remember nothing.</param>
    /// <param name="clock">The time; <see cref="DateTimeOffset.UtcNow"/> unless a test says otherwise.</param>
    public ProfileSync(AccountService service, Func<IRequestSigner?> signer, ISyncedProfile profile, string? memoryPath, Func<DateTimeOffset>? clock = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(signer);
        ArgumentNullException.ThrowIfNull(profile);
        _service = service;
        _signer = signer;
        _profile = profile;
        _memoryPath = memoryPath;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _memory = Recall(memoryPath);
    }

    /// <summary>Gets how the profile stands with the account.</summary>
    public ProfileSyncState State { get; private set; } = ProfileSyncState.Idle;

    /// <summary>Gets when it was last in step, or <see langword="null"/>.</summary>
    public DateTimeOffset? SyncedAt { get; private set; }

    /// <summary>Gets whether this computer was signed in when the game last ran: worth asking the master at start.</summary>
    public bool WasSignedIn => _memory.Account is not null;

    /// <summary>Gets whether a request is on its way.</summary>
    public bool IsBusy => _pending is not null;

    /// <summary>The player signed in (or was found signed in): the account's profile is read and merged.</summary>
    /// <param name="accountId">The account.</param>
    public void SignedIn(string accountId)
    {
        ArgumentException.ThrowIfNullOrEmpty(accountId);
        if (_account == accountId)
        {
            return;
        }

        _account = accountId;
        _readDone = false;
        _writeAt = null;
        _retryAt = _clock();
    }

    /// <summary>The player signed out: nothing more is written, and this computer forgets it was in step.</summary>
    public void SignedOut()
    {
        _account = null;
        _readDone = false;
        _writeAt = null;
        _retryAt = null;
        State = ProfileSyncState.Idle;
        Remember(new SyncMemory(null, null));
    }

    /// <summary>The profile was kept: written up shortly, when it changed.</summary>
    public void Changed()
    {
        if (_account is not null && _readDone && _profile.Fingerprint() is { } now && now != _memory.Fingerprint)
        {
            _writeAt = _clock() + WriteAfter;
        }
    }

    /// <summary>Collects what came back, and reads or writes when it is time. Called every frame.</summary>
    public void Update()
    {
        if (_pending is { IsCompleted: true } finished)
        {
            Action<Task>? collect = _collect;
            _pending = null;
            _collect = null;
            collect?.Invoke(finished);
        }

        if (_pending is not null || _account is not { } account || _signer() is not { } key)
        {
            return;
        }

        DateTimeOffset now = _clock();
        if (!_readDone)
        {
            if (_retryAt is { } retry && now >= retry)
            {
                _retryAt = null;
                Read(key, account);
            }

            return;
        }

        if (_writeAt is { } at && now >= at)
        {
            _writeAt = null;
            Write(key, account);
        }
    }

    private void Read(IRequestSigner key, string account)
    {
        State = ProfileSyncState.Syncing;
        Run(_service.GetProfileAsync(key), task =>
        {
            if (task is not Task<RemoteProfile> { IsCompletedSuccessfully: true } done || _account != account)
            {
                Failed();
                return;
            }

            _readDone = true;
            if (done.Result.Data is not { } remote)
            {
                // Nothing kept yet: this computer's profile is the account's first.
                _writeAt = _clock();
                return;
            }

            // A change here since the two were last in step is newer than the account's copy; one made before this
            // computer ever kept step with this account is not.
            string? local = _profile.Fingerprint();
            bool localIsNewer = _memory.Account == account && local is not null && local != _memory.Fingerprint;
            if (_profile.Adopt(remote, localIsNewer) is not { } merged)
            {
                State = ProfileSyncState.Failed;
                return;
            }

            if (JsonElement.DeepEquals(merged, remote))
            {
                InStep(account);
            }
            else
            {
                _writeAt = _clock();
            }
        });
    }

    private void Write(IRequestSigner key, string account)
    {
        if (_profile.Take() is not { } document)
        {
            return;
        }

        State = ProfileSyncState.Syncing;
        Run(_service.PutProfileAsync(key, document), task =>
        {
            if (!task.IsCompletedSuccessfully || _account != account)
            {
                Failed();
                return;
            }

            InStep(account);
        });
    }

    private void InStep(string account)
    {
        State = ProfileSyncState.Synced;
        SyncedAt = _clock();
        Remember(new SyncMemory(account, _profile.Fingerprint()));
        Changed();
    }

    private void Failed()
    {
        State = ProfileSyncState.Failed;
        if (_readDone)
        {
            _writeAt = _clock() + RetryAfter;
        }
        else
        {
            _retryAt = _clock() + RetryAfter;
        }
    }

    private void Run(Task task, Action<Task> collect)
    {
        _pending = task;
        _collect = collect;
    }

    private void Remember(SyncMemory memory)
    {
        _memory = memory;
        if (_memoryPath is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_memoryPath)) ?? ".");
            File.WriteAllText(_memoryPath, JsonSerializer.Serialize(memory, SyncMemoryJson.Default.SyncMemory));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Not kept: the next start merges as for a computer new to the account, which loses nothing.
        }
    }

    private static SyncMemory Recall(string? path)
    {
        try
        {
            return path is not null && File.Exists(path)
                ? JsonSerializer.Deserialize(File.ReadAllText(path), SyncMemoryJson.Default.SyncMemory) ?? new SyncMemory(null, null)
                : new SyncMemory(null, null);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return new SyncMemory(null, null);
        }
    }
}

/// <summary>What this computer last kept in step: with which account, and the profile's fingerprint then.</summary>
/// <param name="Account">The account, or <see langword="null"/> for none.</param>
/// <param name="Fingerprint">The profile's fingerprint.</param>
internal sealed record SyncMemory(string? Account, string? Fingerprint);

/// <summary>The JSON of <see cref="SyncMemory"/>, generated ahead of time.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SyncMemory))]
internal sealed partial class SyncMemoryJson : JsonSerializerContext;

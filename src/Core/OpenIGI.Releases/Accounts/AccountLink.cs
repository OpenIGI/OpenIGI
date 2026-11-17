namespace OpenIGI.Releases.Accounts;

/// <summary>Where an install stands with its account.</summary>
public enum AccountLinkState
{
    /// <summary>Not yet asked: what the master says is on its way.</summary>
    Checking,

    /// <summary>Playing as the key alone; no account is linked.</summary>
    Unlinked,

    /// <summary>A link is being asked for.</summary>
    Starting,

    /// <summary>The player is approving it with Discord, in their browser.</summary>
    Waiting,

    /// <summary>Linked to an account.</summary>
    Linked,

    /// <summary>The master can't be reached; the last account known is still shown.</summary>
    Offline,
}

/// <summary>
/// An install's account, kept up to date frame by frame: asked of the master once, linked through Discord on request (the
/// browser opened on the master's page and the link asked about every few seconds until it is approved), unlinked or
/// deleted. Every request runs on its own and is collected by <see cref="Update"/>, so it suits a game loop and a browser
/// alike; nothing here blocks or runs on another thread.
/// </summary>
public sealed class AccountLink
{
    /// <summary>How often a link waiting on the player is asked about.</summary>
    public static readonly TimeSpan PollEvery = TimeSpan.FromSeconds(2);

    /// <summary>How long after the master couldn't be reached it is asked again.</summary>
    public static readonly TimeSpan RetryOfflineEvery = TimeSpan.FromSeconds(30);

    /// <summary>How long a link is waited on before it is given up.</summary>
    public static readonly TimeSpan GiveUpAfter = TimeSpan.FromMinutes(15);

    private readonly AccountService _service;
    private readonly Func<IRequestSigner?> _signer;
    private readonly Action<string>? _openLink;
    private readonly Func<DateTimeOffset> _clock;
    private readonly string _label;

    private Task? _pending;
    private Action<Task>? _collect;
    private DateTimeOffset _nextPoll;
    private DateTimeOffset _gaveUpAt;
    private DateTimeOffset _retryAt;
    private string? _code;

    /// <summary>Initializes a new instance of the <see cref="AccountLink"/> class.</summary>
    /// <param name="service">The master's accounts.</param>
    /// <param name="signer">The key the player plays under, or <see langword="null"/> while it isn't loaded.</param>
    /// <param name="openLink">Opens a page in the player's browser, or <see langword="null"/> where nothing can (the page is then only shown).</param>
    /// <param name="label">What this install calls itself on the account, such as "Desktop" or "Browser".</param>
    /// <param name="clock">The time; <see cref="DateTimeOffset.UtcNow"/> unless a test says otherwise.</param>
    public AccountLink(AccountService service, Func<IRequestSigner?> signer, Action<string>? openLink, string label, Func<DateTimeOffset>? clock = null)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(signer);
        _service = service;
        _signer = signer;
        _openLink = openLink;
        _label = label;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>Raised when the account changes: linked, unlinked, deleted, or first learned.</summary>
    public event Action<PlayerAccount?>? AccountChanged;

    /// <summary>Gets where the install stands.</summary>
    public AccountLinkState State { get; private set; } = AccountLinkState.Checking;

    /// <summary>Gets the account linked, or the last one known while offline.</summary>
    public PlayerAccount? Account { get; private set; }

    /// <summary>Gets the page the player approves the link on, while one is waiting.</summary>
    public string? LinkUrl { get; private set; }

    /// <summary>Gets what went wrong last, in words for the player, or <see langword="null"/>.</summary>
    public string? Error { get; private set; }

    /// <summary>Gets whether a request is on its way.</summary>
    public bool IsBusy => _pending is not null;

    /// <summary>Remembers an account known from before (the profile's own copy), shown until the master answers.</summary>
    /// <param name="account">The account.</param>
    public void Remember(PlayerAccount? account) => Account ??= account;

    /// <summary>Asks the master which account the key is linked to.</summary>
    public void Refresh()
    {
        if (_pending is not null || _signer() is not { } key)
        {
            return;
        }

        Run(_service.OwnAsync(key), task =>
        {
            if (task is Task<OwnAccount> { IsCompletedSuccessfully: true } done)
            {
                Error = null;
                SetAccount(done.Result.Linked ? done.Result.Account : null);
                State = Account is null ? AccountLinkState.Unlinked : AccountLinkState.Linked;
            }
            else
            {
                Fail(task, offline: true);
            }
        });
    }

    /// <summary>Begins linking: a link asked for, then the browser opened on its page.</summary>
    public void Link()
    {
        if (_pending is not null || State is AccountLinkState.Waiting || _signer() is not { } key)
        {
            return;
        }

        State = AccountLinkState.Starting;
        Error = null;
        Run(_service.StartLinkAsync(key, _label), task =>
        {
            if (task is not Task<LinkStart> { IsCompletedSuccessfully: true } done)
            {
                Fail(task, offline: false);
                return;
            }

            _code = done.Result.Code;
            LinkUrl = done.Result.Url;
            State = AccountLinkState.Waiting;
            _nextPoll = _clock() + PollEvery;
            _gaveUpAt = _clock() + GiveUpAfter;
            _openLink?.Invoke(done.Result.Url);
        });
    }

    /// <summary>Stops waiting on a link; the code simply lapses on the master.</summary>
    public void CancelLink()
    {
        if (State != AccountLinkState.Waiting && State != AccountLinkState.Starting)
        {
            return;
        }

        StopWaiting();
        State = Account is null ? AccountLinkState.Unlinked : AccountLinkState.Linked;
    }

    /// <summary>Takes this install off its account; the account keeps its other installs.</summary>
    public void Unlink()
    {
        if (_pending is not null || _signer() is not { } key)
        {
            return;
        }

        Run(_service.UnlinkAsync(key), task => Forgotten(task));
    }

    /// <summary>Deletes the whole account and everything the master keeps of it.</summary>
    public void DeleteAccount()
    {
        if (_pending is not null || _signer() is not { } key)
        {
            return;
        }

        Run(_service.DeleteAccountAsync(key), task => Forgotten(task));
    }

    /// <summary>Collects what has come back, and asks about a waiting link when it is time. Called every frame.</summary>
    public void Update()
    {
        if (_pending is { IsCompleted: true } finished)
        {
            Action<Task>? collect = _collect;
            _pending = null;
            _collect = null;
            collect?.Invoke(finished);
        }

        DateTimeOffset now = _clock();

        // Not yet asked (the key may have been loading), or offline a while: ask again.
        if (_pending is null && (State == AccountLinkState.Checking || (State == AccountLinkState.Offline && now >= _retryAt)))
        {
            Refresh();
            return;
        }

        if (_pending is not null || State != AccountLinkState.Waiting || _code is null)
        {
            return;
        }

        if (now >= _gaveUpAt)
        {
            StopWaiting();
            State = Account is null ? AccountLinkState.Unlinked : AccountLinkState.Linked;
            Error = "The link wasn't approved in time. Try again.";
            return;
        }

        if (now < _nextPoll || _signer() is not { } key)
        {
            return;
        }

        _nextPoll = now + PollEvery;
        Run(_service.LinkStateAsync(key, _code), task =>
        {
            if (task is not Task<LinkState> { IsCompletedSuccessfully: true } done)
            {
                // A missed answer is asked again at the next poll; the player is still in their browser.
                return;
            }

            if (done.Result.IsLinked)
            {
                StopWaiting();
                Error = null;
                SetAccount(done.Result.Account);
                State = AccountLinkState.Linked;
            }
            else if (!done.Result.IsPending)
            {
                StopWaiting();
                State = Account is null ? AccountLinkState.Unlinked : AccountLinkState.Linked;
                Error = "The link expired. Try again.";
            }
        });
    }

    private void Forgotten(Task task)
    {
        if (!task.IsCompletedSuccessfully)
        {
            Fail(task, offline: false);
            return;
        }

        Error = null;
        SetAccount(null);
        State = AccountLinkState.Unlinked;
    }

    private void StopWaiting()
    {
        _code = null;
        LinkUrl = null;
    }

    private void SetAccount(PlayerAccount? account)
    {
        if (Equals(account, Account) && State != AccountLinkState.Checking)
        {
            return;
        }

        Account = account;
        AccountChanged?.Invoke(account);
    }

    private void Fail(Task task, bool offline)
    {
        Exception? error = task.Exception?.GetBaseException();
        if (State is AccountLinkState.Starting)
        {
            State = Account is null ? AccountLinkState.Unlinked : AccountLinkState.Linked;
        }

        if (offline || error is HttpRequestException or TaskCanceledException)
        {
            State = AccountLinkState.Offline;
            _retryAt = _clock() + RetryOfflineEvery;
            Error = "Can't reach the OpenIGI servers right now.";
            return;
        }

        Error = error is AccountRefusedException refused ? refused.Message : "Something went wrong. Try again.";
    }

    private void Run(Task task, Action<Task> collect)
    {
        _pending = task;
        _collect = collect;
    }
}

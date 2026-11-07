using OpenIGI.Core.Diagnostics;

namespace OpenIGI.Launcher;

/// <content>
/// Invites: the launcher opened by an <c>openigi://</c> link from an invite page starts the game into the friend's room or
/// party, as soon as there is a build and a game folder to start it from.
/// </content>
internal sealed partial class LauncherModel
{
    // How long an invite waits for the launcher to be able to start the game, in seconds, before it is given up.
    private const float InviteWaitSeconds = 60f;

    private IReadOnlyList<string>? _invite;
    private float _inviteWaited;

    /// <summary>Takes the invite the launcher was opened with, to start the game into once it can.</summary>
    /// <param name="arguments">What the game is started with, as <see cref="Services.InviteLinks.Arguments"/> reads a link.</param>
    public void TakeInvite(IReadOnlyList<string>? arguments)
    {
        _invite = arguments;
        _inviteWaited = 0f;
        if (arguments is not null)
        {
            _logger.Information(GameLogCategory, $"Opened by an invite: {string.Join(' ', arguments)}.");
        }
    }

    // Each frame: an invite waiting started, once a build is installed, the game's folder is known and no game runs.
    private void TickInvite(float elapsedSeconds)
    {
        if (_invite is not { } invite)
        {
            return;
        }

        if (_installed is not null && Current.Retail is { } title && Services.GameStarter.Locate(title, Settings.GameFolders.GetValueOrDefault(title.Id)) is not null && _running is null)
        {
            _invite = null;
            Play(Current, invite);
            return;
        }

        _inviteWaited += elapsedSeconds;
        if (_inviteWaited >= InviteWaitSeconds)
        {
            _invite = null;
            Report(_installed is null
                ? "The invite needs OpenIGI installed: install it, then open the invite again."
                : "The invite needs the game's folder: choose it, then open the invite again.");
        }
    }
}

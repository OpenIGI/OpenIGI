using OpenIGI.Core.Diagnostics;

namespace OpenIGI.Launcher;

/// <content>
/// Invites: the launcher opened by an <c>openigi://</c> link -- an invite page's, or a session's on the website -- starts the
/// link's game into the room, the party or the server, as soon as there is a build and that game's folder to start it from.
/// </content>
internal sealed partial class LauncherModel
{
    // How long an invite waits for the launcher to be able to start the game, in seconds, before it is given up.
    private const float InviteWaitSeconds = 60f;

    private Services.Invite? _invite;
    private float _inviteWaited;

    /// <summary>Takes an invite -- the one the launcher was opened with, or one another launcher passed on -- to start its game into once it can.</summary>
    /// <param name="invite">The game and what it is started with, as <see cref="Services.InviteLinks.Read"/> reads a link.</param>
    public void TakeInvite(Services.Invite? invite)
    {
        _invite = invite;
        _inviteWaited = 0f;
        if (invite is not null)
        {
            _logger.Information(GameLogCategory, $"Opened by an invite to {invite.Game}: {string.Join(' ', invite.Arguments)}.");
        }
    }

    // Each frame: an invite waiting started, once a build is installed, the game's folder is known and no game runs.
    private void TickInvite(float elapsedSeconds)
    {
        if (_invite is not { } invite)
        {
            return;
        }

        // The invite's own game, chosen on the launcher so its page is the one shown; never whichever game was showing.
        int index = Games.ToList().FindIndex(game => game.Id == invite.Game);
        LauncherGame wanted = Games[Math.Max(0, index)];
        if (_installed is not null && wanted.Retail is { } title && Services.GameStarter.Locate(title, Settings.GameFolders.GetValueOrDefault(title.Id)) is not null && _running is null)
        {
            _invite = null;
            Selected = Math.Max(0, index);
            Play(wanted, invite.Arguments);
            return;
        }

        _inviteWaited += elapsedSeconds;
        if (_inviteWaited >= InviteWaitSeconds)
        {
            _invite = null;
            Report(_installed is null
                ? "The invite needs OpenIGI installed: install it, then open the invite again."
                : $"The invite needs {wanted.Title}'s folder: choose it, then open the invite again.");
        }
    }
}

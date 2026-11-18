using OpenIGI.Core.Numerics;
using OpenIGI.Game.Ai;
using OpenIGI.Rendering;

namespace OpenIGI.Host;

/// <summary>
/// A teammate going down, told the moment it happens: a short sound and a line at the top of the screen -- who, and how far --
/// so the player looks for the revive marker rather than finding the body later.
/// </summary>
public sealed partial class GameHost
{
    // How long the line stays up, in milliseconds; it fades over its last part.
    private const int DownedAlertMilliseconds = 4_000;

    // The teammates down last frame, by name, so only a new fall is told; and the lines up now.
    private readonly HashSet<string> _downSeen = new(StringComparer.Ordinal);
    private readonly List<(string Name, Vector3d Metres, int At)> _downAlerts = [];

    // How long this player's spawn protection lasted when it began, which its bar runs down from.
    private float _shieldTotal;

    // Each frame: a teammate newly down is told once, with the sound; one back up is forgotten, so a second fall is told again.
    private void UpdateDownedAlerts(IReadOnlyList<ReviveMarker> fallen)
    {
        int now = _loop.TimeSource.Milliseconds;
        HashSet<string> down = new(fallen.Select(static f => f.Name), StringComparer.Ordinal);
        foreach (ReviveMarker mark in fallen)
        {
            if (_downSeen.Add(mark.Name))
            {
                _downAlerts.RemoveAll(a => a.Name == mark.Name);
                _downAlerts.Add((mark.Name, mark.AnchorMetres, now));
                if (HudCVars.DownedSound.Value is { Length: > 0 } sound)
                {
                    _audioOut.Sounds?.PlayDefinition(sound, priority: 200);
                }
            }
        }

        _downSeen.RemoveWhere(name => !down.Contains(name));
        _downAlerts.RemoveAll(a => now - a.At > DownedAlertMilliseconds || !down.Contains(a.Name));
    }

    // The lines up now, newest first: who, how far from the eye, and how much of each is left to see.
    private IEnumerable<(string Name, double Metres, float Opacity)> DownedAlertsShown(Vector3d eye)
    {
        int now = _loop.TimeSource.Milliseconds;
        for (int i = _downAlerts.Count - 1; i >= 0; i--)
        {
            (string name, Vector3d metres, int at) = _downAlerts[i];
            Vector3d to = metres - eye;
            yield return (name, Math.Sqrt((to.X * to.X) + (to.Y * to.Y) + (to.Z * to.Z)), Math.Clamp((DownedAlertMilliseconds - (now - at)) / 800f, 0f, 1f));
        }
    }
}

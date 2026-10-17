using OpenIGI.Core.Configuration;

namespace OpenIGI.Host;

/// <summary>What the game does when a setting changes, and taking that back when the game ends.</summary>
public sealed partial class GameHost
{
    // Every handler this game put on a setting. The settings belong to the process, not to the game, so a
    // handler left on one outlives it: after F10 built a second game, changing the preset still ran the
    // first one's, which called its disposed renderer and brought the process down.
    private readonly List<(ICVar Variable, EventHandler<CVarChangedEventArgs> Handler)> _settingHandlers = [];

    /// <summary>Runs something whenever a setting changes, for as long as this game runs.</summary>
    /// <param name="variable">The setting.</param>
    /// <param name="handler">What to do.</param>
    private void WhenChanged(ICVar variable, Action handler)
    {
        EventHandler<CVarChangedEventArgs> subscription = (_, _) => handler();
        variable.Changed += subscription;
        _settingHandlers.Add((variable, subscription));
    }

    /// <summary>Takes every handler this game put on a setting back off it.</summary>
    private void ReleaseSettingHandlers()
    {
        foreach ((ICVar variable, EventHandler<CVarChangedEventArgs> handler) in _settingHandlers)
        {
            variable.Changed -= handler;
        }

        _settingHandlers.Clear();
    }
}

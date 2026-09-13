namespace OpenIGI.Rendering;

/// <summary>The weather a mission is played in, as the campaign screen offers it.</summary>
/// <remarks>
/// Anything but <see cref="Default"/> overrides what the level's own <c>RainEffect</c> task says, which
/// is what makes it a choice worth having: only three of the fourteen retail missions author any weather
/// at all, so without an override the setting would do nothing in eleven of them.
/// </remarks>
public enum MissionWeather
{
    /// <summary>Whatever the level's own data asks for, which for most missions is clear.</summary>
    Default,

    /// <summary>Rain, in every mission — the remastered storm on any preset above vanilla.</summary>
    Rain,

    /// <summary>Snow, in every mission.</summary>
    Snow,
}

namespace OpenIGI.Rendering;

/// <summary>How much of the remastered HUD stays on screen.</summary>
public enum HudDensity
{
    /// <summary>Everything, all the time.</summary>
    Full,

    /// <summary>What matters now: parts dim when nothing about them changes and come back when something does.</summary>
    Contextual,

    /// <summary>Only what is urgent: health when low, rounds when low or reloading, threats; the rest shows briefly on change.</summary>
    Minimal,
}

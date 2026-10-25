using System.Text.Json.Serialization;

namespace OpenIGI.Core.Graphics;

/// <summary>Where in the picture's making a mod's pass runs.</summary>
public enum PostPassStage
{
    /// <summary>
    /// Over the lit scene, before the bloom and the tone map: linear, unexposed radiance at the scene's resolution, which the
    /// game's exposure and grade then see.
    /// </summary>
    [JsonStringEnumMemberName("scene")]
    Scene,

    /// <summary>
    /// Over the finished picture, after the tone map and the grade and before the interface: display-encoded colour, nought to
    /// one, at the screen's resolution, as the player sees it.
    /// </summary>
    [JsonStringEnumMemberName("display")]
    Display,
}

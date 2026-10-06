namespace OpenIGI.Rendering;

/// <summary>How many frames reach the screen for each one rendered (DLSS frame generation).</summary>
/// <remarks>
/// The multipliers above two need a GPU that can generate more than one frame per rendered one; a
/// request for more than the hardware offers is clamped to what it reports, and the log says so.
/// </remarks>
public enum FrameGenerationMode
{
    /// <summary>Every frame on screen is a rendered one.</summary>
    Off,

    /// <summary>One generated frame per rendered one.</summary>
    X2,

    /// <summary>Two generated frames per rendered one.</summary>
    X3,

    /// <summary>Three generated frames per rendered one.</summary>
    X4,

    /// <summary>Four generated frames per rendered one.</summary>
    X5,

    /// <summary>Five generated frames per rendered one.</summary>
    X6,
}

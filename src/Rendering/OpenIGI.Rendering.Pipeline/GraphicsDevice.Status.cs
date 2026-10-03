using OpenIGI.Core.Diagnostics;

namespace OpenIGI.Rendering.Pipeline;

/// <summary>The lines the renderer says about itself: what it is tracing, what the world holds, what glazes.</summary>
/// <remarks>
/// <para>
/// Each is written on CHANGE, never once and never every frame. Every gate behind them opens and closes
/// during a session -- a switch flipped live, a world and a surface cache that arrive part of the way
/// through a mission load -- so a once-only line would report whatever was true on the first frame after
/// a level opened and never correct itself, and a line a frame would be a wall of text. What a player
/// toggling a setting needs is one line each time the answer moves.
/// </para>
/// <para>
/// They exist because every failure they describe is a silence in the picture: a reflection that is not
/// traced looks like one with nothing in front of it, and a world without its soldiers looks like a world
/// with nobody standing there.
/// </para>
/// </remarks>
public sealed partial class GraphicsDevice
{
    private readonly IOpenIgiLogger? _logger;
    private readonly Dictionary<string, string> _reported = new(StringComparer.Ordinal);

    // How the panes that reached the forward pass this frame reflected, for the glazing's line.
    private string _glazingReflection = "not reflecting";

    /// <summary>Writes a line about one subject, when what it says has changed since it was last written.</summary>
    /// <param name="subject">What the line is about.</param>
    /// <param name="state">What is now true of it.</param>
    private void ReportOnChange(string subject, string state)
    {
        if (_logger is not { } logger
            || (_reported.TryGetValue(subject, out string? last) && string.Equals(last, state, StringComparison.Ordinal)))
        {
            return;
        }

        _reported[subject] = state;
        logger.Information("Renderer", $"{subject}: {state}.");
    }

    /// <summary>Says whether the scene's reflections trace, and when they do not, which gate stopped them.</summary>
    /// <param name="complete">Whether the surface cache was whole enough to bind this frame.</param>
    private void ReportReflectionTrace(bool complete) => ReportOnChange(
        "Traced reflections",
        _device.RayTracing is null ? "off: this adapter cannot trace"
        : !RenderCVars.HardwareRayTracing.Value ? "off: r_rt is 0"
        : !RenderCVars.TracedReflections.Value ? "off: r_rt_reflections is 0"
        : !_surfaceCacheBinding.IsPresent ? "off: no lit surface cache (SetSurfaceCache handed over nothing)"
        : TracedWorld is not { IsWorldReady: true, WorldScene: not null } ? "off: the world's acceleration structure is not built yet"
        : !complete ? "off: the surface cache named a resource this device no longer holds"
        : "on");

    /// <summary>Says what the traced world holds, where there is one.</summary>
    private void ReportTracedWorld()
    {
        if (TracedWorld is { } world)
        {
            ReportOnChange("Traced world", world.Statistics.ToString());
        }
    }

    /// <summary>Says how many panes the frame just drawn glazed, and how they reflected.</summary>
    /// <remarks>
    /// For the previous frame, at the point the next one's glazing is about to start over: the one place
    /// in the frame where the count is known and has not yet been reset.
    /// </remarks>
    private void ReportGlazing() => ReportOnChange(
        "Glazing",
        _glassPanes == 0
            ? "no pane reached the forward pass (nothing in view is marked glass in materials.json)"
            : $"{_glassPanes} pane draw(s), {_glazingReflection}");
}

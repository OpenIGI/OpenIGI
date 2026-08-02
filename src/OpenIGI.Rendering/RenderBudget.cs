namespace OpenIGI.Rendering;

/// <summary>
/// What each <see cref="RenderStage"/> is allowed to cost, in milliseconds.
/// </summary>
/// <remarks>
/// <para>
/// The figures the remaster track committed to at stage R3 and then wrote five more stages against
/// without an instrument to check them: shadows 1.0, occlusion 1.0, lighting 2.0, reflections 1.5, fog
/// 1.5, post 1.5. They are for <b>ultra at 1080p</b>, which is the configuration R9's acceptance
/// criterion names — a preset that runs fewer of these stages, or a smaller window, is not what the
/// numbers describe.
/// </para>
/// <para>
/// Together they come to 8.5 ms, which against a 16.6 ms frame leaves 8 ms for the G-buffer, the
/// forward pass, the temporal resolve and the present. That is the sense in which this is a budget
/// rather than a target: the parts have to add up to a frame.
/// </para>
/// <para>
/// The G-buffer fill, the forward pass and the temporal resolve carry no figure. They were never
/// budgeted separately — they are the frame the effects are added to, not effects that can be turned
/// off — so they are measured and reported without a line to beat.
/// </para>
/// </remarks>
public static class RenderBudget
{
    /// <summary>The frame time a 60 Hz presentation has to fit inside, in milliseconds.</summary>
    public const double FrameMilliseconds = 1000.0 / 60.0;

    /// <summary>Gets what a stage is allowed to cost.</summary>
    /// <param name="stage">The stage.</param>
    /// <returns>Its budget in milliseconds, or <see langword="null"/> when it was never given one.</returns>
    public static double? For(RenderStage stage) => stage switch
    {
        RenderStage.Shadows => 1.0,
        RenderStage.AmbientOcclusion => 1.0,
        RenderStage.Lighting => 2.0,
        RenderStage.Reflections => 1.5,
        RenderStage.Fog => 1.5,
        RenderStage.Post => 1.5,
        _ => null,
    };
}

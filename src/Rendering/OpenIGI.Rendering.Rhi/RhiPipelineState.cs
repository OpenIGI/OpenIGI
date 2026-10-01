namespace OpenIGI.Rendering.Rhi;

/// <summary>What the vertices of a draw are assembled into.</summary>
public enum RhiPrimitiveTopology
{
    /// <summary>Each vertex is a point.</summary>
    PointList,

    /// <summary>Each pair of vertices is a line.</summary>
    LineList,

    /// <summary>Each vertex continues a line from the last.</summary>
    LineStrip,

    /// <summary>Each three vertices are a triangle.</summary>
    TriangleList,

    /// <summary>Each vertex continues a triangle strip.</summary>
    TriangleStrip,
}

/// <summary>Which face of a triangle is discarded.</summary>
public enum RhiCullMode
{
    /// <summary>Neither; both faces draw.</summary>
    None,

    /// <summary>The front face is discarded.</summary>
    Front,

    /// <summary>The back face is discarded.</summary>
    Back,
}

/// <summary>Which winding is the front face.</summary>
public enum RhiFrontFace
{
    /// <summary>Counter-clockwise in framebuffer space.</summary>
    CounterClockwise,

    /// <summary>Clockwise in framebuffer space.</summary>
    Clockwise,
}

/// <summary>How a new value is compared against the one already stored.</summary>
/// <remarks>
/// This renderer draws reverse-Z, so the test that keeps the nearer surface is
/// <see cref="GreaterEqual"/> rather than the <see cref="LessEqual"/> a reader expects. Every new
/// pipeline gets this wrong once and draws nothing at all.
/// </remarks>
public enum RhiCompare
{
    /// <summary>Never passes.</summary>
    Never,

    /// <summary>Passes when the new value is smaller.</summary>
    Less,

    /// <summary>Passes when the two are equal.</summary>
    Equal,

    /// <summary>Passes when the new value is smaller or equal.</summary>
    LessEqual,

    /// <summary>Passes when the new value is greater.</summary>
    Greater,

    /// <summary>Passes when the two differ.</summary>
    NotEqual,

    /// <summary>Passes when the new value is greater or equal.</summary>
    GreaterEqual,

    /// <summary>Always passes.</summary>
    Always,
}

/// <summary>One side of a blend equation.</summary>
public enum RhiBlendFactor
{
    /// <summary>Zero.</summary>
    Zero,

    /// <summary>One.</summary>
    One,

    /// <summary>The source colour.</summary>
    Source,

    /// <summary>One minus the source colour.</summary>
    OneMinusSource,

    /// <summary>The source alpha.</summary>
    SourceAlpha,

    /// <summary>One minus the source alpha.</summary>
    OneMinusSourceAlpha,

    /// <summary>The destination colour.</summary>
    Destination,

    /// <summary>One minus the destination colour.</summary>
    OneMinusDestination,

    /// <summary>The destination alpha.</summary>
    DestinationAlpha,

    /// <summary>One minus the destination alpha.</summary>
    OneMinusDestinationAlpha,
}

/// <summary>How the two sides of a blend are combined.</summary>
public enum RhiBlendOperation
{
    /// <summary>Source plus destination.</summary>
    Add,

    /// <summary>Source minus destination.</summary>
    Subtract,

    /// <summary>Destination minus source.</summary>
    ReverseSubtract,

    /// <summary>The smaller of the two.</summary>
    Min,

    /// <summary>The larger of the two.</summary>
    Max,
}

/// <summary>Which channels a colour target is written to.</summary>
[Flags]
public enum RhiColorWrite
{
    /// <summary>None; the target is read but not written.</summary>
    None = 0,

    /// <summary>Red.</summary>
    Red = 1 << 0,

    /// <summary>Green.</summary>
    Green = 1 << 1,

    /// <summary>Blue.</summary>
    Blue = 1 << 2,

    /// <summary>Alpha.</summary>
    Alpha = 1 << 3,

    /// <summary>Every channel.</summary>
    All = Red | Green | Blue | Alpha,
}

/// <summary>How one channel pair is blended.</summary>
/// <param name="Source">What the incoming value is scaled by.</param>
/// <param name="Destination">What the stored value is scaled by.</param>
/// <param name="Operation">How the two are combined.</param>
public readonly record struct RhiBlendComponent(
    RhiBlendFactor Source,
    RhiBlendFactor Destination,
    RhiBlendOperation Operation)
{
    /// <summary>Gets the component that keeps the incoming value outright.</summary>
    public static RhiBlendComponent Replace =>
        new(RhiBlendFactor.One, RhiBlendFactor.Zero, RhiBlendOperation.Add);

    /// <summary>Gets straight alpha blending.</summary>
    public static RhiBlendComponent AlphaBlend =>
        new(RhiBlendFactor.SourceAlpha, RhiBlendFactor.OneMinusSourceAlpha, RhiBlendOperation.Add);

    /// <summary>Gets additive blending, which is what the terrain's extra material passes use.</summary>
    public static RhiBlendComponent Additive =>
        new(RhiBlendFactor.One, RhiBlendFactor.One, RhiBlendOperation.Add);
}

/// <summary>How a colour target is blended, or nothing for a target that is written outright.</summary>
/// <param name="Color">How the colour channels are blended.</param>
/// <param name="Alpha">How the alpha channel is blended.</param>
public readonly record struct RhiBlendState(RhiBlendComponent Color, RhiBlendComponent Alpha)
{
    /// <summary>Gets straight alpha blending on colour, keeping the incoming alpha.</summary>
    public static RhiBlendState AlphaBlend =>
        new(RhiBlendComponent.AlphaBlend, RhiBlendComponent.AlphaBlend);

    /// <summary>Gets additive blending.</summary>
    public static RhiBlendState Additive => new(RhiBlendComponent.Additive, RhiBlendComponent.Additive);
}

/// <summary>One colour target of a render pipeline.</summary>
/// <param name="Format">The format of the texture that will be attached here.</param>
/// <param name="Blend">How it is blended, or <see langword="null"/> to write outright.</param>
/// <param name="Write">Which channels are written.</param>
public readonly record struct RhiColorTargetState(
    RhiTextureFormat Format,
    RhiBlendState? Blend = null,
    RhiColorWrite Write = RhiColorWrite.All);

/// <summary>How a render pipeline tests and writes depth.</summary>
/// <param name="Format">The depth format that will be attached.</param>
/// <param name="DepthWrite">Whether passing fragments store their depth.</param>
/// <param name="DepthCompare">How a fragment's depth is compared against the stored one.</param>
/// <param name="DepthBiasConstant">A constant added to depth, in the target's own units.</param>
/// <param name="DepthBiasSlopeScale">A bias proportional to the surface's slope.</param>
public readonly record struct RhiDepthStencilState(
    RhiTextureFormat Format,
    bool DepthWrite,
    RhiCompare DepthCompare,
    float DepthBiasConstant = 0f,
    float DepthBiasSlopeScale = 0f);

/// <summary>How the vertices of a draw are assembled and which faces survive.</summary>
/// <param name="Topology">What the vertices are assembled into.</param>
/// <param name="CullMode">Which face is discarded.</param>
/// <param name="FrontFace">Which winding is the front.</param>
/// <param name="StripIndexFormat">
/// The index width a strip topology restarts on, which WebGPU requires a strip pipeline to state.
/// </param>
public readonly record struct RhiPrimitiveState(
    RhiPrimitiveTopology Topology = RhiPrimitiveTopology.TriangleList,
    RhiCullMode CullMode = RhiCullMode.Back,
    RhiFrontFace FrontFace = RhiFrontFace.CounterClockwise,
    RhiIndexFormat? StripIndexFormat = null);

/// <summary>One attribute within a vertex buffer.</summary>
/// <param name="Location">The shader location it feeds.</param>
/// <param name="Format">How wide it is and how it is read.</param>
/// <param name="Offset">Its byte offset within the vertex.</param>
public readonly record struct RhiVertexElement(int Location, RhiVertexFormat Format, int Offset);

/// <summary>Whether a vertex buffer advances per vertex or per instance.</summary>
public enum RhiVertexStepMode
{
    /// <summary>Once per vertex.</summary>
    Vertex,

    /// <summary>Once per instance.</summary>
    Instance,
}

/// <summary>One vertex buffer a pipeline reads, and the attributes packed into it.</summary>
/// <param name="Stride">How many bytes one element occupies.</param>
/// <param name="StepMode">Whether it advances per vertex or per instance.</param>
/// <param name="Attributes">The attributes within it.</param>
public sealed record RhiVertexBufferLayout(
    int Stride,
    RhiVertexStepMode StepMode,
    IReadOnlyList<RhiVertexElement> Attributes);

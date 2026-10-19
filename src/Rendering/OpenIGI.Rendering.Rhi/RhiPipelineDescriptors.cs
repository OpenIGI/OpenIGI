using OpenIGI.Core.Graphics;
using OpenIGI.Shaders;

namespace OpenIGI.Rendering.Rhi;

/// <summary>
/// One stage of a pipeline: which module, which entry point, and what it was specialised with.
/// </summary>
/// <param name="Module">The compiled module.</param>
/// <param name="EntryPoint">The entry point within it, as the module's reflection names it.</param>
/// <param name="Constants">
/// Specialisation constants by identifier. Vulkan takes them as specialisation data; WebGPU takes them
/// as pipeline-overridable constants, which is the same idea under the other name.
/// </param>
public sealed record RhiShaderStage(
    IRhiShaderModule Module,
    string EntryPoint,
    IReadOnlyDictionary<int, RhiConstant>? Constants = null);

/// <summary>What a graphics pipeline is created as.</summary>
/// <param name="Layout">What its bind groups and push data look like.</param>
/// <param name="Vertex">The vertex stage.</param>
/// <param name="Fragment">The fragment stage, or <see langword="null"/> for a depth-only pipeline.</param>
/// <param name="VertexBuffers">The vertex buffers it reads.</param>
/// <param name="ColorTargets">The colour targets it writes, in attachment order.</param>
/// <param name="DepthStencil">How it tests and writes depth, or <see langword="null"/> for none.</param>
/// <param name="Primitive">How its vertices are assembled and which faces survive.</param>
/// <param name="SampleCount">How many samples its targets have.</param>
/// <param name="Label">A name for captures and diagnostics.</param>
/// <remarks>
/// Viewport and scissor are not here. Both are set on the pass, because both change with the window and
/// baking them into a pipeline would mean rebuilding every one of them on a resize.
/// </remarks>
public sealed record RhiRenderPipelineDescriptor(
    IRhiPipelineLayout Layout,
    RhiShaderStage Vertex,
    RhiShaderStage? Fragment,
    IReadOnlyList<RhiVertexBufferLayout> VertexBuffers,
    IReadOnlyList<RhiColorTargetState> ColorTargets,
    RhiDepthStencilState? DepthStencil = null,
    RhiPrimitiveState Primitive = default,
    int SampleCount = 1,
    string Label = "");

/// <summary>What a compute pipeline is created as.</summary>
/// <param name="Layout">What its bind groups and push data look like.</param>
/// <param name="Compute">The compute stage.</param>
/// <param name="Label">A name for captures and diagnostics.</param>
public sealed record RhiComputePipelineDescriptor(
    IRhiPipelineLayout Layout,
    RhiShaderStage Compute,
    string Label = "");

/// <summary>Which module and variant of the shader library to load.</summary>
/// <param name="Module">The module.</param>
/// <param name="Variant">Which compiled form.</param>
/// <param name="Label">A name for captures and diagnostics; the module's own name when empty.</param>
/// <remarks>
/// A backend loads the target it can run — SPIR-V or WGSL — from the same identifier, which is the whole
/// point of compiling both at build time. A module with no form for this backend is a creation failure
/// naming the module, not a shader that fails to link later.
/// </remarks>
public readonly record struct RhiShaderModuleDescriptor(
    ShaderModuleId Module,
    ShaderVariant Variant = default,
    string Label = "");

/// <summary>What a query set is created as.</summary>
/// <param name="Type">What it counts.</param>
/// <param name="Count">How many slots.</param>
/// <param name="Label">A name for captures and diagnostics.</param>
public readonly record struct RhiQuerySetDescriptor(RhiQueryType Type, int Count, string Label = "");

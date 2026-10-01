using System.Runtime.InteropServices;

namespace OpenIGI.Rendering.WebGpu;

/// <summary>
/// The entry points a frame calls thousands of times, declared in this assembly as well as in the
/// generated binding.
/// </summary>
/// <remarks>
/// <para>
/// The generated binding assembly is interpreted in a browser build (<c>OpenIGI.Web.csproj</c> keeps it
/// out of the ahead-of-time compile, because the compiler crashes on it), so every call through it
/// crosses from compiled code into the interpreter and back: on mission 3 that crossing was a tenth of
/// the browser's frame, spent on the binds and draws alone. Declared here, in an assembly that IS
/// compiled, the same entry points are called directly. The pinvoke table the browser build writes
/// merges the two declarations of one function.
/// </para>
/// <para>
/// <b>Plain DllImports of integers.</b> The generated binding is a LibraryImport of one-field handle
/// structs, and that is what the ahead-of-time compiler dies on (the sgen assertion that keeps the
/// binding interpreted): a LibraryImport's stub is a local extern function, and the compiler asserted
/// on this assembly too the moment it held one, integer handles or not. A DllImport of integers and
/// pointers needs no stub at all, so there is nothing for the compiler to generate.
/// </para>
/// <para>
/// Only what a frame repeats belongs here: a call made once per resource is not worth a second
/// declaration to keep in step with the header.
/// </para>
/// </remarks>
internal static unsafe class WebGpuHotCalls
{
    [DllImport(Wgpu.LibraryName, EntryPoint = "wgpuRenderPassEncoderSetPipeline", ExactSpelling = true)]
    public static extern void wgpuRenderPassEncoderSetPipeline(nint renderPassEncoder, nint pipeline);

    [DllImport(Wgpu.LibraryName, EntryPoint = "wgpuRenderPassEncoderSetBindGroup", ExactSpelling = true)]
    public static extern void wgpuRenderPassEncoderSetBindGroup(nint renderPassEncoder, uint groupIndex, nint group, nuint dynamicOffsetCount, uint* dynamicOffsets);

    [DllImport(Wgpu.LibraryName, EntryPoint = "wgpuRenderPassEncoderSetVertexBuffer", ExactSpelling = true)]
    public static extern void wgpuRenderPassEncoderSetVertexBuffer(nint renderPassEncoder, uint slot, nint buffer, ulong offset, ulong size);

    [DllImport(Wgpu.LibraryName, EntryPoint = "wgpuRenderPassEncoderSetIndexBuffer", ExactSpelling = true)]
    public static extern void wgpuRenderPassEncoderSetIndexBuffer(nint renderPassEncoder, nint buffer, uint format, ulong offset, ulong size);

    [DllImport(Wgpu.LibraryName, EntryPoint = "wgpuRenderPassEncoderDraw", ExactSpelling = true)]
    public static extern void wgpuRenderPassEncoderDraw(nint renderPassEncoder, uint vertexCount, uint instanceCount, uint firstVertex, uint firstInstance);

    [DllImport(Wgpu.LibraryName, EntryPoint = "wgpuRenderPassEncoderDrawIndexed", ExactSpelling = true)]
    public static extern void wgpuRenderPassEncoderDrawIndexed(nint renderPassEncoder, uint indexCount, uint instanceCount, uint firstIndex, int baseVertex, uint firstInstance);

    [DllImport(Wgpu.LibraryName, EntryPoint = "wgpuComputePassEncoderSetPipeline", ExactSpelling = true)]
    public static extern void wgpuComputePassEncoderSetPipeline(nint computePassEncoder, nint pipeline);

    [DllImport(Wgpu.LibraryName, EntryPoint = "wgpuComputePassEncoderSetBindGroup", ExactSpelling = true)]
    public static extern void wgpuComputePassEncoderSetBindGroup(nint computePassEncoder, uint groupIndex, nint group, nuint dynamicOffsetCount, uint* dynamicOffsets);

    [DllImport(Wgpu.LibraryName, EntryPoint = "wgpuComputePassEncoderDispatchWorkgroups", ExactSpelling = true)]
    public static extern void wgpuComputePassEncoderDispatchWorkgroups(nint computePassEncoder, uint workgroupCountX, uint workgroupCountY, uint workgroupCountZ);

    [DllImport(Wgpu.LibraryName, EntryPoint = "wgpuQueueWriteBuffer", ExactSpelling = true)]
    public static extern void wgpuQueueWriteBuffer(nint queue, nint buffer, ulong bufferOffset, void* data, nuint size);
}

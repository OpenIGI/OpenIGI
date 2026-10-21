using OpenIGI.Core.Numerics;

using OpenIGI.Shaders;

namespace OpenIGI.Rendering;

/// <summary>The compute seam: programs, storage buffers, image bindings, dispatches and barriers.</summary>
/// <remarks>Not every backend can honour it; see <see cref="GraphicsCapabilities.SupportsCompute"/>. Part of <see cref="IGraphicsDevice"/>.</remarks>
public interface IComputeDevice
{
    /// <summary>Creates a compute program from one of the shader library's modules.</summary>
    /// <param name="shaderModule">The compute module.</param>
    /// <param name="variant">Which compiled form to load.</param>
    /// <param name="name">A name for the program, used in diagnostics.</param>
    /// <returns>A handle to the program.</returns>
    /// <remarks>
    /// <para>
    /// A member of this seam that not every backend can honour. Compute needs GL 4.3, which desktop has
    /// and macOS -- frozen at 4.1 -- does not, so a caller must check
    /// <see cref="GraphicsCapabilities.SupportsCompute"/> and have a path that does not need it.
    /// </para>
    /// <para>
    /// The module was compiled by the build rather than at load, so a mistake in it is a failed build
    /// instead of a level that does not start. Programs are still created up front rather than lazily:
    /// a pipeline that cannot be built is a fault to find at load, not on the frame that first needed
    /// the pass.
    /// </para>
    /// </remarks>
    /// <exception cref="GraphicsDeviceException">Compute is unsupported, or the pipeline failed.</exception>
    ComputeShaderHandle CreateComputeShader(ShaderModuleId shaderModule, ShaderVariant variant, string name);

    /// <summary>Releases a compute program.</summary>
    /// <param name="shader">The program to release. The null handle is ignored.</param>
    void DestroyComputeShader(ComputeShaderHandle shader);

    /// <summary>Allocates a shader-writable buffer.</summary>
    /// <param name="sizeInBytes">How large the buffer is.</param>
    /// <param name="name">A name for the buffer, used only in diagnostics.</param>
    /// <returns>A handle to the buffer, zero-filled.</returns>
    /// <remarks>
    /// Zero-filled on creation, and that is part of the contract rather than an accident of the driver:
    /// almost every buffer here is an append list with a count at the front, and a count that starts at
    /// whatever was in memory is a pass that reads garbage on its first frame.
    /// </remarks>
    /// <exception cref="GraphicsDeviceException">Compute is unsupported, or the size is not positive.</exception>
    StorageBufferHandle CreateStorageBuffer(int sizeInBytes, string name);

    /// <summary>Replaces the contents of a storage buffer.</summary>
    /// <typeparam name="T">The element type. Must be blittable.</typeparam>
    /// <param name="buffer">The buffer to write.</param>
    /// <param name="data">The data to write, from the start of the buffer.</param>
    /// <exception cref="GraphicsDeviceException"><paramref name="buffer"/> is not a buffer of this device.</exception>
    void UpdateStorageBuffer<T>(StorageBufferHandle buffer, ReadOnlySpan<T> data)
        where T : unmanaged;

    /// <summary>Replaces part of a storage buffer's contents.</summary>
    /// <typeparam name="T">The element type. Must be blittable.</typeparam>
    /// <param name="buffer">The buffer to write.</param>
    /// <param name="elementOffset">Where to start writing, in elements of <typeparamref name="T"/>.</param>
    /// <param name="data">The data to write there.</param>
    /// <exception cref="GraphicsDeviceException"><paramref name="buffer"/> is not a buffer of this device.</exception>
    void UpdateStorageBuffer<T>(StorageBufferHandle buffer, int elementOffset, ReadOnlySpan<T> data)
        where T : unmanaged;

    /// <summary>Reads a storage buffer back into memory.</summary>
    /// <typeparam name="T">The element type. Must be blittable.</typeparam>
    /// <param name="buffer">The buffer to read.</param>
    /// <param name="destination">Receives the contents, from the start of the buffer.</param>
    /// <remarks>
    /// <b>Synchronising, and therefore for diagnostics and load-time work rather than for a frame.</b>
    /// Reading a buffer the GPU may still be writing means waiting for it, which costs whatever
    /// pipelining the driver had built up. Every per-frame consumer of a compute result should be
    /// another shader.
    /// </remarks>
    /// <exception cref="GraphicsDeviceException"><paramref name="buffer"/> is not a buffer of this device.</exception>
    void ReadStorageBuffer<T>(StorageBufferHandle buffer, Span<T> destination)
        where T : unmanaged;

    /// <summary>Releases a storage buffer.</summary>
    /// <param name="buffer">The buffer to release. The null handle is ignored.</param>
    void DestroyStorageBuffer(StorageBufferHandle buffer);

    /// <summary>Binds a texture as a readable or writable image for the next dispatch.</summary>
    /// <param name="slot">The image unit, matching the shader's <c>binding =</c> layout qualifier.</param>
    /// <param name="texture">The texture, or the null handle to unbind.</param>
    /// <param name="access">How the pass intends to touch it.</param>
    /// <param name="level">Which mip level to bind.</param>
    /// <remarks>
    /// Distinct from <see cref="ITextureDevice.SetTexture"/>, and not interchangeable with it: a sampler reads through
    /// filtering and normalised coordinates, while an image binding is unfiltered, integer-addressed,
    /// and writable. A compute pass that builds a volume wants the second; one that traces through a
    /// finished volume wants the first.
    /// </remarks>
    void BindImage(int slot, TextureHandle texture, ImageAccess access, int level = 0);

    /// <summary>Runs a compute program over a grid of work groups.</summary>
    /// <param name="shader">The program to run.</param>
    /// <param name="groupsX">Work groups in x. Must be positive.</param>
    /// <param name="groupsY">Work groups in y. Must be positive.</param>
    /// <param name="groupsZ">Work groups in z. Must be positive.</param>
    /// <remarks>
    /// Counts are <em>groups</em>, not threads: the group's size is declared by the shader, so a pass
    /// over a 1920×1080 image with an 8×8 group dispatches 240×135.
    /// </remarks>
    /// <exception cref="GraphicsDeviceException"><paramref name="shader"/> is not a program of this device.</exception>
    void Dispatch(ComputeShaderHandle shader, int groupsX, int groupsY = 1, int groupsZ = 1);

    /// <summary>Runs a compute program over a grid a buffer decides.</summary>
    /// <param name="shader">The program to run.</param>
    /// <param name="arguments">A buffer holding three consecutive unsigned integers: the group counts.</param>
    /// <param name="offsetInBytes">Where in that buffer the three counts start.</param>
    /// <remarks>
    /// What lets a pass size the next one. The culling stage cannot know how many meshes fall inside a
    /// clipmap until it has looked, and reading its count back to the CPU to launch the next dispatch
    /// would cost a full synchronisation every frame — so it writes the count into a buffer and the
    /// GPU reads it without anybody waiting.
    /// </remarks>
    /// <exception cref="GraphicsDeviceException">A handle does not belong to this device.</exception>
    void DispatchIndirect(ComputeShaderHandle shader, StorageBufferHandle arguments, int offsetInBytes = 0);

    /// <summary>Makes a previous dispatch's writes visible to what follows.</summary>
    /// <param name="scope">What kind of access has to become visible.</param>
    /// <remarks>
    /// Dispatches are not ordered against each other without this. Omitting it does not usually fail
    /// on the machine it was written on — the timing simply happens to work — which is what makes a
    /// missing barrier one of the harder faults to find later.
    /// </remarks>
    void MemoryBarrier(MemoryBarrierScope scope);

    /// <summary>Sets a scalar uniform on a compute program.</summary>
    /// <param name="shader">The program.</param>
    /// <param name="name">The uniform's name.</param>
    /// <param name="value">The value.</param>
    /// <remarks>
    /// Deliberately small. Anything with real structure belongs in a storage buffer, where it can be
    /// indexed and written by another pass; this is for the handful of scalars that decide what a
    /// dispatch is doing, and a uniform block would be more machinery than that is worth.
    /// </remarks>
    void SetComputeUniform(ComputeShaderHandle shader, string name, float value);

    /// <summary>Sets an integer uniform on a compute program.</summary>
    /// <param name="shader">The program.</param>
    /// <param name="name">The uniform's name.</param>
    /// <param name="value">The value.</param>
    void SetComputeUniform(ComputeShaderHandle shader, string name, int value);

    /// <summary>Sets a three-component uniform on a compute program.</summary>
    /// <param name="shader">The program.</param>
    /// <param name="name">The uniform's name.</param>
    /// <param name="value">The value.</param>
    void SetComputeUniform(ComputeShaderHandle shader, string name, in Vector3f value);

    /// <summary>Sets a four-component uniform on a compute program.</summary>
    /// <param name="shader">The program.</param>
    /// <param name="name">The uniform's name.</param>
    /// <param name="x">The first component.</param>
    /// <param name="y">The second component.</param>
    /// <param name="z">The third component.</param>
    /// <param name="w">The fourth component.</param>
    void SetComputeUniform(ComputeShaderHandle shader, string name, float x, float y, float z, float w);
}

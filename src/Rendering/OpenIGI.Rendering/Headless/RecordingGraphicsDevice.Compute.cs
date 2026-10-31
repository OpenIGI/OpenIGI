using OpenIGI.Core.Numerics;

using OpenIGI.Shaders;

namespace OpenIGI.Rendering.Headless;

public sealed partial class RecordingGraphicsDevice
{
    /// <summary>The compute programs asked for, by the name they were given.</summary>
    public List<string> ComputeShaders { get; } = [];

    /// <summary>Every dispatch made, as the program and its group counts.</summary>
    public List<(ComputeShaderHandle Shader, int X, int Y, int Z)> Dispatches { get; } = [];

    /// <summary>Every memory barrier issued, in order.</summary>
    public List<MemoryBarrierScope> Barriers { get; } = [];

    // The compute seam, recorded rather than performed. This device exists to check what the shell
    // ASKS a backend to do, and there is no context here to do any of it in -- so a handle is a
    // counter and a dispatch is an entry in a list. A test that wants to know whether a pass actually
    // computed the right answer needs a real device, which is what the capture tooling is for.
    private uint _nextComputeId = 1;
    private uint _nextBufferId = 1;

    /// <inheritdoc/>
    public ComputeShaderHandle CreateComputeShader(
        ShaderModuleId shaderModule,
        ShaderVariant variant,
        string name)
    {
        Record(ComputeShaders, name);
        return new ComputeShaderHandle(_nextComputeId++);
    }

    /// <inheritdoc/>
    public void DestroyComputeShader(ComputeShaderHandle shader)
    {
    }

    /// <inheritdoc/>
    public StorageBufferHandle CreateStorageBuffer(int sizeInBytes, string name) =>
        new(_nextBufferId++);

    /// <inheritdoc/>
    public void UpdateStorageBuffer<T>(StorageBufferHandle buffer, ReadOnlySpan<T> data)
        where T : unmanaged
    {
    }

    /// <inheritdoc/>
    public void UpdateStorageBuffer<T>(StorageBufferHandle buffer, int elementOffset, ReadOnlySpan<T> data)
        where T : unmanaged
    {
    }

    /// <inheritdoc/>
    public void ReadStorageBuffer<T>(StorageBufferHandle buffer, Span<T> destination)
        where T : unmanaged
    {
    }

    /// <inheritdoc/>
    public void DestroyStorageBuffer(StorageBufferHandle buffer)
    {
    }

    /// <inheritdoc/>
    public void BindStorageBuffer(int slot, StorageBufferHandle buffer)
    {
    }

    /// <inheritdoc/>
    public void BindImage(int slot, TextureHandle texture, ImageAccess access, int level = 0)
    {
    }

    /// <inheritdoc/>
    public void Dispatch(ComputeShaderHandle shader, int groupsX, int groupsY = 1, int groupsZ = 1) =>
        Record(Dispatches, (shader, groupsX, groupsY, groupsZ));

    /// <inheritdoc/>
    public void DispatchIndirect(
        ComputeShaderHandle shader, StorageBufferHandle arguments, int offsetInBytes = 0) =>
        Record(Dispatches, (shader, -1, -1, -1));

    /// <inheritdoc/>
    public void MemoryBarrier(MemoryBarrierScope scope) => Record(Barriers, scope);

    /// <inheritdoc/>
    public void SetComputeUniform(ComputeShaderHandle shader, string name, float value)
    {
    }

    /// <inheritdoc/>
    public void SetComputeUniform(ComputeShaderHandle shader, string name, int value)
    {
    }

    /// <inheritdoc/>
    public void SetComputeUniform(ComputeShaderHandle shader, string name, in Vector3f value)
    {
    }

    /// <inheritdoc/>
    public void SetComputeUniform(
        ComputeShaderHandle shader, string name, float x, float y, float z, float w)
    {
    }
}

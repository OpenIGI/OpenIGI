using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using OpenIGI.Core.Configuration;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Rendering.D3D12.Interop;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D12;

namespace OpenIGI.Rendering.D3D12.Pipelines;

/// <summary>
/// Remembers what the driver made of a pipeline, so the same one is never compiled twice: a pipeline library
/// kept on disk between runs.
/// </summary>
/// <remarks>
/// <para>
/// The DXIL is compiled at build time; what is left is the driver's half, which turns it into what the card
/// runs, and which otherwise lands as a hitch the first time a frame needs a permutation. A pipeline is
/// stored under a name hashed from everything its description holds -- its bytecode, its root signature,
/// every fixed-function field -- so a description that changes is a new name and never a wrong pipeline.
/// </para>
/// <para>
/// The runtime itself refuses a library written by another driver or adapter, which is then a miss, as is a
/// missing, corrupt or unwritable file. Nothing here can fail in a way that matters.
/// </para>
/// </remarks>
internal sealed unsafe class D3D12PipelineCache : IDisposable
{
    private const string LogCategory = "D3D12";
    private const string FileName = "pipelines-d3d12.bin";

    private readonly ID3D12Device10* _device;
    private readonly IOpenIgiLogger _logger;
    private readonly string? _path;
    private readonly Lock _lock = new();
    private ID3D12PipelineLibrary1* _library;

    // The library reads its pipelines straight out of the blob it was created from, so the blob lives as
    // long as the library does.
    private void* _blob;
    private bool _dirty;

    /// <summary>Initialises a new instance of the <see cref="D3D12PipelineCache"/> class.</summary>
    /// <param name="device">The device.</param>
    /// <param name="adapter">The adapter's name, which the file is kept under.</param>
    /// <param name="logger">Where it reports.</param>
    public D3D12PipelineCache(ID3D12Device10* device, string adapter, IOpenIgiLogger logger)
    {
        _device = device;
        _logger = logger;

        byte[]? initial = null;
        try
        {
            string directory = Path.Combine(SettingsPaths.GetUserDirectory(), "cache");
            Directory.CreateDirectory(directory);
            _path = Path.Combine(directory, FileName);
            if (File.Exists(_path))
            {
                initial = File.ReadAllBytes(_path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _path = null;
        }

        if (initial is { Length: > 0 } && Open(initial))
        {
            _logger.Debug(LogCategory, $"Pipeline library loaded for {adapter}, {initial.Length / 1024} KiB.");
            return;
        }

        if (initial is { Length: > 0 })
        {
            _logger.Debug(LogCategory, "Discarding the pipeline library: it was written by a different driver or adapter.");
        }

        if (!Open([]))
        {
            // A driver without libraries still compiles pipelines; it just never remembers them.
            _logger.Debug(LogCategory, "No pipeline library on this driver; pipelines are compiled every run.");
        }
    }

    /// <summary>Gets a graphics pipeline from the library, or compiles and stores it.</summary>
    /// <param name="state">Its description, every pointer in it valid.</param>
    /// <param name="rootSignature">Its root signature's fingerprint, which identifies it across runs.</param>
    /// <param name="label">A name for an error.</param>
    /// <returns>The pipeline.</returns>
    public ID3D12PipelineState* Graphics(GraphicsPipelineStateDesc* state, ReadOnlySpan<byte> rootSignature, string label)
    {
        string name = Name(state, rootSignature);
        Guid iid = ID3D12PipelineState.Guid;
        ID3D12PipelineState* pipeline = null;

        lock (_lock)
        {
            fixed (char* key = name)
            {
                if (_library is not null && _library->LoadGraphicsPipeline(key, state, &iid, (void**)&pipeline) >= 0)
                {
                    return pipeline;
                }

                D3D12Check.Ok(_device->CreateGraphicsPipelineState(state, &iid, (void**)&pipeline), $"CreateGraphicsPipelineState ({label})");
                Store(key, pipeline);
                return pipeline;
            }
        }
    }

    /// <summary>Gets a compute pipeline from the library, or compiles and stores it.</summary>
    /// <param name="state">Its description.</param>
    /// <param name="rootSignature">Its root signature's fingerprint.</param>
    /// <param name="label">A name for an error.</param>
    /// <returns>The pipeline.</returns>
    public ID3D12PipelineState* Compute(ComputePipelineStateDesc* state, ReadOnlySpan<byte> rootSignature, string label)
    {
        string name = Name(state, rootSignature);
        Guid iid = ID3D12PipelineState.Guid;
        ID3D12PipelineState* pipeline = null;

        lock (_lock)
        {
            fixed (char* key = name)
            {
                if (_library is not null && _library->LoadComputePipeline(key, state, &iid, (void**)&pipeline) >= 0)
                {
                    return pipeline;
                }

                D3D12Check.Ok(_device->CreateComputePipelineState(state, &iid, (void**)&pipeline), $"CreateComputePipelineState ({label})");
                Store(key, pipeline);
                return pipeline;
            }
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Save();
        D3D12Check.Release(ref _library);
        if (_blob is not null)
        {
            NativeMemory.Free(_blob);
            _blob = null;
        }
    }

    /// <summary>The name a graphics pipeline is stored under.</summary>
    /// <param name="state">Its description.</param>
    /// <param name="rootSignature">Its root signature's fingerprint.</param>
    /// <returns>The name.</returns>
    /// <remarks>
    /// The fixed-function fields as the structure lays them out, with every pointer taken out and what it
    /// points at hashed in its place: the bytecode, the root signature's serialised form, and the input
    /// elements field by field.
    /// </remarks>
    internal static string Name(GraphicsPipelineStateDesc* state, ReadOnlySpan<byte> rootSignature)
    {
        GraphicsPipelineStateDesc copy = *state;
        copy.PRootSignature = null;
        copy.VS = default;
        copy.PS = default;
        copy.DS = default;
        copy.HS = default;
        copy.GS = default;
        copy.StreamOutput = default;
        copy.InputLayout = default;
        copy.CachedPSO = default;

        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(new ReadOnlySpan<byte>(&copy, sizeof(GraphicsPipelineStateDesc)));
        AppendBytecode(hash, state->VS);
        AppendBytecode(hash, state->PS);
        hash.AppendData(rootSignature);
        for (uint i = 0; i < state->InputLayout.NumElements; i++)
        {
            InputElementDesc element = state->InputLayout.PInputElementDescs[i];
            hash.AppendData(Encoding.ASCII.GetBytes($"{element.SemanticIndex},{element.Format},{element.InputSlot},{element.AlignedByteOffset},{element.InputSlotClass},{element.InstanceDataStepRate};"));
        }

        return "g" + Convert.ToHexString(hash.GetHashAndReset());
    }

    /// <summary>The name a compute pipeline is stored under.</summary>
    /// <param name="state">Its description.</param>
    /// <param name="rootSignature">Its root signature's fingerprint.</param>
    /// <returns>The name.</returns>
    internal static string Name(ComputePipelineStateDesc* state, ReadOnlySpan<byte> rootSignature)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendBytecode(hash, state->CS);
        hash.AppendData(rootSignature);
        hash.AppendData(BitConverter.GetBytes(state->NodeMask));
        hash.AppendData(BitConverter.GetBytes((uint)state->Flags));
        return "c" + Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void AppendBytecode(IncrementalHash hash, ShaderBytecode code)
    {
        hash.AppendData(BitConverter.GetBytes((ulong)code.BytecodeLength));
        if (code.PShaderBytecode is not null)
        {
            hash.AppendData(new ReadOnlySpan<byte>(code.PShaderBytecode, (int)code.BytecodeLength));
        }
    }

    private bool Open(byte[] initial)
    {
        void* blob = initial.Length > 0 ? NativeMemory.Alloc((nuint)initial.Length) : null;
        if (blob is not null)
        {
            initial.AsSpan().CopyTo(new Span<byte>(blob, initial.Length));
        }

        ID3D12PipelineLibrary1* library = null;
        Guid iid = ID3D12PipelineLibrary1.Guid;
        if (_device->CreatePipelineLibrary(blob, (nuint)initial.Length, &iid, (void**)&library) < 0 || library is null)
        {
            if (blob is not null)
            {
                NativeMemory.Free(blob);
            }

            return false;
        }

        _library = library;
        _blob = blob;
        return true;
    }

    private void Store(char* name, ID3D12PipelineState* pipeline)
    {
        // A name already taken -- two descriptions that hash alike, or a pipeline another thread stored
        // first -- is refused, and the pipeline simply goes unremembered.
        if (_library is not null && _library->StorePipeline(name, pipeline) >= 0)
        {
            _dirty = true;
        }
    }

    /// <summary>Writes the library back to disk, through a temporary and a rename.</summary>
    private void Save()
    {
        if (_path is null || _library is null || !_dirty)
        {
            return;
        }

        try
        {
            nuint size = _library->GetSerializedSize();
            if (size == 0)
            {
                return;
            }

            byte[] blob = new byte[(int)size];
            fixed (byte* data = blob)
            {
                if (_library->Serialize(data, size) < 0)
                {
                    return;
                }
            }

            string temporary = _path + ".tmp";
            File.WriteAllBytes(temporary, blob);
            File.Move(temporary, _path, overwrite: true);
            _dirty = false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Debug(LogCategory, "The pipeline library could not be written; it will be rebuilt.");
        }
    }
}

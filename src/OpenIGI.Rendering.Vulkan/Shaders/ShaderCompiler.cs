using System.Text;
using OpenIGI.Rendering;
using Silk.NET.Core.Native;
using Silk.NET.Shaderc;

namespace OpenIGI.Rendering.Vulkan.Shaders;

/// <summary>
/// Compiles GLSL to SPIR-V at load with shaderc, the same compiler front-end glslang gives the other
/// engines that target Vulkan.
/// </summary>
/// <remarks>
/// <para>
/// This is the first half of the reflection-based hybrid. Shaders reach Vulkan as SPIR-V, and rather
/// than ship pre-compiled blobs the backend compiles the GLSL at load — which keeps one text source of
/// truth and lets a shader be edited without a separate build step. The options turned on here are the
/// ones that let a shader omit its binding and location decorations and have the compiler assign them,
/// so a source ported from the GL backend need not hand-number every resource; the layout is then read
/// back from the SPIR-V by reflection.
/// </para>
/// <para>
/// A failed compile throws with the compiler's own message. A shader that does not compile is a fault
/// in the build, not a condition to recover from, so it surfaces at load with the file and line the
/// compiler reported rather than as a missing pipeline later.
/// </para>
/// </remarks>
internal sealed unsafe class ShaderCompiler : IDisposable
{
    private readonly Shaderc _shaderc;
    private readonly Compiler* _compiler;
    private readonly CompileOptions* _options;
    private bool _disposed;

    /// <summary>Initialises a new instance of the <see cref="ShaderCompiler"/> class.</summary>
    /// <exception cref="GraphicsDeviceException">The compiler could not be created.</exception>
    public ShaderCompiler()
    {
        _shaderc = Shaderc.GetApi();
        _compiler = _shaderc.CompilerInitialize();
        _options = _shaderc.CompileOptionsInitialize();
        if (_compiler is null || _options is null)
        {
            throw new GraphicsDeviceException("Could not initialise the shaderc compiler.");
        }

        // Target Vulkan SPIR-V. 1.2 is the widest environment modern drivers accept while still giving
        // the descriptor-indexing and buffer-device-address features the later GI and RT paths want.
        _shaderc.CompileOptionsSetTargetEnv(_options, TargetEnv.Vulkan, (uint)EnvVersion.Vulkan12);
        _shaderc.CompileOptionsSetOptimizationLevel(_options, OptimizationLevel.Performance);

        // Let a shader omit binding and location numbers and have them assigned. Reflection then reads
        // whatever the compiler settled on, so neither the author nor this code has to keep a hand-built
        // table of which resource sits at which binding.
        _shaderc.CompileOptionsSetAutoBindUniforms(_options, true);
        _shaderc.CompileOptionsSetAutoMapLocations(_options, true);
    }

    /// <summary>Compiles one GLSL source to a SPIR-V word blob.</summary>
    /// <param name="source">The GLSL source.</param>
    /// <param name="stage">Which pipeline stage the source is for.</param>
    /// <param name="name">A name for the source, used in compiler diagnostics.</param>
    /// <returns>The SPIR-V, as a byte array whose length is a multiple of four.</returns>
    /// <exception cref="GraphicsDeviceException">The source did not compile.</exception>
    public byte[] Compile(string source, ShaderStage stage, string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(source);

        nint sourcePtr = SilkMarshal.StringToPtr(source, NativeStringEncoding.UTF8);
        nint namePtr = SilkMarshal.StringToPtr(name, NativeStringEncoding.UTF8);
        nint entryPtr = SilkMarshal.StringToPtr("main", NativeStringEncoding.UTF8);
        nuint sourceSize = (nuint)Encoding.UTF8.GetByteCount(source);

        CompilationResult* result = null;
        try
        {
            result = _shaderc.CompileIntoSpv(
                _compiler,
                (byte*)sourcePtr,
                sourceSize,
                ToKind(stage),
                (byte*)namePtr,
                (byte*)entryPtr,
                _options);

            if (_shaderc.ResultGetCompilationStatus(result) != CompilationStatus.Success)
            {
                string message = _shaderc.ResultGetErrorMessageS(result);
                throw new GraphicsDeviceException($"Shader '{name}' failed to compile:{Environment.NewLine}{message}");
            }

            nuint length = _shaderc.ResultGetLength(result);
            byte* bytes = _shaderc.ResultGetBytes(result);
            return new ReadOnlySpan<byte>(bytes, (int)length).ToArray();
        }
        finally
        {
            if (result is not null)
            {
                _shaderc.ResultRelease(result);
            }

            SilkMarshal.Free(sourcePtr);
            SilkMarshal.Free(namePtr);
            SilkMarshal.Free(entryPtr);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_options is not null)
        {
            _shaderc.CompileOptionsRelease(_options);
        }

        if (_compiler is not null)
        {
            _shaderc.CompilerRelease(_compiler);
        }

        _shaderc.Dispose();
    }

    private static ShaderKind ToKind(ShaderStage stage) => stage switch
    {
        ShaderStage.Vertex => ShaderKind.VertexShader,
        ShaderStage.Fragment => ShaderKind.FragmentShader,
        ShaderStage.Compute => ShaderKind.ComputeShader,
        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "Unknown shader stage."),
    };
}

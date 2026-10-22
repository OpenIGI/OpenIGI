using System.Diagnostics;
using OpenIGI.Shaders;

namespace OpenIGI.Tools.ShaderBuild;

/// <summary>Which form <c>slangc</c> is asked to produce.</summary>
public enum SlangTarget
{
    /// <summary>SPIR-V, for Vulkan.</summary>
    Spirv,

    /// <summary>WGSL, for WebGPU.</summary>
    Wgsl,

    /// <summary>DXIL at shader model 6.6, for Direct3D 12, through the pinned DXC.</summary>
    Dxil,
}

/// <summary>The files one compile produced.</summary>
/// <param name="Code">The compiled module: SPIR-V or DXIL bytes, or WGSL text as UTF-8.</param>
/// <param name="ReflectionJson">Slang's own reflection JSON, or <see langword="null"/> when not requested.</param>
public sealed record SlangOutput(byte[] Code, string? ReflectionJson);

/// <summary>Runs <c>slangc</c>, the one place the compiler's command line is written down.</summary>
/// <remarks>
/// <para>
/// Two flags are not optional and both were settled by measurement rather than by reading.
/// <c>-matrix-layout-column-major</c> makes <c>mul(M, v)</c> agree with the column-major matrices the
/// engine already uploads; without it a transform silently transposes. And the WGSL target is given
/// <c>OPENIGI_NO_PUSH_CONSTANTS</c> because Slang emits an unbound <c>var&lt;uniform&gt;</c> for a
/// push constant there, which no browser will accept.
/// </para>
/// <para>
/// <c>slangc</c> is deterministic for a given release: the same source compiles to the same bytes,
/// which is what lets the committed output be checked rather than merely regenerated.
/// </para>
/// </remarks>
public sealed class SlangCompiler
{
    private readonly string _executable;
    private readonly string? _dxcDirectory;

    /// <summary>Gets the warnings every compile so far has reported, in the order they arrived.</summary>
    public List<string> Warnings { get; } = [];

    /// <summary>The line separator slangc writes its diagnostics with.</summary>
    private const char Newline = (char)10;

    /// <summary>Initialises a new instance of the <see cref="SlangCompiler"/> class.</summary>
    /// <param name="executable">The full path to <c>slangc</c>.</param>
    /// <param name="dxcDirectory">
    /// The directory holding the pinned DXC's compiler library, which slangc hands DXIL to; null where no
    /// DXIL is compiled. Named explicitly so the Vulkan SDK's own DXC, a different build, is never picked up.
    /// </param>
    public SlangCompiler(string executable, string? dxcDirectory = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(executable);
        _executable = executable;
        _dxcDirectory = dxcDirectory;
    }

    /// <summary>Gets or sets the folders an <c>#include</c> is also looked for in, after the source's own.</summary>
    /// <remarks>A mod's shaders name the game's library here, for the contract they are written against.</remarks>
    public IReadOnlyList<string> IncludeDirectories { get; init; } = [];

    /// <summary>Gets the compiler's version string, as <c>slangc -v</c> reports it.</summary>
    /// <returns>The version string.</returns>
    public string Version()
    {
        // slangc writes its version to standard error, not standard output.
        (int exitCode, string standardOutput, string standardError) = Run(["-v"]);
        if (exitCode != 0)
        {
            throw new ShaderBuildException($"slangc -v failed ({exitCode}): {standardError}");
        }

        string reported = string.IsNullOrWhiteSpace(standardError) ? standardOutput : standardError;
        return reported.Trim();
    }

    /// <summary>Compiles one module variant for one target.</summary>
    /// <param name="sourcePath">The Slang source's full path.</param>
    /// <param name="target">The form to produce.</param>
    /// <param name="defines">Preprocessor defines for this variant.</param>
    /// <param name="withReflection">Whether to ask for Slang's reflection JSON as well.</param>
    /// <param name="workingDirectory">A directory the compiler may write its outputs into.</param>
    /// <param name="entryPoint">
    /// The one entry point to compile, and its stage; required for DXIL, where a pipeline takes one blob per
    /// stage and slangc writes a library of every entry point otherwise.
    /// </param>
    /// <returns>The compiled output.</returns>
    /// <exception cref="ShaderBuildException">The compile failed.</exception>
    public SlangOutput Compile(
        string sourcePath,
        SlangTarget target,
        IReadOnlyDictionary<string, string> defines,
        bool withReflection,
        string workingDirectory,
        ShaderEntryPoint? entryPoint = null)
    {
        if (target == SlangTarget.Dxil && entryPoint is null)
        {
            throw new ShaderBuildException("DXIL is compiled one entry point at a time; name it.");
        }

        ArgumentException.ThrowIfNullOrEmpty(sourcePath);
        ArgumentNullException.ThrowIfNull(defines);
        ArgumentException.ThrowIfNullOrEmpty(workingDirectory);

        string extension = target switch
        {
            SlangTarget.Spirv => "spv",
            SlangTarget.Wgsl => "wgsl",
            _ => "dxil",
        };
        string codePath = Path.Combine(workingDirectory, $"out.{extension}");
        string reflectionPath = Path.Combine(workingDirectory, "out.reflection.json");

        List<string> arguments =
        [
            sourcePath,
            "-target", target switch
            {
                SlangTarget.Spirv => "spirv",
                SlangTarget.Wgsl => "wgsl",
                _ => "dxil",
            },
            "-matrix-layout-column-major",

            // Keep the entry point's own name in the SPIR-V. Slang otherwise renames a module's ONLY
            // entry point to "main", so a module with one stage disagrees with the reflection that
            // names it -- and a pipeline asking for the name the reflection gave fails to create, with
            // nothing to say why. A module with two entry points keeps both names and would have hidden
            // this until the first single-stage module was migrated.
            "-fvk-use-entrypoint-name",
        ];

        foreach (string include in IncludeDirectories)
        {
            arguments.Add("-I");
            arguments.Add(include);
        }

        if (withReflection)
        {
            arguments.Add("-reflection-json");
            arguments.Add(reflectionPath);
        }

        // WGSL has no push constants; the shared include turns the block into a bound uniform
        // buffer when this is defined. See Source/Common/PushConstants.slang. The target's own name is
        // for a declaration that has to differ between the two, where a __target_switch cannot reach --
        // see Source/Common/ImageHistory.slang.
        if (target == SlangTarget.Wgsl)
        {
            arguments.Add("-DOPENIGI_NO_PUSH_CONSTANTS=1");
            arguments.Add("-DOPENIGI_TARGET_WGSL=1");
        }

        // Shader model 6.6 for the descriptor heap a traced hit reaches its mesh through and for derivatives
        // in compute. Specialisation constants become a constant buffer there, which is the design, so the
        // warning saying so is not one to read every build. And the DXC is the pinned one, never whatever
        // slangc would find beside itself.
        if (target == SlangTarget.Dxil)
        {
            if (_dxcDirectory is null)
            {
                throw new ShaderBuildException("DXIL was asked for with no DXC located; see DxcLocator.");
            }

            arguments.Add("-profile");
            arguments.Add("sm_6_6");
            arguments.Add("-dxc-path");
            arguments.Add(_dxcDirectory);
            arguments.Add("-warnings-disable");
            arguments.Add("39019");

            arguments.Add("-entry");
            arguments.Add(entryPoint!.Name);
            arguments.Add("-stage");
            arguments.Add(entryPoint.Stage switch
            {
                ShaderStage.Vertex => "vertex",
                ShaderStage.Fragment => "fragment",
                _ => "compute",
            });
        }

        foreach ((string key, string value) in defines)
        {
            arguments.Add($"-D{key}={value}");
        }

        // Last: slangc ties an output to the entry point named before it.
        arguments.Add("-o");
        arguments.Add(codePath);

        (int exitCode, string standardOutput, string standardError) = Run(arguments);
        if (exitCode != 0 || !File.Exists(codePath))
        {
            string message = string.IsNullOrWhiteSpace(standardError) ? standardOutput : standardError;
            throw new ShaderBuildException(
                $"slangc failed on {Path.GetFileName(sourcePath)} for {target} ({exitCode}):{Environment.NewLine}{message}");
        }

        // Slang reports an overlapping explicit binding as a warning, but on the WGSL target an
        // overlap is a module no browser will accept -- it is the combined-sampler trap. Treat it
        // as the error it is.
        string diagnostics = standardOutput + standardError;
        if (diagnostics.Contains("explicit binding overlap", StringComparison.Ordinal))
        {
            throw new ShaderBuildException(
                $"{Path.GetFileName(sourcePath)} has overlapping bindings, which is invalid on the WGSL "
                + $"target. Declare textures and samplers separately.{Environment.NewLine}{diagnostics}");
        }

        // Slang's warnings are worth reading rather than discarding. The one that matters most here is
        // E31105: a storage image format the WGSL backend does not support is silently WIDENED to one
        // it does, so the same module binds r8 on Vulkan and rgba32float in a browser. The reflection
        // is taken from the SPIR-V compile and records the narrow format, so a WebGPU backend building
        // layouts from it would disagree with its own shader.
        foreach (string line in diagnostics.Split(Newline, StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Contains("warning", StringComparison.OrdinalIgnoreCase))
            {
                Warnings.Add($"{Path.GetFileName(sourcePath)} [{target}] {line.Trim()}");
            }
        }

        byte[] code = File.ReadAllBytes(codePath);
        string? reflection = withReflection && File.Exists(reflectionPath)
            ? File.ReadAllText(reflectionPath)
            : null;
        return new SlangOutput(code, reflection);
    }

    private (int ExitCode, string StandardOutput, string StandardError) Run(IReadOnlyList<string> arguments)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = _executable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)
            ?? throw new ShaderBuildException($"Could not start {_executable}.");

        string standardOutput = process.StandardOutput.ReadToEnd();
        string standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, standardOutput, standardError);
    }
}

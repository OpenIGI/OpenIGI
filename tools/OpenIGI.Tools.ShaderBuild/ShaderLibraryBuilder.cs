using System.Text;
using System.Text.Json;
using OpenIGI.Shaders;

namespace OpenIGI.Tools.ShaderBuild;

/// <summary>What one artefact was found to be, comparing what is committed against a fresh compile.</summary>
public enum ArtifactState
{
    /// <summary>The committed file already matches.</summary>
    Unchanged,

    /// <summary>The committed file differs and was rewritten, or would be.</summary>
    Changed,

    /// <summary>There is no committed file and one was written, or would be.</summary>
    Added,
}

/// <summary>One generated artefact and what happened to it.</summary>
/// <param name="RelativePath">Its path under <c>Generated/</c>.</param>
/// <param name="State">What was found.</param>
public sealed record ArtifactResult(string RelativePath, ArtifactState State);

/// <summary>
/// Compiles every module and variant in the manifest for both targets and writes the results into
/// <c>Generated/</c>, or reports how the committed outputs differ.
/// </summary>
public sealed class ShaderLibraryBuilder
{
    private readonly SlangCompiler _compiler;
    private readonly string _projectDirectory;

    /// <summary>Initialises a new instance of the <see cref="ShaderLibraryBuilder"/> class.</summary>
    /// <param name="compiler">The Slang compiler to use.</param>
    /// <param name="projectDirectory">The <c>src/OpenIGI.Shaders</c> directory.</param>
    public ShaderLibraryBuilder(SlangCompiler compiler, string projectDirectory)
    {
        ArgumentNullException.ThrowIfNull(compiler);
        ArgumentException.ThrowIfNullOrEmpty(projectDirectory);
        _compiler = compiler;
        _projectDirectory = projectDirectory;
    }

    /// <summary>Gets the directory the compiled artefacts live in.</summary>
    public string GeneratedDirectory => Path.Combine(_projectDirectory, "Generated");

    /// <summary>Compiles the whole library.</summary>
    /// <param name="checkOnly">When true, nothing is written and differences are only reported.</param>
    /// <returns>One result per artefact, in the order they were produced.</returns>
    /// <exception cref="ShaderBuildException">A module failed to compile, or the manifest is unreadable.</exception>
    public IReadOnlyList<ArtifactResult> Build(bool checkOnly)
    {
        ShaderManifest manifest = ReadManifest();
        RequireThePinnedCompiler(manifest, checkOnly);
        List<ArtifactResult> results = [];

        string scratch = Path.Combine(Path.GetTempPath(), "openigi-shaderbuild-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);

        try
        {
            if (!checkOnly)
            {
                Directory.CreateDirectory(GeneratedDirectory);
            }

            foreach (ShaderModuleEntry module in manifest.Modules)
            {
                string sourcePath = Path.Combine(_projectDirectory, "Source", module.Source.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(sourcePath))
                {
                    throw new ShaderBuildException($"{module.Id}: the source {module.Source} does not exist.");
                }

                foreach (ShaderVariantEntry variant in module.Variants)
                {
                    results.AddRange(BuildVariant(module, variant, sourcePath, scratch, checkOnly));
                }
            }
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }

        // Only once every module has compiled: a pin recorded beside output that was never written
        // would name a compiler the committed artefacts did not come from.
        if (Pin is { } moved)
        {
            WritePin(manifest.SlangVersion, moved);
        }

        return results;
    }

    /// <summary>Moves the manifest's pin to the compiler this run used.</summary>
    /// <param name="previous">The version the manifest pinned.</param>
    /// <param name="current">The version that produced the output just written.</param>
    /// <exception cref="ShaderBuildException">The manifest does not carry the pin where it is expected.</exception>
    /// <remarks>
    /// One line, rewritten in place rather than the document re-serialised: the manifest is hand-kept and
    /// re-serialising it would reformat every module entry to say one string.
    /// </remarks>
    private void WritePin(string previous, string current)
    {
        string path = Path.Combine(_projectDirectory, ShaderManifest.FileName);
        string text = File.ReadAllText(path);
        string from = $"\"slangVersion\": \"{previous}\"";
        if (!text.Contains(from, StringComparison.Ordinal))
        {
            throw new ShaderBuildException(
                $"{ShaderManifest.FileName} does not carry slangVersion {previous} as written text, so the "
                + "pin cannot be moved in place.");
        }

        File.WriteAllText(path, text.Replace(from, $"\"slangVersion\": \"{current}\"", StringComparison.Ordinal));
    }

    private IEnumerable<ArtifactResult> BuildVariant(
        ShaderModuleEntry module,
        ShaderVariantEntry variant,
        string sourcePath,
        string scratch,
        bool checkOnly)
    {
        // Only the SPIR-V compile is asked for reflection. The two targets agree on binding numbers
        // and specialization identifiers -- which is exactly what declaring textures and samplers
        // separately buys -- so one table describes both, and a second would only be a second thing
        // to keep in step.
        SlangOutput spirv = _compiler.Compile(sourcePath, SlangTarget.Spirv, variant.Defines, withReflection: true, scratch);

        if (spirv.ReflectionJson is null)
        {
            throw new ShaderBuildException($"{module.Id}: slangc produced no reflection for variant '{variant.Name}'.");
        }

        ShaderReflection reflection = ReflectionReader.Read(module.Id, variant.Name, spirv.ReflectionJson);
        byte[] reflectionBytes = Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(reflection, ShaderJsonContext.Default.ShaderReflection));

        yield return Write($"{module.Id}.{variant.Name}.spv", spirv.Code, checkOnly);

        // A variant that traces rays has no WGSL form, because WGSL has no ray tracing.
        if (variant.Compiles(ShaderTarget.Wgsl))
        {
            RejectVulkanOnlyBindings(module.Id, variant.Name, reflection);

            SlangOutput wgsl = _compiler.Compile(sourcePath, SlangTarget.Wgsl, variant.Defines, withReflection: false, scratch);
            yield return Write($"{module.Id}.{variant.Name}.wgsl", NormaliseNewlines(wgsl.Code), checkOnly);
        }

        yield return Write($"{module.Id}.{variant.Name}.reflection.json", NormaliseNewlines(reflectionBytes), checkOnly);
    }

    /// <summary>Refuses a WGSL target for a module that declares something WGSL has no form for.</summary>
    /// <param name="module">The module.</param>
    /// <param name="variant">The variant's name.</param>
    /// <param name="reflection">What the module binds.</param>
    /// <exception cref="ShaderBuildException">The module cannot have a WGSL form.</exception>
    /// <remarks>
    /// Both of these are ordinary in Vulkan and absent from WebGPU: a combined image sampler is one
    /// descriptor where WGSL has a texture and a sampler apart, and a subpass input has no counterpart
    /// at all because WebGPU has no subpasses. A module that uses either is a Vulkan-only module and
    /// says so in the manifest; this is what makes that a build failure rather than something to
    /// remember.
    /// </remarks>
    private static void RejectVulkanOnlyBindings(
        ShaderModuleId module,
        string variant,
        ShaderReflection reflection)
    {
        foreach (ShaderBinding binding in reflection.Bindings)
        {
            string? reason = binding.Kind switch
            {
                ShaderBindingKind.CombinedImageSampler =>
                    "a combined image sampler, which WGSL has no form for -- declare the texture and "
                    + "its sampler apart",
                ShaderBindingKind.InputAttachment =>
                    "a subpass input, which WebGPU has no equivalent of -- read the attachment as a "
                    + "texture in a second pass",
                _ => null,
            };

            if (reason is not null)
            {
                throw new ShaderBuildException(
                    $"{module}: variant '{variant}' is compiled for WGSL but '{binding.Name}' is "
                    + $"{reason}. Give the variant a SPIR-V only target.");
            }
        }
    }

    /// <summary>
    /// Rewrites CRLF to LF in a text artefact.
    /// </summary>
    /// <param name="content">The artefact's bytes.</param>
    /// <returns>The same bytes with LF line endings.</returns>
    /// <remarks>
    /// The committed artefacts have to be byte-identical to what the tool produces, because that
    /// comparison is what the check mode is. Two things would otherwise break it: slangc writes
    /// CRLF on Windows and LF elsewhere, and .gitattributes stores this repository's text as LF, so
    /// a file committed from Windows comes back out of a fresh clone different from how it went in.
    /// Writing LF everywhere settles both. SPIR-V is binary and is never touched.
    /// </remarks>
    private static byte[] NormaliseNewlines(byte[] content)
    {
        // Written as the byte values they are: a carriage return and a line feed.
        const byte CarriageReturn = 13;
        const byte LineFeed = 10;

        if (Array.IndexOf(content, CarriageReturn) < 0)
        {
            return content;
        }

        List<byte> normalised = new(content.Length);
        for (int i = 0; i < content.Length; i++)
        {
            if (content[i] == CarriageReturn && i + 1 < content.Length && content[i + 1] == LineFeed)
            {
                continue;
            }

            normalised.Add(content[i]);
        }

        return [.. normalised];
    }

    private ArtifactResult Write(string fileName, byte[] content, bool checkOnly)
    {
        string path = Path.Combine(GeneratedDirectory, fileName);

        if (!File.Exists(path))
        {
            if (!checkOnly)
            {
                File.WriteAllBytes(path, content);
            }

            return new ArtifactResult(fileName, ArtifactState.Added);
        }

        if (File.ReadAllBytes(path).AsSpan().SequenceEqual(content))
        {
            return new ArtifactResult(fileName, ArtifactState.Unchanged);
        }

        if (!checkOnly)
        {
            File.WriteAllBytes(path, content);
        }

        return new ArtifactResult(fileName, ArtifactState.Changed);
    }

    /// <summary>Holds a check to the compiler the committed output was produced with.</summary>
    /// <param name="manifest">The manifest, which pins that compiler.</param>
    /// <param name="checkOnly">Whether this run only reports differences.</param>
    /// <exception cref="ShaderBuildException">A check ran against a different compiler.</exception>
    /// <remarks>
    /// Two RELEASES of slangc do not agree byte for byte about SPIR-V they both consider correct, so a
    /// check against the wrong one reports every artefact stale and says nothing about the sources. Two
    /// builds of one release do agree -- see <see cref="ShaderManifest.ReleaseOf"/> -- so the release is
    /// what is held to. A regenerate is where the pin legitimately moves, so there it is written down
    /// rather than enforced: the new version lands in the manifest beside the output it produced.
    /// </remarks>
    private void RequireThePinnedCompiler(ShaderManifest manifest, bool checkOnly)
    {
        string version = _compiler.Version();
        if (manifest.Accepts(version))
        {
            return;
        }

        if (checkOnly)
        {
            throw new ShaderBuildException(
                $"The committed output was produced with slangc {manifest.SlangVersion}, and this is "
                + $"{version}. Two releases do not agree byte for byte about SPIR-V they both accept, so "
                + "a check against a different one reports every artefact stale. Install the pinned "
                + "release, or regenerate (without --check) to move the pin.");
        }

        Pin = version;
    }

    /// <summary>Gets the compiler version a regenerate found, when it differs from the pinned one.</summary>
    /// <remarks>Null while the pin still holds, which is every run that did not move it.</remarks>
    public string? Pin { get; private set; }

    private ShaderManifest ReadManifest()
    {
        string path = Path.Combine(_projectDirectory, ShaderManifest.FileName);
        if (!File.Exists(path))
        {
            throw new ShaderBuildException($"No {ShaderManifest.FileName} at {path}.");
        }

        return JsonSerializer.Deserialize(File.ReadAllBytes(path), ShaderJsonContext.Default.ShaderManifest)
            ?? throw new ShaderBuildException($"{path} is empty.");
    }
}

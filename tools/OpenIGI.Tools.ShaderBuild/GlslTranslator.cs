using System.Runtime.InteropServices;
using System.Text;
using OpenIGI.Shaders;
using Silk.NET.Core.Contexts;
using Silk.NET.SPIRV;
using Silk.NET.SPIRV.Cross;
using CrossApi = Silk.NET.SPIRV.Cross.Cross;

namespace OpenIGI.Tools.ShaderBuild;

/// <summary>One stage of a variant's GLSL form.</summary>
/// <param name="Stage">The stage.</param>
/// <param name="Source">The GLSL 4.30 text, LF line endings, its entry point renamed <c>main</c> as GLSL requires.</param>
public sealed record GlslStage(ShaderStage Stage, string Source);

/// <summary>A variant's GLSL form: a text per stage, and the reflection with where each resource landed.</summary>
/// <param name="Stages">One text per entry point, in the reflection's order.</param>
/// <param name="Reflection">The reflection, its GLSL units filled in.</param>
public sealed record GlslForm(IReadOnlyList<GlslStage> Stages, ShaderReflection Reflection);

/// <summary>Translates a variant's SPIR-V into the GLSL 4.30 the OpenGL backend compiles.</summary>
/// <remarks>
/// <para>
/// Through SPIRV-Cross, from SPIR-V compiled without push constants (<see cref="SlangTarget.SpirvForGlsl"/>).
/// Not from Slang's own GLSL target: Slang keeps textures and samplers apart in GLSL, which only Vulkan's
/// dialect accepts. SPIRV-Cross joins every texture-and-sampler pair a stage reads into one
/// <c>sampler2D</c>, which is the only way OpenGL reads a texture.
/// </para>
/// <para>
/// OpenGL also has no descriptor sets: a buffer, an image and a texture unit are each numbered from nought in
/// a namespace of their own kind. So every resource is given a unit here, once for the whole module, so a
/// vertex and a fragment stage linked into one program agree on them, and the units are written into the
/// reflection for the backend to bind by.
/// </para>
/// <para>
/// Specialisation constants need nothing: SPIRV-Cross declares each as <c>SPIRV_CROSS_CONSTANT_ID_n</c> with
/// its default, and the backend defines the macro to change it. A draw's base vertex and base instance come
/// from <c>GL_ARB_shader_draw_parameters</c> where the driver has it and from two uniforms the backend sets
/// where it does not; SPIRV-Cross writes both forms.
/// </para>
/// </remarks>
public static unsafe class GlslTranslator
{
    private const uint GlslVersion = 430;

    /// <summary>Translates one variant.</summary>
    /// <param name="module">The module, for messages.</param>
    /// <param name="variant">The variant, for messages.</param>
    /// <param name="spirv">The variant compiled for <see cref="SlangTarget.SpirvForGlsl"/>.</param>
    /// <param name="reflection">The variant's reflection, from its Vulkan compile.</param>
    /// <returns>The GLSL form.</returns>
    /// <exception cref="ShaderBuildException">SPIRV-Cross refused it, or it binds something the reflection does not name.</exception>
    public static GlslForm Translate(ShaderModuleId module, string variant, ReadOnlySpan<byte> spirv, ShaderReflection reflection)
    {
        ArgumentNullException.ThrowIfNull(reflection);
        uint[] words = SpirvDepthImages.Clear(spirv);
        string where = $"{module}.{variant}";

        CrossApi api = OpenApi();
        Context* context = null;
        Check(api, null, api.ContextCreate(&context), where, "create a context");

        try
        {
            // One compiler per stage, kept alive until the units are known: the units have to cover every
            // pair any stage reads before any stage is written out.
            List<StageCompiler> stages = [];
            foreach (ShaderEntryPoint entryPoint in reflection.EntryPoints)
            {
                stages.Add(StageCompiler.Open(api, context, words, entryPoint, where));
            }

            GlslUnits units = GlslUnits.Assign(api, stages, reflection, where);

            List<GlslStage> sources = [];
            foreach (StageCompiler stage in stages)
            {
                sources.Add(new GlslStage(stage.EntryPoint.Stage, stage.Compile(api, units, where)));
            }

            return new GlslForm(sources, units.Apply(reflection));
        }
        finally
        {
            api.ContextDestroy(context);
        }
    }

    /// <summary>Opens SPIRV-Cross from the package's folder for this platform, beside the tool.</summary>
    /// <remarks>
    /// By its path, not by name: the binding looks for that folder under the distribution's own name
    /// (<c>ubuntu.24.04-x64</c>) and knows the way from there to <c>linux-x64</c> for some distributions only,
    /// so by name the library is not found on Ubuntu. Where the folder is not there, as in a published tool
    /// with the library beside it, the binding's own search is what is left.
    /// </remarks>
    private static CrossApi OpenApi()
    {
        string name = OperatingSystem.IsWindows() ? "spirv-cross.dll"
            : OperatingSystem.IsMacOS() ? "libspirv-cross.dylib"
            : "libspirv-cross.so";
        string path = Path.Combine(AppContext.BaseDirectory, "runtimes", RuntimeInformation.RuntimeIdentifier, "native", name);
        return File.Exists(path) ? new CrossApi(new DefaultNativeContext(path)) : CrossApi.GetApi();
    }

    /// <summary>Throws with SPIRV-Cross's own message where a call failed.</summary>
    internal static void Check(CrossApi api, Context* context, Result result, string where, string what)
    {
        if (result == Result.Success)
        {
            return;
        }

        string detail = context is null ? result.ToString() : api.ContextGetLastErrorStringS(context);
        throw new ShaderBuildException($"{where}: SPIRV-Cross could not {what}: {detail}");
    }

    /// <summary>One stage's compiler, with what it binds read off it.</summary>
    internal sealed class StageCompiler
    {
        private StageCompiler(Context* context, Compiler* compiler, ShaderEntryPoint entryPoint, uint dummySampler)
        {
            Context = context;
            Compiler = compiler;
            EntryPoint = entryPoint;
            DummySampler = dummySampler;
        }

        public Context* Context { get; }

        public Compiler* Compiler { get; }

        public ShaderEntryPoint EntryPoint { get; }

        /// <summary>Gets the sampler SPIRV-Cross invents for a texture read with no sampler, or nought for none.</summary>
        public uint DummySampler { get; }

        public static StageCompiler Open(CrossApi api, Context* context, uint[] words, ShaderEntryPoint entryPoint, string where)
        {
            ParsedIr* ir = null;
            fixed (uint* code = words)
            {
                Check(api, context, api.ContextParseSpirv(context, code, (nuint)words.Length, &ir), where, "parse the SPIR-V");
            }

            Compiler* compiler = null;
            Check(api, context, api.ContextCreateCompiler(context, Backend.Glsl, ir, CaptureMode.TakeOwnership, &compiler), where, "create a GLSL compiler");

            ExecutionModel model = entryPoint.Stage switch
            {
                ShaderStage.Vertex => ExecutionModel.Vertex,
                ShaderStage.Fragment => ExecutionModel.Fragment,
                _ => ExecutionModel.GLCompute,
            };

            byte[] name = Encoding.UTF8.GetBytes(entryPoint.Name + "\0");
            fixed (byte* entry = name)
            {
                Check(api, context, api.CompilerSetEntryPoint(compiler, entry, model), where, $"select the entry point {entryPoint.Name}");
            }

            // A texture only ever fetched is read through no sampler; SPIRV-Cross needs one to make a sampler2D
            // of it, and invents one, which this records so the pair it lands in says "no sampler".
            uint dummy = 0;
            Check(api, context, api.CompilerBuildDummySamplerForCombinedImages(compiler, &dummy), where, "add a sampler for fetched textures");
            Check(api, context, api.CompilerBuildCombinedImageSamplers(compiler), where, "join textures to their samplers");

            return new StageCompiler(context, compiler, entryPoint, dummy);
        }

        /// <summary>Gets the texture-and-sampler pairs this stage reads.</summary>
        public IEnumerable<CombinedImageSampler> Pairs(CrossApi api, string where)
        {
            CombinedImageSampler* pairs = null;
            nuint count = 0;
            Check(api, Context, api.CompilerGetCombinedImageSamplers(Compiler, &pairs, &count), where, "list the joined samplers");

            CombinedImageSampler[] copy = new CombinedImageSampler[(int)count];
            for (int i = 0; i < copy.Length; i++)
            {
                copy[i] = pairs[i];
            }

            return copy;
        }

        /// <summary>Gets the resources of one kind this stage declares.</summary>
        public IEnumerable<ReflectedResource> Resources(CrossApi api, ResourceType type, string where)
        {
            Resources* resources = null;
            Check(api, Context, api.CompilerCreateShaderResources(Compiler, &resources), where, "list the resources");

            ReflectedResource* list = null;
            nuint count = 0;
            Check(api, Context, api.ResourcesGetResourceListForType(resources, type, &list, &count), where, "list the resources");

            ReflectedResource[] copy = new ReflectedResource[(int)count];
            for (int i = 0; i < copy.Length; i++)
            {
                copy[i] = list[i];
            }

            return copy;
        }

        /// <summary>Gets where SPIR-V placed a resource: its descriptor set and binding.</summary>
        public (int Group, int Binding) Placement(CrossApi api, uint id) => (
            (int)api.CompilerGetDecoration(Compiler, id, Decoration.DescriptorSet),
            (int)api.CompilerGetDecoration(Compiler, id, Decoration.Binding));

        /// <summary>Writes the stage out with every resource at its unit.</summary>
        public string Compile(CrossApi api, GlslUnits units, string where)
        {
            foreach (ResourceType type in GlslUnits.BufferAndImageKinds)
            {
                foreach (ReflectedResource resource in Resources(api, type, where))
                {
                    Rebind(api, resource.Id, units.UnitOf(type, Placement(api, resource.Id), where));
                    MarkUnaliased(api, type, resource.Id);
                }
            }

            foreach (CombinedImageSampler pair in Pairs(api, where))
            {
                Rebind(api, pair.CombinedId, units.UnitOf(units.PairOf(this, api, pair, where), where));
            }

            CompilerOptions* options = null;
            Check(api, Context, api.CompilerCreateCompilerOptions(Compiler, &options), where, "create options");
            api.CompilerOptionsSetUint(options, CompilerOption.GlslVersion, GlslVersion);
            api.CompilerOptionsSetBool(options, CompilerOption.GlslES, 0);
            api.CompilerOptionsSetBool(options, CompilerOption.GlslVulkanSemantics, 0);

            // Binding qualifiers in the text, so nothing has to be set by name after linking.
            api.CompilerOptionsSetBool(options, CompilerOption.GlslEnable420PackExtension, 1);

            // gl_InstanceID starts at nought whatever the draw's first instance, where SPIR-V's InstanceIndex
            // starts at it; a draw reading its push block by instance depends on the difference.
            api.CompilerOptionsSetBool(options, CompilerOption.GlslSupportNonzeroBaseInstance, 1);

            // Depth stays [0, w] and y as SPIR-V has them: the backend sets glClipControl to match.
            api.CompilerOptionsSetBool(options, CompilerOption.FixupDepthConvention, 0);
            api.CompilerOptionsSetBool(options, CompilerOption.FlipVertexY, 0);
            Check(api, Context, api.CompilerInstallCompilerOptions(Compiler, options), where, "set options");

            byte* text = null;
            Check(api, Context, api.CompilerCompile(Compiler, &text), where, $"write the {EntryPoint.Stage} stage");
            string source = new((sbyte*)text);
            source = source.Replace("\r\n", "\n", StringComparison.Ordinal);
            return GlslSingleTripLoops.AsBranches(GlslShadowLookups.AtTopLevel(GlslConstantArrays.Hoist(source, EntryPoint.Stage)));
        }

        // SPIR-V takes a buffer or image to alias nothing unless it is decorated Aliased; GLSL takes the opposite
        // unless it is declared restrict, and SPIRV-Cross writes restrict only where the SPIR-V says Restrict
        // outright, which Slang never does. Without it the driver must assume any write may change any read, so it
        // can neither keep a read-only buffer's loads in the read-only cache nor hoist a load out of a loop: the
        // passes that walk the lamp grid and the cards ran at about half the speed of the same SPIR-V under Vulkan.
        private void MarkUnaliased(CrossApi api, ResourceType type, uint id)
        {
            if (type is ResourceType.StorageBuffer or ResourceType.StorageImage
                && api.CompilerHasDecoration(Compiler, id, Decoration.Aliased) == 0)
            {
                api.CompilerSetDecoration(Compiler, id, Decoration.Restrict, 0);
            }
        }

        private void Rebind(CrossApi api, uint id, int unit)
        {
            api.CompilerUnsetDecoration(Compiler, id, Decoration.DescriptorSet);
            api.CompilerSetDecoration(Compiler, id, Decoration.Binding, (uint)unit);
        }
    }
}

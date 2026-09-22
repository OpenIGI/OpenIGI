using System.Text.RegularExpressions;
using Xunit;

namespace OpenIGI.Shaders.Tests;

/// <summary>
/// The compute seam's binding blocks, which the macros and the C# constants describe twice.
/// </summary>
/// <remarks>
/// The backend is handed a kind and a slot and has to arrive at the binding the module declared. That
/// only works while both sides agree where each kind's block starts, and nothing else would notice if
/// they stopped: a storage buffer bound into an image's binding is a descriptor write of the wrong type,
/// which is a validation error at best and a wrong picture at worst.
/// </remarks>
public sealed partial class ComputeBindingSpaceTests
{
    /// <summary>The macros place each kind where the constants say it is.</summary>
    [Fact]
    public void The_macros_and_the_constants_agree()
    {
        string source = ShaderRepository.ReadSource("Common/ComputeBindings.slang");

        Assert.Contains("#define OPENIGI_COMPUTE_IMAGE(slot) [[vk::binding(slot, 0)]]",
            source, StringComparison.Ordinal);
        Assert.Equal(0, ComputeBindingSpace.ImageBase);

        Assert.Contains(
            $"#define OPENIGI_COMPUTE_BUFFER(slot) [[vk::binding((slot) + {ComputeBindingSpace.BufferBase}, 0)]]",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            $"#define OPENIGI_COMPUTE_TEXTURE(slot) [[vk::binding((slot) + {ComputeBindingSpace.TextureBase}, 0)]]",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            $"#define OPENIGI_COMPUTE_SCENE [[vk::binding({ComputeBindingSpace.SceneBinding}, 0)]]",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            $"#define OPENIGI_COMPUTE_GLOBALS [[vk::binding({ComputeBindingSpace.GlobalsBinding}, 0)]]",
            source,
            StringComparison.Ordinal);
    }

    /// <summary>The blocks do not overlap, and the slot is recoverable from the binding.</summary>
    [Fact]
    public void Each_kind_has_its_own_block()
    {
        int[] bases =
        [
            ComputeBindingSpace.ImageBase,
            ComputeBindingSpace.BufferBase,
            ComputeBindingSpace.TextureBase,
        ];

        Assert.Equal(bases.Length, bases.Distinct().Count());
        Assert.All(bases, b => Assert.Equal(0, b % ComputeBindingSpace.SlotsPerKind));

        for (int slot = 0; slot < ComputeBindingSpace.SlotsPerKind; slot++)
        {
            foreach (int start in bases)
            {
                Assert.Equal(slot, ComputeBindingSpace.SlotOf(start + slot));
            }
        }

        // And the two singletons sit past every block, so neither can be mistaken for a slot of one.
        Assert.True(ComputeBindingSpace.SceneBinding
            >= ComputeBindingSpace.TextureBase + ComputeBindingSpace.SlotsPerKind);
        Assert.True(ComputeBindingSpace.GlobalsBinding > ComputeBindingSpace.SceneBinding);
    }

    /// <summary>Every compute module the seam drives declares its resources through the macros.</summary>
    /// <remarks>
    /// A literal binding would compile and land wherever it was written, which the backend would then read
    /// back as a slot of whichever kind's block it happened to fall in.
    /// </remarks>
    [Fact]
    public void The_seam_driven_modules_declare_their_bindings_through_the_macros()
    {
        foreach (ShaderModuleEntry module in ShaderCatalog.Manifest.Modules)
        {
            if (!module.Source.StartsWith("Field/", StringComparison.Ordinal))
            {
                continue;
            }

            string source = ShaderRepository.ReadSource(module.Source);
            Assert.Contains("ComputeBindings.slang", source, StringComparison.Ordinal);
            Assert.DoesNotMatch(LiteralBinding(), source);
        }
    }

    [GeneratedRegex(@"\[\[vk::binding\(\d")]
    private static partial Regex LiteralBinding();
}

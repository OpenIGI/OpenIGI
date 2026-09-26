using OpenIGI.Rendering.Pipeline.Compute;
using OpenIGI.Shaders;
using Xunit;

namespace OpenIGI.Rendering.Pipeline.Tests;

/// <summary>
/// That a texture is taken for an in-place history only where the module declared one, read off the
/// compiled reflections the device itself reads.
/// </summary>
public sealed class ImageHistoriesTests
{
    [Fact]
    public void The_surface_cache_lightings_read_back_is_a_history()
    {
        ShaderReflection reflection = ShaderCatalog.GetReflection(ShaderModuleId.CardsLighting, new ShaderVariant("software"));

        ShaderBinding? image = ImageHistories.ImageOf(reflection, Texture(reflection, "uDirectHistory"));

        Assert.NotNull(image);
        Assert.Equal("uDirect", image.Name);
    }

    [Fact]
    public void Last_frames_positions_are_the_games_own_texture_not_a_copy_of_the_image()
    {
        // The temporal gather writes uPosition and reads uPositionHistory, which the game binds: named
        // like a history, placed like an ordinary texture.
        ShaderReflection reflection = ShaderCatalog.GetReflection(
            ShaderModuleId.CardsScreenGatherTemporal, new ShaderVariant("software"));

        Assert.Null(ImageHistories.ImageOf(reflection, Texture(reflection, "uPositionHistory")));
    }

    private static ShaderBinding Texture(ShaderReflection reflection, string name) =>
        reflection.Bindings.Single(binding =>
            binding.Kind == ShaderBindingKind.Texture && string.Equals(binding.Name, name, StringComparison.Ordinal));
}

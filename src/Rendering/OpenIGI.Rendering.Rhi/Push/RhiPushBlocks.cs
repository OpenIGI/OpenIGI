using System.Collections.Concurrent;
using OpenIGI.Shaders;

namespace OpenIGI.Rendering.Rhi.Push;

/// <summary>Where a target's forms of the library put the push block, and the largest block any module declares.</summary>
/// <param name="Group">The bind group the push block is bound as.</param>
/// <param name="Binding">The binding within that group.</param>
/// <param name="LargestBlock">The largest push block any module declares, which sizes one slice of a ring.</param>
public readonly record struct RhiPushPlacement(int Group, int Binding, int LargestBlock);

/// <summary>
/// Where the shader library puts push data on a target that has no push constants to put it in.
/// </summary>
/// <remarks>
/// <para>
/// Neither WGSL nor OpenGL's GLSL has anything like a push constant, so the library compiles the push block for
/// both as an ordinary uniform in a group of its own (<c>Source/Common/PushConstants.slang</c>). Which group is the
/// compiler's choice, not a backend's, so it is read from the compiled modules -- and a library whose modules
/// disagreed about where it goes would be a library no single pipeline layout could serve.
/// </para>
/// <para>
/// Every module in the library today puts it at group 3, binding 0.
/// </para>
/// </remarks>
public static class RhiPushBlocks
{
    private static readonly ConcurrentDictionary<ShaderTarget, RhiPushPlacement> Placements = new();

    /// <summary>Finds where a target's forms put the push block.</summary>
    /// <param name="target">The target, one compiled without push constants.</param>
    /// <returns>The placement, read once per target.</returns>
    /// <exception cref="InvalidOperationException">Two modules put the block in different places.</exception>
    public static RhiPushPlacement For(ShaderTarget target) => Placements.GetOrAdd(target, Find);

    private static RhiPushPlacement Find(ShaderTarget target)
    {
        (int Group, int Binding)? placement = null;
        string? declaredBy = null;
        int largest = 0;

        foreach (ShaderModuleEntry module in ShaderCatalog.Manifest.Modules)
        {
            foreach (ShaderVariantEntry variant in module.Variants)
            {
                if (!variant.Compiles(target))
                {
                    continue;
                }

                ShaderPushBlock? push = ShaderCatalog
                    .GetReflection(module.Id, new ShaderVariant(variant.Name))
                    .PushBlock;

                if (push is null)
                {
                    continue;
                }

                largest = Math.Max(largest, push.Size);
                string where = $"{module.Id}.{variant.Name}";

                if (placement is { } already && already != (push.Group, push.Binding))
                {
                    throw new InvalidOperationException(
                        $"The shader library puts its push block at group {already.Group} binding "
                        + $"{already.Binding} in {declaredBy} and at group {push.Group} binding "
                        + $"{push.Binding} in {where}. One pipeline layout cannot serve both.");
                }

                placement = (push.Group, push.Binding);
                declaredBy = where;
            }
        }

        // A library with no push blocks at all still needs an answer, and the group after the ones a
        // pipeline uses is the one that cannot collide with anything.
        (int group, int binding) = placement ?? (3, 0);
        return new RhiPushPlacement(group, binding, largest);
    }
}

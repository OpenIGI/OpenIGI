using System.Runtime.InteropServices;

namespace OpenIGI.Rendering.Rhi;

/// <summary>One indexed draw's arguments as an indirect call reads them: the layout both APIs share.</summary>
/// <param name="IndexCount">How many indices.</param>
/// <param name="InstanceCount">How many instances.</param>
/// <param name="FirstIndex">The first index.</param>
/// <param name="BaseVertex">What is added to every index.</param>
/// <param name="FirstInstance">The first instance.</param>
/// <remarks>
/// <c>VkDrawIndexedIndirectCommand</c> and <c>D3D12_DRAW_INDEXED_ARGUMENTS</c> are the same five 32-bit fields
/// in the same order, so one array serves either.
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly record struct RhiDrawIndexedArguments(
    uint IndexCount,
    uint InstanceCount,
    uint FirstIndex,
    int BaseVertex,
    uint FirstInstance)
{
    /// <summary>The bytes one draw's arguments take, which is also the stride between draws.</summary>
    public const int Size = 20;
}

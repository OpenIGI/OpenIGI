using System.Runtime.InteropServices;
using OpenIGI.Core.Numerics;
using Silk.NET.Vulkan;

namespace OpenIGI.Rendering.Vulkan.RayTracing;

/// <summary>
/// One placement of a bottom-level structure in the world, in the exact 64-byte layout the driver reads.
/// </summary>
/// <remarks>
/// <para>
/// The top-level build reads this array straight out of memory, so the layout is fixed by the
/// specification rather than by convenience: a row-major three-by-four transform, then two packed words
/// carrying a 24-bit value and an 8-bit one each, then the bottom-level structure's device address. It
/// is declared here rather than assembled by hand at each call site so the packing is written once.
/// </para>
/// <para>
/// The two packed words are where a hit gets its identity. The custom index is whatever the application
/// wants to find the instance again by — a scene index the shading looks a material up from — and the
/// mask is what lets one traversal ignore instances another cares about, so a shadow ray can skip
/// geometry that casts none without a second structure.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal struct VulkanRayTracingInstance
{
    /// <summary>The size of one instance descriptor in bytes, fixed by the specification.</summary>
    public const int Size = 64;

    /// <summary>The mask value for geometry every ray should see.</summary>
    public const byte VisibleToAll = 0xFF;

    /// <summary>The mask value for the first-person rig, which only a reflection may see.</summary>
    /// <remarks>
    /// <para>
    /// The weapon and hands are real geometry standing half a metre from the eye, and a reflection is the
    /// one thing that has any business finding them there. Every other trace in the renderer would be
    /// made worse by them: the sun's would lay the shadow of a floating rifle on the ground in front of a
    /// player who has no body to hold it, the lamps' would do the same indoors, and the indirect gather
    /// would have a hand-sized occluder permanently wrapped around the camera, darkening whatever the
    /// player happens to be facing.
    /// </para>
    /// <para>
    /// So the rig goes into the same structure as everything else -- there is only one, and a second one
    /// would have to be built and kept in step -- and is separated by the mask instead. It takes the top
    /// bit alone; the world keeps <see cref="VisibleToAll"/>, so every ray that asks for
    /// <see cref="ExcludingFirstPerson"/> still sees all of it and none of the rig. The reference divides
    /// its own scene the same way and for the same reason: see its first-person world-space mask.
    /// </para>
    /// </remarks>
    public const byte FirstPersonOnly = 0x80;

    /// <summary>The cull mask for a ray that must not find the first-person rig, which is most of them.</summary>
    public const uint ExcludingFirstPerson = 0x7F;

    /// <summary>The row-major three-by-four transform, twelve floats.</summary>
    public TransformMatrixKHR Transform;

    /// <summary>The custom index in the low 24 bits and the visibility mask in the high 8.</summary>
    public uint InstanceCustomIndexAndMask;

    /// <summary>The hit-group record offset in the low 24 bits and the instance flags in the high 8.</summary>
    public uint InstanceShaderBindingTableRecordOffsetAndFlags;

    /// <summary>The device address of the bottom-level structure this instance places.</summary>
    public ulong AccelerationStructureReference;

    /// <summary>Builds an instance descriptor.</summary>
    /// <param name="transform">The placement, as a world matrix.</param>
    /// <param name="customIndex">
    /// What a hit will report as the instance's identity; at most 24 bits. The scene index the shading
    /// looks a material up from.
    /// </param>
    /// <param name="mask">Which rays may see this instance.</param>
    /// <param name="hitGroupOffset">Which shader binding table record the instance's hits start at.</param>
    /// <param name="flags">How traversal should treat the instance.</param>
    /// <param name="structureAddress">The device address of the bottom-level structure.</param>
    /// <returns>The packed descriptor.</returns>
    public static VulkanRayTracingInstance Create(
        in Matrix4f transform,
        uint customIndex,
        byte mask,
        uint hitGroupOffset,
        GeometryInstanceFlagsKHR flags,
        ulong structureAddress)
    {
        return new VulkanRayTracingInstance
        {
            Transform = ToRowMajor(transform),
            InstanceCustomIndexAndMask = (customIndex & 0x00FFFFFFu) | ((uint)mask << 24),
            InstanceShaderBindingTableRecordOffsetAndFlags =
                (hitGroupOffset & 0x00FFFFFFu) | ((uint)flags << 24),
            AccelerationStructureReference = structureAddress,
        };
    }

    /// <summary>Converts a world matrix into the three-by-four the build reads.</summary>
    /// <param name="matrix">The world matrix.</param>
    /// <returns>The transform in the layout the driver expects.</returns>
    /// <remarks>
    /// A straight copy of the first three rows. The engine stores its matrices row-major with vectors as
    /// columns, so a transform is <c>M·v</c> and the translation sits in the fourth column — which is
    /// exactly the convention a Vulkan instance transform uses. Only the fourth row is dropped: an
    /// instance transform is affine by definition, so that row is always (0,&#160;0,&#160;0,&#160;1) and
    /// is implied rather than stored.
    /// </remarks>
    public static unsafe TransformMatrixKHR ToRowMajor(in Matrix4f matrix)
    {
        TransformMatrixKHR result = default;
        float* m = result.Matrix;

        m[0] = matrix.M11;
        m[1] = matrix.M12;
        m[2] = matrix.M13;
        m[3] = matrix.M14;

        m[4] = matrix.M21;
        m[5] = matrix.M22;
        m[6] = matrix.M23;
        m[7] = matrix.M24;

        m[8] = matrix.M31;
        m[9] = matrix.M32;
        m[10] = matrix.M33;
        m[11] = matrix.M34;

        return result;
    }
}

using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Mesh3D;
using OpenIGI.Rendering.Pipeline.Mesh3D;
using OpenIGI.Rendering.Rhi;

namespace OpenIGI.Rendering.Pipeline;

/// <summary>Runs of copies of one mesh range, drawn with the work they share done once.</summary>
public sealed partial class GraphicsDevice
{
    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// On the direct path the first copy is an ordinary draw, and every copy after it is only its block, its material where
    /// its lightmap differs, and the draw (<see cref="Mesh3DPass.Redraw"/>): the mesh, the detail map, the pipeline, the
    /// environment and the buffers are the first copy's, because nothing that chooses them differs within the run. A copy's
    /// own sun goes into the kept forward state directly, rather than marking it stale and building it whole again.
    /// </para>
    /// <para>
    /// While a scene is being captured, a shadow cascade drawn or cards captured, each copy takes the draw it always has,
    /// <see cref="MeshInstances.DrawEach"/>: those paths each do more for a draw than this shares.
    /// </para>
    /// </remarks>
    public void DrawMeshInstances(
        MeshHandle mesh,
        int indexOffset,
        int indexCount,
        TextureHandle diffuse,
        BlendMode blend,
        ReadOnlySpan<MeshInstance> instances,
        bool setsVertexSun = false)
    {
        ThrowIfDisposed();

        if (instances.IsEmpty || indexCount <= 0)
        {
            return;
        }

        if (DrawingShadow || CapturingCards || Capturing)
        {
            MeshInstances.DrawEach(this, mesh, indexOffset, indexCount, diffuse, blend, instances, setsVertexSun);
            return;
        }

        MeshDrawCall call = Call(mesh, indexOffset, indexCount, diffuse, instances[0].Lightmap, blend);
        (bool reflective, IRhiBindGroup environment) = EnvironmentFor();
        IRhiRenderPipeline pipeline = _pipelines.ForState(_depthState, _cullMode, blend, _alphaReference, reflective);
        IRhiRenderPassEncoder pass = Pass();
        Span<float> push = stackalloc float[ForwardPushBlock.Floats];

        for (int i = 0; i < instances.Length; i++)
        {
            ref readonly MeshInstance instance = ref instances[i];
            if (setsVertexSun)
            {
                UseVertexSun(instance.VertexSun);
            }

            WriteForward(push, instance.ModelView, blend, instance.Lightmap.IsValid, exposure: 1f);
            ReadOnlySpan<byte> block = System.Runtime.InteropServices.MemoryMarshal.AsBytes(push);
            if (i == 0)
            {
                if (!_meshes.Draw(pass, pipeline, call, block, _frameCounter, environment))
                {
                    // A range the mesh does not hold is dropped for every copy alike.
                    return;
                }

                continue;
            }

            _meshes.Redraw(pass, call with { Lightmap = instance.Lightmap }, block);
        }
    }

    // SetVertexSun for a copy in a run: the kept forward state takes the new sun as it is, rather than being built again
    // from every field because one changed.
    private void UseVertexSun(Vector3f towardsSun)
    {
        if (towardsSun == _vertexSun)
        {
            return;
        }

        _vertexSun = towardsSun;
        if (!_forwardStateStale)
        {
            _forwardState = _forwardState with { VertexSun = towardsSun };
        }
    }
}

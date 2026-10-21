using System.Runtime.InteropServices;
using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Rendering.Pipeline.Mesh3D;
using OpenIGI.Rendering.Pipeline.Targets;

namespace OpenIGI.Rendering.Pipeline;

/// <summary>I.G.I. 2's water, drawn by its own pipeline over the frame or over the scene's lit image.</summary>
/// <remarks>
/// Nothing of a sheet is in the G-buffer, a shadow cascade or a card: the original draws water once, in the
/// forward pass, after the opaque world.
/// </remarks>
public sealed partial class GraphicsDevice
{
    private WaterPipelineCache? _waterPipelines;
    private WaterPipelineCache? _litWaterPipelines;

    /// <inheritdoc/>
    public void DrawWater(
        MeshHandle mesh,
        int indexOffset,
        int indexCount,
        in Matrix4f modelView,
        TextureHandle cube,
        in WaterShading shading)
    {
        ThrowIfDisposed();

        // WaterRender_Draw returns before drawing a sheet whose reflection is not valid.
        if (indexCount <= 0
            || DrawingShadow
            || CapturingCards
            || !cube.IsValid
            || !_cubes.TryGetValue(cube.Id, out EnvironmentCube reflection))
        {
            return;
        }

        MeshDrawCall call = Call(mesh, indexOffset, indexCount, TextureHandle.None, TextureHandle.None, BlendMode.SourcePlusAttenuatedDestination);
        Span<float> push = stackalloc float[WaterPipelineCache.PushDataBytes / sizeof(float)];

        if (Capturing)
        {
            if (CurrentPass is not ScenePass.Forward || _scene is not { } scene)
            {
                return;
            }

            TakeColourBeforeTransparency(scene, transparent: true);
            if (_scenePass is not { } scenePass)
            {
                return;
            }

            _litWaterPipelines ??= new WaterPipelineCache(
                _device,
                _materialLayout,
                _cubeLayout,
                SceneTargetSet.FormatOf(SceneTargetId.Lit),
                SceneTargetSet.FormatOf(SceneTargetId.Depth),
                linearScene: true,
                _retired);

            WriteWater(push, modelView, shading, 1f / MathF.Max(_sceneExposure, 1e-3f));
            _meshes.Draw(scenePass, _litWaterPipelines.For(shading), call, MemoryMarshal.AsBytes(push), _frameCounter, reflection.Group);
            return;
        }

        _waterPipelines ??= new WaterPipelineCache(
            _device, _materialLayout, _cubeLayout, PresentFormat, DepthFormat, linearScene: false, _retired);

        WriteWater(push, modelView, shading, 1f);
        _meshes.Draw(Pass(), _waterPipelines.For(shading), call, MemoryMarshal.AsBytes(push), _frameCounter, reflection.Group);
    }

    // The water shader's block: its transform, then the eye and the reflection's floor, the water's colour and
    // the output's scale, its glint's colour and the fog's density, the fog's colour and the normal's sign.
    private void WriteWater(Span<float> push, in Matrix4f modelView, in WaterShading shading, float scale)
    {
        (_projection * modelView).CopyColumnMajorTo(push[..16]);

        push[16] = shading.Eye.X;
        push[17] = shading.Eye.Y;
        push[18] = shading.Eye.Z;
        push[19] = shading.MinimumFacing;

        push[20] = shading.Diffuse.X;
        push[21] = shading.Diffuse.Y;
        push[22] = shading.Diffuse.Z;
        push[23] = scale;

        push[24] = shading.Specular.X;
        push[25] = shading.Specular.Y;
        push[26] = shading.Specular.Z;
        push[27] = _fog.DensityPerMetre;

        // WaterRender_SetGenericState fogs against the objects' fog colour, whatever the blend.
        push[28] = _fog.Colour.X;
        push[29] = _fog.Colour.Y;
        push[30] = _fog.Colour.Z;

        // WaterRender_vNormalScale: the normals turn over under the surface.
        push[31] = shading.IsAboveWater ? 1f : -1f;
    }

    private void DisposeWater()
    {
        _waterPipelines?.Dispose();
        _waterPipelines = null;
    }

    private void DisposeLitWater()
    {
        _litWaterPipelines?.Dispose();
        _litWaterPipelines = null;
    }
}

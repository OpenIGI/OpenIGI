using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Rendering;

/// <summary>
/// Draws the retail <c>Explosion0</c>: its <c>1006_01_1</c> FireBalls and its <c>1009_01_1</c> fragments, as a title's
/// simulation has them (<see cref="IExplosionScene"/>).
/// </summary>
/// <remarks>
/// Owns the two authored effect models and their textures; what is drawn, and where, is the simulation's. The smoke is
/// particle systems, which the particle drawer shows.
/// </remarks>
public sealed class RetailExplosionEffects
{
    private const float UnitsPerMetre = (float)WorldScene.UnitsPerMetre;

    // A FireBall's size is stored over 2048 (0x480060), and its depth never falls below a hundredth.
    private const float RetailFireballSizeDivisor = 2048f;
    private const float RetailFireballDepthFloor = 0.0099999998f;
    private const string RetailFragmentModelName = "1009_01_1";
    private const string RetailFireballModelName = "1006_01_1";

    private readonly IGraphicsDevice _device;
    private readonly ResourceRegistry? _resources;
    private MeshGeometry? _retailFragmentGeometry;
    private MeshHandle _retailFragmentMesh = MeshHandle.None;
    private TextureHandle _retailFragmentTexture = TextureHandle.None;
    private BlendMode _retailFragmentBlend = BlendMode.Opaque;
    private MeshGeometry? _retailFireballGeometry;
    private MeshHandle _retailFireballMesh = MeshHandle.None;
    private TextureHandle[] _retailFireballFrameTextures = [];
    private BlendMode _retailFireballBlend = BlendMode.Additive;

    /// <summary>Initializes the drawer over a device and the bound resources.</summary>
    /// <param name="device">The device the models upload to and draw with.</param>
    /// <param name="resources">The bound retail resources the model textures load from.</param>
    public RetailExplosionEffects(IGraphicsDevice device, ResourceRegistry? resources)
    {
        _device = device;
        _resources = resources;
    }

    /// <summary>Gets whether the IGI1 common pack supplied the native <c>1009_01_1</c> fragment model.</summary>
    public bool UsesRetailExplosionModel => _retailFragmentGeometry is not null
        && _retailFragmentTexture.IsValid;

    /// <summary>Gets whether the common pack supplied the native 1006 FireBall model.</summary>
    public bool UsesRetailFireballModel => _retailFireballGeometry is not null
        && _retailFireballFrameTextures.Length > 0;

    /// <summary>Gets how many animation frames the FireBall's texture has, for parity tests.</summary>
    public int RetailFireballModelFrameCount => _retailFireballFrameTextures.Length;

    /// <summary>Gets the blend the FireBall model's face group asks for.</summary>
    public BlendMode RetailFireballBlend => _retailFireballBlend;

    /// <summary>Gets the blend the fragment model's face group asks for.</summary>
    public BlendMode RetailFragmentBlend => _retailFragmentBlend;

    /// <summary>Binds the authored fragment and FireBall models from the mission's packs.</summary>
    /// <param name="mission">The loaded IGI1 mission whose common pack owns the effect.</param>
    /// <returns><see langword="true"/> when <c>1009_01_1</c> and its texture resolved.</returns>
    public bool Load(LoadedMission mission)
    {
        ReleaseRetailFragmentModel();
        ReleaseRetailFireballModel();
        if (_resources is null)
        {
            return false;
        }

        foreach (LoadedPack pack in new[] { mission.CommonPack, mission.LevelPack })
        {
            foreach (MtpInstance instance in pack.Instances)
            {
                bool isFragment = string.Equals(instance.Name, RetailFragmentModelName, StringComparison.OrdinalIgnoreCase);
                bool isFireball = string.Equals(instance.Name, RetailFireballModelName, StringComparison.OrdinalIgnoreCase);
                if ((!isFragment && !isFireball)
                    || !pack.TryGetModel(instance, out MefModel? model)
                    || model is null)
                {
                    continue;
                }

                MeshGeometry? geometry = MefMeshBuilder.Build(model, 1f / UnitsPerMetre);
                if (geometry is null || !pack.TryGetTextureName(instance, 0, out string textureName))
                {
                    continue;
                }

                BlendMode blend = BlendOf(model);
                if (isFragment)
                {
                    TextureHandle texture = SpriteStrip.LoadTexture(_device, _resources, textureName);
                    if (!texture.IsValid)
                    {
                        continue;
                    }

                    ReleaseRetailFragmentModel();
                    _retailFragmentGeometry = geometry;
                    _retailFragmentTexture = texture;
                    _retailFragmentBlend = blend;
                }
                else
                {
                    TextureHandle[] frames = LoadRetailAnimationFrames(textureName);
                    if (frames.Length == 0)
                    {
                        continue;
                    }

                    ReleaseRetailFireballModel();
                    _retailFireballGeometry = geometry;
                    _retailFireballFrameTextures = frames;
                    _retailFireballBlend = blend;
                }
            }
        }

        return UsesRetailExplosionModel;
    }

    /// <summary>Draws the live fragments, then the started FireBalls.</summary>
    /// <param name="camera">The eye.</param>
    /// <param name="projection">The world projection.</param>
    /// <param name="alpha">The frame's position between the last two ticks, 0–1.</param>
    /// <param name="scene">The title's explosions.</param>
    public void Draw(IExplosionScene scene, in Camera3D camera, in Matrix4f projection, float alpha)
    {
        ArgumentNullException.ThrowIfNull(scene);
        DrawRetailFragments(scene, camera, projection, alpha);
        DrawRetailFireballs(scene, camera, projection);
    }

    /// <summary>Releases both effect models.</summary>
    public void Release()
    {
        ReleaseRetailFragmentModel();
        ReleaseRetailFireballModel();
    }

    /// <summary>Draws the thrown <c>1009_01_1</c> pieces as the ordinary models they are.</summary>
    /// <remarks>
    /// A fragment is a <c>SimpointObj</c>, drawn by the plain model path: its face group is not
    /// transparent (<c>0x49DF60</c>), so it is lit and opaque, a small dark rock.
    /// </remarks>
    private void DrawRetailFragments(IExplosionScene scene, in Camera3D camera, in Matrix4f projection, float alpha)
    {
        if (scene.FragmentGroups.Count == 0 || _retailFragmentGeometry is null || !_retailFragmentTexture.IsValid)
        {
            return;
        }

        try
        {
            if (!_retailFragmentMesh.IsValid)
            {
                _retailFragmentMesh = _device.CreateMesh(
                    _retailFragmentGeometry.Vertices,
                    _retailFragmentGeometry.Indices);
            }

            bool blended = _retailFragmentBlend != BlendMode.Opaque;
            _device.SetProjection(projection);
            _device.SetCullMode(CullMode.None);
            _device.SetDepthState(blended ? DepthState.TestOnly : DepthState.TestAndWrite);
            _device.SetMaterialOverride(_retailFragmentBlend == BlendMode.Additive ? ImpactParticleRenderer.GlowMaterial : SurfaceMaterial.Default);

            foreach (ExplosionFragment fragment in scene.FragmentGroups.SelectMany(group => group.Flying))
            {
                Vector3d positionUnits = Vector3d.Lerp(fragment.PreviousPosition, fragment.Position, alpha);
                float[] o = fragment.Orientation;
                Matrix3f orientation = Matrix3f.FromRows(
                    new Vector3f(o[0], o[1], o[2]),
                    new Vector3f(o[3], o[4], o[5]),
                    new Vector3f(o[6], o[7], o[8]));
                Matrix4f modelView = ScaleModelView(
                    camera.CreateModelView(positionUnits / WorldScene.UnitsPerMetre, orientation),
                    fragment.Scale);

                foreach (MeshSubset subset in _retailFragmentGeometry.Subsets)
                {
                    if (subset.IndexCount == 0)
                    {
                        continue;
                    }

                    _device.DrawMesh(
                        _retailFragmentMesh,
                        subset.IndexOffset,
                        subset.IndexCount,
                        modelView,
                        _retailFragmentTexture,
                        TextureHandle.None,
                        _retailFragmentBlend);
                }
            }
        }
        catch (Exception ex) when (ex is ArgumentException or GraphicsDeviceException)
        {
            // The effect is optional presentation. A backend upload failure must not stop gameplay.
            _ = ex;
        }
        finally
        {
            _device.SetDepthState(DepthState.TestAndWrite);
            _device.SetMaterialOverride(SurfaceMaterial.Default);
        }
    }

    /// <summary>Draws every started FireBall the way <c>Explosion0_FireBallEnterHandler</c> does.</summary>
    /// <remarks>
    /// <para>
    /// The dome is turned so its axis points at the eye and scaled by its size over 2048 across, while
    /// its depth grows from nothing, <c>(1 - remaining / life) * size + 0.01</c>. Its texture is the
    /// model's animation (<c>Mesh3D_SetAnimTextureFrame</c>), stepped through once over its life.
    /// </para>
    /// <para>
    /// The face group is transparent and additive, so it draws at <c>ONE, ONE</c>, unlit and unfogged
    /// (<c>0x49EC70</c>): the black around the flame adds nothing. Drawn alpha-blended instead, every
    /// card was an opaque square that the fog lightened into the box around the blast.
    /// </para>
    /// </remarks>
    private void DrawRetailFireballs(IExplosionScene scene, in Camera3D camera, in Matrix4f projection)
    {
        if (scene.FireBalls.Count == 0 || _retailFireballGeometry is null || _retailFireballFrameTextures.Length == 0)
        {
            return;
        }

        try
        {
            if (!_retailFireballMesh.IsValid)
            {
                _retailFireballMesh = _device.CreateMesh(
                    _retailFireballGeometry.Vertices,
                    _retailFireballGeometry.Indices);
            }

            _device.SetProjection(projection);
            _device.SetCullMode(CullMode.None);
            _device.SetDepthState(_retailFireballBlend == BlendMode.Opaque ? DepthState.TestAndWrite : DepthState.TestOnly);
            _device.SetMaterialOverride(_retailFireballBlend == BlendMode.Additive ? ImpactParticleRenderer.GlowMaterial : SurfaceMaterial.Default);

            int frames = _retailFireballFrameTextures.Length;
            foreach (ExplosionFireBall fireball in scene.FireBalls)
            {
                if (fireball.Delay > 0 || fireball.TotalLifetime <= 0 || fireball.AnimationDuration <= 0)
                {
                    continue;
                }

                // 0x4806E0 steps the texture once over the FireBall's animation; the game's FPU chops the frame.
                int frame = (int)((double)(fireball.TotalLifetime - fireball.Lifetime) / fireball.AnimationDuration * frames) % frames;
                float across = fireball.Size / RetailFireballSizeDivisor;
                float depth = ((1f - ((float)fireball.Lifetime / fireball.TotalLifetime)) * across)
                    + RetailFireballDepthFloor;
                Vector3d positionMetres = fireball.Position / WorldScene.UnitsPerMetre;
                Matrix4f modelView = camera.CreateModelView(
                    positionMetres,
                    FacingEye(positionMetres, camera.Position, across, depth));

                foreach (MeshSubset subset in _retailFireballGeometry.Subsets)
                {
                    if (subset.IndexCount == 0)
                    {
                        continue;
                    }

                    _device.DrawMesh(
                        _retailFireballMesh,
                        subset.IndexOffset,
                        subset.IndexCount,
                        modelView,
                        _retailFireballFrameTextures[frame],
                        TextureHandle.None,
                        _retailFireballBlend);
                }
            }
        }
        catch (Exception ex) when (ex is ArgumentException or GraphicsDeviceException)
        {
            _ = ex;
        }
        finally
        {
            _device.SetDepthState(DepthState.TestAndWrite);
            _device.SetMaterialOverride(SurfaceMaterial.Default);
        }
    }

    /// <summary>Builds the FireBall's eye-facing, scaled orientation.</summary>
    /// <remarks>
    /// The rows are the model's axes: across the view <c>(-f.y, f.x, 0)</c>, then <c>f x across</c>,
    /// then the direction to the eye <c>f</c>, which is the dome's own axis.
    /// </remarks>
    internal static Matrix3f FacingEye(Vector3d positionMetres, Vector3d eyeMetres, float across, float depth)
    {
        Vector3f toEye = new(
            (float)(eyeMetres.X - positionMetres.X),
            (float)(eyeMetres.Y - positionMetres.Y),
            (float)(eyeMetres.Z - positionMetres.Z));
        Vector3f f = toEye.LengthSquared > 0f ? toEye.Normalized() : new Vector3f(1f, 0f, 0f);
        Vector3f side;
        Vector3f up;
        if (f.X == 0f && f.Y == 0f)
        {
            side = new Vector3f(1f, 0f, 0f);
            up = new Vector3f(0f, f.Z > 0f ? 1f : -1f, 0f);
        }
        else
        {
            side = new Vector3f(-f.Y, f.X, 0f).Normalized();
            up = Vector3f.Cross(f, side).Normalized();
        }

        return Matrix3f.FromRows(side * across, up * across, f * depth);
    }

    private static Matrix4f ScaleModelView(in Matrix4f rigid, float scale) => new(
        rigid.M11 * scale, rigid.M12 * scale, rigid.M13 * scale, rigid.M14,
        rigid.M21 * scale, rigid.M22 * scale, rigid.M23 * scale, rigid.M24,
        rigid.M31 * scale, rigid.M32 * scale, rigid.M33 * scale, rigid.M34,
        rigid.M41, rigid.M42, rigid.M43, rigid.M44);

    private void ReleaseRetailFragmentModel()
    {
        if (_retailFragmentMesh.IsValid)
        {
            _device.DestroyMesh(_retailFragmentMesh);
            _retailFragmentMesh = MeshHandle.None;
        }

        if (_retailFragmentTexture.IsValid)
        {
            _device.DestroyTexture(_retailFragmentTexture);
            _retailFragmentTexture = TextureHandle.None;
        }

        _retailFragmentGeometry = null;
    }

    private void ReleaseRetailFireballModel()
    {
        if (_retailFireballMesh.IsValid)
        {
            _device.DestroyMesh(_retailFireballMesh);
            _retailFireballMesh = MeshHandle.None;
        }

        foreach (TextureHandle frame in _retailFireballFrameTextures)
        {
            if (frame.IsValid)
            {
                _device.DestroyTexture(frame);
            }
        }

        _retailFireballFrameTextures = [];
        _retailFireballGeometry = null;
    }

    /// <summary>Loads an animated model texture's frames, <c>name000</c> onwards, until one is missing.</summary>
    /// <remarks>
    /// The FireBall's material names the first frame (<c>1006_anim000</c>); its animation is the
    /// numbered run the texture archive carries after it, which <c>Mesh3D_SetAnimTextureFrame</c> steps.
    /// A name that does not end in a three-digit frame number is a single still frame.
    /// </remarks>
    private TextureHandle[] LoadRetailAnimationFrames(string firstFrame)
    {
        List<TextureHandle> frames = [];
        int digits = firstFrame.Length >= 3 && firstFrame[^3..].All(char.IsAsciiDigit) ? 3 : 0;
        if (digits == 0)
        {
            TextureHandle still = SpriteStrip.LoadTexture(_device, _resources, firstFrame);
            return still.IsValid ? [still] : [];
        }

        string stem = firstFrame[..^3];
        int first = int.Parse(firstFrame[^3..], System.Globalization.CultureInfo.InvariantCulture);
        for (int frame = first; frame < 1000; frame++)
        {
            TextureHandle texture = SpriteStrip.LoadTexture(_device, _resources, 
                stem + frame.ToString("D3", System.Globalization.CultureInfo.InvariantCulture));
            if (!texture.IsValid)
            {
                break;
            }

            frames.Add(texture);
        }

        return [.. frames];
    }

    /// <summary>Reads the blend a model's face groups ask the retail draw for.</summary>
    /// <remarks>
    /// <c>0x49DF60</c> draws a group opaque and lit unless it is flagged transparent, in which case
    /// <c>0x49EC70</c> adds it (<c>ONE, ONE</c>) when additive and alpha-blends it otherwise.
    /// </remarks>
    private static BlendMode BlendOf(MefModel model)
    {
        if (model.DrawRecords.Any(record => record.IsAdditive))
        {
            return BlendMode.Additive;
        }

        return model.DrawRecords.Any(record => record.IsTransparent) ? BlendMode.AlphaBlend : BlendMode.Opaque;
    }
}

using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Rendering;

/// <summary>
/// Draws the retail <c>Explosion0</c>'s <c>1006_01_1</c> FireBalls, as a title's simulation has them (<see cref="IExplosionScene"/>).
/// </summary>
/// <remarks>
/// Owns the FireBall's model and its animated texture; what is drawn, and where, is the simulation's. An explosion's
/// fragments are ordinary models, drawn by the title's impact drawer with a round's chips, and its smoke is particle
/// systems, which the particle drawer shows.
/// </remarks>
public sealed class RetailExplosionEffects
{
    private const float UnitsPerMetre = (float)WorldScene.UnitsPerMetre;

    // A FireBall's size is stored over 2048 (0x480060), and its depth never falls below a hundredth.
    private const float RetailFireballSizeDivisor = 2048f;
    private const float RetailFireballDepthFloor = 0.0099999998f;
    private const string RetailFireballModelName = "1006_01_1";

    private readonly IGraphicsDevice _device;
    private readonly ResourceRegistry? _resources;
    private MeshGeometry? _retailFireballGeometry;
    private MeshHandle _retailFireballMesh = MeshHandle.None;
    private TextureHandle[] _retailFireballFrameTextures = [];
    private BlendMode _retailFireballBlend = BlendMode.Additive;
    private int _retailFireballOwnFrame;
    private bool _ownsFireballTextures = true;

    /// <summary>Initializes the drawer over a device and the bound resources.</summary>
    /// <param name="device">The device the models upload to and draw with.</param>
    /// <param name="resources">The bound retail resources the model textures load from.</param>
    public RetailExplosionEffects(IGraphicsDevice device, ResourceRegistry? resources)
    {
        _device = device;
        _resources = resources;
    }

    /// <summary>Gets whether the common pack supplied the native 1006 FireBall model.</summary>
    public bool UsesRetailFireballModel => _retailFireballGeometry is not null
        && _retailFireballFrameTextures.Length > 0;

    /// <summary>Gets how many animation frames the FireBall's texture has, for parity tests.</summary>
    public int RetailFireballModelFrameCount => _retailFireballFrameTextures.Length;

    /// <summary>Gets the blend the FireBall model's face group asks for.</summary>
    public BlendMode RetailFireballBlend => _retailFireballBlend;

    /// <summary>Binds the authored FireBall model from the mission's packs.</summary>
    /// <param name="mission">The loaded IGI1 mission whose common pack owns the effect.</param>
    /// <returns><see langword="true"/> when <c>1006_01_1</c> and its animation resolved.</returns>
    public bool Load(LoadedMission mission)
    {
        ReleaseRetailFireballModel();
        _ownsFireballTextures = true;
        if (_resources is null)
        {
            return false;
        }

        foreach (LoadedPack pack in new[] { mission.CommonPack, mission.LevelPack })
        {
            foreach (MtpInstance instance in pack.Instances)
            {
                if (!string.Equals(instance.Name, RetailFireballModelName, StringComparison.OrdinalIgnoreCase)
                    || !pack.TryGetModel(instance, out MefModel? model)
                    || model is null)
                {
                    continue;
                }

                MeshGeometry? geometry = MefMeshBuilder.Build(model, 1f / UnitsPerMetre);
                if (geometry is null)
                {
                    continue;
                }

                LoadedPack owner = pack;
                MtpInstance named = instance;
                int faceGroupSlot = FireBallSlot(model);
                TextureHandle[] frames = AnimationFrames(
                    model,
                    faceGroupSlot,
                    slot => owner.TryGetTextureName(named, slot, out string textureName)
                        ? SpriteStrip.LoadTexture(_device, _resources, textureName)
                        : TextureHandle.None);
                if (frames.Length == 0)
                {
                    continue;
                }

                ReleaseRetailFireballModel();
                _retailFireballGeometry = geometry;
                _retailFireballFrameTextures = frames;
                _retailFireballOwnFrame = OwnFrame(faceGroupSlot, frames.Length);
                // Project I.G.I.'s own model: its additive face group adds at full strength (0x49EC70).
                _retailFireballBlend = BlendOf(model) == BlendMode.Additive ? BlendMode.AddColour : BlendOf(model);
            }
        }

        return UsesRetailFireballModel;
    }

    /// <summary>Takes a FireBall model a title built from its own packs, whose frames the title keeps and frees.</summary>
    /// <param name="geometry">The <c>1006_01_1</c> model, in metres.</param>
    /// <param name="frames">Its animated texture's frames, in order.</param>
    /// <param name="blend">The blend its face group asks for (<see cref="BlendOf"/>).</param>
    /// <param name="slot">The material slot its face group draws with (<see cref="FireBallSlot"/>).</param>
    /// <returns><see langword="true"/> when there is something to draw.</returns>
    public bool UseFireBall(MeshGeometry geometry, TextureHandle[] frames, BlendMode blend, int slot)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(frames);
        ReleaseRetailFireballModel();
        _ownsFireballTextures = false;
        _retailFireballGeometry = geometry;
        _retailFireballFrameTextures = [.. frames.Where(static frame => frame.IsValid)];
        _retailFireballBlend = blend;
        _retailFireballOwnFrame = OwnFrame(slot, _retailFireballFrameTextures.Length);
        return UsesRetailFireballModel;
    }

    /// <summary>Draws the started FireBalls.</summary>
    /// <param name="camera">The eye.</param>
    /// <param name="projection">The world projection.</param>
    /// <param name="alpha">The frame's position between the last two ticks, 0–1.</param>
    /// <param name="scene">The title's explosions.</param>
    public void Draw(IExplosionScene scene, in Camera3D camera, in Matrix4f projection, float alpha)
    {
        ArgumentNullException.ThrowIfNull(scene);
        _ = alpha;
        DrawRetailFireballs(scene, camera, projection);
    }

    /// <summary>Releases the FireBall's model.</summary>
    public void Release()
    {
        ReleaseRetailFireballModel();
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
            _device.SetMaterialOverride(_retailFireballBlend is BlendMode.Additive or BlendMode.AddColour ? ImpactParticleRenderer.GlowMaterial : SurfaceMaterial.Default);

            int frames = _retailFireballFrameTextures.Length;
            foreach (ExplosionFireBall fireball in scene.FireBalls)
            {
                if (fireball.Delay > 0 || fireball.TotalLifetime <= 0 || fireball.AnimationDuration <= 0)
                {
                    continue;
                }

                // The run handler steps the texture once over the FireBall's animation, from the ticks shown before it counted
                // the life down; the game's FPU chops the frame. Not yet stepped, the face group keeps its own slot's texture.
                int frame = fireball.AnimationTicks < 0
                    ? _retailFireballOwnFrame
                    : (int)((double)fireball.AnimationTicks / fireball.AnimationDuration * frames) % frames;
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
    /// <c>0x480790</c> builds three rows -- across the view <c>(-f.y, f.x, 0)</c>, then <c>f x across</c>, then the direction to the
    /// eye <c>f</c>, the dome's own axis -- and then takes their transpose as the model's frame, so in this engine's terms they
    /// are its columns. Used as rows, the dome's axis lay across the view and the flame showed edge on, a thin ring behind
    /// whatever exploded.
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

        return Matrix3f.FromRows(side * across, up * across, f * depth).Transposed();
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
            if (frame.IsValid && _ownsFireballTextures)
            {
                _device.DestroyTexture(frame);
            }
        }

        _retailFireballFrameTextures = [];
        _retailFireballGeometry = null;
    }

    /// <summary>Gets the material slot the FireBall's textured face group draws with, or -1 for none.</summary>
    /// <param name="model">The <c>1006_01_1</c> model.</param>
    /// <returns>The slot.</returns>
    public static int FireBallSlot(MefModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        foreach (MefDrawRecord record in model.DrawRecords)
        {
            if (record.MaterialSlot != MefDrawRecord.NoMaterialSlot)
            {
                return record.MaterialSlot;
            }
        }

        return -1;
    }

    // The frame that draws a face group's own slot: that slot's frame when animated, the one still frame otherwise.
    private static int OwnFrame(int slot, int frameCount) => Math.Clamp(slot, 0, Math.Max(frameCount - 1, 0));

    /// <summary>Loads the frames of the texture a model's face group draws with, through a title's loader.</summary>
    /// <param name="model">The model.</param>
    /// <param name="slot">The face group's material slot.</param>
    /// <param name="load">Loads the texture of one of the instance's material slots, <see cref="TextureHandle.None"/> when there is none.</param>
    /// <returns>The frames, in order; one for a slot that is not animated, none when nothing loads.</returns>
    /// <remarks>
    /// The model's <c>TXAN</c> chunk for the slot gives the frame count, and the game draws frame <c>n</c> with the texture of
    /// slot <c>n</c> (<c>Mesh3D_SetDrawAnimTextures</c>). The FireBall's face group draws with slot 25, the last frame
    /// (<c>1006_anim025</c>), and its <c>TXAN</c> steps it through slots 0 to 25. Counting texture names up from the face group's
    /// own slot found only that last frame, so the flame showed its faded end for its whole life.
    /// </remarks>
    public static TextureHandle[] AnimationFrames(MefModel model, int slot, Func<int, TextureHandle> load)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(load);
        if (model.TextureAnimations.FirstOrDefault(animation => animation.Slot == slot) is not { FrameCount: > 0 } animated)
        {
            TextureHandle still = load(slot);
            return still.IsValid ? [still] : [];
        }

        TextureHandle[] frames = new TextureHandle[animated.FrameCount];
        for (int frame = 0; frame < frames.Length; frame++)
        {
            frames[frame] = load(frame);
            if (!frames[frame].IsValid)
            {
                return [.. frames[..frame]];
            }
        }

        return frames;
    }

    /// <summary>Reads the blend a model's face groups ask the retail draw for.</summary>
    /// <remarks>
    /// <c>0x49DF60</c> draws a group opaque and lit unless it is flagged transparent, in which case
    /// <c>0x49EC70</c> adds it (<c>ONE, ONE</c>) when additive and alpha-blends it otherwise.
    /// </remarks>
    public static BlendMode BlendOf(MefModel model)
    {
        if (model.DrawRecords.Any(record => record.IsAdditive))
        {
            return BlendMode.Additive;
        }

        return model.DrawRecords.Any(record => record.IsTransparent) ? BlendMode.AlphaBlend : BlendMode.Opaque;
    }
}

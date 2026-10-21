using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Resources;
using OpenIGI.Formats.World;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Rendering;

/// <summary>
/// The retail <c>Explosion0</c> effect: its <c>1006_01_1</c> FireBalls, its <c>1009_01_1</c>
/// fragments and the smoke column's second puff.
/// </summary>
/// <remarks>
/// Owns the two authored effect models and their textures, the live fragments, FireBalls and smoke
/// timers, and draws the models. The smoke puffs themselves are sprite particles, so they are handed
/// back to <see cref="ImpactParticleRenderer"/>'s pool through the smoke callback. It draws from the
/// renderer's own random stream, in the order the renderer always drew.
/// </remarks>
internal sealed class RetailExplosionEffects
{
    private const float UnitsPerMetre = (float)WorldScene.UnitsPerMetre;
    // Explosion0 (FUN_0047F200) and its children. The FireBall is 1006_01_1 (FUN_00480060), 75 ticks
    // long, its size stored over 2048; a fragment is a 1009_01_1 SimpointObj (FUN_0047F0D0) at 204.8
    // over 2048, living the container's 45 ticks (FUN_00503C00's first argument).
    private const int RetailFireballLifeTicks = 75;
    private const float RetailFireballSizeDivisor = 2048f;
    private const float RetailFireballDepthFloor = 0.0099999998f;
    private const float RetailSeveralFireballsScale = 0.75f;
    private const int RetailFragmentLifeTicks = 45;
    private const float RetailFragmentScale = 204.8f / 2048f;
    private const float RetailFragmentRollRange = 0.031415928f;
    private const float RetailFragmentConeRadians = 1.5707964f;
    private const float RetailFragmentMinSpeed = 272f;
    private const float RetailFragmentMaxSpeed = 1360f;
    private const float RetailFragmentMassPerUnit = 0.00021972656f;
    private const int RetailExplosionSmokeTicks = 150;
    private const int RetailExplosionSmokeSecondSpawnAge = 15;
    private const float RetailExplosionDrag = 0.98f;
    private const float RetailExplosionAngleDrag = 0.99f;
    private const float RetailExplosionAngleRange = 0.628318548f;
    private const float RetailExplosionGravity = 44.600006f;
    private const string RetailFragmentModelName = "1009_01_1";
    private const string RetailFireballModelName = "1006_01_1";

    private readonly IGraphicsDevice _device;
    private readonly ResourceRegistry? _resources;
    private readonly IgiRandom _random;
    private readonly Action<Vector3d, Vector3f, float, int> _emitSmokePuff;
    private readonly List<RetailFragment> _retailFragments = [];
    private readonly List<RetailFireball> _retailFireballs = [];
    private readonly List<RetailSmoke> _retailSmokes = [];
    private MeshGeometry? _retailFragmentGeometry;
    private MeshHandle _retailFragmentMesh = MeshHandle.None;
    private TextureHandle _retailFragmentTexture = TextureHandle.None;
    private BlendMode _retailFragmentBlend = BlendMode.Opaque;
    private MeshGeometry? _retailFireballGeometry;
    private MeshHandle _retailFireballMesh = MeshHandle.None;
    private TextureHandle[] _retailFireballFrameTextures = [];
    private BlendMode _retailFireballBlend = BlendMode.Additive;

    /// <summary>Initializes the effect over the renderer's device, resources and random stream.</summary>
    /// <param name="device">The device the models upload to and draw with.</param>
    /// <param name="resources">The bound retail resources the model textures load from.</param>
    /// <param name="random">The renderer's random stream.</param>
    /// <param name="emitSmokePuff">Adds one smoke sprite particle: position, velocity, size in metres, life in ticks.</param>
    internal RetailExplosionEffects(
        IGraphicsDevice device,
        ResourceRegistry? resources,
        IgiRandom random,
        Action<Vector3d, Vector3f, float, int> emitSmokePuff)
    {
        _device = device;
        _resources = resources;
        _random = random;
        _emitSmokePuff = emitSmokePuff;
    }

    /// <summary>Gets whether the IGI1 common pack supplied the native <c>1009_01_1</c> fragment model.</summary>
    internal bool UsesRetailExplosionModel => _retailFragmentGeometry is not null
        && _retailFragmentTexture.IsValid;

    /// <summary>Gets whether the common pack supplied the native 1006 FireBall model.</summary>
    internal bool UsesRetailFireballModel => _retailFireballGeometry is not null
        && _retailFireballFrameTextures.Length > 0;

    /// <summary>Gets how many animation frames the FireBall's texture has, for parity tests.</summary>
    internal int RetailFireballModelFrameCount => _retailFireballFrameTextures.Length;

    /// <summary>Gets the blend the FireBall model's face group asks for.</summary>
    internal BlendMode RetailFireballBlend => _retailFireballBlend;

    /// <summary>Gets the blend the fragment model's face group asks for.</summary>
    internal BlendMode RetailFragmentBlend => _retailFragmentBlend;

    /// <summary>Gets whether any fragment or FireBall is alive to draw.</summary>
    internal bool HasDrawable => _retailFragments.Count != 0 || _retailFireballs.Count != 0;

    /// <summary>Binds the authored fragment and FireBall models from the mission's packs.</summary>
    /// <param name="mission">The loaded IGI1 mission whose common pack owns the effect.</param>
    /// <returns><see langword="true"/> when <c>1009_01_1</c> and its texture resolved.</returns>
    internal bool Load(LoadedMission mission)
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

    /// <summary>Spawns one <c>Explosion0</c> with the retail models bound.</summary>
    /// <param name="positionUnits">The blast's centre, in world units.</param>
    /// <param name="shape">The caller's <c>Explosion0</c> arguments.</param>
    /// <param name="includeSmoke">Whether the native <c>ExplosionSmoke</c> child is present.</param>
    internal void Spawn(Vector3d positionUnits, RetailExplosionShape shape, bool includeSmoke)
    {
        float range = Math.Max(0f, shape.FullRangeUnits);
        int fireballs = Math.Max(0, shape.Fireballs);

        // FUN_00480FE0 draws the blast's axis first, and FUN_004810A0 the container's spin.
        Vector3f axis = new(
            _random.NextSingle() - 0.5f,
            _random.NextSingle() - 0.5f,
            _random.NextSingle() - 0.3f);
        Vector3f spin = new(
            _random.NextSingle() * RetailExplosionAngleRange,
            _random.NextSingle() * RetailExplosionAngleRange,
            _random.NextSingle() * RetailExplosionAngleRange);

        if (fireballs > 0)
        {
            // The first FireBall sits on the blast, a quarter smaller when others share it; the others
            // are scattered through the range, smaller, shorter-lived and staggered in.
            float firstSize = fireballs > 1 ? range * RetailSeveralFireballsScale : range;
            _retailFireballs.Add(new RetailFireball(
                positionUnits, firstSize, DelayTicks: 0, RetailFireballLifeTicks, RetailFireballLifeTicks));

            for (int i = 1; i < fireballs; i++)
            {
                float size = ((_random.NextSingle() * 0.25f) + 0.5f) * range;
                _ = _random.NextSingle();
                Vector3d offset = new(
                    (_random.NextSingle() - 0.5d) * range,
                    (_random.NextSingle() - 0.5d) * range,
                    (_random.NextSingle() - 0.5d) * range);
                int life = (int)((_random.NextSingle() + 1d) * RetailFireballLifeTicks * 0.5d);
                int delay = (i * (RetailFireballLifeTicks / fireballs)) + (int)(_random.NextSingle() * 15f);
                _retailFireballs.Add(new RetailFireball(positionUnits + offset, size, delay, life, life));
            }
        }

        if (includeSmoke)
        {
            float smokeScale = 0.9f * range / UnitsPerMetre;
            int smokeTicks = fireballs <= 1
                ? RetailExplosionSmokeTicks
                : (int)(RetailExplosionSmokeTicks - (RetailExplosionSmokeTicks / (double)fireballs) - 1d
                    + RetailExplosionSmokeTicks);
            EmitSmokePuff(
                positionUnits,
                new Vector3f(0f, 0f, UnitsPerMetre / 30f),
                sizeMetres: 0.8f * smokeScale,
                lifeTicks: smokeTicks);
            _retailSmokes.Add(new RetailSmoke(
                positionUnits,
                smokeScale,
                smokeTicks,
                RetailExplosionSmokeSecondSpawnAge));
        }

        float gravityScale = range * RetailFragmentMassPerUnit;
        for (int i = 0; i < Math.Max(0, shape.Fragments); i++)
        {
            float speed = (_random.NextSingle() * (RetailFragmentMaxSpeed - RetailFragmentMinSpeed))
                + RetailFragmentMinSpeed;
            Vector3f direction = RandomUnitVectorInCone(RetailFragmentConeRadians, axis);
            Matrix3f roll = Matrix3f.FromEngineEulerAngles(
                _random.NextSingle() * RetailFragmentRollRange,
                _random.NextSingle() * RetailFragmentRollRange,
                _random.NextSingle() * RetailFragmentRollRange);
            _retailFragments.Add(new RetailFragment(
                positionUnits,
                positionUnits,
                direction * speed,
                roll,
                spin,
                gravityScale,
                RetailFragmentLifeTicks));
        }
    }

    /// <summary>Picks a direction inside a cone the way <c>GameFunctions_GetRandomUnitVectorInCone</c> does.</summary>
    /// <remarks>
    /// A random turn about the axis and a random fraction of the cone's tangent. At the right-angle cone
    /// every destructible uses, the single-precision tangent is enormous, so almost every fragment leaves
    /// square to the blast's axis: a flat spray, not a sphere.
    /// </remarks>
    private Vector3f RandomUnitVectorInCone(float coneRadians, Vector3f axis)
    {
        float turn = _random.NextSingle() * MathF.Tau;
        float reach = _random.NextSingle() * MathF.Tan(coneRadians);
        float x = MathF.Sin(turn) * reach;
        float y = MathF.Cos(turn) * reach;
        float length = MathF.Sqrt((x * x) + (y * y) + 1f);
        x /= length;
        y /= length;
        float z = 1f / length;

        Vector3f d = axis.Normalized();
        Vector3f side = new(d.Y, -d.X, 0f);
        Vector3f up;
        if (side.Length <= 0.1f)
        {
            up = new Vector3f(0f, -d.Z, d.Y).Normalized();
            side = Vector3f.Cross(d, up).Normalized();
        }
        else
        {
            side = side.Normalized();
            up = Vector3f.Cross(side, d).Normalized();
        }

        return (side * x) + (up * y) + (d * z);
    }

    /// <summary>Advances one <c>SimpointObj</c> fragment through the retail container update.</summary>
    /// <remarks>
    /// IGI1's <c>FUN_005035B0</c> multiplies velocity by the container drag (<c>0.98</c>) and subtracts
    /// <c>mass * 44.600006 * 0.98</c> from Z. The mass is the blast's full range times
    /// <c>0.000219726557</c> (<c>FUN_004810A0</c>), so a bigger blast throws heavier-falling debris.
    /// </remarks>
    internal static (Vector3d PositionUnits, Vector3f Velocity) AdvanceRetailExplosionMotion(
        Vector3d positionUnits,
        Vector3f velocity,
        float gravityScale)
    {
        float gravity = gravityScale * RetailExplosionGravity * RetailExplosionDrag;
        Vector3f nextVelocity = new(
            velocity.X * RetailExplosionDrag,
            velocity.Y * RetailExplosionDrag,
            (velocity.Z * RetailExplosionDrag) - gravity);
        return (
            new Vector3d(
                positionUnits.X + nextVelocity.X,
                positionUnits.Y + nextVelocity.Y,
                positionUnits.Z + nextVelocity.Z),
            nextVelocity);
    }

    /// <summary>Advances the fragments, the FireBalls and the smoke timers by one game tick.</summary>
    internal void Tick()
    {
        for (int i = _retailFragments.Count - 1; i >= 0; i--)
        {
            RetailFragment current = _retailFragments[i];
            if (current.RemainingTicks <= 0)
            {
                _retailFragments.RemoveAt(i);
                continue;
            }

            // The container damps its spin first, then turns every piece by it and moves it on.
            Vector3f spin = current.Spin * RetailExplosionAngleDrag;
            (Vector3d positionUnits, Vector3f velocity) = AdvanceRetailExplosionMotion(
                current.PositionUnits,
                current.Velocity,
                current.GravityScale);
            _retailFragments[i] = current with
            {
                PreviousPositionUnits = current.PositionUnits,
                PositionUnits = positionUnits,
                Velocity = velocity,
                Orientation = Matrix3f.FromEngineEulerAngles(spin.X, spin.Y, spin.Z) * current.Orientation,
                Spin = spin,
                RemainingTicks = current.RemainingTicks - 1,
            };
        }

        // FUN_004806E0: a FireBall waits out its delay, then counts its life down to nothing.
        for (int i = _retailFireballs.Count - 1; i >= 0; i--)
        {
            RetailFireball fireball = _retailFireballs[i];
            if (fireball.DelayTicks > 0)
            {
                _retailFireballs[i] = fireball with { DelayTicks = fireball.DelayTicks - 1 };
            }
            else if (fireball.RemainingTicks > 0)
            {
                _retailFireballs[i] = fireball with { RemainingTicks = fireball.RemainingTicks - 1 };
            }
            else
            {
                _retailFireballs.RemoveAt(i);
            }
        }

        for (int i = _retailSmokes.Count - 1; i >= 0; i--)
        {
            RetailSmoke current = _retailSmokes[i];
            if (!current.SecondEmitted && current.AgeTicks >= current.SecondSpawnAge)
            {
                EmitSmokePuff(
                    current.PositionUnits,
                    new Vector3f(0f, 0f, UnitsPerMetre / 30f),
                    sizeMetres: 0.8f * current.Scale,
                    lifeTicks: RetailExplosionSmokeTicks - current.SecondSpawnAge);
                current = current with { SecondEmitted = true };
            }

            current = current with { AgeTicks = current.AgeTicks + 1 };
            if (current.AgeTicks >= current.TotalTicks)
            {
                _retailSmokes.RemoveAt(i);
            }
            else
            {
                _retailSmokes[i] = current;
            }
        }
    }

    /// <summary>Draws the live fragments, then the started FireBalls.</summary>
    /// <param name="camera">The eye.</param>
    /// <param name="projection">The world projection.</param>
    /// <param name="alpha">The frame's position between the last two ticks, 0–1.</param>
    internal void Draw(in Camera3D camera, in Matrix4f projection, float alpha)
    {
        DrawRetailFragments(camera, projection, alpha);
        DrawRetailFireballs(camera, projection);
    }

    /// <summary>Removes every fragment, FireBall and smoke timer, as changing level does.</summary>
    internal void Clear()
    {
        _retailFragments.Clear();
        _retailFireballs.Clear();
        _retailSmokes.Clear();
    }

    /// <summary>Releases both effect models.</summary>
    internal void Release()
    {
        ReleaseRetailFragmentModel();
        ReleaseRetailFireballModel();
    }

    private void EmitSmokePuff(Vector3d positionUnits, Vector3f velocity, float sizeMetres, int lifeTicks) =>
        _emitSmokePuff(positionUnits, velocity, sizeMetres, lifeTicks);

    /// <summary>Draws the thrown <c>1009_01_1</c> pieces as the ordinary models they are.</summary>
    /// <remarks>
    /// A fragment is a <c>SimpointObj</c>, drawn by the plain model path: its face group is not
    /// transparent (<c>0x49DF60</c>), so it is lit and opaque, a small dark rock.
    /// </remarks>
    private void DrawRetailFragments(in Camera3D camera, in Matrix4f projection, float alpha)
    {
        if (_retailFragments.Count == 0 || _retailFragmentGeometry is null || !_retailFragmentTexture.IsValid)
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

            foreach (RetailFragment fragment in _retailFragments)
            {
                Vector3d positionUnits = Vector3d.Lerp(
                    fragment.PreviousPositionUnits,
                    fragment.PositionUnits,
                    alpha);
                Matrix4f modelView = ScaleModelView(
                    camera.CreateModelView(positionUnits / WorldScene.UnitsPerMetre, fragment.Orientation),
                    RetailFragmentScale);

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
    private void DrawRetailFireballs(in Camera3D camera, in Matrix4f projection)
    {
        if (_retailFireballs.Count == 0 || _retailFireballGeometry is null || _retailFireballFrameTextures.Length == 0)
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
            foreach (RetailFireball fireball in _retailFireballs)
            {
                if (fireball.DelayTicks > 0 || fireball.LifeTicks <= 0)
                {
                    continue;
                }

                int frame = (int)((double)(fireball.LifeTicks - fireball.RemainingTicks)
                    / fireball.LifeTicks * frames) % frames;
                float across = fireball.SizeUnits / RetailFireballSizeDivisor;
                float depth = ((1f - ((float)fireball.RemainingTicks / fireball.LifeTicks)) * across)
                    + RetailFireballDepthFloor;
                Vector3d positionMetres = fireball.PositionUnits / WorldScene.UnitsPerMetre;
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

    private readonly record struct RetailFragment(
        Vector3d PreviousPositionUnits,
        Vector3d PositionUnits,
        Vector3f Velocity,
        Matrix3f Orientation,
        Vector3f Spin,
        float GravityScale,
        int RemainingTicks);

    private readonly record struct RetailFireball(
        Vector3d PositionUnits,
        float SizeUnits,
        int DelayTicks,
        int LifeTicks,
        int RemainingTicks);

    private readonly record struct RetailSmoke(
        Vector3d PositionUnits,
        float Scale,
        int TotalTicks,
        int SecondSpawnAge,
        int AgeTicks = 0,
        bool SecondEmitted = false);
}

using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Rendering.Blast;

/// <summary>
/// A bomb's fire and smoke as sprites: the flash, the fireball's tongues, the column and its cap, the ring rolled out along
/// the ground, the burning pieces thrown out on smoking ropes, the embers, the grit, and the smoulder left behind.
/// </summary>
/// <remarks>
/// <para>
/// Cheap where the march it replaces was dear: a few hundred camera-facing quads through one mesh, the smoke blended over the
/// scene far to near, the fire, embers and flash added to its light. The smoke is lit from above in its vertex colours and
/// glows from the fire inside it while it is young; the fire is drawn brighter than white, so the bloom takes it.
/// </para>
/// <para>
/// Stepped on the game's fixed tick and drawn between ticks. Every random part is drawn from the blast's own seed, so every
/// machine shows the same blast; the light tier throws about a third as much.
/// </para>
/// </remarks>
/// <param name="device">The device it draws with.</param>
public sealed partial class BombBlastParticles(IGraphicsDevice device) : IDisposable
{
    /// <summary>The most sprites alive at once; a blast that would pass it is trimmed.</summary>
    public const int MaximumSprites = 3072;

    // How much brighter than white the fire, embers and flash are drawn, for the bloom.
    private const float FireGain = 3.2f;
    private const float FlareGain = 2.4f;

    // Inside this distance from the eye a sprite is not drawn, and over the next few metres it comes in.
    private const float NearCullMetres = 0.4f;
    private const float NearFadeMetres = 3f;

    private const float UnitsPerMetre = (float)WorldScene.UnitsPerMetre;

    private static readonly SurfaceMaterial Glow = new(Roughness: 1f, Metallic: 0f, Emissive: 1f, CastsOcclusion: false, NoHistory: true);

    private readonly BlastSprite[] _sprites = new BlastSprite[MaximumSprites];
    private readonly List<BlastEmitter> _emitters = [];
    private readonly Vertex3D[] _vertices = new Vertex3D[MaximumSprites * 2 * 4];
    private readonly int[] _order = new int[MaximumSprites];
    private readonly float[] _depth = new float[MaximumSprites];
    private int _count;
    private int _used;
    private int _lastUsed;
    private MeshHandle _mesh = MeshHandle.None;
    private TextureHandle _smoke = TextureHandle.None;
    private TextureHandle _fire = TextureHandle.None;
    private TextureHandle _flare = TextureHandle.None;
    private TextureHandle _streak = TextureHandle.None;
    private bool _disposed;

    /// <summary>Gets how many sprites are alive.</summary>
    public int LiveCount => _count;

    /// <summary>Gets whether anything is still to be drawn or thrown.</summary>
    public bool IsActive => _count > 0 || _emitters.Count > 0;

    /// <summary>Steps every sprite and everything still throwing them one game tick.</summary>
    public void Tick()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        for (int i = _emitters.Count - 1; i >= 0; i--)
        {
            if (!StepEmitter(_emitters[i]))
            {
                _emitters.RemoveAt(i);
            }
        }

        for (int i = _count - 1; i >= 0; i--)
        {
            ref BlastSprite s = ref _sprites[i];
            if (s.Age < 0)
            {
                s.Age++;
                s.Previous = s.Position;
                continue;
            }

            if (++s.Age >= s.Life)
            {
                s = _sprites[--_count];
                continue;
            }

            s.Previous = s.Position;
            s.Velocity = (s.Velocity + s.Push) * s.Keep;
            s.Position = new Vector3d(s.Position.X + s.Velocity.X, s.Position.Y + s.Velocity.Y, s.Position.Z + s.Velocity.Z);
            s.Turn += s.Spin;
        }
    }

    /// <summary>Forgets every sprite and every emitter, as the level is put back.</summary>
    public void Clear()
    {
        _count = 0;
        _emitters.Clear();
    }

    /// <summary>Draws every sprite over the lit scene: the smoke far to near, then the fire's light over it.</summary>
    /// <param name="camera">The eye.</param>
    /// <param name="projection">The world's projection.</param>
    /// <param name="blend">How far the frame is between the last tick and the next.</param>
    public void Draw(in Camera3D camera, in Matrix4f projection, float blend)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_count == 0)
        {
            return;
        }

        Vector3d eye = camera.Position;
        _used = 0;

        // The smoke, far to near, so each billow covers the ones behind it.
        int smokeCount = 0;
        for (int i = 0; i < _count; i++)
        {
            ref BlastSprite s = ref _sprites[i];
            if (s.Age >= 0 && s.Look == BlastSpriteLook.Smoke)
            {
                _order[smokeCount] = i;
                _depth[smokeCount] = -(float)((Vector3d.Lerp(s.Previous, s.Position, blend) / UnitsPerMetre) - eye).LengthSquared;
                smokeCount++;
            }
        }

        Array.Sort(_depth, _order, 0, smokeCount);
        int smokeFirst = _used;
        for (int i = 0; i < smokeCount; i++)
        {
            AddBillboard(ref _sprites[_order[i]], eye, blend, heat: false);
        }

        int smokeQuads = (_used - smokeFirst) / 4;
        int gritFirst = _used;
        AddStreaks(BlastSpriteLook.Grit, eye, blend);
        int gritQuads = (_used - gritFirst) / 4;

        // The glow of the fire still inside the young smoke, added over it.
        int heatFirst = _used;
        for (int i = 0; i < smokeCount; i++)
        {
            ref BlastSprite s = ref _sprites[_order[i]];
            if (s.Heat > 0f)
            {
                AddBillboard(ref s, eye, blend, heat: true);
            }
        }

        int heatQuads = (_used - heatFirst) / 4;
        int fireFirst = _used;
        for (int i = 0; i < _count; i++)
        {
            ref BlastSprite s = ref _sprites[i];
            if (s.Age >= 0 && s.Look == BlastSpriteLook.Fire)
            {
                AddBillboard(ref s, eye, blend, heat: false);
            }
        }

        int fireQuads = (_used - fireFirst) / 4;
        int emberFirst = _used;
        AddStreaks(BlastSpriteLook.Ember, eye, blend);
        int emberQuads = (_used - emberFirst) / 4;
        int flareFirst = _used;
        for (int i = 0; i < _count; i++)
        {
            ref BlastSprite s = ref _sprites[i];
            if (s.Age >= 0 && s.Look == BlastSpriteLook.Flare)
            {
                AddBillboard(ref s, eye, blend, heat: false);
            }
        }

        int flareQuads = (_used - flareFirst) / 4;
        if (_used == 0)
        {
            return;
        }

        if (_lastUsed > _used)
        {
            Array.Clear(_vertices, _used, _lastUsed - _used);
        }

        _lastUsed = _used;
        device.SetProjection(projection);
        device.SetCullMode(CullMode.None);
        device.SetDepthState(DepthState.TestOnly);
        device.SetMaterialOverride(Glow);
        Matrix4f modelView = camera.CreateModelView(camera.Position, Matrix3f.Identity);
        try
        {
            if (!_mesh.IsValid)
            {
                _mesh = device.CreateAnimatedMesh(_vertices, QuadIndices.Build(_vertices.Length / 4));
            }

            device.UpdateAnimatedMesh(_mesh, _vertices.AsSpan(0, _used));
            device.ClearPoseInterpolation();
            DrawRange(smokeFirst, smokeQuads, modelView, Texture(ref _smoke, BlastSpriteTextures.CreateSmoke), BlendMode.AlphaBlend);
            DrawRange(gritFirst, gritQuads, modelView, Texture(ref _streak, ImpactTextures.CreateSpark), BlendMode.AlphaBlend);
            device.SetLightmapScale(FireGain);
            DrawRange(heatFirst, heatQuads, modelView, Texture(ref _smoke, BlastSpriteTextures.CreateSmoke), BlendMode.Additive);
            DrawRange(fireFirst, fireQuads, modelView, Texture(ref _fire, BlastSpriteTextures.CreateFire), BlendMode.Additive);
            DrawRange(emberFirst, emberQuads, modelView, Texture(ref _streak, ImpactTextures.CreateSpark), BlendMode.Additive);

            // The flash is seen through whatever stands between: it lights the air, not a surface.
            device.SetLightmapScale(FlareGain);
            device.SetDepthState(DepthState.Disabled);
            DrawRange(flareFirst, flareQuads, modelView, Texture(ref _flare, BlastSpriteTextures.CreateFlare), BlendMode.Additive);
        }
        catch (Exception ex) when (ex is ArgumentException or GraphicsDeviceException)
        {
            // A blast that cannot upload this frame is simply not drawn.
            _ = ex;
        }
        finally
        {
            device.SetLightmapScale(1f);
            device.SetDepthState(DepthState.TestAndWrite);
            device.SetMaterialOverride(SurfaceMaterial.Default);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_mesh.IsValid)
        {
            device.DestroyMesh(_mesh);
            _mesh = MeshHandle.None;
        }

        Destroy(ref _smoke);
        Destroy(ref _fire);
        Destroy(ref _flare);
        Destroy(ref _streak);
    }

    private void DrawRange(int firstVertex, int quads, in Matrix4f modelView, TextureHandle texture, BlendMode blend)
    {
        if (quads > 0)
        {
            device.DrawMesh(_mesh, (firstVertex / 4) * 6, quads * 6, modelView, texture, TextureHandle.None, blend);
        }
    }

    private TextureHandle Texture(ref TextureHandle handle, Func<IGraphicsDevice, TextureHandle> create)
    {
        if (!handle.IsValid)
        {
            handle = create(device);
        }

        return handle;
    }

    private void Destroy(ref TextureHandle handle)
    {
        if (handle.IsValid)
        {
            device.DestroyTexture(handle);
            handle = TextureHandle.None;
        }
    }

    private void Emit(in BlastSprite sprite)
    {
        if (_count < MaximumSprites)
        {
            _sprites[_count++] = sprite;
        }
    }

    // A camera-facing quad: turned by the sprite's spin, or stood up along the world's up when it is taller than wide. Smoke
    // is lit from above in its corner colours; its heat pass is the same quad in the fire's colour.
    private void AddBillboard(ref BlastSprite s, Vector3d eye, float blend, bool heat)
    {
        if (_used + 4 > _vertices.Length)
        {
            return;
        }

        float lived = s.Lived(blend);
        float strength = s.FadeAt(lived);
        if (heat)
        {
            float glow = Math.Clamp(1f - (lived / Math.Max(s.HeatLife, 1e-3f)), 0f, 1f);
            strength *= s.Heat * glow * glow;
        }

        Vector3d centre = (Vector3d.Lerp(s.Previous, s.Position, blend) / UnitsPerMetre) - eye;
        double distance = centre.Length;
        strength *= NearFade(distance);
        if (!(strength > 0.002f) || !(distance > 1e-6))
        {
            return;
        }

        Vector3d toEye = centre / -distance;
        Vector3d up = new Vector3d(0, 0, 1) - (toEye * toEye.Z);
        if (up.LengthSquared < 1e-6)
        {
            up = new Vector3d(1, 0, 0);
        }

        up = up / up.Length;
        Vector3d right = Vector3d.Cross(up, toEye);
        if (MathF.Abs(s.Aspect - 1f) < 1e-3f)
        {
            double c = Math.Cos(s.Turn);
            double n = Math.Sin(s.Turn);
            (right, up) = ((right * c) + (up * n), (up * c) - (right * n));
        }

        float half = s.SizeAt(lived) * 0.5f;
        Vector3f r = ToF(right * half);
        Vector3f u = ToF(up * (half * Math.Max(s.Aspect, 0.02f)));
        Vector3f c0 = ToF(centre);
        Vector3f normal = ToF(toEye);
        if (!FiniteChecks.IsFinite(c0) || !FiniteChecks.IsFinite(r) || !FiniteChecks.IsFinite(u))
        {
            return;
        }

        float u0 = (s.Variant & 1) * 0.5f;
        float v0 = (s.Variant >> 1) * 0.5f;
        bool atlas = s.Look is BlastSpriteLook.Smoke or BlastSpriteLook.Fire;
        float span = atlas ? 0.5f : 1f;
        if (!atlas)
        {
            u0 = 0f;
            v0 = 0f;
        }

        // The colour of each corner: smoke brighter where it faces up into the sky's light and darker beneath; the heat pass
        // and the fire their own colour throughout.
        (float red, float green, float blue) = heat ? HeatColour(lived) : s.Look == BlastSpriteLook.Fire ? FireColour(s, lived) : (s.Red, s.Green, s.Blue);
        float topLight = s.Look == BlastSpriteLook.Smoke && !heat ? 1.12f : 1f;
        float underLight = s.Look == BlastSpriteLook.Smoke && !heat ? 0.7f : 1f;
        Bgra32 bottom = Tint(red * underLight, green * underLight, blue * underLight, strength);
        Bgra32 top = Tint(red * topLight, green * topLight, blue * topLight, strength);

        // Under a turn the corners' up is not the world's: weight each by how far it actually stands up.
        float upward = (float)Math.Clamp(up.Z, -1.0, 1.0);
        Bgra32 lowLeft = Mix(bottom, top, 0.5f - (0.5f * upward) + (0.5f * (float)Math.Clamp(-right.Z, -1.0, 1.0)));
        Bgra32 lowRight = Mix(bottom, top, 0.5f - (0.5f * upward) + (0.5f * (float)Math.Clamp(right.Z, -1.0, 1.0)));
        Bgra32 highRight = Mix(bottom, top, 0.5f + (0.5f * upward) + (0.5f * (float)Math.Clamp(right.Z, -1.0, 1.0)));
        Bgra32 highLeft = Mix(bottom, top, 0.5f + (0.5f * upward) + (0.5f * (float)Math.Clamp(-right.Z, -1.0, 1.0)));

        _vertices[_used] = new Vertex3D(c0 - r - u, normal, u0, v0 + span, colour: lowLeft);
        _vertices[_used + 1] = new Vertex3D(c0 + r - u, normal, u0 + span, v0 + span, colour: lowRight);
        _vertices[_used + 2] = new Vertex3D(c0 + r + u, normal, u0 + span, v0, colour: highRight);
        _vertices[_used + 3] = new Vertex3D(c0 - r + u, normal, u0, v0, colour: highLeft);
        _used += 4;
    }

    // Every sprite of a look drawn as a streak along its travel: an ember a line of light, a chip a dark scratch.
    private void AddStreaks(BlastSpriteLook look, Vector3d eye, float blend)
    {
        for (int i = 0; i < _count; i++)
        {
            ref BlastSprite s = ref _sprites[i];
            if (s.Age < 0 || s.Look != look || _used + 4 > _vertices.Length)
            {
                continue;
            }

            float lived = s.Lived(blend);
            Vector3d headMetres = (Vector3d.Lerp(s.Previous, s.Position, blend) / UnitsPerMetre) - eye;
            double distance = headMetres.Length;
            float strength = s.FadeAt(lived) * NearFade(distance);
            if (!(strength > 0.002f))
            {
                continue;
            }

            Vector3d tailMetres = headMetres - (new Vector3d(s.Velocity.X, s.Velocity.Y, s.Velocity.Z) * (s.Streak / UnitsPerMetre));
            Vector3d along = tailMetres - headMetres;
            Vector3d across = Vector3d.Cross(along, headMetres);
            double length = across.Length;
            float half = (s.SizeAt(lived) * 0.5f) + ((float)distance * 0.0015f);
            if (!(length > 1e-9))
            {
                across = Vector3d.Cross(new Vector3d(0, 0, 1), headMetres);
                length = across.Length;
                if (!(length > 1e-9))
                {
                    continue;
                }
            }

            Vector3f side = ToF(across * (half / length));
            Vector3f head = ToF(headMetres);
            Vector3f tail = ToF(tailMetres);
            Vector3f normal = ToF(headMetres / -Math.Max(distance, 1e-6));
            if (!FiniteChecks.IsFinite(head) || !FiniteChecks.IsFinite(tail) || !FiniteChecks.IsFinite(side))
            {
                continue;
            }

            (float red, float green, float blue) = look == BlastSpriteLook.Ember ? EmberColour(s, lived) : (s.Red, s.Green, s.Blue);
            Bgra32 headColour = Tint(red, green, blue, strength);
            Bgra32 tailColour = Tint(red * 0.6f, green * 0.45f, blue * 0.35f, strength * 0.15f);
            _vertices[_used] = new Vertex3D(head + side, normal, 0.5f, 0f, colour: headColour);
            _vertices[_used + 1] = new Vertex3D(head - side, normal, 0.5f, 1f, colour: headColour);
            _vertices[_used + 2] = new Vertex3D(tail - side, normal, 1f, 1f, colour: tailColour);
            _vertices[_used + 3] = new Vertex3D(tail + side, normal, 1f, 0f, colour: tailColour);
            _used += 4;
        }
    }

    // The fire cools as it burns: white-yellow, orange, a dull red.
    private static (float Red, float Green, float Blue) FireColour(in BlastSprite s, float lived)
    {
        float t = Math.Clamp(lived, 0f, 1f);
        float red = s.Red;
        float green = s.Green * Lerp(1f, 0.32f, Smooth(0.05f, 0.75f, t));
        float blue = s.Blue * Lerp(1f, 0.08f, Smooth(0f, 0.4f, t));
        return (red, green, blue);
    }

    private static (float Red, float Green, float Blue) EmberColour(in BlastSprite s, float lived)
    {
        float cool = Smooth(0.3f, 1f, lived);
        return (s.Red, s.Green * Lerp(1f, 0.55f, cool), s.Blue * Lerp(1f, 0.3f, cool));
    }

    private static (float Red, float Green, float Blue) HeatColour(float lived)
    {
        float cool = Smooth(0f, 0.25f, lived);
        return (1f, Lerp(0.62f, 0.32f, cool), Lerp(0.28f, 0.08f, cool));
    }

    private static float NearFade(double distanceMetres) =>
        Math.Clamp(((float)distanceMetres - NearCullMetres) / NearFadeMetres, 0f, 1f);

    // A corner's colour, its strength in alpha whether it is blended or added: the forward pass decodes colour from the
    // display's encoding and would bend a fade held there, but takes alpha as it is.
    private static Bgra32 Tint(float red, float green, float blue, float strength)
    {
        static byte Channel(float value) => (byte)Math.Clamp((int)(value * 255f), 0, 255);

        // Alpha floored at one: Vertex3D reads an all-zero colour as "no tint" and substitutes white.
        return new Bgra32(Channel(blue), Channel(green), Channel(red), Math.Max((byte)1, Channel(strength)));
    }

    private static Bgra32 Mix(Bgra32 a, Bgra32 b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        static byte Lerp8(byte x, byte y, float t) => (byte)Math.Clamp((int)MathF.Round(x + ((y - x) * t)), 0, 255);
        return new Bgra32(Lerp8(a.B, b.B, t), Lerp8(a.G, b.G, t), Lerp8(a.R, b.R, t), Math.Max((byte)1, Lerp8(a.A, b.A, t)));
    }

    private static Vector3f ToF(Vector3d v) => new((float)v.X, (float)v.Y, (float)v.Z);

    private static float Lerp(float a, float b, float t) => a + ((b - a) * t);

    private static float Smooth(float from, float to, float x)
    {
        float t = Math.Clamp((x - from) / MathF.Max(to - from, 1e-6f), 0f, 1f);
        return t * t * (3f - (2f * t));
    }
}

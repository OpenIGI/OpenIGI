using OpenIGI.Core.Graphics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game.World;
using OpenIGI.Presentation.Cameras;
using OpenIGI.Rendering;

namespace OpenIGI.Presentation.Rendering.Blast;

/// <summary>
/// The remastered bomb blast as the frame shows it: the blasts going off, the shake they give the view, the light they throw,
/// and everything drawn for them -- stepped on the game's tick, forgotten when the level is put back.
/// </summary>
/// <remarks>
/// <para>
/// Presentation alone. The bomb's harm, its kills and the destructibles it breaks for good are the mode's and the blast's,
/// decided on the server and sent as they always were; this only shows it, the same on every machine because every random
/// part of it is drawn from <see cref="BombBlast.Seed"/>.
/// </para>
/// <para>
/// <see cref="CombatEffectCVars.BlastQuality"/> decides how much: nought is the original's rocket fire alone and this does
/// nothing; one adds the light, the shockwave, the shake, the ground and the debris; two the volumetric fireball.
/// </para>
/// </remarks>
public sealed class BombBlastEffects
{
    // The brightest the blast's light is at its flash, against a level lamp's one.
    private const float PeakLightIntensity = 90f;

    private readonly List<BombBlast> _blasts = [];
    private readonly TraumaShake _shake = new();

    /// <summary>Gets the blasts still burning, oldest first.</summary>
    public IReadOnlyList<BombBlast> Blasts => _blasts;

    /// <summary>Gets the shake the blasts gave the view.</summary>
    public TraumaShake Shake => _shake;

    /// <summary>Gets a value indicating whether anything of a blast is still to be shown.</summary>
    public bool IsActive => _blasts.Count > 0 || _shake.IsActive;

    /// <summary>Gets how much of the blast is drawn: <see cref="CombatEffectCVars.BlastQuality"/>.</summary>
    /// <remarks>
    /// The browser and phones run the light tier whatever the preset says: the raymarched fireball is a full-screen march a
    /// mobile GPU cannot spare in the frame it matters most, and a quarter of the pieces is what their CPUs can step.
    /// </remarks>
    public static int Quality => IsLightPlatform
        ? Math.Min(CombatEffectCVars.BlastQuality.Value, 1)
        : CombatEffectCVars.BlastQuality.Value;

    private static bool IsLightPlatform => OperatingSystem.IsBrowser() || OperatingSystem.IsAndroid() || OperatingSystem.IsIOS();

    /// <summary>Raised as a blast goes off, for what draws it (the debris, the breaches, the ground) to start their part.</summary>
    public event Action<BombBlast>? Started;

    /// <summary>Raised as the level is put back, for what draws the blasts to forget theirs.</summary>
    public event Action? Cleared;

    /// <summary>Sets a blast off.</summary>
    /// <param name="atUnits">Where it went off, in world units.</param>
    /// <param name="radiusUnits">How far it kills, in world units.</param>
    /// <param name="listenerMetres">Where the view is now, in world metres, which its shake is measured from; null for none.</param>
    /// <returns>The blast, or <see langword="null"/> when the remastered blast is off.</returns>
    public BombBlast? Start(Vector3d atUnits, float radiusUnits, Vector3d? listenerMetres)
    {
        if (Quality <= 0)
        {
            return null;
        }

        var blast = new BombBlast(WorldScene.ToMetres(atUnits), radiusUnits / (float)WorldUnits.PerMetre, BombBlast.SeedAt(atUnits));
        _blasts.Add(blast);

        if (listenerMetres is { } ear)
        {
            float distance = (float)(ear - blast.CentreMetres).Length;
            float trauma = blast.TraumaAt(distance);

            // The view thrown up and a little aside, the side drawn from the blast's seed so every machine agrees.
            float side = (blast.Seed & 1) == 0 ? 1f : -1f;
            _shake.Add(trauma, BombBlast.ArrivalSeconds(distance), kickPitch: 0.07f, kickYaw: 0.03f * side);

            // The ground's rumble keeps coming for a moment after the front has passed.
            _shake.Add(trauma * 0.35f, BombBlast.ArrivalSeconds(distance) + 0.45f);
        }

        Started?.Invoke(blast);
        return blast;
    }

    /// <summary>Steps every blast and the shake one game tick, forgetting the blasts that have burned out.</summary>
    public void Tick()
    {
        _shake.Tick();
        for (int i = _blasts.Count - 1; i >= 0; i--)
        {
            _blasts[i].Tick();
            if (_blasts[i].IsSpent)
            {
                _blasts.RemoveAt(i);
            }
        }
    }

    /// <summary>Forgets every blast and its shake, as the level is put back for the next round.</summary>
    public void Clear()
    {
        _blasts.Clear();
        _shake.Clear();
        Cleared?.Invoke();
    }

    /// <summary>The shake the blasts give the view at this frame, scaled by the player's setting.</summary>
    /// <param name="blend">How far the frame is between the last tick and the next.</param>
    /// <returns>The shake; none while nothing shakes.</returns>
    public ShakeSample SampleShake(float blend) =>
        !_shake.IsActive || Quality <= 0 ? default : _shake.Sample(blend, CombatEffectCVars.BlastShake.Value);

    /// <summary>What the blasts do to the eyes and ears of somebody at a place: the whiteout of the flash and the stun after.</summary>
    /// <param name="eyeMetres">Where they are, in world metres.</param>
    /// <param name="blend">How far the frame is between the last tick and the next.</param>
    /// <returns>The concussion; none far from every blast.</returns>
    /// <remarks>
    /// The flash is seen at once; the stun comes with the shock front, at the speed of sound, holds for most of a second and
    /// lifts over the next few -- the picture drained of colour, closed in at the edges and split, the sound muffled under a
    /// ringing.
    /// </remarks>
    public BlastConcussion Concussion(Vector3d eyeMetres, float blend)
    {
        if (Quality <= 0)
        {
            return default;
        }

        float flash = 0f;
        float stun = 0f;
        foreach (BombBlast blast in _blasts)
        {
            float age = blast.Age(blend);
            float distance = (float)(eyeMetres - blast.CentreMetres).Length;
            float near = blast.ConcussionAt(distance);

            // The flash: everybody who can see the sky lit sees it, the near most; gone in a quarter of a second.
            float seen = MathF.Max(near, 0.25f * (1f - BombBlast.Smooth(blast.RadiusMetres * 3f, blast.RadiusMetres * 12f, distance)));
            flash = MathF.Max(flash, seen * BombBlast.Smooth(0f, 0.02f, age) * MathF.Exp(-age / 0.09f));

            float arrives = BombBlast.ArrivalSeconds(distance);
            float held = BombBlast.Smooth(arrives, arrives + 0.06f, age) * (1f - BombBlast.Smooth(arrives + 0.7f, arrives + 4.5f, age));
            stun = MathF.Max(stun, near * held);
        }

        return new BlastConcussion(Math.Clamp(flash, 0f, 1f), Math.Clamp(stun, 0f, 1f));
    }

    /// <summary>Writes the blasts' light as local lights for the deferred pass, in view space.</summary>
    /// <param name="eye">The frame's eye.</param>
    /// <param name="blend">How far the frame is between the last tick and the next.</param>
    /// <param name="lights">Where to write them.</param>
    /// <returns>How many were written.</returns>
    public int WriteLights(in Camera3D eye, float blend, Span<DeferredLocalLight> lights)
    {
        int written = 0;
        if (Quality <= 0)
        {
            return 0;
        }

        Matrix3f worldToView = eye.Orientation.Transposed();
        foreach (BombBlast blast in _blasts)
        {
            if (written >= lights.Length)
            {
                break;
            }

            float age = blast.Age(blend);
            float level = blast.LightLevel(age);
            if (level <= 0.002f)
            {
                continue;
            }

            // The light sits in the fireball, which rises as it burns.
            float rise = blast.FireballRadiusMetres(age) * (0.35f + (0.6f * BombBlast.Smooth(0.2f, 3f, age)));
            Vector3f cameraRelative = new(
                (float)(blast.CentreMetres.X - eye.Position.X),
                (float)(blast.CentreMetres.Y - eye.Position.Y),
                (float)(blast.CentreMetres.Z + rise - eye.Position.Z));

            lights[written++] = new DeferredLocalLight(
                worldToView.Transform(cameraRelative),
                blast.LightReachMetres(age),
                ColourTemperature.ToLinearRgb(BombBlast.LightKelvin(age)),
                PeakLightIntensity * level,
                default,

                // A fireball metres across: a wide source, so what it lights has soft shadows.
                MathF.Max(0.5f, blast.FireballRadiusMetres(age) * 0.5f));
        }

        return written;
    }
}

/// <summary>What a blast does to the senses of somebody near it at one frame.</summary>
/// <param name="Flash">How white the flash whites the picture out, in <c>[0,1]</c>.</param>
/// <param name="Stun">How stunned they are by the shock front: colour drained, edges closed, sound muffled, in <c>[0,1]</c>.</param>
public readonly record struct BlastConcussion(float Flash, float Stun)
{
    /// <summary>Gets a value indicating whether it does nothing.</summary>
    public bool IsNone => Flash <= 0f && Stun <= 0f;
}

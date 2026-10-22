using OpenIGI.Core.Numerics;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.Detection;

/// <summary>
/// How well I.G.I. 2's soldiers see a player of the game hosting them: I.G.I. 2's own measure of its player
/// (<see cref="Igi2VisibilityFactor"/>), worked out for the host's player each tick.
/// </summary>
/// <remarks>
/// <para>
/// I.G.I. 2's detectors see a human only as far as its visibility factor times their cone's length (<c>AIDetection_RunDetection</c>),
/// and its player's factor is never one. A host's player is measured by nothing of the kind, so without this it would be seen at a
/// cone's full length: a soldier 70 metres off opens fire as a level starts.
/// </para>
/// <para>
/// The light is what the host's level bakes under the feet, its layers asked in turn (<see cref="IBakedLight"/>), else the
/// tuning's. Moving is read from how far the feet go in a tick, since the host's player has no I.G.I. 2 locomotion; turning on
/// the spot is not seen through the body. A loud shot is one of the player's own gunshots that reaches further than I.G.I. 2's
/// loud range, heard as the host's level broadcasts it.
/// </para>
/// </remarks>
/// <param name="player">The host's player.</param>
/// <param name="tuning">The visibility parameters.</param>
/// <param name="light">The host level's baked light, its layers in the order they are asked.</param>
public sealed class Igi2HostPlayerVisibility(HumanPlayerTask player, Igi2VisibilityTuning tuning, IReadOnlyList<IBakedLight>? light = null)
    : IHumanAiEventTarget
{
    /// <summary>How fast, in metres a second, the feet go at the least that counts as moving.</summary>
    public const double MovingSpeed = 0.3;

    /// <summary>How fast the feet go at the least that counts as running: between Project I.G.I.'s walk and its 4.9 m/s run.</summary>
    public const double RunningSpeed = 3.0;

    private readonly HumanPlayerTask _player = player ?? throw new ArgumentNullException(nameof(player));
    private readonly Igi2VisibilityTuning _tuning = tuning ?? throw new ArgumentNullException(nameof(tuning));
    private readonly IReadOnlyList<IBakedLight> _light = light ?? [];
    private Vector3d? _lastFeet;
    private int _ignoreLight;

    /// <summary>Gets the player.</summary>
    public HumanPlayerTask Player => _player;

    /// <summary>Gets the light the player stood in at the last update.</summary>
    public float Light { get; private set; } = tuning?.Light ?? 1f;

    /// <inheritdoc/>
    /// <remarks>The player's own gunshot past I.G.I. 2's loud range lights it for <see cref="Igi2GunFire.LoudVisibilityTicks"/>.</remarks>
    public void SendEvent(in HumanAiEvent aiEvent)
    {
        if (aiEvent.Type == (int)AiEvent.Gunshot && ReferenceEquals(aiEvent.Source, _player) && aiEvent.Radius > Igi2GunFire.LoudRange)
        {
            _ignoreLight = Igi2GunFire.LoudVisibilityTicks;
        }
    }

    /// <summary>Works the factor out for the tick just run and gives it to the player.</summary>
    /// <returns>The factor.</returns>
    public float Update()
    {
        Vector3d feet = _player.Body?.Feet ?? _player.Position;
        double speed = 0.0;
        if (_lastFeet is { } last)
        {
            double dx = feet.X - last.X, dy = feet.Y - last.Y;
            speed = Math.Sqrt((dx * dx) + (dy * dy)) * 30.0 / WorldScene.UnitsPerMetre;
        }

        _lastFeet = feet;
        Light = LightAt(feet) ?? _tuning.Light;
        float factor = Measure(_ignoreLight != 0, _player.Body?.IsKeepingLow ?? false, speed, _tuning, Light);
        if (_ignoreLight > 0)
        {
            _ignoreLight--;
        }

        _player.VisibilityFactor = factor;
        return factor;
    }

    /// <summary>The factor for a stance and a speed.</summary>
    /// <param name="lit">Whether a loud shot keeps the player seen whatever the light.</param>
    /// <param name="low">Whether the player keeps low, which I.G.I. 2 counts as crouched.</param>
    /// <param name="speed">How fast the feet go, in metres a second.</param>
    /// <param name="tuning">The visibility parameters.</param>
    /// <param name="light">The light the player stands in; <see langword="null"/> for the tuning's.</param>
    /// <returns>The factor.</returns>
    public static float Measure(bool lit, bool low, double speed, Igi2VisibilityTuning tuning, float? light = null)
    {
        ArgumentNullException.ThrowIfNull(tuning);
        float movement = speed >= RunningSpeed ? Igi2VisibilityFactor.Running
            : speed >= MovingSpeed ? Igi2VisibilityFactor.Walking
            : 0f;
        return Igi2VisibilityFactor.Measure(
            lit,
            light ?? tuning.Light,
            tuning.Gamma,
            low ? Igi2VisibilityFactor.Crouched : null,
            movement,
            tuning.Minimum,
            tuning.Maximum);
    }

    /// <summary>Gives the player back the factor its own game sees it at, as the soldiers leave the level.</summary>
    public void Reset() => _player.VisibilityFactor = 1f;

    private float? LightAt(Vector3d feet)
    {
        foreach (IBakedLight layer in _light)
        {
            if (layer.LightAt(feet) is { } lit)
            {
                return lit;
            }
        }

        return null;
    }
}

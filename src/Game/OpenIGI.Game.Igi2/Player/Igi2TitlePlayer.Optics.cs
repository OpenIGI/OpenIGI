using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Player;

namespace OpenIGI.Game.Igi2.Player;

/// <summary>The binoculars as the host presents them in the remastered style. See <see cref="ITitlePlayerOptics"/>.</summary>
public sealed partial class Igi2TitlePlayer : ITitlePlayerOptics
{
    // A person's middle above their feet, and half their height, in world units: 0.85 and 0.95 metres.
    private const double ContactMiddle = 0.85 * 4096.0;
    private const double ContactHalfHeight = 0.95 * 4096.0;

    // How long a fresh contact's entry pulse lasts, in milliseconds, and how wide it starts.
    private const float PulseMilliseconds = 250f;
    private const float PulseWidth = 10f;

    private readonly List<TitleBinocularContact> _contacts = [];
    private long _contactsTick = -1;

    /// <inheritdoc/>
    public bool IsBinocularsOpen => !IsCutscenePlaying && !Human.IsDead && ZoomOverlay == BinocularOverlayType;

    /// <inheritdoc/>
    /// <remarks>The bearing of the view's forward across the ground, as the retail compass reads it (<c>BinocularOverlay_DrawCompass</c>).</remarks>
    public float BinocularHeadingRadians
    {
        get
        {
            Matrix3f view = Human.View.Orientation;
            float heading = Igi2Pose.DirectionGamma(new Vector3f(view.M12, view.M22, view.M32));
            return heading < 0f ? heading + MathF.Tau : heading;
        }
    }

    /// <inheritdoc/>
    public float BinocularMagnification =>
        Human.View.FieldOfView > 1e-4f ? Igi2HumanView.DefaultFieldOfView / Human.View.FieldOfView : 1f;

    /// <inheritdoc/>
    /// <remarks>The overlay's own range, along the view to the first thing it rests on, a world unit being 1/4096 of a metre.</remarks>
    public float? BinocularRangeMetres => IsBinocularsOpen && ZoomRange is { } range ? (float)(range / 4096.0) : null;

    /// <inheritdoc/>
    /// <remarks>
    /// Each held slot: seen this tick at full strength, fading over the slot's keeping once lost; hostile, or one of the
    /// player's side, or out of the fight once dead. Built once a tick.
    /// </remarks>
    public IReadOnlyList<TitleBinocularContact> BinocularContacts
    {
        get
        {
            if (_contactsTick == _ticks)
            {
                return _contacts;
            }

            _contactsTick = _ticks;
            _contacts.Clear();
            if (!IsBinocularsOpen)
            {
                return _contacts;
            }

            long now = BinocularClock;
            for (int slot = 0; slot < _marks.Length; slot++)
            {
                if (_marks[slot] is not { } mark)
                {
                    continue;
                }

                float alpha = mark.IsActive ? 1f : Math.Clamp(1f - ((now - mark.LastSeen) / (float)BinocularForgetMilliseconds), 0f, 1f);
                if (alpha <= 0f)
                {
                    continue;
                }

                float fresh = Math.Clamp(1f - ((now - mark.FirstSeen) / PulseMilliseconds), 0f, 1f);
                BinocularMarkerColor marker = mark.Body.IsDead ? BinocularMarkerColor.Disabled
                    : mark.IsFriendly ? BinocularMarkerColor.Team : BinocularMarkerColor.Hostile;
                Vector3d feet = mark.Body.Position;
                _contacts.Add(new TitleBinocularContact(
                    mark.Body,
                    new Vector3d(feet.X, feet.Y, feet.Z + ContactMiddle),
                    ContactHalfHeight,
                    marker,
                    slot,
                    alpha,
                    1f + (PulseWidth * fresh)));
            }

            return _contacts;
        }
    }
}

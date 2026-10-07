using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.Igi2.Humans;

/// <summary>The player's first-person view, <c>HumanView_t</c>: where it looks, how wide, and what kicks it.</summary>
/// <remarks>Grows with the view's port (<c>HumanView</c>, <c>igi2 0x4D5C50</c>).</remarks>
public sealed class Igi2HumanView
{
    /// <summary>The steepest the view looks up or down, 85 degrees, the float <c>0x3FBDE44F</c>.</summary>
    public const float PitchLimit = 1.4835299f;

    /// <summary>The widest the view turns sideways of the body, a quarter turn.</summary>
    public const float SideLimit = 1.5707964f;

    /// <summary>Below this the look input is eased, <c>0x3D75C28F</c>.</summary>
    private const float FineAim = 0.06f;

    private const double FineAimSlope = 16.666666666666668;

    /// <summary>Gets the default field of view's scale, <c>tan(45°)</c> at single precision: one.</summary>
    public static float DefaultFieldOfView { get; } = (float)X87.ToFloat(Math.Tan(0.7853981852531433));

    /// <summary>Gets the default object field of view's scale, <c>tan(30°)</c>.</summary>
    public static float DefaultObjectFieldOfView { get; } = (float)X87.ToFloat(Math.Tan(0.5235987901687622));

    /// <summary>Gets or sets how far a landing pushed the view down, <c>vImpactOffset</c>.</summary>
    public float ImpactOffset { get; set; }

    /// <summary>Gets or sets a value indicating whether the view's orientation is frozen, <c>isLockOrientation</c>.</summary>
    public bool IsLockOrientation { get; set; }

    /// <summary>Gets or sets the impact's angle, <c>vImpactAngle</c>; a landing zeroes it.</summary>
    public float ImpactAngle { get; set; }

    /// <summary>Gets or sets the field of view's scale, <c>vFOV</c>: the tangent of half the view's width. Turning and looking are slowed by it.</summary>
    public float FieldOfView { get; set; } = DefaultFieldOfView;

    /// <summary>Gets or sets the object field of view's scale, <c>vObjectFOV</c>.</summary>
    public float ObjectFieldOfView { get; set; } = DefaultObjectFieldOfView;

    /// <summary>Gets or sets the pitch the view is aimed at, <c>vIdealViewAlpha</c>, in radians, up positive.</summary>
    public float IdealPitch { get; set; }

    /// <summary>Gets the pitch the view looks at, <c>vViewAlpha</c>: the aim plus the offsets.</summary>
    public float Pitch { get; private set; }

    /// <summary>Gets or sets what kicks the pitch for a while, <c>vOffsetViewAlpha</c>.</summary>
    public float PitchOffset { get; set; }

    /// <summary>Gets or sets the breathing sway on the pitch, <c>vBreathOffsetViewAlpha</c>.</summary>
    public float BreathPitchOffset { get; set; }

    /// <summary>Gets or sets the turn the view is aimed at sideways of the body, <c>vIdealViewGamma</c>.</summary>
    public float IdealYaw { get; set; }

    /// <summary>Gets the turn the view looks at sideways of the body, <c>vViewGamma</c>.</summary>
    public float Yaw { get; private set; }

    /// <summary>Gets or sets what kicks the sideways turn for a while, <c>vOffsetViewGamma</c>.</summary>
    public float YawOffset { get; set; }

    /// <summary>
    /// Aims the view up or down from the look input: <c>HumanView_UpdateView</c> (<c>igi2 0x4D62F0</c>).
    /// </summary>
    /// <param name="channels">The human's channels: the vertical look, and the field-of-view buttons.</param>
    /// <remarks>
    /// <para>
    /// At the default field of view the vertical look is the average of this tick's and last tick's input; zoomed
    /// in, it is this tick's alone. The step is π times the input times the field of view, so a narrow view aims
    /// finely. Input below 0.06 is eased further, divided by two less its size times 16⅔, which takes the jitter out
    /// of small mouse movements. The aim is held within 85 degrees up and down.
    /// </para>
    /// <para>
    /// The pitch looked at is the aim plus the kick and the breathing; the sideways turn is its aim plus its kick,
    /// held within a quarter turn. The field-of-view buttons narrow it by four percent, widen it by the same step,
    /// or put it back.
    /// </para>
    /// </remarks>
    public void UpdateView(Igi2HumanChannels channels)
    {
        ArgumentNullException.ThrowIfNull(channels);
        Fpu look = channels[Igi2HumanChannel.LookY];
        if (FieldOfView == DefaultFieldOfView)
        {
            look = (look + channels.Last[(int)Igi2HumanChannel.LookY]) * 0.5;
        }

        if (IsLockOrientation)
        {
            look = 0f;
        }

        float step = (((Fpu)3.1415927f * look) * FieldOfView).F;
        Fpu size = look.Value < 0.0 ? -look : look;
        if (size.Value < FineAim)
        {
            step = ((Fpu)step / ((Fpu)2.0 - (size * FineAimSlope))).F;
        }

        Fpu aim = (Fpu)step + IdealPitch;
        IdealPitch = aim.Value < -PitchLimit ? -PitchLimit : !(aim.Value > PitchLimit) ? aim.F : PitchLimit;

        Fpu pitch = (Fpu)BreathPitchOffset + PitchOffset + IdealPitch;
        Pitch = pitch.F;
        float yaw = ((Fpu)YawOffset + IdealYaw).F;
        Yaw = yaw;

        if (channels.IsHeld(Igi2HumanChannel.FieldOfViewNarrow))
        {
            FieldOfView = ((Fpu)FieldOfView * 0.96f).F;
            ObjectFieldOfView = ((Fpu)ObjectFieldOfView * 0.96f).F;
        }

        if (channels.IsHeld(Igi2HumanChannel.FieldOfViewWiden))
        {
            FieldOfView = ((Fpu)FieldOfView * 1.0416667f).F;
            ObjectFieldOfView = ((Fpu)ObjectFieldOfView * 1.0416667f).F;
        }

        if (channels.IsHeld(Igi2HumanChannel.FieldOfViewReset))
        {
            FieldOfView = DefaultFieldOfView;
            ObjectFieldOfView = DefaultObjectFieldOfView;
        }

        if (pitch.Value < -PitchLimit)
        {
            Pitch = -PitchLimit;
        }
        else if (pitch.Value > PitchLimit)
        {
            Pitch = PitchLimit;
        }

        if (yaw < -SideLimit)
        {
            Yaw = -SideLimit;
        }
        else if (yaw > SideLimit)
        {
            Yaw = SideLimit;
        }
    }
}

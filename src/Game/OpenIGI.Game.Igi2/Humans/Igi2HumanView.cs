using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Igi2.Animation;

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

    /// <summary>Gets or sets the rig of the first-person model the view carries, or <see langword="null"/> for none.</summary>
    /// <remarks>
    /// The view is itself a boned object with the human's first-person model (<c>HumanView_CreateHandler</c>,
    /// <c>igi2 0x4D5A30</c>, falling back to <c>005_01_1</c> for a missing or unboned one); the arms and the weapon in
    /// them play their clips on it.
    /// </remarks>
    public Igi2BoneHierarchy? ObjectRig { get; set; }

    /// <summary>Gets or sets the name of the first-person model the view carries, empty for none.</summary>
    public string ObjectModel { get; set; } = string.Empty;

    /// <summary>Gets where the first-person model stands, <c>tModelObj.tPos</c>: the eyes plus the object's offset.</summary>
    public Vector3d ObjectPosition { get; private set; }

    /// <summary>Gets the first-person model's orientation, <c>tModelObj.tOrientation</c>: the view's turned by the object's pitch and turn.</summary>
    public Matrix3f ObjectOrientation { get; private set; } = Matrix3f.Identity;

    /// <summary>Gets or sets how far the held object would be pulled back from what is in front of it, <c>vRetractIdeal</c>; zero or less.</summary>
    public float RetractIdeal { get; set; }

    /// <summary>Gets or sets how far the held object is pulled back now, <c>vRetractReal</c>; it steps toward <see cref="RetractIdeal"/>.</summary>
    public float RetractReal { get; set; }

    /// <summary>Gets a value indicating whether the first-person model is drawn with the view, <c>isObjectRegistered</c>.</summary>
    public bool IsObjectRegistered => ObjectRig is not null;

    /// <summary>Gets the first-person model's clip, <c>ptAnimController</c>.</summary>
    /// <remarks>Made not stepping itself (<c>HumanView_CreateHandler</c> clears the bit): only the view's update steps it.</remarks>
    public Igi2AnimController ObjectAnimation { get; } = new() { Flags = Igi2AnimControllerBits.NoClip };

    /// <summary>Gets or sets a value indicating whether a scope's zoom narrows the view, <c>isZoomMode</c>.</summary>
    public bool IsZoomMode { get; set; }

    /// <summary>Takes the scope's zoom off and the fields of view back to their defaults (<c>HumanView_DisableZoomMode</c>, <c>igi2 0x4D72E0</c>).</summary>
    public void DisableZoomMode()
    {
        IsZoomMode = false;
        FieldOfView = DefaultFieldOfView;
        ObjectFieldOfView = DefaultObjectFieldOfView;
    }

    /// <summary>Gets or sets how far a landing pushed the view down, <c>vImpactOffset</c>.</summary>
    public float ImpactOffset { get; set; }

    /// <summary>Gets or sets a value indicating whether the view's orientation is frozen, <c>isLockOrientation</c>.</summary>
    public bool IsLockOrientation { get; set; }

    /// <summary>
    /// Gets a value indicating whether something else says where the eyes are, <c>isOverride</c>: a mounted gun's seat. The camera
    /// and the human's weapon take <see cref="OverridePosition"/> and <see cref="OverrideOrientation"/> then.
    /// </summary>
    public bool IsOverride { get; private set; }

    /// <summary>Gets where the eyes are while overridden, <c>tOverridePos</c>.</summary>
    public Vector3d OverridePosition { get; private set; }

    /// <summary>Gets which way the eyes face while overridden, <c>tOverrideOrientation</c>, row by row, its columns right, forward, up.</summary>
    public Matrix3f OverrideOrientation { get; private set; } = Matrix3f.Identity;

    /// <summary>Puts the eyes somewhere else, <c>HumanView_OverrideView</c>.</summary>
    /// <param name="position">Where.</param>
    /// <param name="orientation">Which way, row by row.</param>
    public void OverrideView(Vector3d position, Matrix3f orientation)
    {
        IsOverride = true;
        OverridePosition = position;
        OverrideOrientation = orientation;
    }

    /// <summary>Gives the eyes back to the view, <c>HumanView_ClearOverrideView</c>.</summary>
    public void ClearOverrideView() => IsOverride = false;

    /// <summary>Gets or sets the impact's angle, <c>vImpactAngle</c>; a landing zeroes it.</summary>
    public float ImpactAngle { get; set; }

    /// <summary>Gets or sets the field of view's scale, <c>vFOV</c>: the tangent of half the view's width. Turning and looking are slowed by it.</summary>
    public float FieldOfView { get; set; } = DefaultFieldOfView;

    /// <summary>Gets or sets the object field of view's scale, <c>vObjectFOV</c>.</summary>
    public float ObjectFieldOfView { get; set; } = DefaultObjectFieldOfView;

    /// <summary>Gets or sets the field of view's scale a zoom steers to, <c>HumanView+0x2A4</c>.</summary>
    public float IdealFieldOfView { get; set; }

    /// <summary>Gets or sets how far the field of view's scale steps toward <see cref="IdealFieldOfView"/> a tick, <c>HumanView+0x2A8</c>.</summary>
    public float FieldOfViewSpeed { get; set; }

    /// <summary>
    /// Steps a zoom's field of view toward its aim, the zoom half of <c>HumanView_RunHandler</c> (<c>igi2 0x4D5C50</c>),
    /// which runs in the tick's run pass: by its speed, and no further than the aim.
    /// </summary>
    public void RunZoom()
    {
        if (!IsZoomMode)
        {
            return;
        }

        if (FieldOfView < IdealFieldOfView)
        {
            FieldOfView = (new Fpu(FieldOfViewSpeed) + FieldOfView).F;
            if (FieldOfView > IdealFieldOfView)
            {
                FieldOfView = IdealFieldOfView;
            }
        }
        else if (FieldOfView > IdealFieldOfView)
        {
            FieldOfView = (new Fpu(FieldOfView) - FieldOfViewSpeed).F;
            if (FieldOfView < IdealFieldOfView)
            {
                FieldOfView = IdealFieldOfView;
            }
        }
    }

    /// <summary>
    /// Aims the zoom at a field of view's scale, at the speed that closes a third of the gap a tick
    /// (<c>Zoom_WeaponFireHandler</c>, <c>igi2 0x525B20</c>; <c>Zoom_WeaponZoomHandler</c>, <c>0x525CC0</c>).
    /// </summary>
    /// <param name="ideal">The scale to steer to.</param>
    public void AimZoom(float ideal)
    {
        IdealFieldOfView = ideal;
        float speed = ((new Fpu(FieldOfView) - ideal) * 0.33333334f).F;
        FieldOfViewSpeed = speed < 0f ? -speed : speed;
    }

    /// <summary>Gets or sets the pitch the view is aimed at, <c>vIdealViewAlpha</c>, in radians, up positive.</summary>
    public float IdealPitch { get; set; }

    /// <summary>Gets or sets the pitch the view looks at, <c>vViewAlpha</c>: the aim plus the offsets.</summary>
    public float Pitch { get; set; }

    /// <summary>Gets or sets what kicks the pitch for a while, <c>vOffsetViewAlpha</c>.</summary>
    public float PitchOffset { get; set; }

    /// <summary>Gets or sets the breathing sway on the pitch, <c>vBreathOffsetViewAlpha</c>.</summary>
    public float BreathPitchOffset { get; set; }

    /// <summary>
    /// Gets or sets the pitch the player's spine is tilted by, <c>HumanView+0x210</c>: eased toward <see cref="Pitch"/> by the lower
    /// back's callback, read by the upper back's and the shoulders'.
    /// </summary>
    public float ShouldersPitch { get; set; }

    /// <summary>Gets or sets the turn the view is aimed at sideways of the body, <c>vIdealViewGamma</c>.</summary>
    public float IdealYaw { get; set; }

    /// <summary>Gets the turn the view looks at sideways of the body, <c>vViewGamma</c>.</summary>
    public float Yaw { get; private set; }

    /// <summary>Gets or sets what kicks the sideways turn for a while, <c>vOffsetViewGamma</c>.</summary>
    public float YawOffset { get; set; }

    /// <summary>
    /// Looks straight ahead of the body: the pitch, the sideways turn, what they are aimed at, their kicks and the breathing
    /// sway all zero, as <c>HumanPlayer_InitStateActivateDoor</c> (<c>igi2 0x4AD4CD</c>) leaves the view.
    /// </summary>
    /// <remarks>The shoulders' pitch is zeroed with them.</remarks>
    public void LookAhead()
    {
        ShouldersPitch = 0f;
        Pitch = 0f;
        IdealPitch = 0f;
        PitchOffset = 0f;
        BreathPitchOffset = 0f;
        Yaw = 0f;
        IdealYaw = 0f;
        YawOffset = 0f;
    }

    /// <summary>Gets or sets how fast a kick to the view dies away each tick, <c>vDampenOffsetView</c>; zero when the view is made.</summary>
    public float OffsetDamping { get; set; }

    /// <summary>Gets or sets where the eyes are, <c>HumanView_t.tPos</c>: placed by <see cref="Igi2Human.UpdateViewBody"/>.</summary>
    public Vector3d Position { get; set; }

    /// <summary>Gets or sets which way the eyes look, <c>HumanView_t.tOrientation</c>: rows are the view's X, Y and Z axes.</summary>
    public Matrix3f Orientation { get; set; } = Matrix3f.Identity;

    /// <summary>Gets or sets how far the view leans out of the body, in the view's frame, <c>tPeekOffset</c>.</summary>
    public Vector3f PeekOffset { get; set; }

    /// <summary>Gets or sets how fast the lean is moving, <c>tPeekSpeed</c>.</summary>
    public Vector3f PeekSpeed { get; set; }

    /// <summary>Gets or sets how hard the view shakes, <c>vShakeFactor</c>; it runs down to zero by <see cref="ShakeTime"/> each tick.</summary>
    public float ShakeFactor { get; set; }

    /// <summary>Gets or sets the largest turn a shake gives the view, <c>vShakeAngle</c>.</summary>
    public float ShakeAngle { get; set; }

    /// <summary>Gets or sets the largest move a shake gives the eyes, <c>vShakeRange</c>.</summary>
    public float ShakeRange { get; set; }

    /// <summary>Gets or sets how much of the shake runs out each tick, <c>vShakeTime</c>.</summary>
    public float ShakeTime { get; set; }

    /// <summary>Gets or sets the held object's pitch, <c>vObjectAlpha</c>; it steps toward <see cref="IdealObjectPitch"/>.</summary>
    public float ObjectPitch { get; set; }

    /// <summary>Gets or sets the held object's aimed pitch, <c>vIdealObjectAlpha</c>.</summary>
    public float IdealObjectPitch { get; set; }

    /// <summary>Gets or sets how far the held object's pitch steps each tick, <c>vStepAlpha</c>.</summary>
    public float ObjectPitchStep { get; set; }

    /// <summary>Gets or sets the held object's turn, <c>vObjectGamma</c>; it steps toward <see cref="IdealObjectYaw"/>.</summary>
    public float ObjectYaw { get; set; }

    /// <summary>Gets or sets the held object's aimed turn, <c>vIdealObjectGamma</c>.</summary>
    public float IdealObjectYaw { get; set; }

    /// <summary>Gets or sets how far the held object's turn steps each tick, <c>vStepGamma</c>.</summary>
    public float ObjectYawStep { get; set; }

    /// <summary>
    /// Lets a kick to the view die away, as the end of <c>HumanPlayer_Update</c> (<c>igi2 0x4A268F</c>) does: each offset
    /// loses <see cref="OffsetDamping"/> of itself.
    /// </summary>
    public void DampenOffsets()
    {
        PitchOffset = ((-(Fpu)PitchOffset * OffsetDamping) + PitchOffset).F;
        YawOffset = ((-(Fpu)YawOffset * OffsetDamping) + YawOffset).F;
    }

    /// <summary>Places the first-person model: at a position and turned by the object's pitch and turn from the view.</summary>
    /// <param name="position">The eyes plus the object's offset.</param>
    /// <param name="orientation">The model's orientation.</param>
    internal void PlaceObject(Vector3d position, Matrix3f orientation)
    {
        ObjectPosition = position;
        ObjectOrientation = orientation;
    }

    /// <summary>Steps the held object's pitch and turn toward their aims, by their steps, without passing them.</summary>
    /// <remarks>The middle of <c>HumanView_UpdateBody</c> (<c>igi2 0x4D6D23</c>).</remarks>
    internal void StepObject()
    {
        if (ObjectPitch == IdealObjectPitch && ObjectYaw == IdealObjectYaw)
        {
            return;
        }

        ObjectPitch = Toward(ObjectPitch, IdealObjectPitch, ObjectPitchStep);
        ObjectYaw = Toward(ObjectYaw, IdealObjectYaw, ObjectYawStep);
    }

    private static float Toward(float value, float ideal, float step)
    {
        if (value < ideal)
        {
            float raised = ((Fpu)step + value).F;
            return raised > ideal ? ideal : raised;
        }

        if (value > ideal)
        {
            float lowered = ((Fpu)value - step).F;
            return lowered < ideal ? ideal : lowered;
        }

        return value;
    }

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

    /// <summary>
    /// Writes its save state, <c>HumanView_ptSerializeParameterList</c>: whether its first-person model shows and is in the world, where
    /// the eyes are and how they are turned, its pitch and yaw with their ideals and offsets, its flags, its override, its peek, its
    /// shake, its impact, its first-person model's angles and steps, its fields of view, its zoom, its translations, and its retraction.
    /// </summary>
    /// <param name="writer">The writer.</param>
    public void WriteState(Saves.Igi2StateWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        Matrix3f o = Orientation;
        writer.Bool(ObjectModel.Length > 0);
        writer.Bool(ObjectModel.Length > 0);
        writer.Real64x3(Position);
        writer.Real9([o.M11, o.M12, o.M13, o.M21, o.M22, o.M23, o.M31, o.M32, o.M33]);
        writer.Real(Pitch);
        writer.Real(IdealPitch);
        writer.Real(PitchOffset);
        writer.Real(Yaw);
        writer.Real(IdealYaw);
        writer.Real(YawOffset);
        writer.Bool(IsLockOrientation);
        writer.Bool(false);
        writer.Bool(IsOverride);
        writer.Real64x3(OverridePosition);
        writer.Real3(PeekOffset);
        writer.Real3(PeekSpeed);
        writer.Real(ShakeFactor);
        writer.Real(ShakeRange);
        writer.Real(ShakeAngle);
        writer.Real(ShakeTime);
        writer.Real(ImpactOffset);
        writer.Real(ImpactAngle);
        writer.Real(ObjectPitch);
        writer.Real(IdealObjectPitch);
        writer.Real(ObjectPitchStep);
        writer.Real(ObjectYaw);
        writer.Real(IdealObjectYaw);
        writer.Real(ObjectYawStep);
        writer.Real(FieldOfView);
        writer.Real(ObjectFieldOfView);
        writer.Bool(IsZoomMode);
        writer.Real3(default);
        writer.Real3(default);
        writer.Real(RetractIdeal);
        writer.Real(RetractReal);
    }

    /// <summary>Reads its save state back; an override is put back only as the stationary gun that set it sets it again.</summary>
    /// <param name="reader">The reader.</param>
    public void ReadState(Saves.Igi2StateReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        reader.Bool();
        reader.Bool();
        Position = reader.Real64x3();
        float[] o = reader.Real9();
        Orientation = new Matrix3f(o[0], o[1], o[2], o[3], o[4], o[5], o[6], o[7], o[8]);
        Pitch = reader.Real();
        IdealPitch = reader.Real();
        PitchOffset = reader.Real();
        Yaw = reader.Real();
        IdealYaw = reader.Real();
        YawOffset = reader.Real();
        IsLockOrientation = reader.Bool();
        reader.Bool();
        reader.Bool();
        reader.Real64x3();
        PeekOffset = reader.Real3();
        PeekSpeed = reader.Real3();
        ShakeFactor = reader.Real();
        ShakeRange = reader.Real();
        ShakeAngle = reader.Real();
        ShakeTime = reader.Real();
        ImpactOffset = reader.Real();
        ImpactAngle = reader.Real();
        ObjectPitch = reader.Real();
        IdealObjectPitch = reader.Real();
        ObjectPitchStep = reader.Real();
        ObjectYaw = reader.Real();
        IdealObjectYaw = reader.Real();
        ObjectYawStep = reader.Real();
        FieldOfView = reader.Real();
        ObjectFieldOfView = reader.Real();
        IsZoomMode = reader.Bool();
        reader.Real3();
        reader.Real3();
        RetractIdeal = reader.Real();
        RetractReal = reader.Real();
    }
}

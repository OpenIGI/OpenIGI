using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Player;

namespace OpenIGI.Game.Igi2.Player;

/// <content>Frame-accurate mouse aim with the same sensitivity curve as IGI1, committed on the fixed tick.</content>
public sealed partial class Igi2TitlePlayer : ITitlePlayerLook
{
    private TitleLookPreview? _look;
    private TitlePlayerInput _lookInput;

    /// <inheritdoc/>
    public bool CanPreviewLook => !IsCutscenePlaying && !Human.IsDead && !Human.IsLockOrientation
        && !Human.View.IsLockOrientation && !Human.View.IsOverride && !Human.Channels.IsLocked;

    /// <inheritdoc/>
    public Matrix3f LookOrientation => Look.Orientation;

    /// <inheritdoc/>
    public void GatherLook(float mouseX, float mouseY, float sensitivity, bool invertMouse) => Look.Gather(mouseX, mouseY, sensitivity, invertMouse);

    /// <inheritdoc/>
    public void ResetLook() => Look.Reset();

    /// <inheritdoc/>
    public Matrix3f PreviewLook(float mouseX, float mouseY, float sensitivity, bool invertMouse) =>
        Look.Preview(mouseX, mouseY, sensitivity, invertMouse);

    // The look between ticks, from where the human faces as of its last.
    private TitleLookPreview Look => _look ??= new(() => CanPreviewLook, () => (Human.Gamma, Human.View.IdealPitch), SmoothAim, Aim);

    private (float Gamma, float Pitch) SmoothAim(float mouseX, float mouseY, float sensitivity, bool invertMouse, float gamma, float pitch)
    {
        // I.G.I. 2's turn for the shared Smooth Motion look (TitleLookPreview). Retail human.c Human_UpdateBody and humanview.c
        // HumanView_UpdateView remain unchanged when smoothing is disabled.
        MouseLookSettings settings = MouseLookSettings.FromSensitivity(sensitivity, invertMouse);
        float zoom = MouseLookSettings.ZoomSensitivityScale(Human.View.FieldOfView);
        // I.G.I. 2's pitch, as the shared one, is up positive (humanview.c HumanView_UpdateView); its gamma turns the other way.
        return (((Fpu)gamma - ((Fpu)settings.YawDelta(mouseX) * zoom)).F,
            Math.Clamp(((Fpu)pitch + ((Fpu)settings.PitchDelta(mouseY) * zoom)).F, -Igi2HumanView.PitchLimit, Igi2HumanView.PitchLimit));
    }

    private void WriteSmoothLook()
    {
        (float gamma, float pitch) = Look.Next(_lookInput.MouseX, _lookInput.MouseY, _lookInput.MouseSensitivity, _lookInput.InvertMouse);
        Fpu divisor = (Fpu)3.1415927f * Human.View.FieldOfView;
        _frame[(int)Igi2HumanChannel.LookX] = divisor.Value == 0.0 ? 0f : (-((Fpu)gamma - Human.Gamma) / divisor).F;
        _frame[(int)Igi2HumanChannel.LookY] = divisor.Value == 0.0 ? 0f : (((Fpu)pitch - Human.View.IdealPitch) / divisor).F;
    }

    private static Matrix3f Aim(float gamma, float pitch) => Igi2Pose.FromAngles(default, new Vector3f(pitch, 0f, gamma)).ToMatrix();
}

using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Player;

namespace OpenIGI.Game.Igi2.Player;

/// <content>Frame-accurate mouse aim with the same sensitivity curve as IGI1, committed on the fixed tick.</content>
public sealed partial class Igi2TitlePlayer : ITitlePlayerLook
{
    private bool _frameAimPending;
    private float _frameGamma;
    private float _framePitch;
    private TitlePlayerInput _lookInput;

    /// <inheritdoc/>
    public bool CanPreviewLook => !IsCutscenePlaying && !Human.IsDead && !Human.IsLockOrientation
        && !Human.View.IsLockOrientation && !Human.View.IsOverride && !Human.Channels.IsLocked;

    /// <inheritdoc/>
    public Matrix3f LookOrientation => Aim(Human.Gamma, Human.View.IdealPitch);

    /// <inheritdoc/>
    public void GatherLook(float mouseX, float mouseY, float sensitivity, bool invertMouse)
    {
        if (!CanPreviewLook) { ResetLook(); return; }
        if (!_frameAimPending)
        {
            _frameGamma = Human.Gamma;
            _framePitch = Human.View.IdealPitch;
        }
        (_frameGamma, _framePitch) = SmoothAim(mouseX, mouseY, sensitivity, invertMouse, _frameGamma, _framePitch);
        _frameAimPending = true;
    }

    /// <inheritdoc/>
    public void ResetLook() => _frameAimPending = false;

    /// <inheritdoc/>
    public Matrix3f PreviewLook(float mouseX, float mouseY, float sensitivity, bool invertMouse)
    {
        if (!CanPreviewLook)
        {
            return LookOrientation;
        }

        if (_frameAimPending) { return Aim(_frameGamma, _framePitch); }
        (float gamma, float pitch) = SmoothAim(mouseX, mouseY, sensitivity, invertMouse, Human.Gamma, Human.View.IdealPitch);
        return Aim(gamma, pitch);
    }

    private (float Gamma, float Pitch) SmoothAim(float mouseX, float mouseY, float sensitivity, bool invertMouse, float gamma, float pitch)
    {
        // Deliberate Smooth Motion policy, shared with IGI1 PlayerCamera.Look. Retail human.c Human_UpdateBody
        // and humanview.c HumanView_UpdateView remain unchanged when smoothing is disabled.
        MouseLookSettings settings = MouseLookSettings.FromSensitivity(sensitivity, invertMouse);
        float zoom = MouseLookSettings.ZoomSensitivityScale(Human.View.FieldOfView);
        return (((Fpu)gamma - ((Fpu)settings.YawDelta(mouseX) * zoom)).F,
            Math.Clamp(((Fpu)pitch + ((Fpu)settings.PitchDelta(mouseY) * zoom)).F, -Igi2HumanView.PitchLimit, Igi2HumanView.PitchLimit));
    }

    private void WriteSmoothLook()
    {
        (float gamma, float pitch) = _frameAimPending ? (_frameGamma, _framePitch)
            : SmoothAim(_lookInput.MouseX, _lookInput.MouseY, _lookInput.MouseSensitivity, _lookInput.InvertMouse,
                Human.Gamma, Human.View.IdealPitch);
        Fpu divisor = (Fpu)3.1415927f * Human.View.FieldOfView;
        _frame[(int)Igi2HumanChannel.LookX] = divisor.Value == 0.0 ? 0f : (-((Fpu)gamma - Human.Gamma) / divisor).F;
        _frame[(int)Igi2HumanChannel.LookY] = divisor.Value == 0.0 ? 0f : (((Fpu)pitch - Human.View.IdealPitch) / divisor).F;
    }

    private static Matrix3f Aim(float gamma, float pitch) => Igi2Pose.FromAngles(default, new Vector3f(pitch, 0f, gamma)).ToMatrix();
}

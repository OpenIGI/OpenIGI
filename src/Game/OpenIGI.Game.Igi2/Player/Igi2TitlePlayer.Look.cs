using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Player;

namespace OpenIGI.Game.Igi2.Player;

/// <content>Render-time mouse aim; simulation still consumes the unchanged input on its fixed tick.</content>
public sealed partial class Igi2TitlePlayer : ITitlePlayerLook
{
    /// <inheritdoc/>
    public bool CanPreviewLook => !IsCutscenePlaying && !Human.IsDead && !Human.IsLockOrientation
        && !Human.View.IsLockOrientation && !Human.View.IsOverride;

    /// <inheritdoc/>
    public Matrix3f LookOrientation => Aim(Human.Gamma, Human.View.IdealPitch);

    /// <inheritdoc/>
    public Matrix3f PreviewLook(float mouseX, float mouseY, float sensitivity, bool invertMouse)
    {
        if (!CanPreviewLook)
        {
            return LookOrientation;
        }

        // human.c Human_UpdateBody / humanview.c HumanView_UpdateView: the same sensitivity,
        // analog bounds, field-of-view scale and fine-aim curve; only the pending mouse contribution.
        float gain = Math.Clamp(sensitivity, Igi2PlayerInputReader.MinimumSensitivity, Igi2PlayerInputReader.MaximumSensitivity);
        float x = ((Fpu)gain * Analog(mouseX)).F;
        Fpu y = (Fpu)gain * Analog(invertMouse ? -mouseY : mouseY);
        float fov = Human.View.FieldOfView;
        float gamma = ((-((Fpu)x * 3.1415927f) * fov) + Human.Gamma).F;
        if (fov == Igi2HumanView.DefaultFieldOfView)
        {
            y *= 0.5;
        }

        float step = (((Fpu)3.1415927f * y) * fov).F;
        Fpu size = y.Value < 0.0 ? -y : y;
        if (size.Value < 0.06f)
        {
            step = ((Fpu)step / ((Fpu)2.0 - (size * 16.666666666666668))).F;
        }

        float pitch = Math.Clamp(((Fpu)step + Human.View.IdealPitch).F, -Igi2HumanView.PitchLimit, Igi2HumanView.PitchLimit);
        return Aim(gamma, pitch);
    }

    private static Matrix3f Aim(float gamma, float pitch) => Igi2Pose.FromAngles(default, new Vector3f(pitch, 0f, gamma)).ToMatrix();
}

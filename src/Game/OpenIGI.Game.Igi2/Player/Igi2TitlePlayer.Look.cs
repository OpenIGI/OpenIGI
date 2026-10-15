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
        && !Human.View.IsLockOrientation && !Human.View.IsOverride && !Human.Channels.IsLocked;

    /// <inheritdoc/>
    public Matrix3f LookOrientation => Aim(Human.Gamma, Human.View.IdealPitch);

    /// <inheritdoc/>
    public Matrix3f PreviewLook(float mouseX, float mouseY, float sensitivity, bool invertMouse)
    {
        if (!CanPreviewLook)
        {
            return LookOrientation;
        }

        // Smooth Motion follows IGI1's frame look: linear pending counts, without retail's tick cap,
        // two-tick pitch average or fine-aim curve. The tick consumes this same mouse policy.
        // With Smooth Motion off, human.c Human_UpdateBody / humanview.c HumanView_UpdateView stay retail.
        float gain = Math.Clamp(sensitivity, Igi2PlayerInputReader.MinimumSensitivity, Igi2PlayerInputReader.MaximumSensitivity);
        float x = ((Fpu)gain * Analog(mouseX, unfilteredMouse: true)).F;
        Fpu y = (Fpu)gain * Analog(invertMouse ? -mouseY : mouseY, unfilteredMouse: true);
        float fov = Human.View.FieldOfView;
        float gamma = ((-((Fpu)x * 3.1415927f) * fov) + Human.Gamma).F;
        float step = (((Fpu)3.1415927f * y) * fov).F;
        float pitch = Math.Clamp(((Fpu)step + Human.View.IdealPitch).F, -Igi2HumanView.PitchLimit, Igi2HumanView.PitchLimit);
        return Aim(gamma, pitch);
    }

    private static Matrix3f Aim(float gamma, float pitch) => Igi2Pose.FromAngles(default, new Vector3f(pitch, 0f, gamma)).ToMatrix();
}

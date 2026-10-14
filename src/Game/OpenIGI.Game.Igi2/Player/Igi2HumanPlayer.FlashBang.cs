using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Weapons;

namespace OpenIGI.Game.Igi2.Player;

/// <content>Flashbang exposure and fading from <c>humanplayer.c HumanPlayer_FlashBangHandler</c>.</content>
public sealed partial class Igi2HumanPlayer
{
    /// <summary>Gets or sets the white flash intensity, the retail save field <c>vFlashBangFlash</c>.</summary>
    public float FlashBangFlash { get; set; }

    /// <inheritdoc/>
    public override void FlashBang(in Igi2FlashBangHit hit)
    {
        if (!IsActive)
        {
            return;
        }

        Vector3d at = hit.Position, eyes = View.Position;
        Fpu ex = (Fpu)at.X - eyes.X, ey = (Fpu)at.Y - eyes.Y, ez = (Fpu)at.Z - eyes.Z;
        Fpu distance = Fpu.Sqrt(((ey * ey) + (ez * ez)) + (ex * ex));
        if (distance.Value >= hit.Range)
        {
            return;
        }

        Fpu x = (Fpu)at.X - Position.X, y = (Fpu)at.Y - Position.Y, z = (Fpu)at.Z - Position.Z;
        Matrix3f o = Orientation;
        float side = (((z * o.M31) + (y * o.M21)) + (x * o.M11)).F;
        float forward = (((z * o.M32) + (y * o.M22)) + (x * o.M12)).F;
        Fpu up = ((z * o.M33) + (y * o.M23)) + (x * o.M13);
        if (forward <= 0f)
        {
            return;
        }

        float share = (((Fpu)1.0 / Fpu.Sqrt((((Fpu)side * side) + ((Fpu)forward * forward)) + (up * up))) * forward * 5.0).F;
        Vector3d raised = new(at.X, at.Y, ((Fpu)at.Z + 409.6000061035156).Value);
        if (hit.Sight is { } sight && !sight.LineOfSight(raised, eyes, 0xFFFFFFFF, out _, this))
        {
            return;
        }

        Fpu falloff = (Fpu)1.0 - (distance / hit.Range);
        if (falloff.Value < 0.0)
        {
            falloff = -falloff;
        }

        FlashBangFlash = ((Fpu)share * falloff).F;
    }

    /// <summary>Fades a flash, <c>humanplayer.c HumanPlayer_UpdateFlashBangFlash</c>, with a slower tail below one.</summary>
    private void UpdateFlashBangFlash()
    {
        if (FlashBangFlash != 0f)
        {
            float fade = FlashBangFlash <= 1f ? 0.008333333767950535f : 0.01666666753590107f;
            FlashBangFlash = MathF.Max(0f, ((Fpu)FlashBangFlash - fade).F);
        }
    }
}

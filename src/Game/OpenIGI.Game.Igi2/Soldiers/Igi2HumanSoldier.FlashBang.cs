using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Ai;
using OpenIGI.Game.Igi2.Weapons;

namespace OpenIGI.Game.Igi2.Soldiers;

/// <content>The soldier's flashbang response, <c>humansoldier.c HumanSoldier_FlashBangHandler</c>.</content>
public sealed partial class Igi2HumanSoldier
{
    /// <inheritdoc/>
    public override void FlashBang(in Igi2FlashBangHit hit)
    {
        Fpu x = (Fpu)hit.Position.X - Position.X, y = (Fpu)hit.Position.Y - Position.Y, z = (Fpu)hit.Position.Z - Position.Z;
        Matrix3f o = Orientation;
        if (Fpu.Sqrt(((x * x) + (y * y)) + (z * z)).Value <= hit.Range
            && (((z * o.M32) + (y * o.M22)) + (x * o.M12)).Value >= 0.0)
        {
            Mind?.Perceive(this, new Igi2AiEvent(18, hit.Team, 67, hit.Range, hit.Position, default, null));
        }
    }
}

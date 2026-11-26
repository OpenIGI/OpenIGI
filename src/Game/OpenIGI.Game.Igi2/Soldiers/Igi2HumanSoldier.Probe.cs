using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Humans;

namespace OpenIGI.Game.Igi2.Soldiers;

/// <content>A debugging tool's harm (<see cref="Game.Multiplayer.NetProbe"/>). <b>Temporary, not retail.</b></content>
public sealed partial class Igi2HumanSoldier
{
    /// <summary>
    /// Takes a share of the soldier's health from its chest, as a round of the server's own would: the hit's flinch and cry
    /// short of its health, its death past it.
    /// </summary>
    /// <param name="share">The share of its health; one or more kills it.</param>
    public void HarmFromConsole(float share)
    {
        if (IsDead)
        {
            return;
        }

        if (share >= 1f)
        {
            KillOutright();
            return;
        }

        float amount = Math.Max(0f, share) * Damage.MaxDamage;
        Damage.LastPart = Igi2DamagePart.Chest;
        Damage.Add(Igi2DamagePart.Chest, amount);
        if (Damage.IsFatal)
        {
            KillOutright();
            return;
        }

        RecoilMeter = ((Fpu)amount + RecoilMeter).F;
        Signal |= Igi2SoldierSignal.Hit;
        PlayHitSound(HurtSounds);
    }
}

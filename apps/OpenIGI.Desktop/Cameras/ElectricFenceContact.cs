using OpenIGI.Core.Numerics;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Player;

namespace OpenIGI.Desktop;

/// <summary>The result of the port's current electric-fence contact rule.</summary>
internal readonly record struct ElectricFenceShock(float Damage, bool IsLethal);

/// <summary>Small, testable pieces of player electric-fence contact handling.</summary>
/// <remarks>
/// <para>
/// <see cref="DamageFactor"/> and <see cref="Zone"/> deliberately preserve the existing port
/// behavior. They are not a claim that the retail fence uses this damage factor or hit zone: that
/// requires the mandated IGI1 <c>IGI.EXE</c> Ghidra+r2 validation, which is unavailable to this
/// implementation environment.
/// </para>
/// <para>
/// Keeping the rule here makes its health, transition and notification behavior regressible without
/// broadening the desktop game's private mission bootstrap into a test API.
/// </para>
/// </remarks>
internal static class ElectricFenceContact
{
    /// <summary>The currently ported fence-contact damage factor, pending retail validation.</summary>
    internal const float DamageFactor = 0.1f;

    /// <summary>The currently ported fence-contact hit zone, pending retail validation.</summary>
    internal const int Zone = HumanDamage.FrontZone;

    /// <summary>
    /// Records an observed contact and returns it only when the player has newly entered that contact.
    /// </summary>
    internal static T? Advance<T>(ref T? activeContact, T? observedContact)
        where T : class
    {
        if (ReferenceEquals(activeContact, observedContact))
        {
            return null;
        }

        activeContact = observedContact;
        return observedContact;
    }

    /// <summary>Applies the current ported shock through the ordinary player health path.</summary>
    internal static ElectricFenceShock Shock(HumanHealth health)
    {
        ArgumentNullException.ThrowIfNull(health);
        float damage = health.Apply(Zone, DamageFactor);
        return new ElectricFenceShock(damage, health.IsDead);
    }

    /// <summary>Posts a damaging shock as a normal ground-impact event for nearby AI.</summary>
    internal static void NotifyAiOnShock(
        ElectricFenceShock shock,
        Action<Vector3d, Vector3d, double>? notifyGroundImpact,
        Vector3d fenceAnchor,
        Vector3d playerPosition)
    {
        if (shock.Damage > 0f)
        {
            notifyGroundImpact?.Invoke(
                fenceAnchor,
                playerPosition,
                AiSoldier.GroundImpactRadiusInUnits);
        }
    }
}

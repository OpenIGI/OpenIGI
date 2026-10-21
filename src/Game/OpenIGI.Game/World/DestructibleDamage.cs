using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>
/// What a weapon hit or an <c>Explosion0</c> contact does to one destructible: the contact dose it
/// receives, the damage it adds up against its threshold, and the ExplodeObject countdown that arms.
/// </summary>
internal static class DestructibleDamage
{
    /// <summary>One destructible's share of an <c>Explosion0</c>: its own <c>ExplosionHit</c>, as the explosion list sends it.</summary>
    /// <param name="item">The destructible.</param>
    /// <param name="source">The object that owns the blast, which its own fireball does not hit.</param>
    /// <param name="originUnits">The explosion centre in engine units.</param>
    /// <param name="radiusUnits">The full-damage radius.</param>
    /// <param name="falloffUnits">The outer contact range after the full-damage radius.</param>
    /// <param name="damageFactor">The damage delivered by a full-strength contact.</param>
    /// <param name="canContact">Optional world-visibility gate for the exact target.</param>
    /// <param name="sourceWeaponId">The weapon whose record the blast carries as its source, or 0.</param>
    /// <returns>The glass or explosive object whose native hit callback should run, or <see langword="null"/>.</returns>
    internal static DestructibleObject? ExplosionHit(
        DestructibleObject item,
        DestructibleObject? source,
        Vector3d originUnits,
        float radiusUnits,
        float falloffUnits = 0f,
        float damageFactor = 1f,
        Func<DestructibleObject, bool>? canContact = null,
        int sourceWeaponId = 0)
    {
        ArgumentNullException.ThrowIfNull(item);
        float contactRadius = Math.Max(0f, radiusUnits) + Math.Max(0f, falloffUnits);

        // Explosion0 messages every listener, near or far (0x480003). An ExplodeObject weighs it by
        // distance and arms its own countdown; nothing happens here until that countdown runs out.
        if (item.ExplodesOnCountdown)
        {
            if (!ReferenceEquals(item, source) && (canContact is null || canContact(item)))
            {
                float reach = (float)(item.PositionUnits - originUnits).Length;
                ArmCountdown(
                    item,
                    ContactDose(reach, item.ExplosionContactRadiusUnits, radiusUnits, falloffUnits, damageFactor),
                    explosion: true);
            }

            return null;
        }

        if (item.IsDestroyed
            || ReferenceEquals(item, source)
            || (item.PositionUnits - originUnits).Length
                > contactRadius + Math.Max(0f, item.ExplosionContactRadiusUnits))
        {
            return null;
        }

        if (item.Kind == DestructibleObjectKind.Vehicle
            && (item.IsDying || sourceWeaponId != DestructibleObjectRegistry.CarDestroyingWeaponId))
        {
            return null;
        }

        if (canContact is not null && !canContact(item))
        {
            return null;
        }

        float distance = (float)(item.PositionUnits - originUnits).Length;
        float dose = ContactDose(
            distance,
            item.ExplosionContactRadiusUnits,
            radiusUnits,
            falloffUnits,
            damageFactor);
        return ApplyDamage(item, dose, explosion: true);
    }

    private static float ContactDose(
        float distance,
        float targetRadiusUnits,
        float radiusUnits,
        float falloffUnits,
        float damageFactor)
    {
        // This is the same order as FUN_00416c90: d = distance - targetBound - blastRadius;
        // d < 0 returns full damage, d >= falloff returns zero, and the remaining interval is linear.
        float d = distance
            - Math.Max(0f, targetRadiusUnits)
            - Math.Max(0f, radiusUnits);
        if (d < 0f)
        {
            return damageFactor > 0f ? damageFactor : 0f;
        }

        if (!(falloffUnits > 0f) || d >= falloffUnits || !(damageFactor > 0f))
        {
            return 0f;
        }

        return (falloffUnits - d) * damageFactor / falloffUnits;
    }

    internal static DestructibleObject? ApplyDamage(DestructibleObject item, float damage, bool explosion)
    {
        if (item.ExplodesOnCountdown)
        {
            ArmCountdown(item, damage, explosion);
            return null;
        }

        if (item.IsDestroyed || !(damage > 0f))
        {
            return null;
        }

        // A car has no round handler: neither TASKTYPE_CAR (0x42DCF0) nor the Vehicle type under it
        // (0x4F4190) registers one for the bullet message (0x416840), so a round never harms it. Its
        // explosion handler (0x42FB40) has no hit points either -- any contact at all sets Car+2332.
        if (item.Kind == DestructibleObjectKind.Vehicle)
        {
            if (explosion)
            {
                item.IsDying = true;
            }

            return null;
        }

        // ExplodeObject, the attached ExplodeMagicObj family and Glass all accumulate the incoming hit
        // value into the same pair (IGI1 0x481e60: +0x1e8 against +0x1ec; magic objects +0x13c/+0x140).
        item.ExplosionDamageAccumulated += damage;
        float threshold = item.ExplosionDamageThreshold > 0f
            ? item.ExplosionDamageThreshold
            : ExplosionDamageDefaults.DamageThreshold;

        // Glass_ExplosionHitHandler (0x449BA0) adds the dose too, but shatters on ANY non-zero contact
        // (0x449C63 compares the dose with zero, not the sum with the threshold): a blast always breaks
        // the panes it reaches. Only a round or a blade (0x449B00) has to wear the pane down.
        bool breaks = (item.Kind == DestructibleObjectKind.Glass && explosion)
            || item.ExplosionDamageAccumulated >= threshold;
        if (!breaks)
        {
            return null;
        }

        item.IsDestroyed = true;
        return item;
    }

    // 0x481E60 (a round) and 0x481EA0 (a blast): the damage adds up, and once it has reached the
    // threshold every further hit arms the countdown again -- to 0 for a round, to the Explosion delay
    // for a blast. A blast re-arms even when it adds nothing.
    private static void ArmCountdown(DestructibleObject item, float damage, bool explosion)
    {
        if (item.IsDestroyed)
        {
            return;
        }

        // 0x481E60 / 0x481EA0: fld the dose, fadd +488, fst +488, fcomp +492 -- under the original's chop
        // rounding (sub_498460 ORs 0x0C00 into the control word). The stored sum truncates, and the test
        // reads the register: five Glock rounds of 0.2 leave 0.99999994, so the radio takes a sixth.
        X87 sum = (X87)Math.Max(0f, damage) + item.ExplosionDamageAccumulated;
        item.ExplosionDamageAccumulated = sum.F;
        float threshold = item.ExplosionDamageThreshold > 0f
            ? item.ExplosionDamageThreshold
            : ExplosionDamageDefaults.DamageThreshold;
        if (sum.Value >= threshold)
        {
            item.Countdown = explosion ? item.ExplosionDelayTicks : 0;
        }
    }
}

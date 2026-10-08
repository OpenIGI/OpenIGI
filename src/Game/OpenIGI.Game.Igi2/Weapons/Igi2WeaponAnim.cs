using OpenIGI.Formats.Animation;
using OpenIGI.Game.Igi2.Animation;

namespace OpenIGI.Game.Igi2.Weapons;

/// <summary>Which of a weapon's clips is asked for, <c>WeaponTypeGetAnimType_e</c>.</summary>
public enum Igi2WeaponAnimRequest
{
    /// <summary>The first-person clip held still, <c>WEAPONTYPE_GETANIMTYPE_1STSTAND</c>.</summary>
    FirstStand = 0,

    /// <summary>The first-person clip on the move.</summary>
    FirstMove = 1,

    /// <summary>The first-person draw, scaled to the weapon's <c>(De)Activate Time</c>.</summary>
    FirstActivate = 2,

    /// <summary>The first-person put-away, scaled the same way.</summary>
    FirstDeactivate = 3,

    /// <summary>The first-person shot (the fire type's).</summary>
    FirstFire = 4,

    /// <summary>The first-person held shot (the fire type's).</summary>
    FirstFireHold = 5,

    /// <summary>The first-person release (the fire type's).</summary>
    FirstFireRelease = 6,

    /// <summary>The first-person reload's start (the fire type's).</summary>
    FirstReloadStart = 7,

    /// <summary>The first-person reload's loop (the fire type's).</summary>
    FirstReloadLoop = 8,

    /// <summary>The first-person reload's end (the fire type's).</summary>
    FirstReloadEnd = 9,

    /// <summary>The third-person stand.</summary>
    ThirdStand = 10,

    /// <summary>The third-person walk.</summary>
    ThirdWalk = 11,

    /// <summary>The third-person run.</summary>
    ThirdRun = 12,

    /// <summary>The third-person crouched stand.</summary>
    ThirdCrouchStand = 13,

    /// <summary>The third-person crouched walk.</summary>
    ThirdCrouchWalk = 14,

    /// <summary>The third-person draw, from the body's weapon table.</summary>
    ThirdActivate = 15,

    /// <summary>The third-person put-away, from the body's weapon table.</summary>
    ThirdDeactivate = 16,

    /// <summary>The third-person shot (the fire type's).</summary>
    ThirdFire = 17,

    /// <summary>The third-person held shot (the fire type's).</summary>
    ThirdFireHold = 18,

    /// <summary>The third-person release (the fire type's).</summary>
    ThirdFireRelease = 19,

    /// <summary>The third-person reload's start (the fire type's).</summary>
    ThirdReloadStart = 20,

    /// <summary>The third-person reload's loop (the fire type's).</summary>
    ThirdReloadLoop = 21,

    /// <summary>The third-person reload's end (the fire type's).</summary>
    ThirdReloadEnd = 22,

    /// <summary>The third-person reload's start lying flat (the fire type's).</summary>
    ThirdReloadStartFlat = 23,

    /// <summary>The third-person reload's loop lying flat (the fire type's).</summary>
    ThirdReloadLoopFlat = 24,

    /// <summary>The third-person reload's end lying flat (the fire type's).</summary>
    ThirdReloadEndFlat = 25,

    /// <summary>The third-person draw lying flat, from the body's weapon table.</summary>
    ThirdActivateFlat = 26,

    /// <summary>The third-person put-away lying flat, from the body's weapon table.</summary>
    ThirdDeactivateFlat = 27,
}

/// <summary>A clip a weapon gives for a request, and how fast to play it.</summary>
/// <param name="Clip">The clip.</param>
/// <param name="TimeScale">The rate, <c>tOut.vTimeScale</c>: one for its own speed.</param>
public readonly record struct Igi2WeaponAnim(Igi2AnimationClip Clip, float TimeScale)
{
    /// <summary>Gets the ticks a frame advances the clip at this rate (<c>(__int64)(AnimController_nTicksPerFrame × vTimeScale)</c>).</summary>
    public int TimeStep => Fpu.ToInt32(new Fpu(Igi2AnimController.TicksPerFrame) * TimeScale);
}

/// <summary>A weapon type's own answer to a clip request, <c>WeaponType_GetAnimHandler</c> (<c>igi2 0x4360F0</c>).</summary>
/// <remarks>
/// <para>
/// The first-person stand and move are its named clips on the asking rig, at their own speed
/// (<c>Hierarchy_GetAnim</c>, <c>0x57F360</c>, looks a name up in one rig's clips). The first-person draw and put-away
/// are its named clips too, played so that the whole clip lasts the weapon's <c>(De)Activate Time</c>: the clip's
/// seconds (its ticks times the float <c>1/4800</c>) over that time. The third-person stand, walk, run and crouched
/// clips are named the same way.
/// </para>
/// <para>
/// The third-person draw and put-away come from the body's weapon table instead
/// (<c>WeaponType_GetAnimHeaderFromWeaponTypeAnim</c>, <c>0x4383B0</c>): rows <c>0xE9</c>–<c>0xEC</c>, the standing and
/// lying <c>weaponraise</c>, scaled the same way unless the time is zero.
/// </para>
/// <para>
/// Everything else is the fire type's to answer, as is any request this finds no clip for: the request goes on to the
/// handler of the fire type in the asker's fire-type slot, when there is one. The gun's is <see cref="GunAnim"/>; the
/// other fire types' handlers are not yet ported and find none.
/// </para>
/// </remarks>
public static class Igi2WeaponTypeAnim
{
    /// <summary>The float a clip's ticks are multiplied by to make seconds, <c>0x395A740E</c>.</summary>
    private const float SecondsPerTick = 0.00020833334f;

    /// <summary>Answers a request.</summary>
    /// <param name="weaponType">The weapon.</param>
    /// <param name="request">What is asked for.</param>
    /// <param name="rig">The asking rig: the first-person model's for a first-person request, the body's otherwise.</param>
    /// <param name="library">The level's clips.</param>
    /// <param name="bodyTable">The weapon's table for <paramref name="rig"/>, or <see langword="null"/> for none.</param>
    /// <param name="fireType">The asker's fire type, <c>nFireType</c>, whose handler answers what this does not; -1 for none.</param>
    /// <returns>The clip and its rate, or <see langword="null"/> when there is none.</returns>
    public static Igi2WeaponAnim? Get(
        Igi2WeaponTypeTask weaponType,
        Igi2WeaponAnimRequest request,
        Igi2BoneHierarchy? rig,
        Igi2AnimationLibrary? library,
        IReadOnlyList<Igi2AnimationClip?>? bodyTable,
        int fireType)
    {
        ArgumentNullException.ThrowIfNull(weaponType);
        Igi2WeaponAnim? own = request switch
        {
            Igi2WeaponAnimRequest.FirstStand => Named(weaponType.FirstPersonStandAnim),
            Igi2WeaponAnimRequest.FirstMove => Named(weaponType.FirstPersonMoveAnim),
            Igi2WeaponAnimRequest.FirstActivate => Scaled(Find(weaponType.FirstPersonActivateAnim), weaponType.DeActivateTime, alwaysScale: true),
            Igi2WeaponAnimRequest.FirstDeactivate => Scaled(Find(weaponType.FirstPersonDeActivateAnim), weaponType.DeActivateTime, alwaysScale: true),
            Igi2WeaponAnimRequest.ThirdStand => Named(weaponType.ThirdPersonStandAnim),
            Igi2WeaponAnimRequest.ThirdWalk => Named(weaponType.ThirdPersonWalkAnim),
            Igi2WeaponAnimRequest.ThirdRun => Named(weaponType.ThirdPersonRunAnim),
            Igi2WeaponAnimRequest.ThirdCrouchStand => Named(weaponType.ThirdPersonCrouchStandAnim),
            Igi2WeaponAnimRequest.ThirdCrouchWalk => Named(weaponType.ThirdPersonCrouchWalkAnim),
            Igi2WeaponAnimRequest.ThirdActivate => Scaled(Row(TableRowActivate), weaponType.DeActivateTime, alwaysScale: false),
            Igi2WeaponAnimRequest.ThirdDeactivate => Scaled(Row(TableRowActivate + 1), weaponType.DeActivateTime, alwaysScale: false),
            Igi2WeaponAnimRequest.ThirdActivateFlat => Scaled(Row(TableRowActivate + 2), weaponType.DeActivateTime, alwaysScale: false),
            Igi2WeaponAnimRequest.ThirdDeactivateFlat => Scaled(Row(TableRowActivate + 3), weaponType.DeActivateTime, alwaysScale: false),
            _ => null,
        };

        if (own is not null || fireType == -1 || weaponType.FireTaskType(fireType) == Igi2WeaponConstants.NoTaskType)
        {
            return own;
        }

        return weaponType.FireType(fireType) is Igi2GunTask gun ? GunAnim(gun, request, Find, Row) : null;

        Igi2AnimationClip? Find(string name) =>
            rig is null || library is null || name.Length == 0 ? null : library.Find(rig, name);

        Igi2WeaponAnim? Named(string name) => Find(name) is { } clip ? new Igi2WeaponAnim(clip, 1f) : null;

        Igi2AnimationClip? Row(int row) => bodyTable is not null && (uint)row < (uint)bodyTable.Count ? bodyTable[row] : null;
    }

    /// <summary>
    /// A gun's answer, <c>Gun_GetAnimHandler</c> (<c>igi2 0x4822E0</c>): its named shot, held shot and release at their
    /// own speed, and its reload's three steps all at one rate, the one at which the whole reload (the start, a loop for
    /// every round of the clip but one, and the end) lasts the gun's <c>Reload Time</c>.
    /// </summary>
    /// <remarks>
    /// The first-person steps are the gun's named clips; the start counts the loop and the end only when the gun names a
    /// first-person loop. The third-person steps are the body table's reload rows (<c>0x4F</c> to <c>0x51</c>, lying
    /// <c>0x7A</c> to <c>0x7C</c>), the start again counting the rest only for a named first-person loop. A missing step
    /// adds nothing. Each clip's ticks become seconds by the float <c>1/4800</c>, the loop's times the rounds less one,
    /// summed in the handler's order and divided by the reload time at the FPU's precision.
    /// </remarks>
    private static Igi2WeaponAnim? GunAnim(
        Igi2GunTask gun,
        Igi2WeaponAnimRequest request,
        Func<string, Igi2AnimationClip?> find,
        Func<int, Igi2AnimationClip?> row)
    {
        bool namesLoop = gun.FirstPersonReloadHoldAnim.Length != 0;
        int flat = request is Igi2WeaponAnimRequest.ThirdReloadStartFlat or Igi2WeaponAnimRequest.ThirdReloadLoopFlat
            or Igi2WeaponAnimRequest.ThirdReloadEndFlat ? FlatReloadRows : 0;
        return request switch
        {
            Igi2WeaponAnimRequest.FirstFire => AtOwnSpeed(find(gun.FirstPersonFireAnim)),
            Igi2WeaponAnimRequest.FirstFireHold => AtOwnSpeed(find(gun.FirstPersonFireHoldAnim)),
            Igi2WeaponAnimRequest.FirstFireRelease => AtOwnSpeed(find(gun.FirstPersonFireReleaseAnim)),
            Igi2WeaponAnimRequest.FirstReloadStart or Igi2WeaponAnimRequest.FirstReloadLoop or Igi2WeaponAnimRequest.FirstReloadEnd =>
                Reload(gun, Step(request), namesLoop, find(gun.FirstPersonReloadStartAnim), find(gun.FirstPersonReloadHoldAnim), find(gun.FirstPersonReloadEndAnim)),
            Igi2WeaponAnimRequest.ThirdFire => AtOwnSpeed(find(gun.ThirdPersonFireAnim)),
            Igi2WeaponAnimRequest.ThirdFireHold => AtOwnSpeed(find(gun.ThirdPersonFireHoldAnim)),
            Igi2WeaponAnimRequest.ThirdFireRelease => AtOwnSpeed(find(gun.ThirdPersonFireReleaseAnim)),
            >= Igi2WeaponAnimRequest.ThirdReloadStart and <= Igi2WeaponAnimRequest.ThirdReloadEndFlat =>
                Reload(gun, Step(request), namesLoop, row(ReloadStartRow + flat), row(ReloadStartRow + 1 + flat), row(ReloadStartRow + 2 + flat)),
            _ => null,
        };

        static Igi2WeaponAnim? AtOwnSpeed(Igi2AnimationClip? clip) => clip is null ? null : new Igi2WeaponAnim(clip, 1f);

        static ReloadStep Step(Igi2WeaponAnimRequest request) => request switch
        {
            Igi2WeaponAnimRequest.FirstReloadStart or Igi2WeaponAnimRequest.ThirdReloadStart or Igi2WeaponAnimRequest.ThirdReloadStartFlat => ReloadStep.Start,
            Igi2WeaponAnimRequest.FirstReloadLoop or Igi2WeaponAnimRequest.ThirdReloadLoop or Igi2WeaponAnimRequest.ThirdReloadLoopFlat => ReloadStep.Loop,
            _ => ReloadStep.End,
        };
    }

    /// <summary>One reload step at the rate the whole reload takes, summed as the handler sums it for that step.</summary>
    private static Igi2WeaponAnim? Reload(
        Igi2GunTask gun,
        ReloadStep step,
        bool namesLoop,
        Igi2AnimationClip? start,
        Igi2AnimationClip? loop,
        Igi2AnimationClip? end)
    {
        int loops = gun.RoundsPerClip - 1;
        Fpu seconds = 0.0;
        Igi2AnimationClip? played;
        switch (step)
        {
            case ReloadStep.Start:
                if (namesLoop)
                {
                    seconds = Add(Add(seconds, loop, loops), end, 1);
                }

                seconds = Add(seconds, start, 1);
                played = start;
                break;
            case ReloadStep.Loop:
                seconds = Add(Add(Add(seconds, end, 1), start, 1), loop, loops);
                played = loop;
                break;
            default:
                seconds = Add(Add(Add(seconds, start, 1), loop, loops), end, 1);
                played = end;
                break;
        }

        return played is null ? null : new Igi2WeaponAnim(played, (seconds / gun.ReloadTimeSeconds).F);

        static Fpu Add(Fpu sum, Igi2AnimationClip? clip, int times) =>
            clip is null ? sum : sum + ((new Fpu(clip.TotalTime) * SecondsPerTick) * (double)times);
    }

    /// <summary>The body table's standing reload start, <c>0x4F</c>; the loop and the end follow it.</summary>
    private const int ReloadStartRow = 0x4F;

    /// <summary>How far past the standing reload rows the lying ones are, <c>0x2B</c>.</summary>
    private const int FlatReloadRows = 0x2B;

    private enum ReloadStep
    {
        Start,
        Loop,
        End,
    }

    /// <summary>The table row of the standing third-person draw, <c>0xE9</c>; the put-away and the lying pair follow it.</summary>
    private const int TableRowActivate = Igi2WeaponTypeAnims.LowerWeapon;

    /// <summary>A clip played so that it lasts the weapon's draw time; a third-person one keeps its own speed when that time is zero.</summary>
    private static Igi2WeaponAnim? Scaled(Igi2AnimationClip? clip, float seconds, bool alwaysScale)
    {
        if (clip is null)
        {
            return null;
        }

        if (!alwaysScale && seconds == 0f)
        {
            return new Igi2WeaponAnim(clip, 1f);
        }

        Fpu clipSeconds = new Fpu(clip.TotalTime) * SecondsPerTick;
        return new Igi2WeaponAnim(clip, (clipSeconds / seconds).F);
    }
}

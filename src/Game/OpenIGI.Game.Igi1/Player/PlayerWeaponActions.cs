using OpenIGI.Core.Numerics;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;

namespace OpenIGI.Game.Igi1.Player;

/// <summary>
/// The weapon actions the player's weapon machine asks for, played on the two skeletons they belong to:
/// the full-body clip on the first-person rig, and the torso clip recorded for the human's own body.
/// </summary>
internal sealed class PlayerWeaponActions
{
    private readonly IgiRandom _random;
    private int _upperBodyAnimation = -1;
    private bool _upperBodyLoops;
    private float _upperBodyBlendSeconds;
    private int _upperBodySequence;

    /// <summary>Initializes the actions over the player's random stream.</summary>
    /// <param name="random">The stream a timed fire's clip skip draws from.</param>
    internal PlayerWeaponActions(IgiRandom random)
    {
        _random = random;
    }

    /// <summary>Gets the weapon clip the player's TORSO is playing, or <c>-1</c> when it has none.</summary>
    /// <value>An animation id in the human's own skeleton-0 set, not the first-person rig's.</value>
    /// <remarks>
    /// <para>
    /// The other half of every weapon action. <c>0x4137E0</c> picks a pair — a full-body clip for the
    /// first-person rig and an <c>UpperBody*</c> clip for the human's own body — and plays them on two
    /// different skeletons. Only the first is visible through the player's own eyes, which is why the
    /// second went unused here until there was a camera that could see the body.
    /// </para>
    /// <para>
    /// Reported rather than played, because the body belongs to the renderer: this object owns the
    /// simulation's copy of what the torso was asked to do, and <see cref="UpperBodySequence"/> is how a
    /// consumer notices a new request without having to diff the id — a second shot plays the same clip
    /// again and must restart it.
    /// </para>
    /// </remarks>
    internal int UpperBodyAnimation => _upperBodyAnimation;

    /// <summary>Gets whether <see cref="UpperBodyAnimation"/> repeats.</summary>
    internal bool UpperBodyAnimationLoops => _upperBodyLoops;

    /// <summary>Gets how long the torso clip eases in — 0.4 s for stances, immediate for a shot.</summary>
    internal float UpperBodyBlendSeconds => _upperBodyBlendSeconds;

    /// <summary>Gets a counter bumped every time a new torso clip is requested.</summary>
    internal int UpperBodySequence => _upperBodySequence;

    /// <summary>Plays a weapon action on the first-person rig.</summary>
    /// <param name="view">The first-person rig, or <see langword="null"/> before one is given.</param>
    /// <param name="weapon">The weapon being held.</param>
    /// <param name="action">What the human is doing with it.</param>
    /// <param name="fireVariant">Which of the three firing clips, for <see cref="WeaponAction.Fire"/>.</param>
    /// <returns><see langword="false"/> when there is no rig, or the rig has no such animation.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="weapon"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// A weapon's <b>full-body</b> animation ids are ids in the view rig's own set, not the body's:
    /// <c>0x489F20</c> plays them on bone 0 of <c>human-&gt;view-&gt;boneObj</c>. The <c>UpperBody*</c>
    /// half goes to bone 1 of the body's own rig and is not played here — the third-person body is not
    /// drawn for the player.
    /// </remarks>
    internal bool Play(HumanView? view, WeaponType weapon, WeaponAction action, int fireVariant = 0)
    {
        ArgumentNullException.ThrowIfNull(weapon);
        if (view is null)
        {
            return false;
        }

        WeaponAnimations animations = WeaponAnimationTable.Select(weapon, action, fireVariant);
        int animationId = animations.FullBody;
        bool loops = animations.LoopFullBody;

        // The upper-body half, recorded for the third-person body to play on bone 1. 0x4137E0 tests the
        // two ids INDEPENDENTLY -- `if (v4 != -1) sub_489F20(...); if (v5 != -1) sub_489ED0(...)` -- so
        // this is settled before the full-body early-out below rather than after it. An action whose
        // full-body id is -1 still moves the torso in retail.
        //
        // Bumped on every request rather than only on a change: PlayerWeapons clears RequestedAction at
        // the top of each tick and raises it only when the machine actually dispatches, so this is
        // called at exactly the cadence 0x4137E0 runs at — and a second shot asks for the SAME clip and
        // has to restart it, which a change test would swallow.
        if (animations.UpperBody >= 0)
        {
            _upperBodyAnimation = animations.UpperBody;
            _upperBodyLoops = animations.LoopUpperBody;
            _upperBodyBlendSeconds = animations.UpperBodyBlendSeconds;
            _upperBodySequence++;
        }

        // Retail's 0x40FB90 forwards FireAnimation2 to the animation dispatcher verbatim, and the
        // shipped medipack leaves that slot at -1. 0x4D61D0 leaves the view untouched for a negative
        // id, so the one-shot entry clip ("insert syringe") simply holds its final frame for the whole
        // sustained injection. Substituting a *looping* FireAnimation1 here replayed that entry over
        // and over while Fire was held; the syringe must be inserted once, then held -- not on repeat.
        if (animationId < 0)
        {
            return false;
        }

        // 0x489F20 forwards the action's blend to BoneObj_Play. The standing/moving actions ease over
        // 0.4 s; firing and reloading snap. This is what eases the medipack (and every weapon) back to
        // its stand pose on release instead of teleporting from the injection frame.
        if (view.PlayWeaponAnimation(animationId, loops, animations.FullBodyBlendSeconds) is null)
        {
            return false;
        }

        // Action 6 alone skips the clip forward by a random fraction, which is why two shots do not
        // look identical. It sets the clip's CLOCK, not its rate.
        if (action == WeaponAction.FireTimed)
        {
            view.SkipTo(WeaponAnimationTable.TimedFireStart(_random.NextSingle()));
        }

        return true;
    }

    /// <summary>Ticks the weapon machine and plays the action it asks the rig for.</summary>
    /// <param name="weapons">The weapon machine.</param>
    /// <param name="input">The player's input this tick.</param>
    /// <param name="view">The first-person rig, or <see langword="null"/> before one is given.</param>
    /// <param name="body">The body whose ground contact and health the machine reads.</param>
    /// <param name="locomotionState">The locomotion state the player is in.</param>
    /// <returns><see langword="true"/> when an action was played and the rig's pose changed.</returns>
    internal bool Tick(
        PlayerWeapons weapons,
        PlayerInputState input,
        HumanView? view,
        PlayerBody body,
        int locomotionState)
    {
        bool posed = false;

        // What the locomotion state asks the torso to play. Standing is the fallback for the states
        // that declare nothing -- the jump, the falls and the climbs leave the request where it was.
        WeaponAction locomotion = HumanLocomotionWeaponActions.TryGet(locomotionState, out WeaponAction declared)
            ? declared
            : WeaponAction.Stand;

        weapons.Tick(input, view?.ClipEnded ?? false, body.IsGrounded, locomotion, view is null ? null : view.CrossedEvent);

        if (weapons.State == HumanWeaponState.Medipack)
        {
            float progress = view?.ClipProgress ?? 0f;
            // The medipack task owns both its entry/loop action and the terminal FireAnimation3
            // request. In particular, full health is a terminal condition for the task, not a
            // reason for the presentation layer to replace the newly requested clip with Stand.
            _ = weapons.TryUseMedipack(body.Health, progress);
        }

        if (weapons.InHand is { } held && weapons.RequestedAction is { } action)
        {
            posed = Play(view, held.Weapon, action, weapons.RequestedFireVariant);
        }
        else if (weapons.InHand is null && _upperBodyAnimation >= 0)
        {
            // 0x4137E0's action zero: it clears Human+3420 and calls 0x489F00, which stands the upper
            // body down. Empty hands are the state that reaches it — there is no weapon record to pick a
            // torso clip out of — and without this the body holds the last weapon's stance after the
            // player puts it away.
            _upperBodyAnimation = -1;
            _upperBodySequence++;
        }

        return posed;
    }
}

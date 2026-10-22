using OpenIGI.Formats.Animation;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.Player;

/// <summary>
/// The player's locomotion: the walk/run latch, the animator whose state picks the clip, and the
/// root-motion step that clip carries the body by.
/// </summary>
internal sealed class PlayerLocomotion
{
    private readonly PlayerBody _body;
    private HumanLocomotionAnimator? _animator;

    /// <summary>
    /// The walk/run latch. <b>True is the retail default</b>: the byte the toggle flips,
    /// <c>HumanPlayer+3397</c>, is zeroed by the constructor at <c>0x410370</c>, and the flag word is
    /// built with <c>if (byte) flags |= 0x40000; else flags &amp;= ~0x40000</c> at <c>0x4105EA</c> — so a
    /// zero byte means the <i>walking</i> bit is clear, which selects the running set, states 1–8.
    /// A player therefore spawns running and the first press of the toggle makes them walk.
    /// </summary>
    private bool _running = true;

    /// <summary>Initializes the locomotion for one body.</summary>
    /// <param name="body">The body the clips carry.</param>
    internal PlayerLocomotion(PlayerBody body)
    {
        _body = body;
    }

    /// <summary>Gets the animator, once the player has been given its animation set.</summary>
    internal HumanLocomotionAnimator? Animator => _animator;

    /// <summary>Gets whether the latch selects running.</summary>
    internal bool Running => _running;

    /// <summary>Gets the locomotion state the player is in, for diagnostics.</summary>
    internal int State => _animator?.State ?? 0;

    /// <summary>Gets the clip the player's legs are actually playing, for the third-person body to mirror.</summary>
    /// <value>
    /// The animation id the current locomotion state names, or
    /// <see cref="HumanLocomotionStates.NoAnimation"/> for the states that name none.
    /// </value>
    /// <remarks>
    /// Asked of the animator rather than looked up again from the state, so there is one answer to the
    /// question of what the player is doing rather than two that can disagree — the body drawn from
    /// outside is the same character the simulation is moving.
    /// </remarks>
    internal int Animation => _animator?.AnimationId ?? HumanLocomotionStates.NoAnimation;

    /// <summary>Gets whether the clip <see cref="Animation"/> names repeats.</summary>
    /// <remarks>The state table's mode of one; every movement state carries it, the one-shots do not.</remarks>
    internal bool AnimationLoops => _animator?.Loops ?? true;

    /// <summary>Gives the player the animation set its skeleton uses.</summary>
    /// <param name="set">The set, normally <c>000.iff</c> — the one the player's own skeleton indexes.</param>
    /// <exception cref="ArgumentNullException"><paramref name="set"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Without this the player still stands and falls but cannot walk, because nothing supplies a
    /// delta translation — which is the honest failure mode rather than a guessed speed.
    /// </remarks>
    internal void Use(BoneAnimationSet set)
    {
        ArgumentNullException.ThrowIfNull(set);
        _animator = new HumanLocomotionAnimator(set);
    }

    /// <summary>Flips the walk/run latch on the toggle's rising edge.</summary>
    /// <param name="input">The player's input this tick.</param>
    internal void TakeToggle(PlayerInputState input)
    {
        if (input.IsRisingEdge(PlayerInputSlot.ToggleWalkRun))
        {
            _running = !_running;
        }
    }

    /// <summary>Moves the body by this tick's command, the animator's clip saying how far.</summary>
    /// <param name="command">The tick's movement command.</param>
    /// <param name="terrain">The level's terrain.</param>
    internal void Move(PlayerCommand command, IGroundHeightField? terrain)
    {
        if (_animator is null)
        {
            // No animation set, so nothing can say how far a step covers. Standing and falling still
            // work; walking does not, which is better than inventing a speed.
            _body.Tick(command, terrain, default);
        }
        else
        {
            _body.TickAnimated(command, terrain, _animator);
        }
    }
}

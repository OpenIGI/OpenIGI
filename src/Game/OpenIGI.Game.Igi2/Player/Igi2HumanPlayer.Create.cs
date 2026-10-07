using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi2.Humans;

namespace OpenIGI.Game.Igi2.Player;

/// <content>Making the player.</content>
public sealed partial class Igi2HumanPlayer
{
    /// <summary>The share of its tuned health the player has on normal, <c>0x3F333333</c>.</summary>
    public const float NormalHealthShare = 0.7f;

    /// <summary>The share of its tuned health the player has on hard, <c>0x3ECCCCCD</c>.</summary>
    public const float HardHealthShare = 0.4f;

    /// <summary>
    /// Readies the player for a mission: the single-player path of <c>HumanPlayer_CreateHandler</c> (<c>igi2 0x4B8010</c>).
    /// </summary>
    /// <param name="difficulty">The active profile's difficulty.</param>
    /// <remarks>
    /// <para>
    /// Every collision is switched on. The locomotion starts in <see cref="Igi2PlayerState.Stand"/> with no signal; its
    /// entry runs on the first tick. The player's health is the tuning's <c>Max damage scale</c>, seven tenths of it on
    /// normal and four tenths on hard, and the damage table is cleared with the single-player multipliers.
    /// </para>
    /// <para>
    /// The rest starts empty: walking off, no jump wait, no activation lock, not swimming, no speed from the legs, and
    /// a movement scale of zero until the first delayed update sets it, so the first tick does not move the legs.
    /// </para>
    /// </remarks>
    public void Create(AiDifficulty difficulty)
    {
        IsGroundCollision = true;
        IsObjectCollision = true;
        IsHumanCollision = true;

        Locomotion.TransitionTo(Igi2PlayerState.Stand);
        Signal = Igi2PlayerSignal.None;

        float health = General.MaxDamageScale;
        Damage.MaxDamage = difficulty switch
        {
            AiDifficulty.Normal => ((Fpu)health * NormalHealthShare).F,
            AiDifficulty.Hard => ((Fpu)health * HardHealthShare).F,
            _ => health,
        };
        Damage.Clear(General.DamageMultipliers);
        Damage.LastPart = Igi2DamagePart.None;

        IsWalk = false;
        IsActivateLocked = false;
        IsSwimming = false;
        MovementSpeedScale = 0f;
        JumpWaitTimer = 0;
        PerfectShotTimer = 0;
        GroundMovementSpeed = default;
    }
}

using OpenIGI.Game.Igi1.Weapons;
using OpenIGI.Game.Player;

namespace OpenIGI.Game.Igi1.Player;

/// <summary>
/// Project I.G.I.'s player's life: its health and whether it has died, the red wash and the flashbang's white a hit leaves on the
/// screen, and the figures <c>humanplayer.qvm</c> tunes it by.
/// </summary>
/// <remarks>The player's own, wherever it stands: the game's mission keeps a reference, and so does whatever draws its HUD.</remarks>
public sealed class Igi1PlayerLife
{
    /// <summary>Gets or sets the player's full health: <c>humanplayer.qvm</c>'s argument 7, or the executable's base until it runs.</summary>
    public float MaximumHealth { get; set; } = HumanHitZoneTable.MaximumHealth;

    /// <summary>Gets or sets how far the player's body animations carry it, the human type's delta translation scale.</summary>
    public float DeltaTranslationScale { get; set; } = HumanMotion.DefaultDeltaTranslationScale;

    /// <summary>Gets or sets the damage factor an electric fence deals the player each tick it is touched.</summary>
    public float ElectricFenceDose { get; set; } = ElectricFenceContact.DefaultDamageFactor;

    /// <summary>Gets the player's health.</summary>
    public HumanHealth Health { get; private set; } = new(HumanHitZoneTable.Player);

    /// <summary>Gets or sets a value indicating whether the player has died.</summary>
    public bool IsDead { get; set; }

    /// <summary>Gets or sets the tick the player may grunt at being hit again — <c>0x4637C0</c>'s per-human gate.</summary>
    public int HitQuietUntil { get; set; }

    /// <summary>Gets the red wash a hit on the player raises, which the HUD draws.</summary>
    /// <remarks>
    /// Four corner alphas that decay a forty-fifth a tick (<c>sub_411300</c>) and paint a screen-filling red quad
    /// (<c>HumanPlayer_DrawDamageFlash</c>). It survives the player's death, so the corpse's-eye view stays red rather than
    /// snapping back to a clean picture.
    /// </remarks>
    public HumanDamageFlash DamageFlash { get; private set; } = new();

    /// <summary>Gets the white a flashbang leaves on the screen.</summary>
    public FlashbangScreenFlash FlashbangFlash { get; private set; } = new();

    /// <summary>Starts the player's life afresh at a difficulty: the executable's full health, alive, and nothing on the screen.</summary>
    /// <param name="difficulty">The profile difficulty the player's own hits are scaled by.</param>
    /// <remarks>
    /// The player's own damage. Its zone table is the player's, whose head is worth 1.0 rather than the soldiers' 100 --
    /// <c>humanplayer.qvm</c> overrides the executable's default. The player's own hit handler (<c>0x412440</c>) additionally
    /// scales every hit by the profile difficulty: x0.75 easy, x1.0 normal, x1.25 hard -- a scale that applies to the PLAYER
    /// only, never to a soldier. <c>0x489C30</c> supplies the universal base value first; mission loading replaces it with
    /// argument 7 (<see cref="Tune"/>).
    /// </remarks>
    public void Reset(int difficulty)
    {
        MaximumHealth = HumanHitZoneTable.MaximumHealth;
        ElectricFenceDose = ElectricFenceContact.DefaultDamageFactor;
        StartFresh(difficulty);
    }

    /// <summary>Starts a fresh life at the same full health: the last one's death and its washes do not carry over.</summary>
    /// <param name="difficulty">The profile difficulty the player's own hits are scaled by.</param>
    public void StartFresh(int difficulty)
    {
        Health = new(HumanHitZoneTable.Player, MaximumHealth) { DamageScale = HumanHealth.DifficultyDamageScale(difficulty) };
        IsDead = false;
        DamageFlash = new();
        FlashbangFlash = new();
    }

    /// <summary>Clears the red wash, as a player brought back to life starts without one.</summary>
    public void ClearDamageFlash() => DamageFlash = new();

    /// <summary>Takes <c>humanplayer.qvm</c>'s figures, and a full health of its maximum.</summary>
    /// <param name="tuning">The script's tuning.</param>
    /// <param name="difficulty">The profile difficulty the player's own hits are scaled by.</param>
    /// <remarks>
    /// Argument 0 scales the animation's delta translation -- the player's, and Ekk's. Argument 7 is the player's maximum
    /// health (<c>Human+600</c>, <c>0x410370</c>'s overwrite), 3.0 in retail and the single number that sets the player's
    /// time-to-kill. Argument 8 is the dose one powered fence contact adds (<c>0x412F80</c>'s fmul against the human's damage
    /// scale), 0.5 in retail: a retuned <c>humanplayer.qvm</c> owns its own shock, not the shipped number.
    /// </remarks>
    public void Tune(HumanPlayerParameters tuning, int difficulty)
    {
        ArgumentNullException.ThrowIfNull(tuning);
        DeltaTranslationScale = tuning.DeltaTranslationScale;
        MaximumHealth = tuning.MaximumHealth;
        ElectricFenceDose = tuning.ElectricFenceDose;
        Health = new(HumanHitZoneTable.Player, MaximumHealth) { DamageScale = HumanHealth.DifficultyDamageScale(difficulty) };
    }
}

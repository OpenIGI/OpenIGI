using OpenIGI.Core.Numerics;

namespace OpenIGI.Game.World;

/// <summary>
/// Picks one of a numbered set of sound definitions — the naming half of <c>0x4638A0</c>, the helper
/// every death state ends with.
/// </summary>
/// <remarks>
/// <para>
/// The original is four lines: stop whatever sound the human already owns, draw a variant, format
/// <c>"%s%d"</c>, and start the result on the human. Only the draw and the format are here; owning
/// the voice belongs to whoever holds the human.
/// </para>
/// <para>
/// The helper has exactly nine call sites, all death states — one <c>player_death_</c> with a count of
/// three, eight <c>ai_death_</c> with a count of four. No hit sound goes through it.
/// </para>
/// </remarks>
public static class HumanVariantSound
{
    /// <summary>The prefix the player's death states pass, and how many variants they draw between.</summary>
    public const string PlayerDeathPrefix = "player_death_";

    /// <summary>The prefix the soldier death states pass.</summary>
    public const string AiDeathPrefix = "ai_death_";

    /// <summary>How many variants all eight soldier death states draw between.</summary>
    /// <remarks>Literal at every one of the eight call sites, and the definitions run 1 to 4.</remarks>
    public const int AiDeathVariants = 4;

    /// <summary>The prefix a soldier's non-fatal hit passes, and how many variants it draws between.</summary>
    public const string AiHitPrefix = "ai_hit_";

    /// <summary>How many variants a soldier's hit draws between.</summary>
    public const int AiHitVariants = 3;

    /// <summary>The prefix the player's non-fatal hit passes.</summary>
    public const string PlayerHitPrefix = "player_hit_";

    /// <summary>How many variants the player's hit draws between.</summary>
    public const int PlayerHitVariants = 4;

    /// <summary>The shortest gap between two grunts from the same human, in ticks.</summary>
    public const int HitCooldownMinimumTicks = 30;

    /// <summary>How many further ticks the gap may randomly extend by.</summary>
    public const int HitCooldownSpanTicks = 30;

    /// <summary>Builds a hit variant's name — the naming half of <c>0x4637C0</c>.</summary>
    /// <param name="prefix">The prefix, such as <see cref="AiHitPrefix"/>.</param>
    /// <param name="count">How many variants to draw between.</param>
    /// <param name="random">The engine's generator.</param>
    /// <returns>The definition name to play.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="random"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is not positive.</exception>
    /// <remarks>
    /// <b>This helper adds the one and the death helper does not</b>, from the same draw — which is the
    /// clearest evidence that <see cref="Choose"/>'s zero-based name is a slip rather than a convention.
    /// So <c>ai_hit_</c> builds <c>ai_hit_1</c>…<c>ai_hit_3</c> and matches the definitions exactly, and
    /// nothing here needs gating.
    /// </remarks>
    public static string ChooseHit(string prefix, int count, IgiRandom random)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);

        return prefix + (random.NextBounded((uint)count) + 1);
    }

    /// <summary>How long to wait before the same human may grunt again, in ticks.</summary>
    /// <param name="random">The engine's generator.</param>
    /// <returns>Between 30 and 60 ticks — one to two seconds.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="random"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <c>0x4637C0</c> computes <c>30 - trunc(rand01() * -30.0)</c> and stores <c>now + that</c>. Without
    /// it a soldier caught in an automatic burst would grunt on every round that landed.
    /// </remarks>
    public static int HitCooldownTicks(IgiRandom random)
    {
        ArgumentNullException.ThrowIfNull(random);
        return HitCooldownMinimumTicks + (int)(random.NextDouble() * HitCooldownSpanTicks);
    }

    /// <summary>Builds the name one of the numbered variants is defined under.</summary>
    /// <param name="prefix">The prefix, such as <see cref="AiDeathPrefix"/>.</param>
    /// <param name="count">How many variants to draw between.</param>
    /// <param name="random">The engine's generator.</param>
    /// <param name="faithful">
    /// <see langword="true"/> to reproduce the original's off-by-one exactly, as <c>-vanilla</c>
    /// requires; <see langword="false"/> to draw the variants that actually exist.
    /// </param>
    /// <returns>The definition name to play.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="random"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is not positive.</exception>
    /// <remarks>
    /// <para>
    /// <b>The original draws one name that was never defined.</b> <c>0x4B47C0</c> is four instructions
    /// ending in an unsigned <c>div</c>, so the draw is <c>[0, count-1]</c> — while the definitions are
    /// numbered from one. So <c>ai_death_0</c> and <c>player_death_0</c> resolve to nothing and are
    /// silent, and <c>ai_death_4</c> can never be drawn at all.
    /// </para>
    /// <para>
    /// The near-identical sibling helper <c>0x4637C0</c> formats <c>n + 1</c> from the same draw, which
    /// is what the correct call looks like — the same programmer wrote the <c>+ 1</c> there and left it
    /// out here.
    /// </para>
    /// <para>
    /// <b>This is a deliberate divergence, gated.</b> Under the vanilla profile the port reproduces the
    /// silence, because <c>-vanilla</c> has to restore the original's mix. Otherwise it adds the one,
    /// which costs nothing and buys the fourth death sound the game shipped and never played.
    /// </para>
    /// </remarks>
    public static string Choose(string prefix, int count, IgiRandom random, bool faithful)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);

        uint draw = random.NextBounded((uint)count);
        return prefix + (faithful ? draw : draw + 1);
    }
}

namespace OpenIGI.Game.Ai;

/// <summary>
/// The three game difficulties, which together with the <see cref="AiType"/> key every row of
/// <c>common/ai/settings.qsc</c>.
/// </summary>
/// <remarks>
/// <para>
/// The keys come out of the binary rather than out of the script: <c>sub_406330(i)</c> is
/// <c>&amp;"GD_1"[16 * i]</c>, three names laid out sixteen bytes apart, and it is what the fallback
/// path passes when it builds an item the script did not declare. <c>GD_1</c>, <c>GD_2</c> and
/// <c>GD_3</c> are the game's <b>easy</b>, <b>normal</b> and <b>hard</b>, which the retail table agrees
/// with from its own side: a guard's maximum health climbs 0.3 → 0.5 → 0.8 across the three and its hit
/// probability at maximum range 0.1 → 0.3 → 0.5.
/// </para>
/// <para>
/// The difficulty in force is the <b>player profile's</b>, read once when a <c>HumanAI</c> is
/// configured (<c>sub_406F70</c> takes it from <c>sub_406220()</c>'s first field). There is no per-AI
/// difficulty.
/// </para>
/// </remarks>
public enum AiDifficulty
{
    /// <summary>Easy — the script's <c>GD_1</c>.</summary>
    Easy = 0,

    /// <summary>Normal — the script's <c>GD_2</c>.</summary>
    Normal = 1,

    /// <summary>Hard — the script's <c>GD_3</c>.</summary>
    Hard = 2,
}

/// <summary>The difficulty names shown to a player, as distinct from the keys the scripts use.</summary>
/// <remarks>
/// <c>HumanAiConfigRegistry.NameOf</c> answers a different question: it returns <c>GD_1</c>, <c>GD_2</c>
/// and <c>GD_3</c>, which are the row keys <c>common/ai/settings.qsc</c> is indexed by and are meaningless
/// outside it. Putting one on screen is how the mission-entry caption came to read <c>GD_2</c>. These are
/// the words for the same three settings, and both the campaign screen's dropdown and that caption take
/// them from here so a player is never shown two names for the difficulty they picked.
/// </remarks>
public static class AiDifficultyNames
{
    /// <summary>The three names, indexed by <see cref="AiDifficulty"/>.</summary>
    public static readonly string[] All = ["Easy", "Normal", "Hard"];

    /// <summary>Gets the name shown for a difficulty.</summary>
    /// <param name="difficulty">The difficulty.</param>
    /// <returns>Its display name, or the normal one for a value outside the three.</returns>
    public static string Of(AiDifficulty difficulty) =>
        (int)difficulty >= 0 && (int)difficulty < All.Length ? All[(int)difficulty] : All[1];

    /// <summary>Gets the name shown for a profile's stored difficulty index.</summary>
    /// <param name="index">The index a player profile carries.</param>
    /// <returns>Its display name, clamped into range.</returns>
    public static string Of(int index) => All[Math.Clamp(index, 0, All.Length - 1)];
}

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

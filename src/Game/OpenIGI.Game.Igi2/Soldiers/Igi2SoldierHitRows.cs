using OpenIGI.Game.Igi2.Humans;

namespace OpenIGI.Game.Igi2.Soldiers;

/// <summary>
/// The animation rows a soldier's flinch plays by the zone hit, <c>DamagePartData_t</c> past its multiplier: a long hit, three
/// short ones, three deaths, three tense short hits, and a kneeling long hit and three kneeling short ones; <c>-1</c> where a zone
/// has none.
/// </summary>
/// <param name="LongHit">The long hit, <c>eLongHitState</c> (<c>+0x4</c>).</param>
/// <param name="ShortHits">The three short hits, <c>aeShortHitState</c> (<c>+0x8</c>).</param>
/// <param name="Deaths">The three deaths (<c>+0x14</c>), which no ported state reads yet.</param>
/// <param name="TenseShortHits">The three tense short hits (<c>+0x20</c>).</param>
/// <param name="KneelLongHit">The kneeling long hit (<c>+0x2C</c>).</param>
/// <param name="KneelShortHits">The three kneeling short hits (<c>+0x30</c>).</param>
public sealed record Igi2SoldierHitRows(
    int LongHit,
    IReadOnlyList<int> ShortHits,
    IReadOnlyList<int> Deaths,
    IReadOnlyList<int> TenseShortHits,
    int KneelLongHit,
    IReadOnlyList<int> KneelShortHits)
{
    /// <summary>
    /// Every zone's rows, as <c>HumanSoldier_InitDamagePartTable</c> (<c>igi2 0x4E29E0</c>) writes them for each AI type: the same for
    /// every type, only the multipliers differing. The right hand's third death is never written and stays nought.
    /// </summary>
    public static IReadOnlyList<Igi2SoldierHitRows> Table { get; } = Build();

    /// <summary>Gets a zone's rows.</summary>
    /// <param name="part">The zone.</param>
    /// <returns>Its rows.</returns>
    public static Igi2SoldierHitRows Of(Igi2DamagePart part) => Table[(int)part];

    private static Igi2SoldierHitRows[] Build()
    {
        Igi2SoldierHitRows leg = new(87, [121, 122, 123], [71, 72, 73], [136, 137, 138], 149, [150, 151, 152]);
        Igi2SoldierHitRows rightArm = new(88, [127, 128, 129], [71, 72, 73], [142, 143, 144], -1, [-1, -1, -1]);
        Igi2SoldierHitRows leftLeg = new(89, [118, 119, 120], [74, 75, 76], [133, 134, 135], 145, [146, 147, 148]);
        Igi2SoldierHitRows leftArm = new(90, [124, 125, 126], [74, 75, 76], [139, 140, 141], -1, [-1, -1, -1]);
        int[] body = [115, 116, 117];
        int[] tenseBody = [130, 131, 132];
        int[] none = [-1, -1, -1];
        return
        [
            leg,
            leg with { Deaths = [71, 72, 74] },
            leg,
            rightArm with { Deaths = [71, 73, 0] },
            rightArm,
            rightArm,
            leftLeg,
            leftLeg,
            leftLeg,
            leftArm,
            leftArm,
            leftArm,
            new(91, body, [71, 72, 73], tenseBody, -1, none),
            new(91, body, [74, 75, 76], tenseBody, -1, none),
            new(91, body, [68, 69, 70], tenseBody, -1, none),
            new(91, body, [79, 79, 79], tenseBody, 91, body),
            new(91, body, [65, 66, 67], tenseBody, -1, none),
        ];
    }
}

namespace OpenIGI.Presentation.Rendering;

/// <summary>The part of one retail <c>Explosion0</c> call that decides what the blast looks like.</summary>
/// <param name="FullRangeUnits">
/// The full-damage range in world units. It is also the FireBall's size, and it sets how fast the
/// thrown fragments fall.
/// </param>
/// <param name="Fireballs">How many <c>1006_01_1</c> FireBalls the blast shows; smoke rises when any do.</param>
/// <param name="Fragments">How many <c>1009_01_1</c> pieces it throws.</param>
/// <remarks>
/// These are the <c>vFullRange</c>, <c>nFireball</c> and <c>nFragment</c> arguments of
/// <c>FUN_00480FE0</c> (<c>Explosion0_NewEx</c> in the sequel's symbols). The authored objects pass
/// their task fields; the two fixed callers pass constants, kept here.
/// </remarks>
public readonly record struct RetailExplosionShape(float FullRangeUnits, int Fireballs, int Fragments)
{
    /// <summary>A shot-out security camera lens (<c>0x441770</c>): <c>2048, 7, 3</c>.</summary>
    public static RetailExplosionShape CameraLens => new(2048f, Fireballs: 3, Fragments: 7);

    /// <summary>A shot-out alarm light (<c>AlarmLight_Explode</c>, <c>0x43A5F0</c>): <c>4096, 5, 2</c>.</summary>
    public static RetailExplosionShape AlarmLight => new(4096f, Fireballs: 2, Fragments: 5);
}

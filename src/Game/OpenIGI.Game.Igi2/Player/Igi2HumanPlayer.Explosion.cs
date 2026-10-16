using OpenIGI.Core.Numerics;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Weapons;

namespace OpenIGI.Game.Igi2.Player;

/// <content>An explosion reaching the player.</content>
public sealed partial class Igi2HumanPlayer : IIgi2ExplosionTarget
{
    /// <summary>The pains the player cries out when a blast hurts it.</summary>
    public static readonly IReadOnlyList<string> ExplosionHitSounds = ["gv_69_03", "gv_70_03", "gv_71_03"];

    private readonly float[] _damageFlash = new float[4];

    /// <summary>Gets the difficulty the player was created at.</summary>
    public AiDifficulty Difficulty { get; private set; }

    /// <summary>Gets the red flash a blast throws over the view, <c>avDamageFlash</c>: four shares, each 0.4 to 0.8.</summary>
    public IReadOnlyList<float> DamageFlash => _damageFlash;

    /// <summary>
    /// A blast reaches the player, <c>HumanPlayer_ExplosionHitHandler</c> (<c>igi2 0x4BA330</c>): it lands on the chest, as its
    /// chest's share, eased on the easiest difficulty and sharpened on the hardest, shakes the view with its nearness, and,
    /// if it did any damage, the player cries out and the view flashes red.
    /// </summary>
    /// <param name="hit">The blast.</param>
    /// <remarks>
    /// <para>
    /// The player's size for the blast is a third of its model's radius and its root bone's distance from its origin. The
    /// shake is full within three times the blast's full range of the eyes and gone at nine times; it runs out over the
    /// blast's ninety ticks. A dead player is hurt and shaken, but does not cry out.
    /// </para>
    /// <para>The original only answers while the player is active, which a player always is here.</para>
    /// </remarks>
    public void ExplosionHit(in Igi2ExplosionHit hit)
    {
        Damage.LastPart = Igi2DamagePart.Chest;
        Vector3f root = RootTranslation;
        float radius = ((Fpu.Sqrt((((Fpu)root.X * root.X) + ((Fpu)root.Y * root.Y)) + ((Fpu)root.Z * root.Z)) + ModelRadius) * 0.33333334f).F;
        Fpu damage = hit.Resolve(Position, radius, this);
        if (damage.Value != 0.0)
        {
            damage *= Damage.Multiplier(Igi2DamagePart.Chest);
            damage = Difficulty switch
            {
                AiDifficulty.Easy => damage * 0.75f,
                AiDifficulty.Hard => damage * 1.25f,
                _ => damage,
            };
            Damage.Add(Igi2DamagePart.Chest, damage.F);
        }

        ShakeView(hit, PlayerShakeAngle);
        if (damage.Value == 0.0)
        {
            return;
        }

        if (!IsDead)
        {
            PlayHitSound(ExplosionHitSounds);
        }

        for (int i = 0; i < _damageFlash.Length; i++)
        {
            _damageFlash[i] = (((Fpu)Random.NextDouble() + 1.0) * 0.4).F;
        }
    }

    /// <summary>How far a blast at full share tilts the player's view, <c>0.01308997</c> radians.</summary>
    private const float PlayerShakeAngle = 0.01308997f;
}

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

    /// <summary>
    /// Gets or sets the translation of the player model's root bone, <c>tRootTranslation</c> (<c>BoneDynCubeObj + 0x130</c>):
    /// its rig's first bind translation, which <c>BoneDynCubeObj_SetLODVirModel</c> (<c>igi2 0x567BE0</c>) copies.
    /// </summary>
    public Vector3f RootTranslation { get; set; }

    /// <summary>Gets or sets the player model's bounding radius, <c>vRadius</c> (<c>BoneDynCubeObj + 0x148</c>): its mesh's <c>+0x78</c>.</summary>
    public float ModelRadius { get; set; } = 1f;

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
        Fpu damage = hit.Resolve(Position, radius);
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

        Shake(hit);
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

    /// <summary>The blast's shake on the view, by the eyes' distance from it.</summary>
    private void Shake(in Igi2ExplosionHit hit)
    {
        Vector3d eyes = View.Position, at = hit.Position;
        Fpu dx = (Fpu)eyes.X - at.X, dy = (Fpu)eyes.Y - at.Y, dz = (Fpu)eyes.Z - at.Z;
        Fpu distance = Fpu.Sqrt(((dy * dy) + (dz * dz)) + (dx * dx));
        Fpu past = distance - ((Fpu)hit.FullRange * 3f);
        Fpu share;
        if (past.Value < 0.0)
        {
            share = 1f;
        }
        else
        {
            share = (Fpu)1f - (past / ((Fpu)hit.FullRange * 6f));
            if (share.Value < 0.0 || share.Value == 0.0)
            {
                return;
            }
        }

        View.ShakeFactor = 1f;
        View.ShakeRange = ((Fpu)204.8f * share).F;
        View.ShakeAngle = (share * 0.01308997f).F;
        View.ShakeTime = ((Fpu)1.0 / (double)hit.ExplosionTime).F;
    }
}

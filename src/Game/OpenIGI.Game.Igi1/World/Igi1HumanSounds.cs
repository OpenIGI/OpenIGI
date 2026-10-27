using OpenIGI.Game.Hosting;
using OpenIGI.Core.Diagnostics;
using OpenIGI.Audio;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Audio;
using OpenIGI.Engine.Flow;
using OpenIGI.Game.Igi1.Weapons;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.World;

/// <summary>Project I.G.I.'s humans' cries and the sounds of rounds landing: which sample, and when it is allowed to play.</summary>
/// <param name="stage">Where they play, whose sound stream picks the samples and whose material table names them.</param>
public sealed class Igi1HumanSounds(IIgi1SoundStage stage) : IMissionCries
{
    private int _impactQuietUntil;
    private int _ricochetQuietUntil;

    /// <summary>
    /// Plays one of a human's numbered death variants, the way <c>0x4638A0</c> names them.
    /// </summary>
    /// <param name="prefix">The prefix the death state passes.</param>
    /// <param name="variants">How many variants that state draws between.</param>
    /// <param name="positionInUnits">
    /// Where the body is, in engine units, or <see langword="null"/> for the player's own death.
    /// </param>
    /// <remarks>
    /// The player's own death is deliberately <b>not</b> positional. The original attaches the sound to
    /// the human it belongs to, and for the player that is the listener itself — distance zero, so full
    /// gain and no pan. Placing it in the world instead would swing the player's own dying breath
    /// around the stereo image as the death camera turned. Same reasoning as the player's weapon.
    /// </remarks>
    public void PlayDeath(string prefix, int variants, Vector3d? positionInUnits)
    {
        if (!stage.IsHeard)
        {
            return;
        }

        bool faithful = AudioCVars.Preset.Value == AudioProfile.Vanilla;
        string name = HumanVariantSound.Choose(prefix, variants, stage.SoundRandom, faithful);

        // Above an ordinary effect but below the score: a firefight must not drop the death that ends it.
        stage.Play(name, positionInUnits, 1f, priority: 300);
    }

    /// <summary>Plays a human's grunt at being hit — the naming half of <c>0x4637C0</c>.</summary>
    /// <param name="prefix">The prefix the damage handler passes.</param>
    /// <param name="variants">How many variants it draws between.</param>
    /// <param name="positionInUnits">Where the body is, or <see langword="null"/> for the player's own.</param>
    /// <remarks>
    /// Unlike the death cry this needs no vanilla gate: <c>0x4637C0</c> formats <c>n + 1</c>, so
    /// <c>ai_hit_</c> builds <c>ai_hit_1</c>…<c>ai_hit_3</c> and matches its definitions exactly.
    /// </remarks>
    public void PlayHit(string prefix, int variants, Vector3d? positionInUnits)
    {
        if (!stage.IsHeard)
        {
            return;
        }

        string name = HumanVariantSound.ChooseHit(prefix, variants, stage.SoundRandom);
        stage.Play(name, positionInUnits, 1f, priority: 250);
    }

    /// <summary>Plays what this tick's bullet impacts struck.</summary>
    /// <remarks>
    /// Hit-scan and ballistic contacts share the retail material table, exactly as the original's shot
    /// resolution does at <c>0x47BA95</c>: the struck material's own six sounds at row <c>+340</c>, one
    /// picked by <c>rand() % 6</c>, and the whole set rate-limited by ONE global timestamp — see
    /// <see cref="BulletImpactSounds"/>. A human resolves to the <c>Flesh</c> row, which is the only
    /// row naming the <c>bul_flesh_*</c> samples at all.
    /// </remarks>
    public void PlayPlayerImpacts()
    {
        if (stage.Player is not { } player)
        {
            return;
        }

        foreach (GunImpact impact in player.Impacts)
        {
            // A knife records no impact (0x46DFB0), so it never reaches the material's sounds.
            if (!impact.HitSomething || impact.Melee)
            {
                continue;
            }

            PlayBulletImpact(impact.Position, impact.StruckHuman, impact.Wall.Material);
        }
    }

    /// <summary>Plays the clatter of every dropped gun that landed this tick.</summary>
    /// <param name="impact">The landing.</param>
    /// <remarks>
    /// <c>GunPickup</c>'s tick (<c>0x46CEA0</c>) plays <c>weapondrop_01</c> or <c>weapondrop_02</c>, a coin
    /// toss between them, at every bounce -- drawn there, in the pickup's tick, whether or not anything can be
    /// heard. The realistic drop reports a landing once per contact, so a gun that tumbles and bounces twice
    /// clatters twice.
    /// </remarks>
    public void PlayWeaponDrop(GunPickupImpact impact)
    {
        string sound = stage.SoundRandom.NextSingle() > 0.5f ? "weapondrop_01" : "weapondrop_02";
        stage.Play(sound, impact.Position, 1f, priority: 50);
    }

    /// <summary>Plays a human's step on the ground it took it on, and in the rain the wet layer as well.</summary>
    /// <param name="stage">Where it plays.</param>
    /// <param name="step">The step.</param>
    /// <param name="raining">Whether the level's weather is rain.</param>
    /// <remarks>
    /// One pitch for the whole step: <c>0x463920</c> computes it once and hands the same value to the material voice and to the rain
    /// voice, so the two halves of a wet step stay together. The wet layer is played as well as the material's own sound, never
    /// instead of it; its gate is <c>0x416FB0</c>, a single global byte -- the level's weather flag rather than a shelter test. Only
    /// the material's voice is quietened for a crouch (<c>0x463920</c>, <c>0x45A7F0</c>): the wet one keeps its full volume.
    /// </remarks>
    public static void PlayFootstep(IIgi1SoundStage stage, in Footstep step, bool raining)
    {
        ArgumentNullException.ThrowIfNull(stage);
        if (!stage.IsHeard || step.Sound.Length == 0)
        {
            return;
        }

        float pitch = FootstepSounds.Pitch(stage.SoundRandom);
        stage.Play(step.Sound, step.PositionInUnits, pitch, priority: 40, gain: step.VolumeScale);
        if (raining)
        {
            stage.Play(FootstepSounds.RainSoundName(step.CycleSlot), step.PositionInUnits, pitch, priority: 40);
        }
    }

    /// <summary>Plays a sound a marched round's contact asks for: its impact, its ricochet.</summary>
    /// <param name="definition">The sound definition.</param>
    /// <param name="position">Where, in world units.</param>
    /// <remarks>The pitch a sound definition plays at is the sound layer's own; the original draws nothing for it from the level stream.</remarks>
    public void PlayMarchedRoundSound(string definition, Vector3d position)
    {
        if (definition.Length == 0 || !GameplayCVars.BallisticsImpactSounds.Value || !stage.IsHeard)
        {
            return;
        }

        stage.Play(definition, position, FootstepSounds.Pitch(_impactPitchRandom), priority: 45);
    }

    // The pitch a marched round's sound plays at; the original's sound layer draws nothing from the level stream.
    private readonly IgiRandom _impactPitchRandom = new(0x2F1Du);

    /// <summary>Plays one player or AI bullet contact through the retail sound definitions.</summary>
    /// <param name="position">Where it struck, in world units.</param>
    /// <param name="struckHuman">Whether it struck a body.</param>
    /// <param name="surfaceMaterial">The surface's material, when it struck the world.</param>
    public void PlayBulletImpact(Vector3d position, bool struckHuman, int surfaceMaterial)
    {
        if (!stage.IsHeard
            || stage.Materials is not { } materials
            || !GameplayCVars.BallisticsImpactSounds.Value
            || stage.Tick < _impactQuietUntil)
        {
            return;
        }

        string sound = BulletImpactSounds.PickForImpact(
            materials,
            struckHuman,
            surfaceMaterial,
            stage.SoundRandom);

        // The quiet window opens on the CONTACT, not on the sample. 0x47BABE's jump skips only the
        // playback call: 0x47BAE1 timestamps and 0x47BAFB re-rolls the interval on the merge path, so a
        // slot that names nothing still spends the window. Half the authored rows are silent for at
        // least half their slots -- Air, Water and Snow name a sound in one slot of six, and
        // HumanCollision, MetalLadder and MetalFence in none -- so returning early here would let a
        // burst into a ladder re-arm on every round and machine-gun the next surface it caught.
        _impactQuietUntil = stage.Tick + BulletImpactSounds.QuietTicks(stage.SoundRandom);

        if (sound.Length > 0)
        {
            stage.Play(sound, position, FootstepSounds.Pitch(stage.SoundRandom), priority: 45);
        }

        // The second half of a retail impact. 0x47C330 runs on the merge path inside the material
        // window, keeps a SEPARATE timestamp and rand(6)+6 interval of its own, and picks one of four
        // riccochet_* samples without reading the material at all -- which is why a burst into a fence
        // is the fence's own ping AND a whizz, arriving on their own two rhythms rather than together.
        PlayRicochet(position);
    }

    /// <summary>Plays the whizz a round makes coming off a surface, on its own retail limit.</summary>
    /// <param name="position">Where it glanced off, in world units.</param>
    /// <remarks>
    /// Separate from the material's sample in every way that matters: its own global timestamp, its own
    /// <c>rand(6)+6</c> interval, and a fixed table of four it reads without consulting the surface.
    /// A round that glances off calls it directly (<c>0x47BD7B</c>) rather than through the material
    /// window, so a ricochet always gets its turn.
    /// </remarks>
    public void PlayRicochet(Vector3d position)
    {
        if (!stage.IsHeard || stage.Tick < _ricochetQuietUntil)
        {
            return;
        }

        _ricochetQuietUntil = stage.Tick + BulletImpactSounds.QuietTicks(stage.SoundRandom);
        stage.Play(BulletImpactSounds.PickRicochet(stage.SoundRandom), position, FootstepSounds.Pitch(stage.SoundRandom), priority: 45);
    }
}

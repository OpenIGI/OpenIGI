using OpenIGI.Audio;
using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Audio;
using OpenIGI.Engine.Flow;
using OpenIGI.Game.Igi1.Weapons;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.World;

/// <summary>What Project I.G.I.'s cries and impact sounds are played through.</summary>
public interface IIgi1SoundHost
{
    /// <summary>Gets the running game, whose clock the impact sounds' quiet windows are timed on.</summary>
    GameLoop Game { get; }

    /// <summary>Gets where sounds play, or <see langword="null"/> for silence.</summary>
    /// <remarks>Silence draws nothing from the sound stream: every cry and impact returns before it picks.</remarks>
    SoundSystem? SoundOutput { get; }
}

/// <summary>Project I.G.I.'s humans' cries and the sounds of rounds landing: which sample, and when it is allowed to play.</summary>
/// <param name="mission">The mission, whose sound stream picks the samples and whose material table names them.</param>
/// <param name="host">Where they play.</param>
public sealed class Igi1HumanSounds(MissionWorld mission, IIgi1SoundHost host)
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
        if (host.SoundOutput is not { } sounds)
        {
            return;
        }

        bool faithful = AudioCVars.Preset.Value == AudioProfile.Vanilla;
        string name = HumanVariantSound.Choose(prefix, variants, mission.SoundRandom, faithful);

        // Above an ordinary effect but below the score: a firefight must not drop the death that ends it.
        sounds.PlayDefinition(name, positionInUnits, priority: 300);
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
        if (host.SoundOutput is not { } sounds)
        {
            return;
        }

        string name = HumanVariantSound.ChooseHit(prefix, variants, mission.SoundRandom);
        sounds.PlayDefinition(name, positionInUnits, priority: 250);
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
        if (mission.Player is not { } player)
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
    /// <remarks>
    /// <c>GunPickup</c>'s tick (<c>0x46CEA0</c>) plays <c>weapondrop_01</c> or <c>weapondrop_02</c>, a coin
    /// toss between them, at every bounce. The realistic drop reports a landing once per contact, so a gun
    /// that tumbles and bounces twice clatters twice.
    /// </remarks>
    public void PlayWeaponDrops()
    {
        if (host.SoundOutput is not { } sounds || mission.GunPickups is not { } pickups)
        {
            return;
        }

        foreach (GunPickupImpact impact in pickups.Impacts)
        {
            string sound = mission.SoundRandom.NextSingle() > 0.5f ? "weapondrop_01" : "weapondrop_02";
            sounds.PlayDefinition(sound, impact.Position, priority: 50);
        }
    }

    /// <summary>Plays one player or AI bullet contact through the retail sound definitions.</summary>
    /// <param name="position">Where it struck, in world units.</param>
    /// <param name="struckHuman">Whether it struck a body.</param>
    /// <param name="surfaceMaterial">The surface's material, when it struck the world.</param>
    public void PlayBulletImpact(Vector3d position, bool struckHuman, int surfaceMaterial)
    {
        if (host.SoundOutput is not { } sounds
            || mission.Materials is null
            || !GameplayCVars.BallisticsImpactSounds.Value
            || host.Game.Clock.TickCount < _impactQuietUntil)
        {
            return;
        }

        string sound = BulletImpactSounds.PickForImpact(
            mission.Materials,
            struckHuman,
            surfaceMaterial,
            mission.SoundRandom);

        // The quiet window opens on the CONTACT, not on the sample. 0x47BABE's jump skips only the
        // playback call: 0x47BAE1 timestamps and 0x47BAFB re-rolls the interval on the merge path, so a
        // slot that names nothing still spends the window. Half the authored rows are silent for at
        // least half their slots -- Air, Water and Snow name a sound in one slot of six, and
        // HumanCollision, MetalLadder and MetalFence in none -- so returning early here would let a
        // burst into a ladder re-arm on every round and machine-gun the next surface it caught.
        _impactQuietUntil = host.Game.Clock.TickCount + BulletImpactSounds.QuietTicks(mission.SoundRandom);

        if (sound.Length > 0)
        {
            sounds.PlayDefinition(
                sound,
                position,
                pitch: FootstepSounds.Pitch(mission.SoundRandom),
                priority: 45);
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
        if (host.SoundOutput is not { } sounds || host.Game.Clock.TickCount < _ricochetQuietUntil)
        {
            return;
        }

        _ricochetQuietUntil = host.Game.Clock.TickCount + BulletImpactSounds.QuietTicks(mission.SoundRandom);
        sounds.PlayDefinition(
            BulletImpactSounds.PickRicochet(mission.SoundRandom),
            position,
            pitch: FootstepSounds.Pitch(mission.SoundRandom),
            priority: 45);
    }
}

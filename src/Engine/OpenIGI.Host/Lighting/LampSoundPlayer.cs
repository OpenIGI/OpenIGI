using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Audio;
using OpenIGI.Game.World;
using OpenIGI.Host.Audio;
using OpenIGI.Rendering;

namespace OpenIGI.Host.Lighting;

/// <summary>What the level's lamps sound like as they switch, strike, hum and fail.</summary>
/// <remarks>
/// <b>Not retail.</b> The original's lamps are silent. The wall switch clicks under the player's hand whichever lamp it
/// works; the rest are heard where the lamp is, a burst at most at once and a steady few a second after it.
/// </remarks>
internal sealed class LampSoundPlayer
{
    /// <summary>How many lamp sounds may start at once, after a quiet spell.</summary>
    private const float LampSoundBurst = 8f;
    /// <summary>How many lamp sounds may start a second, held over time.</summary>
    /// <remarks>
    /// A scrub through dusk strikes a whole level's lamps in a second or two. Every one of them sounding is
    /// a wall of crackle that buries the gunfire; the budget lets the first few of a run through, which is
    /// what the ear follows anyway, and the voice pool's own priorities still put a shot above any of them.
    /// </remarks>
    private const float LampSoundsPerSecond = 14f;
    private readonly List<LampEvent> _lampEvents = [];
    private float _lampSoundBudget = LampSoundBurst;
    private int _lampStarterCount;
    /// <summary>Plays what the lamps did this pass, where they stand.</summary>
    /// <param name="board">The level's lamps.</param>
    /// <param name="elapsed">Real seconds since the last pass, which refills the sound budget.</param>
    /// <param name="audible">Whether the level is being played rather than shown behind a menu.</param>
    /// <param name="sounds">The sound system, when there is one.</param>
    /// <param name="fixtures">The level's fixtures.</param>
    /// <remarks>
    /// Drained every pass whether or not anything is played, so a lamp that struck behind the menu is not
    /// heard the moment the menu closes.
    /// </remarks>
    public void Play(LampSwitchboard board, float elapsed, bool audible, SoundSystem? sounds, IReadOnlyList<LightFixture> fixtures)
    {
        _lampEvents.Clear();
        board.DrainEvents(_lampEvents);
        _lampSoundBudget = Math.Min(LampSoundBurst, _lampSoundBudget + (elapsed * LampSoundsPerSecond));

        if (!audible || sounds is null || _lampEvents.Count == 0)
        {
            return;
        }

        Vector3d listener = sounds.Listener.Position;

        foreach (LampEvent lampEvent in _lampEvents)
        {
            if ((uint)lampEvent.Index >= (uint)fixtures.Count)
            {
                continue;
            }

            // The wall switch is under the player's own hand, whichever lamp it works.
            if (lampEvent.Sound == LampSound.Switch)
            {
                sounds.PlayExternal(LampSounds.SwitchName, gain: 0.55f, priority: 60, pitch: LampPitch(lampEvent.Index, 0.06f));
                continue;
            }

            (string name, float gain, float reach, float spread) = LampVoice(lampEvent, board.CharacterOf(lampEvent.Index));
            Vector3d at = fixtures[lampEvent.Index].Position;

            if ((at - listener).Length > reach || _lampSoundBudget < 1f)
            {
                continue;
            }

            _lampSoundBudget -= 1f;
            sounds.PlayExternal(
                name,
                at * SoundSystem.UnitsPerMetre,
                gain,
                priority: 30,
                pitch: LampPitch(lampEvent.Index, spread),
                minimumDistanceInMetres: 1.5f,
                maximumDistanceInMetres: reach);
        }
    }
    /// <summary>Which sound a lamp's event is, how loud, how far it carries and how much its pitch may vary.</summary>
    /// <remarks>
    /// <para>
    /// The hums vary least: a ballast hums at twice the mains frequency, and every one on a site shares the
    /// mains. The sparks and ticks vary most, being small parts that are never quite the same twice.
    /// </para>
    /// <para>
    /// Every sound is normalised to the same peak, so the gains here are the mix. The hums carry several
    /// times the energy of a tick for a second or more, and are turned well down for it: what the ear should
    /// pick out of a dusk is the starters, with the ballasts a texture under them.
    /// </para>
    /// </remarks>
    private (string Name, float Gain, float Reach, float Spread) LampVoice(LampEvent lampEvent, LampCharacter character) =>
        lampEvent.Sound switch
        {
            LampSound.Starter => ((_lampStarterCount++ & 1) == 0 ? LampSounds.StarterName : LampSounds.StarterAltName, 0.55f, 24f, 0.07f),
            LampSound.Ignitor => (LampSounds.IgnitorName, 0.5f, 30f, 0.05f),
            LampSound.Arc when character == LampCharacter.Discharge => (LampSounds.DischargeHumName, 0.26f, 34f, 0.012f),
            LampSound.Arc => (LampSounds.TubeHumName, 0.18f, 20f, 0.012f),
            LampSound.Filament => (LampSounds.FilamentName, 0.25f, 10f, 0.08f),
            LampSound.Extinguish => (LampSounds.OffName, 0.25f, 16f, 0.03f),
            _ => (LampSounds.SputterName, 0.3f, 22f, 0.06f),
        };
    /// <summary>A lamp's own pitch: fixed per fitting, so the same lamp always sounds like itself.</summary>
    private static float LampPitch(int index, float spread)
    {
        uint h = (uint)(index + 1) * 0x9E3779B1u;
        h ^= h >> 15;
        h *= 0x85EBCA6Bu;
        h ^= h >> 13;
        float unit = (h >> 8) * (1f / 16777216f);

        return 1f + (((unit * 2f) - 1f) * spread);
    }
}

using OpenIGI.Audio;
using OpenIGI.Core.Numerics;
using OpenIGI.Core.Titles;
using OpenIGI.Game.Multiplayer.Modes;
using OpenIGI.Host.Audio;
using OpenIGI.Presentation.Rendering.Blast;

namespace OpenIGI.Host;

/// <content>
/// The bomb heard: the plant and the defuse as a player begins them, cut short when it lets go; the planted bomb's tick once a
/// second, as I.G.I. 2's bomb ticks; and a warning beep over it at the end, once a second in the last ten and twice in the
/// last five; and the blast when it goes off, I.G.I. 2's bomb's own near it, a far rumble over the whole map and a deep thump
/// under both -- and a moment later its front, rushing at the ear and striking it.
/// </content>
public sealed partial class GameHost
{
    private const double BombWarnSeconds = 10;
    private const double BombHurrySeconds = 5;

    // The plant or defuse each working player's hands are making, by slot.
    private readonly Dictionary<int, AudioVoiceHandle> _bombWork = [];

    // When, on this end's clock, the planted bomb goes off (the report says how long is left only when it changes), and the
    // last tick and beep played, as counts of seconds and half seconds left; none while no bomb is planted.
    private double? _bombBlowsAt;
    private long _bombLastTick = long.MaxValue;
    private long _bombLastBeep = long.MaxValue;

    // Where the planted bomb last lay, for its blast once the report has it gone.
    private Vector3d? _bombLastAt;

    // The blast whose front this machine's ear has heard coming already.
    private BombBlast? _bombFrontHeard;

    // Once a frame.
    private void UpdateBombSounds()
    {
        if (TickObserver is not Multiplayer.ISessionMode { Mode: MatchModes.SearchAndDestroy }
            || TickObserver is not ISessionScoreboard board
            || board.ModeReport is not { } report
            || _audioOut.Sounds is not { } sounds)
        {
            StopBombSounds();
            return;
        }

        // The hands: a player that began working starts its sound where it kneels, one that stopped cuts its sound short.
        HashSet<int> working = [];
        foreach (ModeWork work in report.Working)
        {
            working.Add(work.Slot);
            if (!_bombWork.ContainsKey(work.Slot))
            {
                Vector3d? at = board.PositionOfSlot(work.Slot) ?? report.BombAt;
                _bombWork[work.Slot] = sounds.PlayExternal(work.Defusing ? BombSounds.Defuse : BombSounds.Plant, at, gain: 1f, priority: 200, minimumDistanceInMetres: 3f, maximumDistanceInMetres: 50f);
            }
        }

        foreach (int slot in _bombWork.Keys.Where(s => !working.Contains(s)).ToArray())
        {
            sounds.Stop(_bombWork[slot]);
            _bombWork.Remove(slot);
        }

        // The bomb: its clock taken from the report when it was planted, then kept here.
        double now = _loop.TimeSource.Milliseconds / 1000.0;
        if (report.BombAt is not { } bomb)
        {
            // Gone: blown when the round ends on it -- in this report or a later one, so the spot is kept until the round
            // says how it ended -- else defused or the round over some other way.
            if (_bombLastAt is { } blown && report.Reason == RoundEnd.Detonated)
            {
                sounds.PlayExternal(BombSounds.Explosion, blown, gain: 1f, priority: 250, minimumDistanceInMetres: 12f, maximumDistanceInMetres: 180f);
                sounds.PlayExternal(BombSounds.DistantExplosion, blown, gain: 0.8f, priority: 240, minimumDistanceInMetres: 60f, maximumDistanceInMetres: 900f);

                // And the deep thump under both, which carries farthest of all.
                sounds.PlayExternal(BombSounds.FarThump, blown, gain: 0.9f, priority: 245, minimumDistanceInMetres: 30f, maximumDistanceInMetres: 1500f);
            }

            if (report.Reason != RoundEnd.None)
            {
                _bombLastAt = null;
            }

            _bombBlowsAt = null;
            _bombLastTick = _bombLastBeep = long.MaxValue;
            return;
        }

        _bombLastAt = bomb;
        _bombBlowsAt ??= now + report.FuseLeft;
        double left = _bombBlowsAt.Value - now;
        if (left <= 0)
        {
            return;
        }

        long tick = (long)Math.Floor(left);
        if (tick < _bombLastTick)
        {
            _bombLastTick = tick;
            sounds.PlayExternal(BombSounds.Tick, bomb, gain: 0.9f, priority: 190, minimumDistanceInMetres: 4f, maximumDistanceInMetres: 45f);
        }

        long beep = (long)Math.Floor(left * 2);
        if (left <= BombWarnSeconds && beep < _bombLastBeep && (left <= BombHurrySeconds || beep % 2 == 0))
        {
            _bombLastBeep = beep;
            sounds.PlayExternal(BombSounds.Warning, bomb, gain: 1f, priority: 195, minimumDistanceInMetres: 4f, maximumDistanceInMetres: 60f);
        }
    }

    /// <summary>
    /// The bomb's front heard coming at this machine's own ear, a beat before it gets there (<see cref="BombWave"/>): the rush
    /// of air and its strike, for anybody near enough for it to matter -- the living it hurts, a camera or the dead alike.
    /// </summary>
    /// <remarks>
    /// Timed here on the blast this machine shows (<see cref="BombBlastEffects"/>), on the same clock the server hits each
    /// player on, so nothing more need be sent for it; measured from where the ear is each frame, so an ear running from the
    /// bomb hears it the later for it.
    /// </remarks>
    private void UpdateBombFrontSound()
    {
        IReadOnlyList<BombBlast> blasts = _effects.Bomb.Blasts;
        if (blasts.Count == 0 || _earCamera is not { } ear || _audioOut.Sounds is not { } sounds)
        {
            _bombFrontHeard = null;
            return;
        }

        BombBlast blast = blasts[^1];
        if (ReferenceEquals(blast, _bombFrontHeard))
        {
            return;
        }

        double distance = (ear.Position - (blast.CentreMetres + new Vector3d(0.0, 0.0, 0.4))).Length;
        if (distance > BombDamage.ReachMetres(blast.RadiusMetres))
        {
            return;
        }

        if (BombWave.IsHeard(blast.Age(0f), distance))
        {
            _bombFrontHeard = blast;
            sounds.PlayExternal(BombSounds.FrontRush, positionInUnits: null, gain: 1f, priority: 252);
        }
    }

    private void StopBombSounds()
    {
        foreach (AudioVoiceHandle voice in _bombWork.Values)
        {
            _audioOut.Sounds?.Stop(voice);
        }

        _bombWork.Clear();
        _bombBlowsAt = null;
        _bombLastAt = null;
        _bombLastTick = _bombLastBeep = long.MaxValue;
    }
}

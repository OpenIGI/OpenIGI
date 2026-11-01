using OpenIGI.Core.Numerics;
using OpenIGI.Audio;
using OpenIGI.Engine.Audio;
using OpenIGI.Game.Physics;
using OpenIGI.Game.Weapons;

namespace OpenIGI.Game.Igi1.World;

/// <content>The sound each missile flies with.</content>
public sealed partial class Igi1MissionAudio
{
    private readonly Dictionary<object, (string Sound, AudioVoiceHandle Voice)> _missileVoices = [];
    private readonly Dictionary<string, string> _missileSounds = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Sounds every missile in the air: the player's, a co-op session's other players', and the vehicles'.</summary>
    /// <remarks>
    /// <c>Missile_StartSound</c> (<c>0x429D80</c>) starts the sound the missile's physics type names as it is made, and
    /// <c>0x428610</c> keeps it at the missile each tick; the missile's delete takes it away.
    /// </remarks>
    public void SoundMissiles()
    {
        if (host.SoundOutput is not { } sounds)
        {
            return;
        }

        HashSet<object> flying = [];
        void Fly(object key, string model, Vector3d position)
        {
            flying.Add(key);
            if (_missileVoices.TryGetValue(key, out (string Sound, AudioVoiceHandle Voice) playing))
            {
                sounds.SetDefinitionVoicePosition(playing.Sound, playing.Voice, position, priority: 150, looping: true);
                return;
            }

            string sound = FlightSound(model);
            AudioVoiceHandle voice = sound.Length == 0
                ? AudioVoiceHandle.None
                : sounds.PlayDefinition(sound, position, priority: 150, looping: true);
            _missileVoices[key] = (sound, voice);
        }

        IEnumerable<LiveMissile> missiles = (mission.Player?.Missiles.Missiles ?? [])
            .Concat(mission.NetworkPlayers.SelectMany(static p => p.Player.Missiles.Missiles))
            .Concat(mission.VehicleMissiles.Missiles);
        foreach (LiveMissile missile in missiles)
        {
            Fly(missile, missile.ModelName, missile.Position);
        }

        // Another game's rockets, as a co-op server's snapshots show them, each followed by whose it is and which.
        foreach (ShownProjectile shown in mission.ShownProjectiles)
        {
            Fly((shown.Owner, shown.Ordinal, shown.Model), shown.Model, shown.Position);
        }

        foreach (object gone in _missileVoices.Keys.Where(key => !flying.Contains(key)).ToArray())
        {
            sounds.Stop(_missileVoices[gone].Voice);
            _missileVoices.Remove(gone);
        }
    }

    // Every missile's voice, as the bank is emptied for a mission.
    private void StopMissileSounds()
    {
        foreach ((string _, AudioVoiceHandle voice) in _missileVoices.Values)
        {
            host.SoundOutput?.Stop(voice);
        }

        _missileVoices.Clear();
        _missileSounds.Clear();
    }

    // The sound a missile's physics type names (its argument after the smoke sprite), by its model.
    private string FlightSound(string model)
    {
        if (!_missileSounds.TryGetValue(model, out string? sound))
        {
            sound = mission.VehiclePhysics?.Types is { } types
                && types.TryFind(model, out PhysicsObjectType type)
                && MissileTypeDefinition.Of<X87>(type) is { } missile
                    ? missile.Sound
                    : string.Empty;
            _missileSounds[model] = sound;
        }

        return sound;
    }
}

using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Net;

namespace OpenIGI.Game.Igi2.Multiplayer;

/// <summary>
/// What the level's bodies fired over the last few ticks -- each round, grenade and rocket, numbered -- written in every snapshot
/// (<see cref="Snapshot.Fired"/>) for each client to fire again from its own copies of the shooters, harmless
/// (<see cref="Igi2WeaponFiring.Replay"/>): their tracers, holes, chips and splashes, the blood of whom they strike, the panes they
/// break, and the grenades and rockets in flight.
/// </summary>
/// <remarks>
/// <para>
/// Each launch rides several snapshots, so one lost on the way costs nothing; a client fires each once, by its number. One its
/// own player fired it showed as it fired it. On joining, what was fired before is passed over.
/// </para>
/// <para>
/// The weapon goes by its number in the session's tables and the fire type by its slot; the orientation by its three angles.
/// </para>
/// </remarks>
internal sealed class Igi2FiredLog
{
    // How long a launch is kept in the snapshots, in ticks: a fifth of a second, several snapshots.
    private const int KeptTicks = 6;

    // The most written at once: a busy tick's shotgun pellets from a room of guards.
    private const int MostWritten = 96;

    private readonly List<(uint Number, long Tick, int Source, Igi2LaunchNote Note)> _recent = [];
    private readonly BitWriter _writer = new(1024);
    private uint _number;
    private uint? _firedUpTo;

    /// <summary>Notes a launch on the server.</summary>
    /// <param name="tick">The tick it was made on.</param>
    /// <param name="source">The entity of the body that fired it.</param>
    /// <param name="note">The launch.</param>
    public void Note(long tick, int source, in Igi2LaunchNote note) => _recent.Add((++_number, tick, source, note));

    /// <summary>Writes the launches of the last few ticks, on the server.</summary>
    /// <param name="tick">The tick just run.</param>
    /// <param name="tables">The session's weapons, by which a weapon is numbered.</param>
    /// <returns>The bytes, or <see langword="null"/> when nothing was fired lately.</returns>
    public byte[]? Capture(long tick, Igi2WeaponTables? tables)
    {
        _recent.RemoveAll(r => tick - r.Tick > KeptTicks);
        if (_recent.Count == 0 || tables is null)
        {
            return null;
        }

        int first = Math.Max(0, _recent.Count - MostWritten);
        _writer.Reset();
        _writer.WriteVarUInt((uint)(_recent.Count - first));
        for (int i = first; i < _recent.Count; i++)
        {
            (uint number, _, int source, Igi2LaunchNote note) = _recent[i];
            Igi2ProjectileLaunch l = note.Launch;
            _writer.WriteVarUInt(number);
            _writer.WriteVarInt(source);
            _writer.WriteBits((uint)note.FireType, 1);
            _writer.WriteVarInt(l.ProjectileTaskType);
            _writer.WriteVarInt(IndexOf(tables.WeaponTypes, l.WeaponType));
            _writer.WriteVarInt(l.AmmoType is null ? -1 : IndexOf(tables.AmmoTypes, l.AmmoType));
            Igi2NetPosition.Write(_writer, l.Position);
            (float alpha, float beta, float gamma) = World.Igi2Pose.Angles(l.Orientation);
            _writer.WriteSingle(alpha);
            _writer.WriteSingle(beta);
            _writer.WriteSingle(gamma);
            _writer.WriteSingle(l.ParentSpeed.X);
            _writer.WriteSingle(l.ParentSpeed.Y);
            _writer.WriteSingle(l.ParentSpeed.Z);
            _writer.WriteSingle(l.Speed);
            _writer.WriteSingle(l.EffectiveRange);
            _writer.WriteVarInt(l.HeldTime);
            _writer.WriteVarInt(l.ExplodeTime);
        }

        return _writer.AsSpan().ToArray();
    }

    /// <summary>Fires again what was fired lately and not yet here, on a client.</summary>
    /// <param name="fired">The bytes, as <see cref="Capture"/> wrote them, or <see langword="null"/>.</param>
    /// <param name="ownEntity">This client's own entity, whose launches it made itself, or <see langword="null"/>.</param>
    /// <param name="tables">The session's weapons.</param>
    /// <param name="weaponOf">The replica weapon of a body by its entity, or <see langword="null"/> for one not here.</param>
    public void Replay(byte[]? fired, int? ownEntity, Igi2WeaponTables? tables, Func<int, Igi2WeaponFiring?> weaponOf)
    {
        if (fired is null || tables is null)
        {
            // Nothing fired lately: whatever comes next is new.
            _firedUpTo ??= 0;
            return;
        }

        BitReader reader = new(fired);
        uint count = reader.ReadVarUInt();
        uint newest = _firedUpTo ?? 0;
        for (int i = 0; i < count && !reader.IsOverflowed; i++)
        {
            uint number = reader.ReadVarUInt();
            int source = reader.ReadVarInt();
            int fireType = (int)reader.ReadBits(1);
            int taskType = reader.ReadVarInt();
            Igi2WeaponTypeTask? weapon = tables.Weapon(reader.ReadVarInt());
            int ammo = reader.ReadVarInt();
            Vector3d position = Igi2NetPosition.Read(ref reader);
            float[] orientation = EngineEuler.Rotation(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            Vector3f parentSpeed = new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            float speed = reader.ReadSingle();
            float range = reader.ReadSingle();
            int held = reader.ReadVarInt();
            int explodeTime = reader.ReadVarInt();
            if (reader.IsOverflowed)
            {
                return;
            }

            if (_firedUpTo is { } upTo && number > upTo && source != ownEntity && weapon is not null && weaponOf(source) is { } firing)
            {
                firing.Replay(
                    new Igi2ProjectileLaunch(taskType, position, orientation, parentSpeed, speed, range, held, weapon, ammo < 0 ? null : tables.Ammo(ammo), explodeTime),
                    fireType);
            }

            newest = Math.Max(newest, number);
        }

        _firedUpTo = newest;
    }

    private static int IndexOf<T>(IReadOnlyList<T> list, T item)
        where T : class
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (ReferenceEquals(list[i], item))
            {
                return i;
            }
        }

        return -1;
    }
}

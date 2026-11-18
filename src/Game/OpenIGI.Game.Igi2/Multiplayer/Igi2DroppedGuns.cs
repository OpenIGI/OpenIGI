using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Pickups;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Net;

namespace OpenIGI.Game.Igi2.Multiplayer;

/// <summary>
/// The guns lying in a co-op level that the level did not place -- a dead guard's, one a player threw down or swapped away --
/// numbered by the server and written with the level's state, for each client to lay where the server's lie.
/// </summary>
/// <remarks>
/// <para>
/// Each machine once made its own: a client's copy of a guard dropped what its own stream and difficulty gave, the gun the copy
/// was made with rather than the one the guard held, and a player's thrown gun lay on its own machine alone. The host took a
/// dead guard's rifle and the client could still take its own; a late joiner saw guns at every corpse.
/// </para>
/// <para>
/// So on a client nothing the level throws down is laid (<see cref="Igi2PickupList.IsReplica"/>): only the server's, once they
/// have come to rest, each made as the server's lies, with its rounds. One the server no longer lists was taken there, and goes.
/// A client's own player still takes what it walks over, as the server's copy of it does from the same command; a gun it took
/// is not laid again while the server's word catches up, and is laid again if the server still lists it after that.
/// </para>
/// </remarks>
internal sealed class Igi2DroppedGuns
{
    // How long a gun this client took is left untaken in its eyes before the server's list is believed again: two seconds.
    private const int TakenPatience = 60;

    // The most written: more than a level's corpses ever leave lying.
    private const int MostWritten = 128;

    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<Igi2GunPickup, object> _ids = [];
    private readonly Dictionary<int, Igi2GunPickup> _laid = [];
    private readonly Dictionary<int, int> _takenHere = [];
    private int _next;

    /// <summary>Writes the guns lying at rest that the level did not place, on the server.</summary>
    /// <param name="writer">Where they go.</param>
    /// <param name="pickups">The level's pickups, or <see langword="null"/>.</param>
    public void Write(BitWriter writer, Igi2PickupList? pickups)
    {
        ArgumentNullException.ThrowIfNull(writer);
        List<(int Id, Igi2GunPickup Gun, int Weapon)> lying = [];
        if (pickups?.Tables is { } tables)
        {
            foreach (Igi2GunPickup gun in pickups.Items)
            {
                if (gun.Task is not null || gun.IsDelete || gun.IsMoving || !gun.IsInWorld || lying.Count >= MostWritten)
                {
                    continue;
                }

                int weapon = IndexOf(tables.WeaponTypes, gun.WeaponType);
                if (weapon >= 0)
                {
                    lying.Add(((int)_ids.GetValue(gun, _ => _next++), gun, weapon));
                }
            }
        }

        writer.WriteVarUInt((uint)lying.Count);
        foreach ((int id, Igi2GunPickup gun, int weapon) in lying)
        {
            writer.WriteVarUInt((uint)id);
            writer.WriteVarUInt((uint)weapon);
            Igi2NetPosition.Write(writer, gun.Position);
            (float alpha, float beta, float gamma) = World.Igi2Pose.Angles(gun.Orientation);
            writer.WriteBits(Quantize.AngleToBits(alpha), 16);
            writer.WriteBits(Quantize.AngleToBits(beta), 16);
            writer.WriteBits(Quantize.AngleToBits(gamma), 16);
            writer.WriteVarInt(gun.AmmoInClip);
            writer.WriteVarUInt((uint)gun.AmmoForFireType.Count);
            foreach (int rounds in gun.AmmoForFireType)
            {
                writer.WriteVarInt(rounds);
            }
        }
    }

    /// <summary>Lays the server's guns here and takes away those it no longer lists, on a client.</summary>
    /// <param name="reader">The guns, as <see cref="Write"/> wrote them.</param>
    /// <param name="pickups">The level's pickups, or <see langword="null"/>.</param>
    public void Read(ref BitReader reader, Igi2PickupList? pickups)
    {
        uint count = reader.ReadVarUInt();
        HashSet<int> listed = [];
        for (int i = 0; i < count && !reader.IsOverflowed; i++)
        {
            int id = (int)reader.ReadVarUInt();
            int weapon = (int)reader.ReadVarUInt();
            Vector3d position = Igi2NetPosition.Read(ref reader);
            float[] orientation = EngineEuler.Rotation(
                Quantize.AngleFromBits((ushort)reader.ReadBits(16)),
                Quantize.AngleFromBits((ushort)reader.ReadBits(16)),
                Quantize.AngleFromBits((ushort)reader.ReadBits(16)));
            int inClip = reader.ReadVarInt();
            int[] rounds = new int[Math.Min(reader.ReadVarUInt(), 8u)];
            for (int r = 0; r < rounds.Length; r++)
            {
                rounds[r] = reader.ReadVarInt();
            }

            if (reader.IsOverflowed)
            {
                return;
            }

            listed.Add(id);
            if (_laid.TryGetValue(id, out Igi2GunPickup? laid) && !laid.IsDelete)
            {
                continue;
            }

            // Taken here: not laid again until the server has had time to say so.
            if (_laid.TryGetValue(id, out Igi2GunPickup? taken) && taken.IsDelete && !_takenHere.ContainsKey(id))
            {
                _takenHere[id] = TakenPatience;
            }

            if (_takenHere.TryGetValue(id, out int left) && left > 0)
            {
                continue;
            }

            _takenHere.Remove(id);
            if (pickups?.Tables is { } tables && tables.Weapon(weapon) is { } weaponType)
            {
                float radius = weaponType.FixedModel.Length > 0 && pickups.Models?.Invoke(weaponType.FixedModel) is { } model ? model.NativeMarkerBound : 1f;
                Igi2GunPickup gun = new(null, weaponType, tables, position, orientation, radius) { IsReplica = true };
                gun.Apply(new Igi2GunPickup.Saved(position, orientation, inClip, rounds, default, weaponType.WeaponId, false, false, true));
                pickups.Add(gun);
                _laid[id] = gun;
            }
        }

        // What the server no longer lists was taken there, or left the world.
        foreach ((int id, Igi2GunPickup gun) in _laid.ToArray())
        {
            if (!listed.Contains(id))
            {
                gun.TakenElsewhere();
                _laid.Remove(id);
                _takenHere.Remove(id);
            }
        }
    }

    /// <summary>Counts down the patience for guns this client took, a tick at a time, on a client.</summary>
    public void Tick()
    {
        foreach (int id in _takenHere.Keys.ToArray())
        {
            _takenHere[id]--;
        }
    }

    private static int IndexOf(IReadOnlyList<Igi2WeaponTypeTask> list, Igi2WeaponTypeTask item)
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

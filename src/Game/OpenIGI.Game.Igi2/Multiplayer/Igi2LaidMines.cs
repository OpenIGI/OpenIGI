using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Player;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Net;

namespace OpenIGI.Game.Igi2.Multiplayer;

/// <summary>
/// The mines lying in a co-op level, numbered by the server and written with the level's state, for each client to lay where the
/// server's lie (<see cref="Igi2LevelMines.IsReplica"/>).
/// </summary>
/// <remarks>
/// A mine is laid inside its fire, never as a launch, so the rounds fired (<see cref="Igi2FiredLog"/>) never carried it: a partner
/// saw none of the mines another laid, and the layer's own game kept a live one of its own that went off by itself. A client now
/// lays only the server's, lying still; one the server no longer lists went off there, or was taken up, and its blast comes with
/// the server's.
/// </remarks>
internal sealed class Igi2LaidMines
{
    // The most written: more than a level's players can carry.
    private const int MostWritten = 64;

    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<Igi2Mine, object> _ids = [];
    private readonly Dictionary<int, Igi2Mine> _laid = [];
    private int _next;

    /// <summary>Writes the mines lying in the level, on the server.</summary>
    /// <param name="writer">Where they go.</param>
    /// <param name="mines">The level's mines, or <see langword="null"/>.</param>
    public void Write(BitWriter writer, Igi2LevelMines? mines)
    {
        ArgumentNullException.ThrowIfNull(writer);
        List<(int Id, Igi2Mine Mine)> lying = [];
        foreach (Igi2Mine mine in mines?.Laid ?? [])
        {
            if (!mine.IsDeleted && !mine.IsExploded && lying.Count < MostWritten)
            {
                lying.Add(((int)_ids.GetValue(mine, _ => _next++), mine));
            }
        }

        writer.WriteVarUInt((uint)lying.Count);
        foreach ((int id, Igi2Mine mine) in lying)
        {
            writer.WriteVarUInt((uint)id);
            Igi2NetPosition.Write(writer, mine.Position);
            (float alpha, float beta, float gamma) = World.Igi2Pose.Angles(mine.Frame);
            writer.WriteBits(Quantize.AngleToBits(alpha), 16);
            writer.WriteBits(Quantize.AngleToBits(beta), 16);
            writer.WriteBits(Quantize.AngleToBits(gamma), 16);
            writer.WriteBool(mine.IsRemote);
        }
    }

    /// <summary>Lays the server's mines here and takes away those it no longer lists, on a client.</summary>
    /// <param name="reader">The mines, as <see cref="Write"/> wrote them.</param>
    /// <param name="mines">The level's mines, or <see langword="null"/>.</param>
    /// <param name="tables">The session's weapons, whose mine-laying weapon gives a mine its model.</param>
    public void Read(ref BitReader reader, Igi2LevelMines? mines, Igi2WeaponTables? tables)
    {
        uint count = reader.ReadVarUInt();
        HashSet<int> listed = [];
        for (int i = 0; i < count && !reader.IsOverflowed; i++)
        {
            int id = (int)reader.ReadVarUInt();
            Vector3d position = Igi2NetPosition.Read(ref reader);
            float[] frame = EngineEuler.Rotation(
                Quantize.AngleFromBits((ushort)reader.ReadBits(16)),
                Quantize.AngleFromBits((ushort)reader.ReadBits(16)),
                Quantize.AngleFromBits((ushort)reader.ReadBits(16)));
            bool remote = reader.ReadBool();
            if (reader.IsOverflowed)
            {
                return;
            }

            listed.Add(id);
            if (_laid.ContainsKey(id) || mines is null || tables is null)
            {
                continue;
            }

            Igi2Mine.Saved saved = new(position, frame, false, false, 0f, -1, 0, 0, 0, 0f, 0f, false, remote);
            if (mines.Recreate(saved, tables, replica: true) is { } laid)
            {
                _laid[id] = laid;
            }
        }

        // What the server no longer lists went off there, or was taken up: its blast, if any, comes with the server's.
        foreach ((int id, Igi2Mine mine) in _laid.ToArray())
        {
            if (!listed.Contains(id))
            {
                mine.Delete();
                mines?.Remove(mine);
                _laid.Remove(id);
            }
        }
    }
}

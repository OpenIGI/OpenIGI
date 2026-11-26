using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Net;

namespace OpenIGI.Game.Igi2.Multiplayer;

/// <summary>
/// The explosions a co-op server's level set off lately -- the soldiers' grenades, a partner's, a barrel's -- each numbered,
/// written in the level's state for every client to show the ones it has not shown yet.
/// </summary>
/// <remarks>
/// A client shows each once, with no harm (<see cref="Igi2ExplosionWorld.Replay"/>): the server's blast has done its harm
/// already, and the client's own player was told how hurt it is. One its own player set off it showed as it threw it. On
/// joining, what went off before is passed over.
/// </remarks>
internal sealed class Igi2BlastLog
{
    // The newest kept: more than a tick's worth, so a burst all reaches a client that takes the state late.
    private const int Kept = 16;

    private readonly List<(uint Number, int Owner, Igi2ExplosionRequest Request)> _recent = [];
    private uint _number;
    private uint? _shownUpTo;

    /// <summary>Notes an explosion the server's level set off.</summary>
    /// <param name="owner">The entity of the player who set it off, or -1.</param>
    /// <param name="request">The explosion.</param>
    public void Note(int owner, Igi2ExplosionRequest request)
    {
        _recent.Add((++_number, owner, request));
        if (_recent.Count > Kept)
        {
            _recent.RemoveAt(0);
        }
    }

    /// <summary>Writes the newest explosions, on the server.</summary>
    /// <param name="writer">Where to.</param>
    public void Write(BitWriter writer)
    {
        writer.WriteVarUInt((uint)_recent.Count);
        foreach ((uint number, int owner, Igi2ExplosionRequest r) in _recent)
        {
            writer.WriteVarUInt(number);
            writer.WriteVarInt(owner);
            Int3 at = Int3.From(r.Position);
            writer.WriteVarInt(at.X);
            writer.WriteVarInt(at.Y);
            writer.WriteVarInt(at.Z);
            writer.WriteVarInt(r.Count);
            writer.WriteVarInt(r.Period);
            writer.WriteSingle(r.FullRange);
            writer.WriteSingle(r.FalloffRange);
            writer.WriteSingle(r.BurnRange);
            writer.WriteVarInt(r.FragmentCount);
            writer.WriteVarInt(r.FireballCount);
            writer.WriteString(r.FragmentModel, 256);
            writer.WriteString(r.Sound, 256);
            writer.WriteSingle(r.Direction.X);
            writer.WriteSingle(r.Direction.Y);
            writer.WriteSingle(r.Direction.Z);
            writer.WriteSingle(r.MaxAlpha);
            writer.WriteSingle(r.MinSpeed);
            writer.WriteSingle(r.MaxSpeed);
            writer.WriteVarUInt((uint)r.Falloff);
        }
    }

    /// <summary>Shows the explosions not shown yet, on a client.</summary>
    /// <param name="reader">The state, at the explosions.</param>
    /// <param name="ownEntity">This client's own entity, whose explosions it showed itself, or <see langword="null"/>.</param>
    /// <param name="explosions">Where they go off here, or <see langword="null"/>.</param>
    public void Show(ref BitReader reader, int? ownEntity, Igi2ExplosionWorld? explosions)
    {
        uint count = reader.ReadVarUInt();
        uint newest = _shownUpTo ?? 0;
        for (int i = 0; i < count && !reader.IsOverflowed; i++)
        {
            uint number = reader.ReadVarUInt();
            int owner = reader.ReadVarInt();
            Vector3d at = new(reader.ReadVarInt(), reader.ReadVarInt(), reader.ReadVarInt());
            Igi2ExplosionRequest request = new(
                at,
                reader.ReadVarInt(),
                reader.ReadVarInt(),
                0f,
                reader.ReadSingle(),
                reader.ReadSingle(),
                reader.ReadSingle(),
                reader.ReadVarInt(),
                reader.ReadVarInt(),
                null,
                reader.ReadString(256),
                reader.ReadString(256),
                -1,
                0,
                0f,
                new Vector3f(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
                reader.ReadSingle(),
                reader.ReadSingle(),
                reader.ReadSingle(),
                null,
                null,
                (Igi2ExplosionFalloff)reader.ReadVarUInt());
            if (reader.IsOverflowed)
            {
                return;
            }

            // Joining, the state's explosions are history: none is shown, and the next new one is.
            if (_shownUpTo is not null && number > _shownUpTo && owner != ownEntity)
            {
                explosions?.Replay(request);
            }

            newest = Math.Max(newest, number);
        }

        _shownUpTo = newest;
    }
}

using System.Globalization;
using System.Text;
using OpenIGI.Net;

namespace OpenIGI.Game.Multiplayer.Modes;

/// <summary>Why a round was decided, as its result card says it.</summary>
public enum RoundEnd : byte
{
    /// <summary>Not decided, or the mode has no rounds.</summary>
    None,

    /// <summary>One side left nobody standing.</summary>
    Eliminated,

    /// <summary>The round's clock ran out.</summary>
    TimeRanOut,

    /// <summary>The bomb went off.</summary>
    Detonated,

    /// <summary>The bomb was defused.</summary>
    Defused,
}

/// <summary>A player at work on the mode's objective: planting or defusing, and how far it has got.</summary>
/// <param name="Slot">The player's slot.</param>
/// <param name="Progress">How far, in <c>[0,1]</c>.</param>
/// <param name="Defusing"><see langword="true"/> for a defuse, <see langword="false"/> for a plant.</param>
public readonly record struct ModeWork(int Slot, float Progress, bool Defusing);

/// <summary>What a mode tells the clients of itself beyond the match's phase and clock: what its HUD is drawn from.</summary>
public sealed record ModeReport
{
    /// <summary>Gets a report of nothing.</summary>
    public static ModeReport None { get; } = new();

    /// <summary>Gets the rounds (or the score) that win the match, or 0 for none.</summary>
    public int Target { get; init; }

    /// <summary>Gets the team that attacks the objective, or -1 for a mode without sides.</summary>
    public int Attackers { get; init; } = -1;

    /// <summary>Gets why the last round was decided.</summary>
    public RoundEnd Reason { get; init; }

    /// <summary>Gets the site the bomb is planted at, or empty while it is not.</summary>
    public string Planted { get; init; } = string.Empty;

    /// <summary>Gets where a planted bomb lies, in world units, or <see langword="null"/>.</summary>
    public Core.Numerics.Vector3d? BombAt { get; init; }

    /// <summary>Gets the seconds left on a planted bomb's fuse.</summary>
    public double FuseLeft { get; init; }

    /// <summary>Gets the fuse's whole length, in seconds.</summary>
    public double FuseSeconds { get; init; }

    /// <summary>Gets the players at work on the objective now.</summary>
    public IReadOnlyList<ModeWork> Working { get; init; } = [];

    /// <summary>Gets a key that changes when anything the clients must be told at once changes (the fuse running down is not).</summary>
    public string Key
    {
        get
        {
            StringBuilder key = new();
            key.Append(CultureInfo.InvariantCulture, $"{Target}|{Attackers}|{Reason}|{Planted}|{BombAt is not null}|{FuseSeconds}");
            foreach (ModeWork work in Working)
            {
                key.Append(CultureInfo.InvariantCulture, $"|{work.Slot}:{(int)(work.Progress * 10)}:{work.Defusing}");
            }

            return key.ToString();
        }
    }

    /// <summary>Writes it for the clients.</summary>
    /// <param name="writer">Where it goes.</param>
    public void Write(BitWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteRanged(Math.Clamp(Target, 0, 255), 0, 255);
        writer.WriteRanged(Math.Clamp(Attackers, -1, 6), -1, 6);
        writer.WriteBits((uint)Reason, 4);
        writer.WriteString(Planted, 8);
        writer.WriteBool(BombAt is not null);
        if (BombAt is { } bomb)
        {
            writer.WriteDouble(bomb.X);
            writer.WriteDouble(bomb.Y);
            writer.WriteDouble(bomb.Z);
        }

        writer.WriteRanged((int)Math.Clamp(Math.Ceiling(FuseLeft * 10), 0, 4095), 0, 4095);
        writer.WriteRanged((int)Math.Clamp(Math.Ceiling(FuseSeconds * 10), 0, 4095), 0, 4095);
        int count = Math.Min(Working.Count, 15);
        writer.WriteBits((uint)count, 4);
        for (int i = 0; i < count; i++)
        {
            ModeWork work = Working[i];
            writer.WriteRanged(Math.Clamp(work.Slot, 0, 65535), 0, 65535);
            writer.WriteRanged((int)Math.Round(Math.Clamp(work.Progress, 0f, 1f) * 255f), 0, 255);
            writer.WriteBool(work.Defusing);
        }
    }

    /// <summary>Reads one a server wrote.</summary>
    /// <param name="reader">What arrived.</param>
    /// <returns>The report.</returns>
    public static ModeReport Read(ref BitReader reader)
    {
        int target = reader.ReadRanged(0, 255);
        int attackers = reader.ReadRanged(-1, 6);
        RoundEnd reason = (RoundEnd)reader.ReadBits(4);
        string planted = reader.ReadString(8);
        Core.Numerics.Vector3d? bombAt = reader.ReadBool() ? new Core.Numerics.Vector3d(reader.ReadDouble(), reader.ReadDouble(), reader.ReadDouble()) : null;
        double fuseLeft = reader.ReadRanged(0, 4095) / 10.0;
        double fuseSeconds = reader.ReadRanged(0, 4095) / 10.0;
        int count = (int)reader.ReadBits(4);
        ModeWork[] working = new ModeWork[count];
        for (int i = 0; i < count; i++)
        {
            int slot = reader.ReadRanged(0, 65535);
            float progress = reader.ReadRanged(0, 255) / 255f;
            working[i] = new ModeWork(slot, progress, reader.ReadBool());
        }

        return new ModeReport
        {
            Target = target,
            Attackers = attackers,
            Reason = reason,
            Planted = planted,
            BombAt = bombAt,
            FuseLeft = fuseLeft,
            FuseSeconds = fuseSeconds,
            Working = working,
        };
    }
}

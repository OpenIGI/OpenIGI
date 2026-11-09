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

/// <summary>What a player at a mode's work does.</summary>
public enum ModeWorkKind
{
    /// <summary>Planting the bomb.</summary>
    Plant,

    /// <summary>Defusing it.</summary>
    Defuse,
}

/// <summary>A player at work on the mode's objective: planting or defusing, and how far it has got.</summary>
/// <param name="Slot">The player's slot.</param>
/// <param name="Progress">How far, in <c>[0,1]</c>.</param>
/// <param name="Defusing"><see langword="true"/> for a defuse, <see langword="false"/> for a plant.</param>
/// <param name="Seconds">How long the whole hold takes, so a client runs the bar on between reports; 0 when it is not known.</param>
public readonly record struct ModeWork(int Slot, float Progress, bool Defusing, float Seconds = 0);

/// <summary>How much of a player's health the planted bomb would take where it stands.</summary>
/// <param name="Slot">The player.</param>
/// <param name="Share">The share of a whole health; one or more is death.</param>
public readonly record struct ModeRisk(int Slot, float Share);

/// <summary>The local player's use key as its own machine sees it, which the HUD starts a plant or defuse from at once.</summary>
/// <param name="Held">Whether it is down.</param>
/// <param name="Seconds">How long it has been down.</param>
public readonly record struct LocalUse(bool Held, double Seconds);

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

    /// <summary>Gets the way the bomb lies turned, in radians: the way its planter faced (or its carrier, dropped).</summary>
    public float BombYaw { get; init; }

    /// <summary>Gets the slot of the attacker carrying the bomb, or -1 while nobody does.</summary>
    public int Carrier { get; init; } = -1;

    /// <summary>Gets where a dropped bomb lies, in world units, or <see langword="null"/> while it is carried or planted.</summary>
    public Core.Numerics.Vector3d? DroppedAt { get; init; }

    /// <summary>Gets whether the dropped bomb was thrown, and is flown and drawn by the world as a dropped gun is.</summary>
    public bool BombFlies { get; init; }

    /// <summary>Gets the seconds left on a planted bomb's fuse.</summary>
    public double FuseLeft { get; init; }

    /// <summary>Gets the fuse's whole length, in seconds.</summary>
    public double FuseSeconds { get; init; }

    /// <summary>Gets the seconds the armoury still sells for this round, as of when it was said; nought while it is shut.</summary>
    public double BuyLeft { get; init; }

    /// <summary>Gets the seconds of the round's freeze time still to run, as of when it was said; nought once it is over.</summary>
    public double FreezeLeft { get; init; }

    /// <summary>Gets the players at work on the objective now.</summary>
    public IReadOnlyList<ModeWork> Working { get; init; } = [];

    /// <summary>Gets what the planted bomb would take from each living player, for their health bars to show.</summary>
    public IReadOnlyList<ModeRisk> Risks { get; init; } = [];

    /// <summary>Gets a key that changes when anything the clients must be told at once changes (the fuse running down is not).</summary>
    public string Key
    {
        get
        {
            StringBuilder key = new();
            key.Append(CultureInfo.InvariantCulture, $"{Target}|{Attackers}|{Reason}|{Planted}|{BombAt is not null}|{FuseSeconds}|{BuyLeft > 0}|{FreezeLeft > 0}|{Carrier}|{DroppedAt}|{BombFlies}");
            foreach (ModeWork work in Working)
            {
                // A hold begun or let go, not its progress: the clients run that on themselves from Seconds.
                key.Append(CultureInfo.InvariantCulture, $"|{work.Slot}:{work.Defusing}");
            }

            // A risk told again once it has moved a twentieth of a health: walking out of the blast shows as it happens.
            foreach (ModeRisk risk in Risks)
            {
                key.Append(CultureInfo.InvariantCulture, $"|r{risk.Slot}:{RiskStep(risk.Share)}");
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

        writer.WriteBits((uint)((int)Math.Round(BombYaw / (2 * Math.PI) * 256) & 255), 8);
        writer.WriteRanged(Math.Clamp(Carrier, -1, 65535), -1, 65535);
        writer.WriteBool(DroppedAt is not null);
        if (DroppedAt is { } dropped)
        {
            writer.WriteDouble(dropped.X);
            writer.WriteDouble(dropped.Y);
            writer.WriteDouble(dropped.Z);
            writer.WriteBool(BombFlies);
        }

        writer.WriteRanged((int)Math.Clamp(Math.Ceiling(FuseLeft * 10), 0, 4095), 0, 4095);
        writer.WriteRanged((int)Math.Clamp(Math.Ceiling(FuseSeconds * 10), 0, 4095), 0, 4095);
        writer.WriteRanged((int)Math.Clamp(Math.Ceiling(BuyLeft * 10), 0, 4095), 0, 4095);
        writer.WriteRanged((int)Math.Clamp(Math.Ceiling(FreezeLeft * 10), 0, 1023), 0, 1023);
        int count = Math.Min(Working.Count, 15);
        writer.WriteBits((uint)count, 4);
        for (int i = 0; i < count; i++)
        {
            ModeWork work = Working[i];
            writer.WriteRanged(Math.Clamp(work.Slot, 0, 65535), 0, 65535);
            writer.WriteRanged((int)Math.Round(Math.Clamp(work.Progress, 0f, 1f) * 255f), 0, 255);
            writer.WriteBool(work.Defusing);
            writer.WriteRanged((int)Math.Clamp(Math.Round(work.Seconds * 10), 0, 1023), 0, 1023);
        }

        int risks = Math.Min(Risks.Count, 31);
        writer.WriteBits((uint)risks, 5);
        for (int i = 0; i < risks; i++)
        {
            writer.WriteRanged(Math.Clamp(Risks[i].Slot, 0, 65535), 0, 65535);
            writer.WriteRanged(RiskByte(Risks[i].Share), 0, 255);
        }
    }

    // A share as a byte: a two-hundredth of a health a step, everything past one and a quarter healths as death.
    private static int RiskByte(float share) => (int)Math.Round(Math.Clamp(share, 0f, 1.275f) * 200f);

    private static int RiskStep(float share) => (int)Math.Round(Math.Clamp(share, 0f, 1.05f) * 20f);

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
        float bombYaw = (float)(reader.ReadBits(8) / 256.0 * 2 * Math.PI);
        int carrier = reader.ReadRanged(-1, 65535);
        Core.Numerics.Vector3d? droppedAt = reader.ReadBool() ? new Core.Numerics.Vector3d(reader.ReadDouble(), reader.ReadDouble(), reader.ReadDouble()) : null;
        bool bombFlies = droppedAt is not null && reader.ReadBool();
        double fuseLeft = reader.ReadRanged(0, 4095) / 10.0;
        double fuseSeconds = reader.ReadRanged(0, 4095) / 10.0;
        double buyLeft = reader.ReadRanged(0, 4095) / 10.0;
        double freezeLeft = reader.ReadRanged(0, 1023) / 10.0;
        int count = (int)reader.ReadBits(4);
        ModeWork[] working = new ModeWork[count];
        for (int i = 0; i < count; i++)
        {
            int slot = reader.ReadRanged(0, 65535);
            float progress = reader.ReadRanged(0, 255) / 255f;
            bool defusing = reader.ReadBool();
            working[i] = new ModeWork(slot, progress, defusing, reader.ReadRanged(0, 1023) / 10f);
        }

        int riskCount = (int)reader.ReadBits(5);
        ModeRisk[] risks = new ModeRisk[riskCount];
        for (int i = 0; i < riskCount; i++)
        {
            int slot = reader.ReadRanged(0, 65535);
            risks[i] = new ModeRisk(slot, reader.ReadRanged(0, 255) / 200f);
        }

        return new ModeReport
        {
            Target = target,
            Attackers = attackers,
            Reason = reason,
            Planted = planted,
            BombAt = bombAt,
            BombYaw = bombYaw,
            Carrier = carrier,
            DroppedAt = droppedAt,
            BombFlies = bombFlies,
            FuseLeft = fuseLeft,
            FuseSeconds = fuseSeconds,
            BuyLeft = buyLeft,
            FreezeLeft = freezeLeft,
            Working = working,
            Risks = risks,
        };
    }
}

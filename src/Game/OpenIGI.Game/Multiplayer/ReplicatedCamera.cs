using OpenIGI.Net;

namespace OpenIGI.Game.Multiplayer;

/// <summary>What a replicated security camera is doing, one bit each.</summary>
[Flags]
public enum ReplicatedCameraCondition : byte
{
    /// <summary>Unpowered, intact, seeing nobody.</summary>
    None = 0,

    /// <summary>The holder is powered for its next tick.</summary>
    Powered = 1 << 0,

    /// <summary>The lens ran powered this tick.</summary>
    Active = 1 << 1,

    /// <summary>The lens is destroyed.</summary>
    Exploded = 1 << 2,

    /// <summary>It exports <c>isDetection</c>.</summary>
    Detection = 1 << 3,

    /// <summary>It exported <c>isDetection</c> the tick before.</summary>
    LastDetection = 1 << 4,

    /// <summary>It has a target in view this tick.</summary>
    Seeing = 1 << 5,

    /// <summary>Its target is one of its own side fallen.</summary>
    Body = 1 << 6,

    /// <summary>Its head turned this tick.</summary>
    Moving = 1 << 7,
}

/// <summary>One of the level's security cameras as the server has it: where its lens points and what it sees.</summary>
/// <param name="Alpha">The lens pitch, a 16-bit angle code.</param>
/// <param name="Gamma">The sweep's yaw on top of the authored one, a 16-bit angle code.</param>
/// <param name="DetectionTime">The confirmation countdown, in ticks.</param>
/// <param name="Flags">What it is doing.</param>
/// <remarks>
/// A camera's lens scans the humans the server has, and its <c>isDetection</c> raises the server's alarm. A client's
/// copy of the level would scan only its own player, so its lamp and beeps would answer to nobody else; the cameras ride
/// the snapshots instead, in the level's own order, and a client's lens is posed by them.
/// </remarks>
public readonly record struct ReplicatedCamera(ushort Alpha, ushort Gamma, byte DetectionTime, ReplicatedCameraCondition Flags)
{
    /// <summary>The most a snapshot carries.</summary>
    public const int MaxPerSnapshot = 128;

    /// <summary>Writes one.</summary>
    /// <param name="writer">Where to.</param>
    public void Write(BitWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteBits(Alpha, 16);
        writer.WriteBits(Gamma, 16);
        writer.WriteBits(DetectionTime, 8);
        writer.WriteBits((byte)Flags, 8);
    }

    /// <summary>Reads one.</summary>
    /// <param name="reader">Where from.</param>
    /// <returns>It.</returns>
    public static ReplicatedCamera Read(ref BitReader reader) => new(
        (ushort)reader.ReadBits(16),
        (ushort)reader.ReadBits(16),
        (byte)reader.ReadBits(8),
        (ReplicatedCameraCondition)reader.ReadBits(8));
}

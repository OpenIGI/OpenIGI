using OpenIGI.Core.Numerics;

namespace OpenIGI.Engine.Audio;

/// <summary>
/// One entry of the sound script: the join between the name game code says and the sound the bank holds.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing in the game names a sound file.</b> Every call site names a <i>definition</i>, and the
/// definition names the bank key. Missing that indirection makes every hook wrong in the same way —
/// a dying guard asks for <c>ai_death_1</c>, which is not a bank key and never was; the definition
/// maps it to <c>AI_DEATH_01</c>.
/// </para>
/// <para>
/// Built by <c>DefineSound</c> (<c>0x4E8C60</c>) into a <c>SoundDefSound</c> task of 304 bytes.
/// The argument slots were recovered from that function's stack frame, and every one of the thirteen
/// lands on a field: <see cref="Name"/> at <c>+223</c>, <see cref="BankKey"/> at <c>+255</c>,
/// <see cref="Offset"/> at <c>+160</c>, <see cref="Volume"/> at <c>+196</c>,
/// <see cref="MinimumDistanceMetres"/> at <c>+208</c>, <see cref="MaximumDistanceMetres"/> at
/// <c>+212</c>, the three parameter indices at <c>+184</c>/<c>+188</c>/<c>+192</c> and
/// <see cref="Loop"/> at <c>+220</c>.
/// </para>
/// <para>
/// <c>DefineGraph</c> and <c>DefineTriggerOnce</c> share arguments 0 to 7 with <c>DefineSound</c>
/// exactly — same order, same types — and diverge only afterwards, so those fields are read for all
/// three kinds. What each adds is recorded on <see cref="SoundDefinitionKind"/> and not yet modelled.
/// </para>
/// </remarks>
public sealed record SoundDefinition
{
    /// <summary>Gets the name game code and scripts say. Argument 0, field <c>+223</c>.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the key the sound bank is searched for. Argument 1, field <c>+255</c>.</summary>
    /// <remarks>Empty for a <see cref="SoundDefinitionKind.Group"/>, which names no sound of its own.</remarks>
    public required string BankKey { get; init; }

    /// <summary>Gets which native produced this entry.</summary>
    public required SoundDefinitionKind Kind { get; init; }

    /// <summary>
    /// Gets where the sound sits, in engine units, <b>as an offset in its emitter's frame</b>.
    /// Arguments 2 to 4, fields <c>+160</c>/<c>+168</c>/<c>+176</c>.
    /// </summary>
    /// <remarks>
    /// Not a world position. <c>0x4E8DC0</c> asks the parent task for a position and orientation every
    /// tick, rotates this offset by the orientation and adds the position — which is exactly why a
    /// sound started on a human follows that human with no work at the call site.
    /// </remarks>
    public Vector3d Offset { get; init; }

    /// <summary>Gets the gain before distance. Argument 5, field <c>+196</c>, clamped to <c>[0,1]</c> at use.</summary>
    public float Volume { get; init; } = 1f;

    /// <summary>Gets the distance within which the sound plays at full gain, <b>in metres</b>. Argument 6.</summary>
    /// <remarks>
    /// Metres among engine units, the same exception the nav graph's node radius makes:
    /// <c>0x496E60</c> begins by multiplying both distances by 4096 before comparing them against a
    /// distance in engine units.
    /// </remarks>
    public float MinimumDistanceMetres { get; init; }

    /// <summary>Gets the distance beyond which the sound is silent, <b>in metres</b>. Argument 7.</summary>
    public float MaximumDistanceMetres { get; init; } = 1f;

    /// <summary>Gets the index of the emitter parameter overriding <see cref="Volume"/>, or <c>-1</c>.</summary>
    /// <remarks>
    /// Arguments 8 to 10 index the parameter array the parent hands back from message <c>0x4E6610</c>,
    /// overriding volume, pitch and the bank key itself. All three are <c>-1</c> in every retail
    /// definition, so nothing reads them yet.
    /// </remarks>
    public int VolumeParameterIndex { get; init; } = -1;

    /// <summary>Gets the index of the emitter parameter overriding pitch, or <c>-1</c>.</summary>
    public int PitchParameterIndex { get; init; } = -1;

    /// <summary>Gets the index of the emitter parameter overriding <see cref="BankKey"/>, or <c>-1</c>.</summary>
    public int BankKeyParameterIndex { get; init; } = -1;

    /// <summary>Gets whether the sound loops until stopped. Argument 12, field <c>+220</c>.</summary>
    /// <remarks>
    /// The per-tick update <c>0x4E8840</c> deletes a finished one-shot but never a loop, so a looping
    /// sound is ended only by whoever started it. Read for <see cref="SoundDefinitionKind.Sound"/>
    /// alone — the other natives have no such argument in that slot.
    /// </remarks>
    public bool Loop { get; init; }

    /// <summary>Gets the nested definitions, for a <see cref="SoundDefinitionKind.Group"/>.</summary>
    public IReadOnlyList<SoundDefinition> Children { get; init; } = [];

    /// <summary>Gets whether this definition names something that can be played on its own.</summary>
    /// <remarks>
    /// <c>0x4E7200</c> rejects a group, a graph and a trigger outright — only a plain
    /// <see cref="SoundDefinitionKind.Sound"/> reaches the mixer down the direct path.
    /// </remarks>
    public bool IsPlayable => Kind == SoundDefinitionKind.Sound;
}

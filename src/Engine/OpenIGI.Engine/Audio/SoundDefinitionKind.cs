namespace OpenIGI.Engine.Audio;

/// <summary>
/// Which of the sound script's four <c>Define*</c> natives produced a <see cref="SoundDefinition"/>.
/// </summary>
/// <remarks>
/// Each native builds a different task class, and <c>0x4E7200</c> — the engine's direct-play path —
/// refuses every kind but <see cref="Sound"/>, so the kind decides what a name may be used for.
/// </remarks>
public enum SoundDefinitionKind
{
    /// <summary>A plain sound: <c>DefineSound</c> (<c>0x4E8C60</c>), building a <c>SoundDefSound</c>.</summary>
    Sound,

    /// <summary>A named container of other definitions: <c>DefineGroup</c> (<c>0x4EBA90</c>).</summary>
    Group,

    /// <summary>
    /// A sound whose volume and pitch follow a driven parameter: <c>DefineGraph</c> (<c>0x4EAD20</c>).
    /// </summary>
    Graph,

    /// <summary>A sound that fires once on a condition: <c>DefineTriggerOnce</c> (<c>0x4E9EC0</c>).</summary>
    TriggerOnce,
}

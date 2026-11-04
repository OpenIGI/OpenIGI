using OpenIGI.Scripting.Qsc;

namespace OpenIGI.Game.Config;

/// <summary>
/// Writes a configuration back out as a compiled script, in the format the original saves.
/// </summary>
/// <remarks>
/// <para>
/// Saving settings means emitting bytecode, because the save file <em>is</em> a script — see
/// <see cref="ConfigScript"/>. The body is one call per setting, in the order a retail file uses.
/// It is built as a QSC syntax tree and compiled by <see cref="QscCompiler"/>, the same compiler every
/// other script goes through, so the call shape, the shortest-push literals and the terminating
/// <c>BRK</c> are the ones the original's compiler produces.
/// </para>
/// <para>
/// The <c>BRK</c> matters: it is the only thing that stops the interpreter. The original's loop reads
/// <c>code[pc]</c> with no bounds check (<c>0x4BCFD0</c>), so a script that merely runs out of
/// instructions carries on into whatever follows its code section.
/// </para>
/// </remarks>
public static class ConfigScriptWriter
{
    private const int ScriptVersion = 5;

    private const int StackSizeBytes = 2048;

    private const int MaxLocals = 256;

    /// <summary>Emits a configuration as a compiled script.</summary>
    /// <param name="config">The configuration to save.</param>
    /// <returns>The file's bytes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="config"/> is <see langword="null"/>.</exception>
    public static byte[] Write(GameConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        List<QscStatement> body = [];
        void Call(string function, params QscExpression[] arguments) =>
            body.Add(new QscExpressionStatement(new QscCall(function, arguments)));

        // The active profile index, which the loader applies after every GOPlayer has been read.
        Call("GOStart", Number(config.ActiveIndex));
        Call("GOContentControlPW", Text(config.ContentControlPassword));
        Call("GOControlsVersion", Number(config.ControlsVersion));

        foreach (PlayerProfile profile in config.Profiles)
        {
            Call("GOPlayer", Text(profile.Name));
            Call("GOActiveMission", Number(profile.ActiveMission));
            Call("GOInMouInv", Number(profile.InvertMouse ? 1 : 0));
            Call("GOInMouSens", Real(profile.MouseSensitivity));

            foreach (InputBinding binding in profile.Bindings)
            {
                Call("GOInRemap", Symbol(binding.Action), Symbol(binding.Device), Symbol(binding.Key));
            }

            Call("GOGfxDisp", Number(profile.DisplayMode));
            Call("GOGfxDevice", Text(profile.DeviceGuid));
            Call("GOGfxGamma", Real(profile.Gamma));
            Call("GOGfxPerformance", Number(profile.PerformanceFlags));
            Call("GOGameLang", Number(profile.Language));
            Call("GOGameDiff", Number(profile.Difficulty));
            Call("GOIsBlood", Number(profile.BloodEnabled ? 1 : 0));
            Call("GOSoundSpeech", Real(profile.SpeechVolume));
            Call("GOSoundMusic", Real(profile.MusicVolume));
            Call("GOSoundFX", Real(profile.SoundEffectsVolume));
        }

        QscScript script = new(body, ScriptVersion)
        {
            RequestedStackSizeBytes = StackSizeBytes,
            RequestedMaxLocals = MaxLocals,
        };
        return QscCompiler.Compile(script);
    }

    // A value too wide for a word takes the full 32-bit immediate rather than a float: a retail file carries
    // values above 2^24 (a saved display mode of 825437497 would come back as 825437504 through a float).
    private static QscIntLiteral Number(int value) => new(value);

    private static QscRealLiteral Real(float value) => new(value);

    private static QscStringLiteral Text(string? value) => new(value ?? string.Empty);

    private static QscSymbol Symbol(string? name) => new(name ?? string.Empty);
}

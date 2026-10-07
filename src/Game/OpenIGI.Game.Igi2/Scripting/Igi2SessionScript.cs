using OpenIGI.Core.Diagnostics;
using OpenIGI.Engine.Resources;
using OpenIGI.Scripting.Qvm;
using OpenIGI.Scripting.Runtime;

namespace OpenIGI.Game.Igi2.Scripting;

/// <summary>Runs one of the compiled scripts the session reads before any level.</summary>
/// <remarks>
/// <c>Flow_CreateHandler</c> (<c>igi2 0x401600</c>) runs each of them through <c>Script_Execute</c> (<c>0x40D710</c>) and
/// treats one it cannot load as fatal.
/// </remarks>
public static class Igi2SessionScript
{
    /// <summary>Loads a compiled script and runs it to the end.</summary>
    /// <param name="resources">Where the script is read from.</param>
    /// <param name="natives">The script namespace it calls into.</param>
    /// <param name="script">The resource name.</param>
    /// <exception cref="EngineFatalException">The script is missing, malformed or faults.</exception>
    public static void Run(ResourceRegistry resources, NativeRegistry natives, string script)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(natives);
        ArgumentException.ThrowIfNullOrEmpty(script);

        if (!resources.TryLoad(script, out ReadOnlyMemory<byte> bytes))
        {
            throw new EngineFatalException($"Couldn't load script: {script}");
        }

        QvmProgram program;
        try
        {
            program = QvmProgram.Read(bytes);
        }
        catch (QvmFormatException ex)
        {
            throw new EngineFatalException($"Couldn't load script: {script}", ex);
        }

        QvmInterpreter interpreter = new(program, natives);
        QvmError error = interpreter.Run();
        if (error != QvmError.None)
        {
            throw new EngineFatalException($"{script} faulted with {error}: {interpreter.ErrorMessage}");
        }
    }
}

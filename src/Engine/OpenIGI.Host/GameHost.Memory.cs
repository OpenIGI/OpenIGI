using OpenIGI.Host.Services;

namespace OpenIGI.Host;

public sealed partial class GameHost
{
    /// <summary>The category memory reports are logged under.</summary>
    public const string MemoryLogCategory = MemoryDiagnostics.LogCategory;

    // Where the game's memory is; made with the device.
    private readonly MemoryDiagnostics _memory;
}

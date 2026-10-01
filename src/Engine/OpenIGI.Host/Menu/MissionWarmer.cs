using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Threading;
using OpenIGI.Engine.Titles;
using OpenIGI.Game.World;

namespace OpenIGI.Host.Menu;

/// <summary>
/// Warms a mission's data on a worker thread when the campaign carousel hovers it, so the level it will
/// load is already in the operating system's file cache by the time the player presses Play.
/// </summary>
/// <remarks>
/// <para>
/// True two-scene streaming — a second live world cross-fading in behind the menu — is more than the
/// single-scene render loop offers, and mutating the task tree from a worker is not safe. What is both
/// safe and useful is this: on hover, cancel the previous warm and start reading the hovered mission's
/// files off disk on a thread-pool thread. It touches no shared state (plain <see cref="File"/> reads),
/// never blocks the UI thread, and turns the eventual synchronous load into a warm-cache one. The visible
/// hand-off is the shell's own cross-fade; the data is what streams.
/// </para>
/// <para>
/// The read is cancellable and best-effort: a hover that moves on before the read finishes cancels it,
/// and any I/O error is swallowed — a warm that fails just means the real load pays full price.
/// </para>
/// </remarks>
public sealed class MissionWarmer(
    string gamePath,
    IMissionCatalogue<MissionDescriptor> missions,
    IIgiLogger logger,
    IWorkScheduler work) : IDisposable
{
    private readonly Lock _gate = new();
    private CancellationTokenSource? _cancellation;
    private int _warming = -1;

    /// <summary>Begins warming a mission's files, cancelling any warm already in progress.</summary>
    /// <param name="missionId">The mission to warm.</param>
    public void Warm(int missionId)
    {
        // Only worth anything where it overlaps the menu. Inline, it would read a mission's every file
        // inside the frame that asked, for a load that reads them again anyway.
        if (!work.IsConcurrent)
        {
            return;
        }

        if (!missions.TryGet(missionId, out MissionDescriptor? descriptor) || descriptor is null)
        {
            return;
        }

        CancellationTokenSource cancellation;
        lock (_gate)
        {
            if (_warming == missionId)
            {
                return; // already warming this one
            }

            _cancellation?.Cancel();
            _cancellation?.Dispose();
            cancellation = _cancellation = new CancellationTokenSource();
            _warming = missionId;
        }

        string directory = Path.Combine(gamePath, descriptor.MissionDirectory);
        CancellationToken token = cancellation.Token;
        _ = work.Run(() => WarmDirectory(directory, token), token);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_gate)
        {
            _cancellation?.Cancel();
            _cancellation?.Dispose();
            _cancellation = null;
        }
    }

    private void WarmDirectory(string directory, CancellationToken token)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        try
        {
            byte[] buffer = new byte[64 * 1024];
            foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                token.ThrowIfCancellationRequested();
                using FileStream stream = File.OpenRead(file);
                while (stream.Read(buffer, 0, buffer.Length) > 0)
                {
                    token.ThrowIfCancellationRequested();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The hover moved on; nothing to do.
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.Warning("Menu", $"Could not warm mission data at {directory}: {ex.Message}");
        }
    }
}

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
/// <para>
/// <see cref="WarmAll"/> does the same for every mission, one after another, once the menu is up: the
/// first backdrop of each mission then reads from memory however far down the list the player jumps. A
/// hover outranks it -- the sweep stops while the hovered mission is read and picks up after it.
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

    // The background sweep: what it has left to read, what it has read in full, and how to stop it.
    private int[] _sweep = [];
    private readonly HashSet<int> _swept = [];
    private CancellationTokenSource? _sweepCancellation;
    private bool _hovering;

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

            // The hovered mission is the one about to be built; the sweep waits for it.
            _hovering = true;
            _sweepCancellation?.Cancel();
        }

        string directory = Path.Combine(gamePath, descriptor.MissionDirectory);
        CancellationToken token = cancellation.Token;
        _ = work.Run(
            () =>
            {
                WarmDirectory(directory, skipSounds: false, token);
                lock (_gate)
                {
                    if (ReferenceEquals(_cancellation, cancellation))
                    {
                        _hovering = false;
                    }
                }

                ResumeSweep();
            },
            token);
    }

    /// <summary>Reads every listed mission's files in the background, one mission at a time.</summary>
    /// <param name="missionIds">The missions, in the order to read them.</param>
    /// <remarks>
    /// For the menu, once it is up: about a quarter of a gigabyte of reads that only fill the operating
    /// system's cache, so every backdrop after the first reads its packs from memory. Sounds are left out
    /// -- a backdrop never mounts them, and a hover still reads them for the mission about to be played.
    /// Nothing here holds the bytes, so the process does not grow by them.
    /// </remarks>
    public void WarmAll(IEnumerable<int> missionIds)
    {
        ArgumentNullException.ThrowIfNull(missionIds);
        if (!work.IsConcurrent)
        {
            return;
        }

        lock (_gate)
        {
            _sweep = [.. missionIds];
        }

        ResumeSweep();
    }

    /// <summary>Starts, or restarts, the sweep over whatever it has not read yet, unless a hover is reading.</summary>
    private void ResumeSweep()
    {
        CancellationToken token;
        int[] remaining;
        lock (_gate)
        {
            if (_hovering)
            {
                return;
            }

            remaining = [.. _sweep.Where(id => !_swept.Contains(id))];
            if (remaining.Length == 0)
            {
                return;
            }

            _sweepCancellation?.Cancel();
            _sweepCancellation?.Dispose();
            _sweepCancellation = new CancellationTokenSource();
            token = _sweepCancellation.Token;
        }

        _ = work.Run(
            () =>
            {
                foreach (int missionId in remaining)
                {
                    if (token.IsCancellationRequested)
                    {
                        return;
                    }

                    if (!missions.TryGet(missionId, out MissionDescriptor? descriptor) || descriptor is null)
                    {
                        continue;
                    }

                    if (WarmDirectory(Path.Combine(gamePath, descriptor.MissionDirectory), skipSounds: true, token))
                    {
                        lock (_gate)
                        {
                            _swept.Add(missionId);
                        }
                    }
                }
            },
            token);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_gate)
        {
            _cancellation?.Cancel();
            _cancellation?.Dispose();
            _cancellation = null;

            _sweepCancellation?.Cancel();
            _sweepCancellation?.Dispose();
            _sweepCancellation = null;
            _sweep = [];
        }
    }

    /// <summary>Reads a directory's files through, for the cache.</summary>
    /// <returns>Whether every file was read.</returns>
    private bool WarmDirectory(string directory, bool skipSounds, CancellationToken token)
    {
        if (!Directory.Exists(directory))
        {
            return false;
        }

        try
        {
            byte[] buffer = new byte[64 * 1024];
            foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                token.ThrowIfCancellationRequested();
                if (skipSounds && IsUnderSounds(directory, file))
                {
                    continue;
                }

                using FileStream stream = File.OpenRead(file);
                while (stream.Read(buffer, 0, buffer.Length) > 0)
                {
                    token.ThrowIfCancellationRequested();
                }
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            // The hover moved on; nothing to do.
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.Warning("Menu", $"Could not warm mission data at {directory}: {ex.Message}");
            return false;
        }
    }

    /// <summary>Whether a file sits in a mission's sound banks, which a backdrop never reads.</summary>
    private static bool IsUnderSounds(string directory, string file)
    {
        string relative = Path.GetRelativePath(directory, file);
        int separator = relative.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]);
        return separator > 0
            && relative.AsSpan(0, separator).Equals("sounds", StringComparison.OrdinalIgnoreCase);
    }
}

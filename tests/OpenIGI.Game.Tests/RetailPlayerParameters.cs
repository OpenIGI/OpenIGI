using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Player;
using OpenIGI.TestSupport;
using Xunit;
using OpenIGI.Game.Igi1.Player;

namespace OpenIGI.Game.Tests;

/// <summary>
/// The retail player tuning, run once through the real script VM.
/// </summary>
/// <remarks>
/// <c>humanplayer/humanplayer.qvm</c> is 1457 bytes and is the whole of the player's tuning data, so
/// more than one suite wants it and none of them wants to run the VM again to get it.
/// </remarks>
internal static class RetailPlayerParameters
{
    private static readonly Lock Gate = new();
    private static HumanPlayerParameters? _cached;

    /// <summary>The 43 values, converted into engine units.</summary>
    public static HumanPlayerParameters Value
    {
        get
        {
            lock (Gate)
            {
                if (_cached is not null)
                {
                    return _cached;
                }

                VirtualFileSystem fileSystem = new();
                fileSystem.RegisterDevice(new LocalFileDevice(GameData.RequireRoot()));
                ResourceRegistry resources = new(fileSystem);

                GameLoop loop = new(new ManualTimeSource());
                HumanPlayerConfig config = new();
                config.Register(loop.Natives);
                config.Load(resources, loop.Natives);

                Assert.NotNull(config.Parameters);
                _cached = config.Parameters;
                return _cached;
            }
        }
    }
}

using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.TestSupport;

namespace OpenIGI.Game.Tests;

/// <summary>
/// The retail <c>humanplayer.qvm</c>, run once — the tuning, the weapon cycle and the categories.
/// </summary>
internal static class RetailHumanPlayerConfig
{
    private static readonly Lock Gate = new();
    private static HumanPlayerConfig? _cached;

    /// <summary>Everything the shipped script publishes.</summary>
    public static HumanPlayerConfig Value
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

                // ORDER MATTERS. WEAPON_ID_* and AMMO_ID_* are pattern-registered symbols: they get
                // their values as the weapon roster and the ammunition table are read. Running
                // humanplayer.qvm first leaves every id in its weapon cycle a placeholder, and the
                // cycle would then name weapons nobody carries. The original loads both tables at
                // start-up, well before HumanPlayer_LoadParameters.
                WeaponTypeRegistry roster = new();
                roster.Register(loop.Natives);
                roster.Load(fileSystem, resources, loop.Natives);

                AmmoTypeRegistry ammunition = new();
                ammunition.Register(loop.Natives);
                ammunition.Load(fileSystem, resources, loop.Natives);

                HumanPlayerConfig config = new();
                config.Register(loop.Natives);
                config.Load(resources, loop.Natives);

                _cached = config;
                return _cached;
            }
        }
    }
}

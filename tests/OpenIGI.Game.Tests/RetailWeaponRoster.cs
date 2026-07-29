using OpenIGI.Engine.Flow;
using OpenIGI.Engine.Resources;
using OpenIGI.Engine.Time;
using OpenIGI.Engine.Vfs;
using OpenIGI.Game.Weapons;
using OpenIGI.TestSupport;

namespace OpenIGI.Game.Tests;

/// <summary>
/// The retail weapon roster, loaded once from the twenty-four <c>weapons/*/weapon.qvm</c> scripts.
/// </summary>
internal static class RetailWeaponRoster
{
    private static readonly Lock Gate = new();
    private static WeaponTypeRegistry? _cached;

    /// <summary>Every weapon the game ships.</summary>
    public static WeaponTypeRegistry Value
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

                WeaponTypeRegistry roster = new();
                roster.Register(loop.Natives);
                roster.Load(fileSystem, resources, loop.Natives);

                _cached = roster;
                return _cached;
            }
        }
    }
}

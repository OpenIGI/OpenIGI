using OpenIGI.Core.Numerics;
using OpenIGI.Game.Physics;

namespace OpenIGI.Game.Igi2.Weapons;

/// <content>Rounds that do no harm: a co-op client's copies of another machine's.</content>
public sealed partial class Igi2GunShot
{
    // What a harmless round does to what it struck: a human bleeds where it went in, a pane breaks as the server's broke, and
    // nothing else is touched -- the server's round did the harm, and its state comes to this client by its own way.
    private static void StrikeHarmlessly(object? struck, WorldIntersection hit, float damage, Vector3f shot, IIgi2WeaponOwner shooter, Igi2GunShotInstant bullet)
    {
        switch (struck)
        {
            case Humans.Igi2Human human when ReferenceEquals(human, shooter):
                break;
            case Humans.Igi2Human human:
                human.CreateFleshEnterEffect(shot, hit.Position, human);
                break;
            case Interaction.Igi2Glass glass:
                glass.WeaponHit(new Igi2WeaponHit(hit, damage, shot, shooter, bullet.WeaponType, bullet.AmmoType));
                break;
        }
    }
}

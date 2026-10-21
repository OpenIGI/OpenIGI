using OpenIGI.Core.Numerics;
using OpenIGI.Formats.Animation;
using OpenIGI.Game.Igi2.Animation;
using OpenIGI.Game.Igi2.Audio;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Flow;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.ViewModel;
using OpenIGI.Game.Igi2.Weapons;
using OpenIGI.Game.Igi2.Systems;
using OpenIGI.Game.Igi2.World;
using OpenIGI.Game.Level;
using OpenIGI.Game.Player;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi2.Player;

/// <summary>The level a bullet flies through: the collision world and the session's materials.</summary>
/// <remarks>Nothing in the collision world is water yet; the boned objects are its humans.</remarks>
internal sealed class Igi2LevelGunShotWorld(IIgi2CollisionQuery collision, Igi2GameMaterials materials, Ai.Runtime.Igi2AiWorld? ai = null)
    : IIgi2GunShotWorld
{
    public void EvaluateGunShot(Igi2GunShot shot, object shooter) => ai?.EvaluateGunShot(shot, shooter);

    public IIgi2CollisionQuery Collision { get; } = collision;

    public Igi2GameMaterials Materials { get; } = materials;

    public bool IsWater(IIgi2Collider struck) => struck is World.Igi2WaterBody;

    public bool IsBoned(IIgi2Collider struck) => struck is Igi2BoneCollider;

    public bool IsHuman(IIgi2Collider struck) => struck is Igi2BoneCollider { Owner: Humans.Igi2Human };
}

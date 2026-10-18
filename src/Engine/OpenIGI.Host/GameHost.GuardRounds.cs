using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Igi1.Ai;
using OpenIGI.Game.Igi1.Player;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;

namespace OpenIGI.Host;

public sealed partial class GameHost
{
    /// <summary>The host's side of a guard's round: the player it holds, and the sound and picture of the hits.</summary>
    /// <param name="host">The host.</param>
    private sealed class GuardRoundHost(GameHost host) : IGuardRoundHost
    {
        public PlayerCamera? Player => host._mission.Player;

        public HumanHealth PlayerHealth => host._mission.PlayerHealth;

        public Random ShotRandom => host._mission.ShotRandom;

        public GameMaterialRegistry? Materials => host._mission.Materials;

        public AmmoTypeRegistry? AmmoTypes => host._mission.AmmoTypes;

        public GameTask? VehicleRoundOwner => host._mission.VehicleRoundOwner;

        public void PlayBulletImpactSound(Vector3d position, bool struckHuman, int surfaceMaterial) =>
            host.PlayBulletImpactSound(position, struckHuman, surfaceMaterial);

        public void ApplyRoundContact(Vector3d impactUnits, GameTask? owner, WorldPlacement? placement, float destructibleDamage) =>
            host.ApplyRoundContact(impactUnits, owner, placement, destructibleDamage);

        public void SpawnImpactBurst(in WorldRayHit worldHit)
        {
            if (host._effects.Particles is { } bursts
                && RenderCVars.ImpactParticles.Value is > 0f and var intensity)
            {
                string material = host._mission.Materials is { } names
                    && names.TryGet(worldHit.Material, out GameMaterial struck)
                    ? struck.Name
                    : string.Empty;
                bursts.SpawnBurst(
                    worldHit.Position,
                    worldHit.Normal,
                    ImpactParticleRenderer.KindOf(material),
                    intensity);
            }
        }

        public void PlayPlayerGrunt() => host._igi1.Harm.PlayPlayerGrunt();

        public void InstallPlayerHitShake() => host.InstallPlayerHitShake();

        public void FlashDamage(Vector3f viewIncoming) => host._mission.DamageFlash.Hit(viewIncoming, host._mission.SoundRandom.NextSingle());

        public void BeginPlayerDeath(Vector3d from, int zone, int bone, WeaponCategory category) =>
            host.BeginPlayerDeath(from, zone, bone, category);
    }
}

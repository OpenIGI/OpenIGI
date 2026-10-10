using OpenIGI.Core.Diagnostics;
using OpenIGI.Core.Numerics;
using OpenIGI.Game;
using OpenIGI.Game.Player;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;
using OpenIGI.Presentation.Igi1;
using OpenIGI.Presentation.Rendering;
using OpenIGI.Rendering;

namespace OpenIGI.Host;

/// <summary>
/// The soldiers' rounds as the original marches them: each gun's <c>GunShot</c> task casts its rounds after the
/// level's walk, through the world, the soldiers and the player, drawing from the level's one stream.
/// </summary>
internal sealed partial class GameHost
{
    // The windows that keep a burst from stacking impact sounds, shared by every gun's march.
    private readonly GunShotSoundWindows _gunShotSounds = new();

    // The pitch a sound definition plays at; the original's sound layer draws nothing from the level stream.
    private readonly IgiRandom _impactPitchRandom = new(0x2F1Du);

    // The placed-object and terrain cast the level was built with, which a round's passes go through.
    private WorldTraceCast? _roundTrace;

    /// <summary>Hands the soldiers the world their guns' <c>GunShot</c> tasks march through.</summary>
    private void AttachGunShotWorld(WorldTraceCast worldTrace)
    {
        _roundTrace = worldTrace;
        _gunShotSounds.Reset();
        _impactEffects.UseRandom(_soundRandom);
        _soldiers?.SetGunShotWorld(new SoldierGunShotWorld(this), _gunShotSounds, _impactEffects.DetonatePendingChips);
    }

    /// <summary>The streak a soldier's round draws, to the first surface along it: visual only.</summary>
    private void SpawnSoldierStreak(Vector3d from, Vector3d at, WeaponType weapon)
    {
        if (_player is not { } player || !GameplayCVars.Ballistics.Value || !GameplayCVars.BallisticsAiTracers.Value)
        {
            return;
        }

        Vector3d to = new(
            from.X + ((at.X - from.X) * 4.0),
            from.Y + ((at.Y - from.Y) * 4.0),
            from.Z + ((at.Z - from.Z) * 4.0));
        Vector3d end = _roundTrace is { } trace
            && trace(new WorldTraceRequest(from, to, WorldTraceMask.World), out WorldTraceHit surface)
            ? surface.Position
            : to;
        Vector3d line = end - from;
        double length = line.Length;
        if (length <= 1e-6)
        {
            return;
        }

        AmmoType? ammo = _ammoTypes is { } table && table.TryGetByCalibre(weapon.CalibreId, out AmmoType found)
            ? found
            : null;
        player.Ballistics.SpawnVisual(
            from,
            new Vector3f((float)(line.X / length), (float)(line.Y / length), (float)(line.Z / length)),
            end,
            weapon,
            ammo);
    }

    /// <summary>One call of the player's hit handler (<c>0x412440</c>) for a soldier's round.</summary>
    /// <returns>Whether it killed the player.</returns>
    private bool StrikePlayerWithRound(in HumanHit struck, Vector3d from, Vector3d to, WeaponType weapon, float damageFactor)
    {
        if (_player is null)
        {
            return false;
        }

        _playerHealth.Apply(struck.Zone, damageFactor);
        _guardHits++;

        // The handler turns the round into camera space before it writes the four corner alphas.
        Vector3f incoming = new((float)(to.X - from.X), (float)(to.Y - from.Y), (float)(to.Z - from.Z));
        Vector3f viewIncoming = _player.Camera.Orientation.Transposed().Transform(incoming);

        // 0x412440's order: the grunt, then the shake, then the flash, each on its own draw.
        if (!_playerHealth.IsDead)
        {
            PlayPlayerGrunt();
        }

        InstallPlayerHitShake();
        _damageFlash.Hit(viewIncoming, _soundRandom.NextSingle());

        // Every connecting round shoves the player 5 km/h along its travel.
        if (!_playerHealth.IsDead)
        {
            double length = Math.Sqrt((incoming.X * (double)incoming.X) + (incoming.Y * (double)incoming.Y)
                + (incoming.Z * (double)incoming.Z));
            if (length > 1e-6)
            {
                _player.Body.AddHitShove(
                    (float)(incoming.X / length * PlayerBody.HitShoveUnitsPerTick),
                    (float)(incoming.Y / length * PlayerBody.HitShoveUnitsPerTick));
            }
        }

        _logger.Information(
            "Weapon",
            $"A guard's {weapon.ScriptId} hit the player in the {HumanDamage.ZoneName(struck.Zone)} "
                + $"({_guardHits} of {_guardRounds} round(s); {_playerHealth.RemainingFraction:P0} left).");
        if (_playerHealth.IsDead)
        {
            BeginPlayerDeath(from, struck.Zone, struck.Bone, weapon.Category);
        }

        return _playerHealth.IsDead;
    }

    /// <summary>What a contact on a soldier's body carries to the strike.</summary>
    private sealed record SoldierContact(SoldierView View, HumanHit Hit);

    /// <summary>What a contact on the player's body carries to the strike.</summary>
    private sealed record PlayerContact(HumanHit Hit);

    /// <summary>The world a soldier's round is cast through: placed objects and terrain, the soldiers, the player.</summary>
    private sealed class SoldierGunShotWorld(GameHost host) : IGunShotWorld
    {
        public bool Cast(GunShotRound round, Vector3d start, Vector3d limit, out GunShotContact contact)
        {
            contact = default;
            bool found = false;
            double best = double.PositiveInfinity;
            SoldierView? shooter = round.Shooter as SoldierView;
            int side = shooter?.Soldier.Team ?? 1;

            if (host._roundTrace is { } trace
                && trace(
                    new WorldTraceRequest(
                        start, limit, WorldTraceMask.World, IgnoredOwner: host._vehicleRoundOwner, RetailArithmetic: true),
                    out WorldTraceHit surface))
            {
                GameMaterial? row = host._worldMaterials is { } rows && rows.TryGet(surface.Material, out GameMaterial face)
                    ? face
                    : null;
                contact = new GunShotContact(
                    surface.Position,
                    surface.Normal,
                    surface.Distance,
                    surface.IsExit ? null : row,
                    surface.IsExit ? row : null,
                    HasObject: surface.Owner is not null,
                    StruckHuman: false,
                    surface);
                best = surface.Distance;
                found = true;
            }

            if (host._soldiers is { } soldiers
                && soldiers.TryCastRoundAtSoldiers(start, limit, shooter, side, out SoldierView? struck, out HumanHit body)
                && struck is not null
                && body.Distance < best)
            {
                contact = BodyContact(body, new SoldierContact(struck, body));
                best = body.Distance;
                found = true;
            }

            // The player is visible to a round only start another side, and only through the evaluate's two rolls of
            // the shooter's probability: the visit's gate and the narrow contact's (0x45F53D, 0x4D76DA).
            if (host._player is { } player
                && !host._playerHealth.IsDead
                && host._soldiers is { } guards
                && side != guards.PlayerTeam
                && guards.TryCastPlayerBody(start, limit, player.Body.Position, player.Body.Yaw, out IReadOnlyList<HumanBodyCrossing> crossings)
                && crossings.Count > 0
                && crossings[0].Hit.Distance < best
                && host._soundRandom.NextDouble() <= round.Probability
                && host._soundRandom.NextDouble() <= round.Probability)
            {
                HumanBodyCrossing first = crossings[0];
                HumanHit hit = first.Hit with { Material = first.Material, IsExit = !first.Entering };
                contact = BodyContact(hit, new PlayerContact(hit));
                found = true;
            }

            return found;
        }

        public void Strike(GunShotRound round, in GunShotContact contact, Vector3f travel)
        {
            Vector3d end = new(round.Origin.X + travel.X, round.Origin.Y + travel.Y, round.Origin.Z + travel.Z);
            switch (contact.Payload)
            {
                case SoldierContact soldier:
                    host._soldiers?.StrikeWithRound(
                        soldier.View, soldier.Hit, round.Origin, end, round.Weapon, round.DamageFactor, round.Shooter as SoldierView);
                    break;
                case PlayerContact player:
                    host.StrikePlayerWithRound(player.Hit, round.Origin, end, round.Weapon, round.DamageFactor);
                    break;
                case WorldTraceHit surface:
                    host.ApplyRoundContact(surface.Position, surface.Owner, surface.Placement, round.DamageFactor);
                    break;
            }
        }

        public void PlaySound(string definition, Vector3d position)
        {
            if (definition.Length == 0 || !GameplayCVars.BallisticsImpactSounds.Value)
            {
                return;
            }

            host._sounds?.PlayDefinition(definition, position, pitch: FootstepSounds.Pitch(host._impactPitchRandom), priority: 45);
        }

        public AmmoType? AmmunitionOf(WeaponType weapon) =>
            host._ammoTypes is { } table && table.TryGetByCalibre(weapon.CalibreId, out AmmoType found) ? found : null;

        public void Effect(GunShotRound round, in GunShotRecord record)
        {
            bool visible = RenderCVars.ImpactEffects.Value;
            if (record.Contact.Payload is not WorldTraceHit surface)
            {
                host._impactEffects.RunGunShotEffect(
                    record.Contact.Position, record.Contact.Normal, record.Vector, record.Effect, struckHuman: true, visible);
                return;
            }

            if (host._player is { } player && host._worldMaterials is { } table)
            {
                player.Marks.TryAdd(
                    surface.RayHit,
                    BulletMarks.SurfaceOf(surface),
                    table,
                    record.IsEntry,
                    record.ExitCrossing,
                    probe: player.MarkProbe,
                    placement: surface.Placement);
            }

            host._impactEffects.RunGunShotEffect(
                surface.Position, surface.Normal, record.Vector, record.Effect, struckHuman: false, visible);

            // The remaster's own burst beside it, from its own stream.
            if (!record.ExitCrossing
                && host._impactParticles is { } bursts
                && RenderCVars.ImpactParticles.Value is > 0f and var intensity)
            {
                string material = host._worldMaterials is { } names && names.TryGet(surface.Material, out GameMaterial struck)
                    ? struck.Name
                    : string.Empty;
                bursts.SpawnBurst(surface.Position, surface.Normal, ImpactParticleRenderer.KindOf(material), intensity);
            }
        }

        private GunShotContact BodyContact(in HumanHit hit, object payload)
        {
            GameMaterial? row = null;
            if (host._worldMaterials is { } rows)
            {
                if (hit.Material >= 0 && rows.TryGet(hit.Material, out GameMaterial skin))
                {
                    row = skin;
                }
                else if (rows.TryGetByName(BulletImpactSounds.FleshMaterialName, out GameMaterial flesh))
                {
                    row = flesh;
                }
            }

            return new GunShotContact(
                hit.Position,
                hit.Normal,
                hit.Distance,
                hit.IsExit ? null : row,
                hit.IsExit ? row : null,
                HasObject: true,
                StruckHuman: true,
                payload);
        }
    }
}

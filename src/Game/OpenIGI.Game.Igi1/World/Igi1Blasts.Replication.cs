using OpenIGI.Core.Numerics;
using OpenIGI.Engine.Tasks;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Igi1.Weapons;
using OpenIGI.Game.Multiplayer;
using OpenIGI.Game.Weapons;
using OpenIGI.Game.World;

namespace OpenIGI.Game.Igi1.World;

/// <summary>
/// The weapons' blasts as everyone sees them, and the level's wrecks as a co-op server has them: a blast is shown the same
/// whether this game set it off or another's did, and only the game that set it off does its harm.
/// </summary>
public sealed partial class Igi1Blasts
{
    // How much of the bomb's killing radius its scorch spans, either way from its centre.
    private const float BombScorchShare = 0.3f;

    // A flashbang (0x476AC0's type-1 branch): only the flash (0x47E9A0) and explo_flash (0x47E820) -- no fireball and no
    // explo_02_m -- the same burn1.tex, 2048 wide, on what the grenade's line found 2048 below, and the player handler's
    // whiteout (0x413090) measured from this game's own player's eye, with no wall trace. A distant flash writes nothing,
    // so it never turns into a zero-strength (but still scheduled) screen flash.
    private void ShowFlashbang(Vector3d at, float radiusUnits)
    {
        host.PlayDefinition("explo_flash", at, 90);
        impacts.Explosions.Flash(at);
        impacts.ScorchBelow(at, GrenadeScorchDropInUnits, FlashScorchHalfWidthInUnits);
        if (mission.Player is not { } watcher)
        {
            return;
        }

        (float visible, float close) = Flashbang.Effect((float)(watcher.EyeUnits - at).Length, radiusUnits);
        if (visible > 0f || close > 0f)
        {
            mission.FlashbangFlash.Apply(visible, close);
        }
    }

    // The frag (type 0, 0x476AC0): the scorch on whatever lies 2048 below, then an Explosion0 of one FireBall and three
    // fragments at the configured radius, which goes off with explo_02_m and its blast at once.
    private void ShowFrag(Vector3d at, float radiusUnits, float falloffUnits, float damage, Action? harm)
    {
        impacts.ScorchBelow(at, GrenadeScorchDropInUnits, Igi1ImpactEffects.MissileScorchHalfWidthInUnits);
        impacts.Explosions.Spawn(new Igi1ExplosionRequest(
            at, 1, 1, damage, radiusUnits, falloffUnits, GrenadeFragments, GrenadeFireballs, "explo_02_m", harm));
    }

    // A mine is an ExplodeObject (0x481A60): an Explosion0 at its model's magic vertex 0 of one burst and no delay, a damage
    // of 1 over its explosion radius and no falloff, five fragments and one FireBall, explo_proxy, and no scorch.
    private void ShowMine(Vector3d at, float radiusUnits, Action? harm) =>
        impacts.Explosions.Spawn(new Igi1ExplosionRequest(
            at, 1, 0, 1f, radiusUnits, 0f, PlacedMine.Fragments, PlacedMine.Fireballs, PlacedMine.BlastSound, harm));

    // The missile's own impact (0x428390, at 0x42854D) sets off an Explosion0 with explo_03_l -- the launcher's rocket and a
    // vehicle's alike, since both are the same Missile task: the scorch on whatever lies 12288 below, then five FireBalls
    // and ten fragments at 10240. The blast carries the firing weapon's record (0x428390 hands Explosion0 the missile's
    // +0x894), which is what lets a LAW rocket -- and nothing else -- blow a car up.
    /// <summary>
    /// A blast a multiplayer mode set off (search and destroy's bomb): shown and heard as a rocket's, shoving and breaking what
    /// is near it, and sent to the clients as one; the mode itself says who it kills.
    /// </summary>
    /// <param name="at">Its centre, in units.</param>
    /// <param name="radiusUnits">How far it reaches, in units.</param>
    public void ModeBlast(Vector3d at, double radiusUnits) => ModeBlast(at, radiusUnits, sparePlayers: false);

    /// <summary>A mode's blast, optionally leaving the players to the mode's own damage.</summary>
    /// <param name="at">Where, in world units.</param>
    /// <param name="radiusUnits">How far it reaches.</param>
    /// <param name="sparePlayers">Whether the players are left out: a bomb whose wave the mode sweeps over them itself.</param>
    public void ModeBlast(Vector3d at, double radiusUnits, bool sparePlayers)
    {
        float radius = (float)radiusUnits;
        ShowMissile(at, 1f, sparePlayers
            ? () => ExplodeSparingPlayers(at, radius * 0.5f, radius * 0.5f, 1f)
            : () => Explode(at, radius * 0.5f, radius * 0.5f, 1f, WeaponCategory.Grenade));
        ShowBomb(at, radius);
        Record(ReplicatedBlastKind.Bomb, at, default, radius * 0.5f, radius * 0.5f);
    }

    // The bomb over the rocket's fire it is drawn with: the remastered blast, which is the host's to show, and with it a
    // scorch the size of its blast on what lies under it. Where the remaster's blast is off, the rocket's alone.
    private void ShowBomb(Vector3d at, float radiusUnits)
    {
        if (host.BombBlast(at, radiusUnits, mission.Soldiers?.RagdollWorldForCorpses()))
        {
            impacts.ScorchBelow(at, MissileScorchDropInUnits, radiusUnits * BombScorchShare);
        }
    }

    private void ShowMissile(Vector3d at, float damage, Action? harm)
    {
        impacts.ScorchBelow(at, MissileScorchDropInUnits, Igi1ImpactEffects.MissileScorchHalfWidthInUnits);
        impacts.Explosions.Spawn(new Igi1ExplosionRequest(
            at, 1, 1, damage, MissileExplosion.RadiusUnits, MissileExplosion.FalloffUnits,
            MissileExplosion.Fragments, MissileExplosion.Fireballs, "explo_03_l", harm));
    }

    // GunM203's native projectile callback (0x476340) sends the proxy explosion tag, not the hand-grenade fireball, and
    // scorches the face it struck.
    private void ShowM203(Vector3d at, Vector3f normal, float radiusUnits, float falloffUnits, float damage, Action? harm)
    {
        impacts.Scorch(at, normal, Igi1ImpactEffects.M203ScorchHalfWidthInUnits);
        impacts.Explosions.Spawn(new Igi1ExplosionRequest(
            at, 1, 1, damage, radiusUnits, falloffUnits, GrenadeFragments, GrenadeFireballs, "explo_proxy", harm));
    }

    // A co-op server's record of a blast it set off, for every client but the one whose weapon it was to show.
    private void Record(ReplicatedBlastKind kind, Vector3d at, Vector3f normal, float radiusUnits, float falloffUnits) =>
        impacts.RecordBlast(kind, at, normal, radiusUnits, falloffUnits);

    /// <summary>Records where one player's grenades bounced in its last update, for a co-op server's clients to hear.</summary>
    /// <param name="rig">The player.</param>
    public void RecordBounces(Igi1.Player.PlayerCamera rig)
    {
        ArgumentNullException.ThrowIfNull(rig);
        foreach (Vector3d at in rig.Grenades.Bounces)
        {
            impacts.RecordBounce(at);
        }
    }

    /// <summary>Records a proximity mine arming, for a co-op server's clients to hear.</summary>
    /// <param name="at">Where the mine is, in world units.</param>
    public void RecordMineArmed(Vector3d at) => impacts.RecordMineArmed(at);

    /// <summary>Shows a blast another player's game set off: its fire, smoke, scorch and report, and none of its harm.</summary>
    /// <param name="impact">The blast, as the server sent it.</param>
    /// <remarks>
    /// The harm is the server's, which sends what it did -- the wounds, the deaths, the wrecks. A flashbang's whiteout is the
    /// one thing measured here, from this game's own player's eye, as the original's player handler measures it.
    /// </remarks>
    public void ReplayBlast(in ReplicatedImpact impact)
    {
        Vector3d at = impact.Position.ToVector();
        float radius = impact.RadiusUnits;
        float falloff = impact.FalloffUnits;
        switch (impact.Blast)
        {
            case ReplicatedBlastKind.Flashbang:
                ShowFlashbang(at, radius);
                break;
            case ReplicatedBlastKind.Mine:
                ShowMine(at, radius, harm: null);
                break;
            case ReplicatedBlastKind.Missile:
                ShowMissile(at, 1f, harm: null);
                break;
            case ReplicatedBlastKind.Bomb:
                // Recorded with half its reach as radius and half as falloff (ModeBlast), the whole of it shown.
                ShowMissile(at, 1f, harm: null);
                ShowBomb(at, radius + falloff);
                break;
            case ReplicatedBlastKind.M203:
                ShowM203(at, impact.Direction, radius, falloff, 1f, harm: null);
                break;
            default:
                ShowFrag(at, radius, falloff, 1f, harm: null);
                break;
        }
    }

    /// <summary>Shoots out an alarm speaker, siren, light or camera another game's round or blast destroyed.</summary>
    /// <param name="task">Its task.</param>
    /// <returns>Whether it was standing here, and is shot out now.</returns>
    public bool ShootOut(GameTask task)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (mission.AlarmSpeakers?.TryDestroy(task) is not { } speaker)
        {
            return false;
        }

        ShootOutSpeaker(speaker);
        return true;
    }

    /// <summary>Gets the level's wrecks as a co-op server sends them: one bit for each object struck for good.</summary>
    /// <returns>
    /// The destructibles' bits in the level's order (a pane, barrel or charge broken, a car dying or gone, an explosive
    /// object's fuse lit), then the alarm speakers' and cameras' (shot out), then the interactions' (a switch broken); or
    /// <see langword="null"/> for a level with none.
    /// </returns>
    public byte[]? CaptureWrecks()
    {
        IReadOnlyList<DestructibleObject> objects = mission.Destructibles?.Objects ?? [];
        IReadOnlyList<AlarmSpeaker> speakers = mission.AlarmSpeakers?.Speakers ?? [];
        IReadOnlyList<WorldInteraction> switches = mission.Interactions?.Items ?? [];
        int count = objects.Count + speakers.Count + switches.Count;
        if (count == 0)
        {
            return null;
        }

        byte[] bits = new byte[(count + 7) / 8];
        int at = 0;
        foreach (DestructibleObject item in objects)
        {
            Set(bits, at++, DestructibleObjectRegistry.IsStruck(item));
        }

        foreach (AlarmSpeaker speaker in speakers)
        {
            Set(bits, at++, speaker.IsDestroyed);
        }

        foreach (WorldInteraction item in switches)
        {
            Set(bits, at++, item.IsDestroyed);
        }

        return bits;
    }

    /// <summary>
    /// Takes the server's wrecks: whatever it has struck for good and this game has not is struck here, through the same
    /// handlers a round or a blast of this game's own would have reached, so it breaks, burns and blows up as it did there.
    /// </summary>
    /// <param name="bits">The wrecks, as <see cref="CaptureWrecks"/> wrote them.</param>
    /// <returns>How many were struck here.</returns>
    /// <remarks>
    /// Nothing is ever mended: what this game broke a moment before the server's word arrives stays broken, as the server's
    /// copy will be once the same round or blast reaches it.
    /// </remarks>
    public int ApplyWrecks(byte[]? bits)
    {
        if (bits is null)
        {
            return 0;
        }

        int struck = 0;
        int at = 0;
        if (mission.Destructibles is { } registry)
        {
            foreach (DestructibleObject item in registry.Objects)
            {
                if (Get(bits, at++) && DestructibleObjectRegistry.Strike(item) is { } broken)
                {
                    HandleDestructibleImpact(broken);
                    struck++;
                }
            }
        }

        if (mission.AlarmSpeakers is { } speakers)
        {
            foreach (AlarmSpeaker speaker in speakers.Speakers)
            {
                if (Get(bits, at++) && !speaker.IsDestroyed && ShootOut(speaker.Task))
                {
                    struck++;
                }
            }
        }

        if (mission.Interactions is { } interactions)
        {
            foreach (WorldInteraction item in interactions.Items)
            {
                if (Get(bits, at++) && item.WeaponHit())
                {
                    struck++;
                }
            }
        }

        if (struck > 0)
        {
            // A generator struck here loses its power this tick, as one the server's round reached did there.
            mission.Interactions?.SettleGenerators();
        }

        return struck;
    }

    private static void Set(byte[] bits, int index, bool on)
    {
        if (on)
        {
            bits[index >> 3] |= (byte)(1 << (index & 7));
        }
    }

    private static bool Get(byte[] bits, int index) =>
        (index >> 3) < bits.Length && (bits[index >> 3] & (1 << (index & 7))) != 0;
}

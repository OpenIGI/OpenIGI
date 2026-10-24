using OpenIGI.Core.Numerics;
using OpenIGI.Game.Ai;
using OpenIGI.Game.Physics;
using OpenIGI.Game.Igi2.Ai.Runtime;
using OpenIGI.Game.Igi2.Collision;
using OpenIGI.Game.Igi2.Detection;
using OpenIGI.Game.Igi2.Humans;
using OpenIGI.Game.Igi2.Soldiers;

namespace OpenIGI.Game.Igi2.Player;

/// <content>Who is noticing the player, and the objective being worked on, as a heads-up display marks them.</content>
public sealed partial class Igi2TitlePlayer
{
    /// <summary>How far over a watching soldier's eyes he is marked, in world units: 0.45 m.</summary>
    public const double OverEyes = 1843.2;

    private readonly List<(Vector3d Position, float Awareness)> _threats = [];

    /// <summary>
    /// The soldiers noticing the player, each with a point a hand over their eyes, in their posture, and how close they are to
    /// having seen it: from their own sight's lists (<c>AIDetection_RunDetection</c>'s enemies), not anything they cannot know.
    /// </summary>
    /// <returns>The soldiers, refilled each call.</returns>
    /// <remarks>
    /// Only a soldier with the player in plain sight now counts: one making out a figure counts from half towards the whole
    /// the longer it looks, one that has confirmed it counts whole. One that has lost sight of the player, or never had it,
    /// gives the player nothing the player could not know.
    /// </remarks>
    public IReadOnlyList<(Vector3d Position, float Awareness)> Threats()
    {
        _threats.Clear();
        foreach (Igi2Human human in MapHumans)
        {
            if (human is not Igi2HumanSoldier { IsDead: false, Mind: Igi2HumanAi ai } soldier || ReferenceEquals(human, Human))
            {
                continue;
            }

            Igi2AiDetection detection;
            try
            {
                detection = ai.Detection;
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            foreach (Igi2DetectionItem item in detection.Enemies)
            {
                if (!ReferenceEquals(item.Target, Human) || !item.IsFullVisible)
                {
                    continue;
                }

                float awareness = item.IsIdentifying
                    ? 0.5f + (0.4f * Math.Clamp((ai.World.LevelTick() - item.BecameVisibleTick) / 60f, 0f, 1f))
                    : 1f;
                Vector3d eyes = Igi2Human.EyesAt(soldier.Position, soldier.Pose);
                _threats.Add((new Vector3d(eyes.X, eyes.Y, eyes.Z + OverEyes), awareness));
                break;
            }
        }

        return _threats;
    }

    private readonly List<(Vector3d Position, float Strength)> _glints = [];

    /// <summary>The widest turn off a sniper's aim at which his scope still shows its full glint, in radians: twelve degrees.</summary>
    public const float GlintFullAngle = 0.21f;

    /// <summary>The turn off his aim past which it shows none, in radians: twenty-five degrees.</summary>
    public const float GlintNoAngle = 0.44f;

    /// <summary>
    /// The snipers whose scopes point at the player, as a shooter's game marks them: living soldiers holding a scoped rifle
    /// (<see cref="Game.Weapons.ScopedWeapons"/>) whose view points within <see cref="GlintNoAngle"/> of the player's eyes, full
    /// within <see cref="GlintFullAngle"/>, with nothing solid between -- seen or not, as the glass catches the light whether he
    /// has made the player out or not, but never through a wall.
    /// </summary>
    /// <returns>The scopes, each at its sniper's eye, refilled each call.</returns>
    public IReadOnlyList<(Vector3d Position, float Strength)> ScopeGlints()
    {
        _glints.Clear();
        Igi2CollisionWorld world = CollisionWorld;
        Vector3d target = EyePosition;
        foreach (Igi2Human human in MapHumans)
        {
            if (human is not Igi2HumanSoldier { IsDead: false, Mind: Igi2HumanAi ai } soldier || ReferenceEquals(human, Human)
                || !Game.Weapons.ScopedWeapons.IsScoped(soldier.Weapon.Inventory?.Current?.WeaponType.WeaponId))
            {
                continue;
            }

            // Where his view points: the cone's frame looks along its second column.
            Igi2DetectionView view = ai.DetectionView;
            float[] m = view.Orientation;
            Vector3d from = view.Position;
            double dx = target.X - from.X, dy = target.Y - from.Y, dz = target.Z - from.Z;
            double length = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
            if (length < 1.0)
            {
                continue;
            }

            double ahead = ((m[1] * dx) + (m[4] * dy) + (m[7] * dz)) / length;
            float off = (float)Math.Acos(Math.Clamp(ahead, -1.0, 1.0));
            float strength = Math.Clamp((GlintNoAngle - off) / (GlintNoAngle - GlintFullAngle), 0f, 1f);
            if (strength <= 0f)
            {
                continue;
            }

            // What his eyes see through -- glass, leaves -- by the materials his own sight reads, which a guest player in
            // another game's level does not hold for itself.
            Igi2AiDetection detection;
            try
            {
                detection = ai.Detection;
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            WorldLineOfSight sight = new(detection.ClearOf(from, target), target, Materials ?? (IWorldMaterialTransparency)detection.World.Materials)
            {
                Includes = owner => !ReferenceEquals(owner, Human) && !ReferenceEquals(owner, soldier),
            };
            world.LineOfSight(sight, uint.MaxValue);

            // Only a sniper who has the player in his sight's list aims at him: a scope that merely sweeps past an unseen
            // player catches no sun he would see.
            if (!sight.IsBlocked && detection.Enemies.Any(item => ReferenceEquals(item.Target, Human)))
            {
                _glints.Add((ScopeAt(soldier, from, dx / length, dy / length, dz / length), strength));
            }
        }

        return _glints;
    }

    // Where a sniper's scope is: on his line of sight to the player, level with the rifle in his hands -- an aimed scope sits on
    // that line, so the point on it nearest the weapon object is the glass. A body with no rifle in its hands glints at the eye.
    private static Vector3d ScopeAt(Igi2HumanSoldier soldier, Vector3d eye, double dx, double dy, double dz)
    {
        if (ViewModel.Igi2BodyFrame.HeldWeaponAt(soldier, soldier.BodyPose(), soldier.Weapon.Inventory?.Current?.WeaponType.FixedModel) is not { } rifle)
        {
            return eye;
        }

        double along = ((rifle.X - eye.X) * dx) + ((rifle.Y - eye.Y) * dy) + ((rifle.Z - eye.Z) * dz);
        return along <= 0.0 ? rifle : new Vector3d(eye.X + (dx * along), eye.Y + (dy * along), eye.Z + (dz * along));
    }

    /// <summary>How far the soldiers are onto the player, in I.G.I. 2's own terms: from their sight's lists and their alarm.</summary>
    /// <returns>
    /// Detected while a soldier has the player in plain sight and has confirmed him; an alarm while a soldier is alarmed;
    /// suspicious while one is still making out a figure he sees; searching while one keeps the player in his list after
    /// losing sight of him; otherwise none.
    /// </returns>
    public PlayerAlertStatus AlertStatus()
    {
        PlayerAlertStatus status = PlayerAlertStatus.None;
        foreach (Igi2Human human in MapHumans)
        {
            if (human is not Igi2HumanSoldier { IsDead: false, Mind: Igi2HumanAi ai } || ReferenceEquals(human, Human))
            {
                continue;
            }

            if (ai.IsAlarmed && status < PlayerAlertStatus.Alarm)
            {
                status = PlayerAlertStatus.Alarm;
            }

            Igi2AiDetection detection;
            try
            {
                detection = ai.Detection;
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            foreach (Igi2DetectionItem item in detection.Enemies)
            {
                if (!ReferenceEquals(item.Target, Human))
                {
                    continue;
                }

                PlayerAlertStatus seen = !item.IsFullVisible ? PlayerAlertStatus.Searching
                    : item.IsIdentifying ? PlayerAlertStatus.Suspicious
                    : PlayerAlertStatus.Detected;
                if (seen > status)
                {
                    status = seen;
                }
            }
        }

        return status;
    }

    /// <summary>
    /// The objective being worked on: the first of the map computer's not yet done or failed, its words in the game's own text,
    /// and where its map would look (<c>LinkToPosition</c>), where it names a place.
    /// </summary>
    /// <returns>The objective's words and place, or <see langword="null"/> for none.</returns>
    public (string Text, Vector3d? Position)? CurrentObjective()
    {
        if (Human.Computer?.Objectives?.Objectives is not { } objectives)
        {
            return null;
        }

        foreach (Computer.Igi2ComputerObjective objective in objectives)
        {
            if (objective.TextResource.Length == 0 || objective.IsComplete || objective.IsFailed)
            {
                continue;
            }

            string text = Strings?.Get(objective.TextResource) ?? objective.TextResource;
            Vector3d? place = objective.LinkToPosition != -1 && TaskById?.Invoke(objective.LinkToPosition) is Flow.IIgi2CameraTarget target
                ? target.CameraTargetPosition
                : null;
            return (text, place);
        }

        return null;
    }
}

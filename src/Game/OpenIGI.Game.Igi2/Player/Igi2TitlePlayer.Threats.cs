using OpenIGI.Core.Numerics;
using OpenIGI.Game.Igi2.Ai.Runtime;
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

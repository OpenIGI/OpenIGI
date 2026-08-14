using OpenIGI.Core.Numerics;
using OpenIGI.Rendering;

namespace OpenIGI.Game.Player;

/// <summary>One projected enemy rectangle emitted by the binocular detector.</summary>
public readonly record struct EnemyOverlayBox(
    int TargetId,
    float Left,
    float Top,
    float Right,
    float Bottom,
    float DistanceMetres,
    float Alpha);

/// <summary>Projects living soldier contacts into the native ten-slot binocular overlay.</summary>
/// <remarks>
/// The retail detector keeps ten records at <c>Human+0x2C4C</c>, stores the projected screen position,
/// distance and a last-seen timestamp, then draws a target rectangle rather than a point. This helper
/// preserves that presentation contract: contacts are bounded by a standing human's authored body box,
/// sorted nearest-first, and capped at the native ten targets. Occlusion remains owned by the soldier
/// contact provider, so the renderer never invents a target from a dead or missing actor.
/// </remarks>
public static class EnemyDetectorOverlay
{
    /// <summary>Maximum number of detector records retained by the retail task.</summary>
    public const int NativeSlotCount = 10;

    /// <summary>Authored standing-human width used to form the detector rectangle.</summary>
    public const float HumanWidthMetres = 0.72f;

    /// <summary>Authored standing-human height used to form the detector rectangle.</summary>
    public const float HumanHeightMetres = 1.8f;

    /// <summary>Projects contacts and returns the visible native-capacity target boxes.</summary>
    public static IReadOnlyList<EnemyOverlayBox> Project(
        IReadOnlyList<Vector3d> contacts,
        in Camera3D camera,
        in Matrix4f projection,
        int screenWidth,
        int screenHeight,
        int tick)
    {
        ArgumentNullException.ThrowIfNull(contacts);
        if (screenWidth <= 0 || screenHeight <= 0)
        {
            return [];
        }

        List<EnemyOverlayBox> boxes = [];
        for (int index = 0; index < contacts.Count; index++)
        {
            Vector3d feet = contacts[index];
            float halfWidth = HumanWidthMetres * 0.5f;
            float minX = float.PositiveInfinity;
            float minY = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float maxY = float.NegativeInfinity;
            bool visible = false;

            for (int x = -1; x <= 1; x += 2)
            {
                for (int y = -1; y <= 1; y += 2)
                {
                    for (int z = 0; z <= 1; z++)
                    {
                        // CreateModelView expects the model origin in double precision.  Transform
                        // the local body corner after that origin is made camera-relative; feeding
                        // absolute world coordinates through a float matrix produces bad boxes on
                        // the large retail maps.
                        Matrix4f targetProjection = projection * camera.CreateModelView(feet);
                        Vector3f projected = targetProjection.TransformPoint(new Vector3f(
                            x * halfWidth,
                            y * halfWidth,
                            z * HumanHeightMetres));
                        if (!float.IsFinite(projected.X) || !float.IsFinite(projected.Y)
                            || projected.Z < 0f || projected.Z > 1f)
                        {
                            continue;
                        }

                        visible = true;
                        float px = (projected.X * 0.5f + 0.5f) * screenWidth;
                        float py = (1f - (projected.Y * 0.5f + 0.5f)) * screenHeight;
                        minX = MathF.Min(minX, px);
                        minY = MathF.Min(minY, py);
                        maxX = MathF.Max(maxX, px);
                        maxY = MathF.Max(maxY, py);
                    }
                }
            }

            if (!visible || maxX <= minX || maxY <= minY)
            {
                continue;
            }

            double dx = feet.X - camera.Position.X;
            double dy = feet.Y - camera.Position.Y;
            double dz = feet.Z - camera.Position.Z;
            float distance = (float)Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
            // Native slots pulse their active marker while a target is retained. Keep the phase
            // deterministic and shared by both HUD flavours instead of using wall-clock render time.
            float alpha = ((tick / 8) & 1) == 0 ? 1f : 0.72f;
            boxes.Add(new EnemyOverlayBox(index + 1, minX, minY, maxX, maxY, distance, alpha));
        }

        return boxes
            .OrderBy(box => box.DistanceMetres)
            .Take(NativeSlotCount)
            .ToArray();
    }
}

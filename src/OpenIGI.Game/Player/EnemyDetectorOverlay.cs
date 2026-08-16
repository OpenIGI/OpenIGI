using OpenIGI.Core.Numerics;
using OpenIGI.Rendering;

namespace OpenIGI.Game.Player;

#pragma warning disable CS1591

/// <summary>Colour class the native detector paints a contact rectangle with.</summary>
/// <remarks>
/// <c>FUN_00471B60</c> selects the marker colour from two entity flags before submitting the strip to
/// <c>FUN_004B5860</c>: the <c>+3317</c> non-collidable flag wins first and yields blue
/// <c>(0,0,128)</c>; otherwise <c>Team != 1</c> yields green <c>(0,128,0)</c>; otherwise a
/// team-1 hostile is red <c>(64,0,0)</c>. The priority order is retained here.
/// </remarks>
public enum BinocularMarkerColor
{
    /// <summary>Team 1 — the retail hostile red rectangle.</summary>
    Hostile,

    /// <summary>Any team other than 1 — the retail green rectangle.</summary>
    Team,

    /// <summary>The <c>+3317</c> non-collidable flag — the retail blue rectangle.</summary>
    Disabled,
}

/// <summary>One projected enemy rectangle emitted by the native binocular detector.</summary>
public readonly record struct EnemyOverlayBox(
    int TargetId,
    float Left,
    float Top,
    float Right,
    float Bottom,
    float DistanceMetres,
    float Alpha,
    float RotationRadians = 0f,
    float PulseScale = 1f,
    BinocularMarkerColor Marker = BinocularMarkerColor.Hostile)
{
    /// <summary>
    /// Clips the projected rectangle to the display viewport, matching the native status-display
    /// handler's per-scanline bounds clamps. Returns <see langword="false"/> when no visible area
    /// remains.
    /// </summary>
    public bool TryClipToViewport(int viewportWidth, int viewportHeight, out EnemyOverlayBox clipped)
    {
        clipped = default;
        if (viewportWidth <= 0 || viewportHeight <= 0
            || !float.IsFinite(Left) || !float.IsFinite(Top)
            || !float.IsFinite(Right) || !float.IsFinite(Bottom)
            || Right <= Left || Bottom <= Top)
        {
            return false;
        }

        float left = Math.Clamp(Left, 0f, viewportWidth);
        float top = Math.Clamp(Top, 0f, viewportHeight);
        float right = Math.Clamp(Right, 0f, viewportWidth);
        float bottom = Math.Clamp(Bottom, 0f, viewportHeight);
        if (right <= left || bottom <= top)
        {
            return false;
        }

        clipped = this with { Left = left, Top = top, Right = right, Bottom = bottom };
        return true;
    }
}

/// <summary>Managed equivalent of the retail ten-slot binocular detector.</summary>
/// <remarks>
/// <c>FUN_00471B60</c> scans living, active humans and calls <c>FUN_00471F60</c>; the latter owns ten
/// records at <c>Human+0x2C44</c>, each 0x2C bytes wide. This class keeps that stateful reuse and
/// three-second retention behavior instead of rebuilding a transient list every render frame.
/// </remarks>
public sealed class EnemyDetectorOverlay
{
    public const int SlotCount = 10;
    public const int SlotStrideBytes = 0x2C;
    public const int NativeSlotTableOffset = 0x2C44;
    public const int NativeDistanceFieldOffset = 0x2C5C;

    // FUN_00471F60 and FUN_00471B60 call Sys_GetTickCount/GetTickCount, so these are milliseconds,
    // not GameClock simulation ticks. Keep the unit in the name because the detector is fed by the
    // desktop clock's Now property while the sweep HUD remains simulation-tick driven.
    private const int NativeRetentionMilliseconds = 3000;
    // FUN_00471B60 clamps the activation age to 250 milliseconds, then passes age*0.004*1.5707 as
    // the marker rotation and ((1-age*0.004)*10)+1 as its scale to FUN_004B5860.
    private const int NativeMarkerPulseMilliseconds = 250;
    private const float NativeMarkerProgressPerMillisecond = 0.0040000002f;
    private const float NativeMarkerQuarterTurn = 1.5707f;
    private readonly TargetSlot[] _slots = new TargetSlot[SlotCount];

    /// <summary>Gets the records in native slot order.</summary>
    public IReadOnlyList<TargetSlot> Slots => _slots;

    public void BeginScan()
    {
        for (int index = 0; index < _slots.Length; index++)
        {
            if (_slots[index].Occupied)
            {
                _slots[index] = _slots[index] with { Active = false };
            }
        }
    }

    /// <summary>Updates or allocates one native detector record.</summary>
    public bool Upsert(
        int targetId,
        float sortingDistance,
        int configFlag,
        float screenX,
        float screenY,
        int eventParam,
        int extraParam1,
        int extraParam2,
        int tick,
        float boxWidth,
        float boxHeight)
    {
        if (targetId == 0)
        {
            return false;
        }

        int index = Array.FindIndex(_slots, slot => slot.TargetId == targetId);
        bool existing = index >= 0;
        if (index < 0)
        {
            index = Array.FindIndex(_slots, slot => slot.TargetId == 0);
        }

        if (index < 0)
        {
            // FUN_00471F60 advances while slot distance <= the new distance and chooses the first
            // greater distance. The unusual comparison is deliberately retained.
            index = Array.FindIndex(_slots, slot => slot.Distance > sortingDistance);
            if (index < 0)
            {
                return false;
            }
        }

        _slots[index] = new TargetSlot(
            targetId,
            sortingDistance,
            configFlag,
            screenX,
            screenY,
            eventParam,
            extraParam1,
            extraParam2,
            // FUN_00471F60 writes +0x20 only on allocation/eviction. FUN_00471B60 uses it as
            // the marker's entry-pulse origin, so a rediscovered target keeps this timestamp.
            existing ? _slots[index].AllocatedTick : tick,
            tick,
            true,
            MathF.Max(0f, boxWidth),
            MathF.Max(0f, boxHeight));
        return true;
    }

    public void ExpireInactive(int tick)
    {
        for (int index = 0; index < _slots.Length; index++)
        {
            TargetSlot slot = _slots[index];
            // FUN_00471B60 compares the current tick with slot[7] (+0x1c), which FUN_00471F60
            // refreshes every successful scan. Slot[8] (+0x20) is only the visual entry-pulse origin.
            if (slot.Occupied && !slot.Active
                && unchecked(tick - slot.LastSeenTick) > NativeRetentionMilliseconds)
            {
                _slots[index] = default;
            }
        }
    }

    public void Clear() => Array.Clear(_slots);

    /// <summary>Converts drawable native records into the rectangle contract used by both HUDs.</summary>
    public IReadOnlyList<EnemyOverlayBox> OverlayBoxes(int tick)
    {
        List<EnemyOverlayBox> boxes = [];
        foreach (TargetSlot slot in _slots)
        {
            if (!slot.Drawable || slot.BoxWidth <= 0f || slot.BoxHeight <= 0f)
            {
                continue;
            }

            // The native primitive rotates and scales the model-projected rectangle during its
            // 250-tick entry pulse. The underlying projected bounds still determine the contact's
            // distance-dependent size.
            float width = slot.BoxWidth;
            float height = slot.BoxHeight;
            int age = Math.Clamp(unchecked(tick - slot.AllocatedTick), 0, NativeMarkerPulseMilliseconds);
            float progress = age * NativeMarkerProgressPerMillisecond;
            // FUN_00471B60 chooses the rectangle colour from the two entity flags it stored in the
            // slot: the +3317 non-collidable flag (ExtraParam2) beats the Team!=1 flag (ExtraParam1).
            BinocularMarkerColor marker = slot.ExtraParam2 != 0
                ? BinocularMarkerColor.Disabled
                : slot.ExtraParam1 != 0
                    ? BinocularMarkerColor.Team
                    : BinocularMarkerColor.Hostile;
            boxes.Add(new EnemyOverlayBox(
                slot.TargetId,
                slot.ScreenX - (width * 0.5f),
                slot.ScreenY - (height * 0.5f),
                slot.ScreenX + (width * 0.5f),
                slot.ScreenY + (height * 0.5f),
                // BinocularTargets stores the native slot distance after projection in metres.
                // Dividing by UnitsPerMetre here converted an already-converted value a second time,
                // making the HUD report zero-ish ranges even when the rectangle was correctly sized.
                slot.Distance,
                1f,
                progress * NativeMarkerQuarterTurn,
                ((1f - progress) * 10f) + 1f,
                marker));
        }

        return boxes;
    }

    public readonly record struct TargetSlot(
        int TargetId,
        float Distance,
        int ConfigFlag,
        float ScreenX,
        float ScreenY,
        int EventParam,
        int ExtraParam1,
        int ExtraParam2,
            int AllocatedTick,
            int LastSeenTick,
        bool Active,
        float BoxWidth,
        float BoxHeight)
    {
        public bool Occupied => TargetId != 0;
        public bool Drawable => Occupied && Active && EventParam == 0;

    }

    /// <summary>Projects the legacy contact list for callers that do not own the live detector.</summary>
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
        foreach ((Vector3d feet, int index) in contacts.Select((position, index) => (position, index)))
        {
            Matrix4f targetProjection = projection * camera.CreateModelView(feet);
            float minX = float.PositiveInfinity;
            float minY = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float maxY = float.NegativeInfinity;
            for (int x = -1; x <= 1; x += 2)
            {
                for (int y = -1; y <= 1; y += 2)
                {
                    for (int z = 0; z <= 1; z++)
                    {
                        Vector3f point = targetProjection.TransformPoint(new Vector3f(
                            x * 0.36f, y * 0.36f, z * 1.8f));
                        if (!float.IsFinite(point.X) || !float.IsFinite(point.Y)
                            || point.Z < 0f || point.Z > 1f)
                        {
                            continue;
                        }

                        minX = MathF.Min(minX, (point.X * 0.5f + 0.5f) * screenWidth);
                        minY = MathF.Min(minY, (1f - (point.Y * 0.5f + 0.5f)) * screenHeight);
                        maxX = MathF.Max(maxX, (point.X * 0.5f + 0.5f) * screenWidth);
                        maxY = MathF.Max(maxY, (1f - (point.Y * 0.5f + 0.5f)) * screenHeight);
                    }
                }
            }

            if (float.IsFinite(minX) && maxX > minX && maxY > minY)
            {
                boxes.Add(new EnemyOverlayBox(index + 1, minX, minY, maxX, maxY, 0f,
                    ((tick / 8) & 1) == 0 ? 1f : 0.72f));
            }
        }

        return boxes.Take(SlotCount).ToArray();
    }
}

#pragma warning restore CS1591

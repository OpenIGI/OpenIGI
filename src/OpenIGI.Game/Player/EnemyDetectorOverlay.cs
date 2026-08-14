using OpenIGI.Core.Numerics;
using OpenIGI.Rendering;

namespace OpenIGI.Game.Player;

#pragma warning disable CS1591

/// <summary>One projected enemy rectangle emitted by the native binocular detector.</summary>
public readonly record struct EnemyOverlayBox(
    int TargetId,
    float Left,
    float Top,
    float Right,
    float Bottom,
    float DistanceMetres,
    float Alpha);

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

    private const int NativeRetentionTicks = 3000;
    private const float NativePulseDurationTicks = 250f;

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
            existing ? _slots[index].LastUsedTick : tick,
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
            if (slot.Occupied && !slot.Active && unchecked(tick - slot.ActivatedTick) > NativeRetentionTicks)
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

            float pulse = slot.NativePulseScale(tick);
            float width = slot.BoxWidth * pulse;
            float height = slot.BoxHeight * pulse;
            boxes.Add(new EnemyOverlayBox(
                slot.TargetId,
                slot.ScreenX - (width * 0.5f),
                slot.ScreenY - (height * 0.5f),
                slot.ScreenX + (width * 0.5f),
                slot.ScreenY + (height * 0.5f),
                slot.Distance / (float)OpenIGI.Game.World.WorldScene.UnitsPerMetre,
                1f));
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
        int LastUsedTick,
        int ActivatedTick,
        bool Active,
        float BoxWidth,
        float BoxHeight)
    {
        public bool Occupied => TargetId != 0;
        public bool Drawable => Occupied && Active && EventParam == 0;

        public int NativePulseAge(int tick) =>
            Math.Clamp(unchecked(tick - LastUsedTick), 0, (int)NativePulseDurationTicks);

        public float NativePulseScale(int tick) =>
            1f + ((NativePulseDurationTicks - NativePulseAge(tick)) * 0.04f);
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

using OpenIGI.Core.Numerics;
using OpenIGI.Game.Player;

namespace OpenIGI.Game.Igi1.Player;

/// <summary>State of the twelve animated columns in the retail binocular overlay.</summary>
/// <remarks>
/// <c>FUN_00471120</c> initializes twelve records at <c>BinocularOverlay+0x2B54</c> with a
/// 0.01 phase step and a cadence of 30. <c>FUN_004719D0</c> advances them from the fixed
/// <c>FUN_004B4720</c> random seed, while <c>FUN_00471A90</c> turns each phase into a screen-space
/// column. Keeping the state here makes the desktop renderer tick it once per simulation tick rather
/// than once per rendered frame.
/// </remarks>
public sealed class BinocularSweep
{
    /// <summary>The number of retail sweep records.</summary>
    public const int ColumnCount = 12;

    private const float PhaseStep = 0.01f;
    private const int Cadence = 30;
    // Reopening after a suspended window must not make the render thread replay an unbounded
    // number of fixed ticks. The retail state is visual-only, so a short catch-up is enough to
    // preserve continuity without turning a long pause into a frame-time spike.
    private const int MaxCatchUpTicks = 90;
    private const float StartX = 0.125f;
    private const float ColumnStepX = 0.015625f;
    private const float ColumnWidth = 0.00625f;
    private const float BottomY = 0.8875f;
    private const float ColumnHeight = 0.125f;

    private readonly SweepRecord[] _records = new SweepRecord[ColumnCount];
    private IgiRandom _random = CreateRetailRandom();
    private int _lastSimulationTick = int.MinValue;

    /// <summary>Advances the retail sweep once for each new simulation tick.</summary>
    public IReadOnlyList<BinocularSweepColumn> Tick(int simulationTick)
    {
        if (_lastSimulationTick == int.MinValue)
        {
            Advance();
        }
        else if (simulationTick < _lastSimulationTick)
        {
            Reset();
            Advance();
        }
        else
        {
            long elapsedTicks = (long)simulationTick - _lastSimulationTick;
            int catchUpTicks = (int)Math.Min(Math.Max(0L, elapsedTicks), MaxCatchUpTicks);
            for (int tick = 0; tick < catchUpTicks; tick++)
            {
                Advance();
            }
        }

        _lastSimulationTick = simulationTick;
        BinocularSweepColumn[] columns = new BinocularSweepColumn[ColumnCount];
        for (int index = 0; index < _records.Length; index++)
        {
            float phase = _records[index].Phase;
            columns[index] = new BinocularSweepColumn(
                StartX + (index * ColumnStepX),
                BottomY - (phase * ColumnHeight),
                ColumnWidth,
                phase * ColumnHeight,
                phase);
        }

        return columns;
    }

    /// <summary>Returns this overlay to the retail constructor state.</summary>
    public void Reset()
    {
        Array.Clear(_records);
        _random = CreateRetailRandom();
        _lastSimulationTick = int.MinValue;
    }

    private static IgiRandom CreateRetailRandom() => new(0xc8fb1a07u, 0x39ea7756u, 0x7ff13ccdu);

    private void Advance()
    {
        for (int index = 0; index < _records.Length; index++)
        {
            ref SweepRecord record = ref _records[index];
            if (record.Phase > 0f)
            {
                record.Phase = Math.Clamp(record.Phase - PhaseStep, 0f, 1f);
            }

            if (record.Cooldown > 0)
            {
                record.Cooldown--;
                continue;
            }

            if (_random.NextSingle() > record.Phase)
            {
                record.Phase = _random.NextSingle();
            }

            // x87 FISTP rounds to nearest-even under the retail process's default control word.
            record.Cooldown = (int)MathF.Round((_random.NextSingle() + 1f) * Cadence * 0.5f) - 1;
        }
    }

    private struct SweepRecord
    {
        public float Phase;
        public int Cooldown;
    }
}

/// <summary>One normalized retail binocular sweep column.</summary>
public readonly record struct BinocularSweepColumn(float X, float Y, float Width, float Height, float Phase);

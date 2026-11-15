namespace OpenIGI.Editor.Panels;

/// <summary>
/// How a lane of cues is laid out so it stays readable however many there are: diamonds that would overlap merge into one mark
/// (its label the first cue's name and how many more), and a label never runs past the next mark -- it is cut to the room there
/// is, or left out where there is too little; hovering a mark lists every cue in it.
/// </summary>
internal static class CueLaneLayout
{
    /// <summary>One mark on the lane.</summary>
    /// <param name="X">Where its diamond is.</param>
    /// <param name="First">The index of its first cue in the lane's cues (which are in order along the lane).</param>
    /// <param name="Count">How many cues it stands for.</param>
    /// <param name="Label">What it says, cut to its room; empty where there is none.</param>
    public readonly record struct Mark(float X, int First, int Count, string Label);

    /// <summary>Lays out a lane.</summary>
    /// <param name="cues">The cues' positions and names, in order along the lane.</param>
    /// <param name="fit">Cuts a text to a width (with an ellipsis), or gives it back whole when it fits.</param>
    /// <param name="mergeWithin">How close two diamonds may come before they are one mark.</param>
    /// <param name="labelGap">The room between a diamond and its label, and between a label and the next diamond.</param>
    /// <param name="shortestLabel">The narrowest room a label is drawn in at all.</param>
    /// <param name="end">Where the lane ends: the last label's room runs to here.</param>
    /// <returns>The marks, in order.</returns>
    public static IReadOnlyList<Mark> Lay(
        IReadOnlyList<(float X, string Name)> cues,
        Func<string, float, string> fit,
        float mergeWithin,
        float labelGap,
        float shortestLabel,
        float end)
    {
        ArgumentNullException.ThrowIfNull(cues);
        ArgumentNullException.ThrowIfNull(fit);
        List<(float X, int First, int Count)> groups = [];
        for (int i = 0; i < cues.Count; i++)
        {
            if (groups.Count > 0 && cues[i].X - groups[^1].X <= mergeWithin)
            {
                groups[^1] = groups[^1] with { Count = groups[^1].Count + 1 };
            }
            else
            {
                groups.Add((cues[i].X, i, 1));
            }
        }

        List<Mark> marks = new(groups.Count);
        for (int g = 0; g < groups.Count; g++)
        {
            (float x, int first, int count) = groups[g];
            float room = (g + 1 < groups.Count ? groups[g + 1].X : end) - x - (labelGap * 2f);
            string text = count == 1 ? cues[first].Name : $"{cues[first].Name} +{count - 1}";
            marks.Add(new Mark(x, first, count, room >= shortestLabel ? fit(text, room) : string.Empty));
        }

        return marks;
    }

    /// <summary>Finds the mark a point on the lane is over.</summary>
    /// <param name="marks">The lane's marks.</param>
    /// <param name="x">The point's position along the lane.</param>
    /// <param name="reach">How far from a diamond still counts as on it.</param>
    /// <returns>The mark's index, or -1.</returns>
    public static int MarkAt(IReadOnlyList<Mark> marks, float x, float reach)
    {
        ArgumentNullException.ThrowIfNull(marks);
        int best = -1;
        float nearest = reach;
        for (int i = 0; i < marks.Count; i++)
        {
            float distance = MathF.Abs(marks[i].X - x);
            if (distance <= nearest)
            {
                nearest = distance;
                best = i;
            }
        }

        return best;
    }
}

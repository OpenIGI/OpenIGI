namespace OpenIGI.Game.Computer;

/// <summary>
/// The tasks a <c>ComputerHilight</c>'s id text names, in both games (Project I.G.I.'s <c>0x468690</c>, I.G.I. 2's
/// <c>ComputerHilight_ExtractTaskID</c>): numbers separated by anything, and a range written with <c>-</c>.
/// </summary>
public static class ComputerHilightIds
{
    /// <summary>The most tasks one highlight names.</summary>
    public const int MaxTasks = 64;

    /// <summary>Reads the ids a text names.</summary>
    /// <param name="text">The text.</param>
    /// <returns>
    /// The ids, at most <see cref="MaxTasks"/>. A number after a <c>-</c> first adds the ones between it and the last before it,
    /// a space keeping the range open and anything else closing it. As in the original, the reading steps on by the digits
    /// of the value read, not of the text: <c>0</c> is read again until the list is full, and leading noughts are read
    /// again as a shorter number.
    /// </returns>
    public static IReadOnlyList<int> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        List<int> ids = [];
        bool range = false;
        int i = 0;
        while (i < text.Length && ids.Count < MaxTasks)
        {
            char c = text[i];
            if (c is >= '0' and <= '9')
            {
                int value = Atoi(text, i);
                if (range && ids.Count > 0)
                {
                    for (int between = ids[^1] + 1; between < value && ids.Count < MaxTasks; between++)
                    {
                        ids.Add(between);
                    }

                    range = false;
                }

                if (ids.Count < MaxTasks)
                {
                    ids.Add(value);
                }

                for (int digits = value; digits != 0; digits /= 10)
                {
                    i++;
                }

                continue;
            }

            range = c == '-' || (c == ' ' && range);
            i++;
        }

        return ids;
    }

    /// <summary>The C library's <c>atoi</c> on the digits from a place: the value wraps as an <see cref="int"/> does.</summary>
    private static int Atoi(string text, int start)
    {
        int value = 0;
        for (int i = start; i < text.Length && text[i] is >= '0' and <= '9'; i++)
        {
            value = unchecked((value * 10) + (text[i] - '0'));
        }

        return value;
    }
}

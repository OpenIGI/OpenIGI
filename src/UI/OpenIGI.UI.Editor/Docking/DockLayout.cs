using System.Globalization;
using System.Text;
using OpenIGI.UI.Kit;
using OpenIGI.UI.Layout;

namespace OpenIGI.UI.Editor.Docking;

/// <summary>A dockable panel: a title, an icon, and the content it shows.</summary>
/// <param name="id">A stable id, used by saved layouts (<c>outliner</c>).</param>
/// <param name="title">The tab title.</param>
/// <param name="content">The content.</param>
public sealed class DockPanel(string id, string title, UiElement content)
{
    /// <summary>Gets the stable id.</summary>
    public string Id { get; } = id;

    /// <summary>Gets or sets the tab title.</summary>
    public string Title { get; set; } = title;

    /// <summary>Gets or sets the tab icon.</summary>
    public KitIcon Icon { get; set; }

    /// <summary>Gets the content.</summary>
    public UiElement Content { get; } = content;

    /// <summary>Gets or sets whether the panel's tab shows a close button.</summary>
    public bool CanClose { get; set; } = true;
}

/// <summary>A node of a dock layout: a split or a tab group.</summary>
public abstract class DockNode
{
    private protected DockNode()
    {
    }

    /// <summary>Gets the split this node sits in, or <see langword="null"/> for the root.</summary>
    public DockSplit? Parent { get; internal set; }

    /// <summary>Gets the rectangle the node was last arranged in.</summary>
    public UiRect Bounds { get; internal set; }
}

/// <summary>Panels stacked as tabs, one showing.</summary>
public sealed class DockTabs : DockNode
{
    /// <summary>Gets the panels, in tab order.</summary>
    public List<DockPanel> Panels { get; } = [];

    /// <summary>Gets or sets the index of the showing panel.</summary>
    public int Active
    {
        get => Panels.Count == 0 ? -1 : Math.Clamp(field, 0, Panels.Count - 1);
        set;
    }

    /// <summary>Gets the showing panel, or <see langword="null"/> for an empty group.</summary>
    public DockPanel? ActivePanel => Active >= 0 ? Panels[Active] : null;
}

/// <summary>Nodes side by side or stacked, each with a share of the space.</summary>
/// <param name="orientation">Horizontal places the children left to right; vertical, top to bottom.</param>
public sealed class DockSplit(Orientation orientation) : DockNode
{
    /// <summary>Gets the direction the children are laid out in.</summary>
    public Orientation Orientation { get; } = orientation;

    /// <summary>Gets the children.</summary>
    public List<DockNode> Children { get; } = [];

    /// <summary>Gets each child's share of the space; they sum to one.</summary>
    public List<float> Ratios { get; } = [];

    /// <summary>Adds a child with a share; shares are renormalised on layout.</summary>
    /// <param name="child">The child.</param>
    /// <param name="ratio">Its share.</param>
    /// <param name="index">Where to insert it, or -1 for the end.</param>
    public void Insert(DockNode child, float ratio, int index = -1)
    {
        ArgumentNullException.ThrowIfNull(child);
        index = index < 0 ? Children.Count : index;
        Children.Insert(index, child);
        Ratios.Insert(index, ratio);
        child.Parent = this;
    }

    /// <summary>Makes the shares sum to one.</summary>
    public void Normalize()
    {
        float sum = Ratios.Sum();
        for (int i = 0; i < Ratios.Count; i++)
        {
            Ratios[i] = sum > 0f ? Ratios[i] / sum : 1f / Ratios.Count;
        }
    }
}

/// <summary>
/// Writes and reads dock layouts as one line of text, so a layout can live in the user's settings and in named
/// presets: <c>H(0.2:T(outliner|assets),0.6:T*(viewport),0.2:V(0.6:T(inspector),0.4:T(history)))</c>.
/// </summary>
/// <remarks>
/// <c>H</c> and <c>V</c> are splits with each child prefixed by its share; <c>T</c> is a tab group listing
/// panel ids, with <c>*</c> after the id of the showing one. Ids the editor no longer has are skipped when read,
/// so an old saved layout still loads.
/// </remarks>
public static class DockLayoutText
{
    /// <summary>Writes a layout.</summary>
    /// <param name="node">The root node.</param>
    /// <returns>The text.</returns>
    public static string Write(DockNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        StringBuilder text = new();
        Write(text, node);
        return text.ToString();
    }

    /// <summary>Reads a layout, resolving panel ids against the panels the editor has.</summary>
    /// <param name="text">The text.</param>
    /// <param name="panels">The panels by id.</param>
    /// <returns>The root node, or <see langword="null"/> when the text is malformed or names no known panel.</returns>
    public static DockNode? Read(string text, IReadOnlyDictionary<string, DockPanel> panels)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(panels);
        int position = 0;
        try
        {
            DockNode? node = ReadNode(text, ref position, panels);
            return position == text.Length ? Prune(node) : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>Removes empty tab groups and collapses splits left with one child.</summary>
    /// <param name="node">The node.</param>
    /// <returns>The node to use in its place, or <see langword="null"/> when nothing is left.</returns>
    public static DockNode? Prune(DockNode? node)
    {
        switch (node)
        {
            case DockTabs tabs:
                return tabs.Panels.Count > 0 ? tabs : null;
            case DockSplit split:
                for (int i = split.Children.Count - 1; i >= 0; i--)
                {
                    DockNode? kept = Prune(split.Children[i]);
                    if (kept is null)
                    {
                        // The space goes to the neighbour that bordered it, as when a desktop dock closes a pane.
                        float freed = split.Ratios[i];
                        split.Children.RemoveAt(i);
                        split.Ratios.RemoveAt(i);
                        if (split.Ratios.Count > 0)
                        {
                            split.Ratios[Math.Max(0, i - 1)] += freed;
                        }
                    }
                    else if (!ReferenceEquals(kept, split.Children[i]))
                    {
                        split.Children[i] = kept;
                        kept.Parent = split;
                    }
                }

                split.Normalize();
                if (split.Children.Count == 1)
                {
                    DockNode only = split.Children[0];
                    only.Parent = null;
                    return only;
                }

                return split.Children.Count == 0 ? null : split;
            default:
                return null;
        }
    }

    private static void Write(StringBuilder text, DockNode node)
    {
        if (node is DockTabs tabs)
        {
            text.Append("T(");
            for (int i = 0; i < tabs.Panels.Count; i++)
            {
                text.Append(i > 0 ? "|" : string.Empty).Append(tabs.Panels[i].Id).Append(i == tabs.Active ? "*" : string.Empty);
            }

            text.Append(')');
            return;
        }

        DockSplit split = (DockSplit)node;
        text.Append(split.Orientation == Orientation.Horizontal ? "H(" : "V(");
        for (int i = 0; i < split.Children.Count; i++)
        {
            if (i > 0)
            {
                text.Append(',');
            }

            text.Append(split.Ratios[i].ToString("0.###", CultureInfo.InvariantCulture)).Append(':');
            Write(text, split.Children[i]);
        }

        text.Append(')');
    }

    private static DockNode? ReadNode(string text, ref int position, IReadOnlyDictionary<string, DockPanel> panels)
    {
        char kind = Next(text, ref position);
        Expect(text, ref position, '(');
        if (kind == 'T')
        {
            DockTabs tabs = new();
            while (true)
            {
                int start = position;
                while (position < text.Length && text[position] is not ('|' or ')'))
                {
                    position++;
                }

                string id = text[start..position];
                bool active = id.EndsWith('*');
                id = id.TrimEnd('*');
                if (panels.TryGetValue(id, out DockPanel? panel) && !tabs.Panels.Contains(panel))
                {
                    tabs.Panels.Add(panel);
                    if (active)
                    {
                        tabs.Active = tabs.Panels.Count - 1;
                    }
                }

                if (Next(text, ref position) == ')')
                {
                    return tabs;
                }
            }
        }

        if (kind is not ('H' or 'V'))
        {
            throw new FormatException();
        }

        DockSplit split = new(kind == 'H' ? Orientation.Horizontal : Orientation.Vertical);
        while (true)
        {
            int start = position;
            while (position < text.Length && text[position] != ':')
            {
                position++;
            }

            float ratio = float.Parse(text.AsSpan(start, position - start), NumberStyles.Float, CultureInfo.InvariantCulture);
            Expect(text, ref position, ':');
            if (ReadNode(text, ref position, panels) is { } child)
            {
                split.Insert(child, ratio);
            }

            if (Next(text, ref position) == ')')
            {
                return split;
            }
        }
    }

    private static char Next(string text, ref int position) =>
        position < text.Length ? text[position++] : throw new FormatException();

    private static void Expect(string text, ref int position, char expected)
    {
        if (Next(text, ref position) != expected)
        {
            throw new FormatException();
        }
    }
}

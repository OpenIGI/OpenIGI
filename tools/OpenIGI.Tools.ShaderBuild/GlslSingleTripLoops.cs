using System.Text;

namespace OpenIGI.Tools.ShaderBuild;

/// <summary>Writes the loops that run exactly once, and are only there to be broken out of, as plain branches.</summary>
/// <remarks>
/// <para>
/// A function that returns early is, once it has been inlined, a region its caller has to be able to leave from the
/// middle. SPIR-V says that with a loop that never repeats, and SPIRV-Cross writes what it is told:
/// <c>do { ... if (c) { break; } ... } while(false);</c>. Every heavy shader here is a few dozen of them deep.
/// </para>
/// <para>
/// NVIDIA's GLSL compiler does not turn such a <c>break</c> into a jump. It sets a flag and tests the flag again
/// around everything that follows, at every level the region is nested in -- and inside a loop over lamps or along
/// a ray, on every turn. The same SPIR-V under Vulkan keeps its jumps, and the difference was a third more
/// instructions over the whole frame: the lighting ran at 2.1 ms against 1.3, the screen trace at 1.9 against 1.2.
/// </para>
/// <para>
/// Leaving a region early is the same as running the rest of it only on the paths that did not leave, so what follows
/// a branch that can leave is moved into the paths of that branch that stay. Where two paths both stay, what follows
/// is written into each. A region whose rest would have to be copied past <see cref="MaxCopiedLines"/> is left as it
/// was, as is anything this does not read as the shape SPIRV-Cross writes.
/// </para>
/// </remarks>
internal static class GlslSingleTripLoops
{
    /// <summary>The most lines one branch may have copied into both of its paths.</summary>
    private const int MaxCopiedLines = 4000;

    private const int Indent = 4;
    private const string RegionEnd = "} while(false);";

    private enum Kind
    {
        Line,
        If,
        Block,
        Loop,
        Switch,
        Region,
    }

    /// <summary>Rewrites every single-trip loop of a stage that can be written as branches.</summary>
    /// <param name="source">The stage's GLSL, as SPIRV-Cross lays it out.</param>
    /// <returns>The rewritten GLSL; the same text where there is nothing to rewrite or it is not laid out as expected.</returns>
    public static string AsBranches(string source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (!source.Contains(RegionEnd, StringComparison.Ordinal))
        {
            return source;
        }

        string[] lines = source.Split('\n');
        List<string> written = new(lines.Length);
        int at = 0;

        while (at < lines.Length)
        {
            string line = lines[at];
            bool function = IndentOf(line) == 0
                && line.EndsWith(')')
                && !line.StartsWith("layout", StringComparison.Ordinal)
                && at + 1 < lines.Length
                && lines[at + 1] == "{";

            if (!function)
            {
                written.Add(line);
                at++;
                continue;
            }

            int next = at + 2;
            List<Node>? body = Parse(lines, ref next, Indent);
            if (body is null || next >= lines.Length || lines[next] != "}")
            {
                return source;
            }

            written.Add(line);
            written.Add("{");
            Write(Rewrite(body), Indent, written);
            written.Add("}");
            at = next + 1;
        }

        return string.Join('\n', written);
    }

    private static int IndentOf(string line) => line.Length - line.AsSpan().TrimStart(' ').Length;

    private static bool Opens(string[] lines, int at) => at + 1 < lines.Length && lines[at + 1].AsSpan().Trim().SequenceEqual("{");

    // The statements at one depth, up to the line that closes them; null where the text is not the expected shape.
    private static List<Node>? Parse(string[] lines, ref int at, int level)
    {
        List<Node> nodes = [];

        while (at < lines.Length)
        {
            string line = lines[at];
            string text = line.Trim();

            if (text.Length == 0)
            {
                nodes.Add(Node.OfLine(string.Empty));
                at++;
                continue;
            }

            int indent = IndentOf(line);
            if (indent < level)
            {
                break;
            }

            if (indent == level && text == "{")
            {
                at++;
                List<Node>? inner = Parse(lines, ref at, level + Indent);
                if (inner is null || !Closes(lines, at, "}"))
                {
                    return null;
                }

                at++;
                nodes.Add(new Node(Kind.Block, string.Empty, string.Empty, inner, null));
                continue;
            }

            if (indent != level || !Heads(text) || !Opens(lines, at))
            {
                nodes.Add(Node.OfLine(text));
                at++;
                continue;
            }

            Node? compound = ParseCompound(lines, ref at, level, text);
            if (compound is null)
            {
                return null;
            }

            nodes.Add(compound);
        }

        return nodes;
    }

    private static bool Heads(string text) =>
        text == "do"
        || text.StartsWith("if (", StringComparison.Ordinal)
        || text.StartsWith("for (", StringComparison.Ordinal)
        || text.StartsWith("while (", StringComparison.Ordinal)
        || text.StartsWith("switch (", StringComparison.Ordinal);

    private static bool Closes(string[] lines, int at, string with) =>
        at < lines.Length && lines[at].AsSpan().Trim().SequenceEqual(with);

    private static Node? ParseCompound(string[] lines, ref int at, int level, string head)
    {
        at += 2;
        List<Node>? body = Parse(lines, ref at, level + Indent);
        if (body is null || at >= lines.Length)
        {
            return null;
        }

        string close = lines[at].Trim();
        if (!close.StartsWith('}'))
        {
            return null;
        }

        at++;

        if (head.StartsWith("if (", StringComparison.Ordinal))
        {
            if (close != "}")
            {
                return null;
            }

            return ParseElse(lines, ref at, level, out List<Node>? otherwise)
                ? new Node(Kind.If, head, string.Empty, body, otherwise)
                : null;
        }

        Kind kind = head switch
        {
            "do" when close == RegionEnd => Kind.Region,
            _ when head.StartsWith("switch (", StringComparison.Ordinal) => Kind.Switch,
            _ => Kind.Loop,
        };

        return new Node(kind, head, close, body, null);
    }

    // What follows an if's closing brace: nothing, an else block, or an else-if, which is an if inside an else.
    private static bool ParseElse(string[] lines, ref int at, int level, out List<Node>? otherwise)
    {
        otherwise = null;
        if (at >= lines.Length || IndentOf(lines[at]) != level || !Opens(lines, at))
        {
            return true;
        }

        string text = lines[at].Trim();

        if (text == "else")
        {
            at += 2;
            otherwise = Parse(lines, ref at, level + Indent);
            if (otherwise is null || !Closes(lines, at, "}"))
            {
                return false;
            }

            at++;
            return true;
        }

        if (!text.StartsWith("else if (", StringComparison.Ordinal))
        {
            return true;
        }

        Node? nested = ParseCompound(lines, ref at, level, text["else ".Length..]);
        if (nested is null)
        {
            return false;
        }

        otherwise = [nested];
        return true;
    }

    // Innermost regions first, so that a region being rewritten holds none of its own.
    private static List<Node> Rewrite(List<Node> nodes)
    {
        List<Node> rewritten = new(nodes.Count);

        foreach (Node node in nodes)
        {
            Node inner = node.Kind == Kind.Line
                ? node
                : node with { Body = Rewrite(node.Body), Else = node.Else is null ? null : Rewrite(node.Else) };

            if (inner.Kind == Kind.Region && !LeavesFromSwitch(inner.Body) && Lower(inner.Body) is { } branches)
            {
                inner = new Node(Kind.Block, string.Empty, string.Empty, branches, null);
            }

            rewritten.Add(inner);
        }

        return rewritten;
    }

    // Inside a switch a break is the switch's own, but a continue still leaves the region, and from there the rest of
    // the region cannot be moved to where it would have to go.
    private static bool LeavesFromSwitch(List<Node> nodes) => nodes.Exists(node => node.Kind switch
    {
        Kind.Switch => Continues(node.Body),
        Kind.If => LeavesFromSwitch(node.Body) || (node.Else is not null && LeavesFromSwitch(node.Else)),
        Kind.Block => LeavesFromSwitch(node.Body),
        _ => false,
    });

    private static bool Continues(List<Node> nodes) => nodes.Exists(node => node.Kind switch
    {
        Kind.Line => node.Text == "continue;",
        Kind.If => Continues(node.Body) || (node.Else is not null && Continues(node.Else)),
        Kind.Block or Kind.Switch => Continues(node.Body),
        _ => false,
    });

    // In a loop that runs once, continuing is leaving too.
    private static bool IsLeave(Node node) => node.Kind == Kind.Line && node.Text is "break;" or "continue;";

    // Whether a statement can leave the region: a loop or a switch inside it keeps its own breaks.
    private static bool Leaves(Node node) => node.Kind switch
    {
        Kind.Line => IsLeave(node),
        Kind.If => node.Body.Exists(Leaves) || (node.Else is not null && node.Else.Exists(Leaves)),
        Kind.Block => node.Body.Exists(Leaves),
        _ => false,
    };

    // Whether running a list of statements can reach its end without leaving the region.
    private static bool ReachesEnd(List<Node>? nodes)
    {
        if (nodes is null)
        {
            return true;
        }

        foreach (Node node in nodes)
        {
            bool stops = node.Kind switch
            {
                Kind.Line => IsLeave(node),
                Kind.If => Leaves(node) && !ReachesEnd(node.Body) && !ReachesEnd(node.Else),
                Kind.Block => Leaves(node) && !ReachesEnd(node.Body),
                _ => false,
            };

            if (stops)
            {
                return false;
            }
        }

        return true;
    }

    private static int LinesOf(List<Node>? nodes) =>
        nodes?.Sum(node => node.Kind == Kind.Line ? 1 : 2 + LinesOf(node.Body) + LinesOf(node.Else)) ?? 0;

    // The same statements with nothing leaving the region: what followed a leave is gone, and what followed a branch
    // that might leave is inside the paths of it that do not. Null where that cannot be written.
    private static List<Node>? Lower(List<Node> nodes)
    {
        List<Node> lowered = [];

        for (int i = 0; i < nodes.Count; i++)
        {
            Node node = nodes[i];

            if (IsLeave(node))
            {
                return lowered;
            }

            if (!Leaves(node))
            {
                lowered.Add(node);
                continue;
            }

            List<Node> rest = nodes.GetRange(i + 1, nodes.Count - i - 1);
            Node? branch = node.Kind switch
            {
                Kind.Block => LowerBlock(node, rest),
                Kind.If => LowerIf(node, rest),
                _ => null,
            };

            if (branch is null)
            {
                return null;
            }

            lowered.Add(branch);
            return lowered;
        }

        return lowered;
    }

    private static Node? LowerBlock(Node block, List<Node> rest) =>
        Lower(ReachesEnd(block.Body) ? [.. block.Body, .. rest] : block.Body) is { } body
            ? block with { Body = body }
            : null;

    private static Node? LowerIf(Node branch, List<Node> rest)
    {
        bool taken = ReachesEnd(branch.Body);
        bool other = ReachesEnd(branch.Else);
        if (taken && other && LinesOf(rest) > MaxCopiedLines)
        {
            return null;
        }

        List<Node>? body = Lower(taken ? [.. branch.Body, .. rest] : branch.Body);
        List<Node>? otherwise = Lower(other ? [.. branch.Else ?? [], .. rest] : branch.Else ?? []);
        if (body is null || otherwise is null)
        {
            return null;
        }

        return branch with { Body = body, Else = otherwise.Count > 0 ? otherwise : null };
    }

    private static bool Empty(List<Node> nodes) => nodes.TrueForAll(node => node.Kind == Kind.Line && node.Text.Length == 0);

    private static void Write(List<Node> nodes, int level, List<string> lines)
    {
        string pad = new(' ', level);

        foreach (Node node in nodes)
        {
            switch (node.Kind)
            {
                case Kind.Line:
                    lines.Add(node.Text.Length == 0 ? string.Empty : pad + node.Text);
                    break;
                case Kind.If:
                    WriteIf(node, level, pad, lines);
                    break;
                case Kind.Block:
                    WriteBraced(node.Body, level, pad, "}", lines);
                    break;
                default:
                    lines.Add(pad + node.Text);
                    WriteBraced(node.Body, level, pad, node.Tail, lines);
                    break;
            }
        }
    }

    private static void WriteIf(Node node, int level, string pad, List<string> lines)
    {
        // A branch that only left has nothing in it now: the other path is written under the opposite test.
        if (Empty(node.Body) && node.Else is { } only)
        {
            lines.Add(new StringBuilder(pad).Append("if (!").Append(node.Text, "if ".Length, node.Text.Length - "if ".Length).Append(')').ToString());
            WriteBraced(only, level, pad, "}", lines);
            return;
        }

        lines.Add(pad + node.Text);
        WriteBraced(node.Body, level, pad, "}", lines);

        if (node.Else is { } otherwise)
        {
            lines.Add(pad + "else");
            WriteBraced(otherwise, level, pad, "}", lines);
        }
    }

    private static void WriteBraced(List<Node> body, int level, string pad, string close, List<string> lines)
    {
        lines.Add(pad + "{");
        Write(body, level + Indent, lines);
        lines.Add(pad + close);
    }

    /// <summary>One statement: a line, or a braced construct with its head, its body and what closes it.</summary>
    /// <param name="Kind">What it is.</param>
    /// <param name="Text">The line, or the construct's head: the test of an if, the header of a loop.</param>
    /// <param name="Tail">What closes a loop or a switch.</param>
    /// <param name="Body">The statements inside; an if's taken path.</param>
    /// <param name="Else">An if's other path, or null.</param>
    private sealed record Node(Kind Kind, string Text, string Tail, List<Node> Body, List<Node>? Else)
    {
        private static readonly List<Node> None = [];

        public static Node OfLine(string text) => new(Kind.Line, text, string.Empty, None, null);
    }
}

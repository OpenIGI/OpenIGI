using OpenIGI.Formats.Ai;
using OpenIGI.TestSupport;
using Xunit;

namespace OpenIGI.Formats.Tests;

/// <summary>
/// Corpus tests for the AI nav graph, over every <c>graph&lt;n&gt;.dat</c> in an installation — 300 files
/// across the fourteen levels. Installation-dependent route counts are checked against connectivity
/// independently computed from the links, so altered navigation cannot pass as a census difference.
/// </summary>
public sealed class RetailGraphGoldenTests
{
    /// <summary>Every graph file walks byte-exactly, in whichever variant it is written.</summary>
    [RequiresGameDataFact]
    public void EveryGraphWalksExactly()
    {
        List<string> problems = [];
        int named = 0;
        int positional = 0;
        int nodes = 0;
        int links = 0;
        int directRoutes = 0;

        foreach (string path in GraphFiles())
        {
            GraphFile graph;
            try
            {
                graph = GraphFile.Read(File.ReadAllBytes(path));
            }
            catch (GraphFormatException e)
            {
                problems.Add($"{Name(path)}: {e.Message}");
                continue;
            }

            if (!graph.IsExact)
            {
                problems.Add($"{Name(path)}: consumed {graph.BytesConsumed} of {graph.Length} bytes");
            }

            if (graph.IsPositional)
            {
                positional++;
            }
            else
            {
                named++;
            }

            nodes += graph.Nodes.Count;
            links += graph.Links.Count;
            directRoutes += CountDirectRoutes(graph);
        }

        Assert.Empty(problems);
        Assert.Equal(293, named);
        Assert.Equal(7, positional);
        Assert.Equal(300, named + positional);
        Assert.Equal(8317, nodes);
        Assert.True(links > 0);
        Assert.Equal(links * 2, directRoutes);
    }

    /// <summary>
    /// No file names a field the engine does not register, which is what makes the eleven known names the
    /// complete vocabulary — and what keeps the skip path (which cannot skip a blob) out of the way.
    /// </summary>
    [RequiresGameDataFact]
    public void NoGraphNamesAnUnknownField()
    {
        List<string> found = [];
        foreach (string path in GraphFiles())
        {
            GraphFile graph = GraphFile.Read(File.ReadAllBytes(path));
            foreach (uint hash in graph.UnknownFieldHashes)
            {
                found.Add($"{Name(path)}: 0x{hash:X}");
            }
        }

        Assert.Empty(found);
    }

    /// <summary>
    /// The slot count is <b>not</b> uniform, which a level-1-only sample would have said it was: 274 graphs
    /// declare 100 slots but 19 declare 200, four 300 and three 400. Every one of them carries a route table
    /// square over its own count. The largest node id shipped is 380, well under the engine's cap of 1000,
    /// and slot 0 is never occupied — ids start at 1.
    /// </summary>
    [RequiresGameDataFact]
    public void TheSlotCountVariesAndEveryGraphCarriesASquareRouteTable()
    {
        List<string> problems = [];
        Dictionary<int, int> slotCounts = [];
        int maxId = -1;
        int slotZeroUsed = 0;

        foreach (string path in GraphFiles())
        {
            GraphFile graph = GraphFile.Read(File.ReadAllBytes(path));

            slotCounts[graph.MaxNodes] = slotCounts.GetValueOrDefault(graph.MaxNodes) + 1;

            if (graph.RouteTable is null)
            {
                problems.Add($"{Name(path)}: no route table");
            }
            else if (graph.RouteTable.MaxNodes != graph.MaxNodes)
            {
                problems.Add($"{Name(path)}: route table spans {graph.RouteTable.MaxNodes} slots");
            }

            Assert.Empty(graph.DiscardedNodeIds);

            if (graph.NodeAt(0) is not null)
            {
                slotZeroUsed++;
            }

            foreach (GraphNode node in graph.Nodes)
            {
                maxId = Math.Max(maxId, node.Id);
            }
        }

        Assert.Empty(problems);
        Assert.Equal(new Dictionary<int, int> { [100] = 274, [200] = 19, [300] = 4, [400] = 3 }, slotCounts);
        Assert.Equal(380, maxId);
        Assert.Equal(0, slotZeroUsed);
    }

    /// <summary>
    /// The whole criteria vocabulary, with its counts. Only four values occur, and the empty string is by
    /// far the most common — an ordinary path node names no criteria at all.
    /// </summary>
    [RequiresGameDataFact]
    public void TheCriteriaVocabularyIsThreeNamesAndTheEmptyString()
    {
        Dictionary<string, int> counts = [];
        foreach (string path in GraphFiles())
        {
            foreach (GraphNode node in GraphFile.Read(File.ReadAllBytes(path)).Nodes)
            {
                counts[node.CriteriaName] = counts.GetValueOrDefault(node.CriteriaName) + 1;
                Assert.Equal(GraphNode.ResolveCriteria(node.CriteriaName), node.Criteria);
            }
        }

        Assert.Equal(4, counts.Count);
        Assert.Equal(7022, counts[string.Empty]);
        Assert.Equal(465, counts["NODECRITERIA_DOOR"]);
        Assert.Equal(161, counts["NODECRITERIA_VIEW"]);
        Assert.Equal(669, counts["NODECRITERIA_STAIR"]);
    }

    /// <summary>
    /// Every link in the game declares type 1. A single value over 24433 records is why nothing branches on
    /// the field.
    /// </summary>
    [RequiresGameDataFact]
    public void EveryLinkIsTypeOne()
    {
        HashSet<int> types = [];
        int total = 0;
        int directRoutes = 0;
        foreach (string path in GraphFiles())
        {
            GraphFile graph = GraphFile.Read(File.ReadAllBytes(path));
            directRoutes += CountDirectRoutes(graph);
            foreach (GraphLink link in graph.Links)
            {
                types.Add(link.LinkType);
                total++;
            }
        }

        Assert.True(total > 0);
        Assert.Equal(total * 2, directRoutes);
        Assert.Equal([1], types);
    }

    /// <summary>
    /// A link never names a missing node, never joins a node to itself, and never repeats a pair — so the
    /// adjacency the route table was built from is well formed.
    /// </summary>
    [RequiresGameDataFact]
    public void TheLinksAreWellFormed()
    {
        List<string> problems = [];
        foreach (string path in GraphFiles())
        {
            GraphFile graph = GraphFile.Read(File.ReadAllBytes(path));
            HashSet<(int, int)> seen = [];
            foreach (GraphLink link in graph.Links)
            {
                if (graph.NodeAt(link.NodeA) is null || graph.NodeAt(link.NodeB) is null)
                {
                    problems.Add($"{Name(path)}: link {link.NodeA}-{link.NodeB} names a missing node");
                }

                if (link.NodeA == link.NodeB)
                {
                    problems.Add($"{Name(path)}: link {link.NodeA} joins a node to itself");
                }

                if (!seen.Add((Math.Min(link.NodeA, link.NodeB), Math.Max(link.NodeA, link.NodeB))))
                {
                    problems.Add($"{Name(path)}: link {link.NodeA}-{link.NodeB} is repeated");
                }
            }
        }

        Assert.Empty(problems);
    }

    /// <summary>
    /// <b>The proof of the route table's indexing.</b> Read as <c>from + to × maxNodes</c>, the node an entry
    /// names is a direct neighbour of the node being walked from — for every reachable pair in the corpus,
    /// with no exceptions. The transposed reading satisfies this for 5.8% of them, so the test distinguishes
    /// the two decisively.
    /// </summary>
    [RequiresGameDataFact]
    public void EveryRouteStepIsToADirectNeighbour()
    {
        long reachable = 0;
        long neighbour = 0;
        long unreachable = 0;
        long expectedReachable = 0;
        long totalPairs = 0;

        foreach (string path in GraphFiles())
        {
            GraphFile graph = GraphFile.Read(File.ReadAllBytes(path));
            GraphRouteTable table = graph.RouteTable!;
            expectedReachable += CountReachablePairs(graph);
            totalPairs += (long)graph.Nodes.Count * (graph.Nodes.Count - 1);

            Dictionary<int, HashSet<int>> adjacency = [];
            foreach (GraphLink link in graph.Links)
            {
                (adjacency.TryGetValue(link.NodeA, out HashSet<int>? a) ? a : adjacency[link.NodeA] = []).Add(link.NodeB);
                (adjacency.TryGetValue(link.NodeB, out HashSet<int>? b) ? b : adjacency[link.NodeB] = []).Add(link.NodeA);
            }

            foreach (GraphNode from in graph.Nodes)
            {
                foreach (GraphNode to in graph.Nodes)
                {
                    if (from.Id == to.Id)
                    {
                        continue;
                    }

                    if (!table.TryGetStep(from.Id, to.Id, out int next, out _))
                    {
                        unreachable++;
                        continue;
                    }

                    reachable++;
                    if (adjacency.TryGetValue(from.Id, out HashSet<int>? links) && links.Contains(next))
                    {
                        neighbour++;
                    }
                }
            }
        }

        Assert.True(reachable > 0);
        Assert.Equal(expectedReachable, reachable);
        Assert.Equal(reachable, neighbour);
        Assert.Equal(totalPairs - expectedReachable, unreachable);
    }

    /// <summary>
    /// The remaining cost strictly decreases at every interior step, which is what guarantees following the
    /// table terminates — the engine walks it with no visited set and no step budget.
    /// </summary>
    [RequiresGameDataFact]
    public void TheCostStrictlyDecreasesAlongEveryRoute()
    {
        long steps = 0;
        long expectedSteps = 0;
        List<string> problems = [];

        foreach (string path in GraphFiles())
        {
            GraphFile graph = GraphFile.Read(File.ReadAllBytes(path));
            GraphRouteTable table = graph.RouteTable!;
            expectedSteps += CountReachablePairs(graph) - (2L * graph.Links.Count);

            foreach (GraphNode from in graph.Nodes)
            {
                foreach (GraphNode to in graph.Nodes)
                {
                    if (from.Id == to.Id || !table.TryGetStep(from.Id, to.Id, out int next, out float cost))
                    {
                        continue;
                    }

                    if (next == to.Id)
                    {
                        continue;
                    }

                    steps++;
                    (_, float remaining) = table[next, to.Id];
                    if (remaining >= cost)
                    {
                        problems.Add($"{Name(path)}: {from.Id}->{to.Id} costs {cost} then {remaining} at {next}");
                    }
                }
            }
        }

        Assert.Empty(problems);
        Assert.True(steps > 0);
        Assert.Equal(expectedSteps, steps);
    }

    /// <summary>
    /// A node has no route to itself, and a slot pair with an unoccupied end is the sentinel — the table is
    /// initialised to it and only real routes overwrite it.
    /// </summary>
    [RequiresGameDataFact]
    public void TheDiagonalAndTheEmptySlotsAreTheSentinel()
    {
        long diagonal = 0;
        long dead = 0;
        List<string> problems = [];

        foreach (string path in GraphFiles())
        {
            GraphFile graph = GraphFile.Read(File.ReadAllBytes(path));
            GraphRouteTable table = graph.RouteTable!;

            for (int from = 0; from < table.MaxNodes; from++)
            {
                for (int to = 0; to < table.MaxNodes; to++)
                {
                    bool live = graph.NodeAt(from) is not null && graph.NodeAt(to) is not null;
                    if (live && from != to)
                    {
                        continue;
                    }

                    if (table[from, to] != GraphRouteTable.Sentinel)
                    {
                        problems.Add($"{Name(path)}: {from}->{to} is {table[from, to]}");
                        continue;
                    }

                    if (live)
                    {
                        diagonal++;
                    }
                    else
                    {
                        dead++;
                    }
                }
            }
        }

        Assert.Empty(problems);
        Assert.Equal(8317, diagonal);
        Assert.Equal(3237161, dead);
    }

    /// <summary>
    /// The seven positional files are all in level 8, and the optional extra float is a property of the file
    /// rather than the node: six carry it on every node and one on none.
    /// </summary>
    [RequiresGameDataFact]
    public void ThePositionalFilesAreLevelEightsAndTheirOptionalFloatIsPerFile()
    {
        List<string> withExtras = [];
        List<string> withoutExtras = [];
        int nodes = 0;
        int withPairs = 0;

        foreach (string path in GraphFiles())
        {
            GraphFile graph = GraphFile.Read(File.ReadAllBytes(path));
            if (!graph.IsPositional)
            {
                Assert.Empty(graph.PositionalExtras);
                continue;
            }

            Assert.Contains("level8", path, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(graph.Nodes.Count, graph.PositionalExtras.Count);

            int present = 0;
            foreach (GraphPositionalNodeExtras extras in graph.PositionalExtras)
            {
                nodes++;
                if (extras.ExtraValue is not null)
                {
                    present++;
                }

                if (extras.FirstList.Count != 0 || extras.SecondList.Count != 0)
                {
                    withPairs++;
                }
            }

            // All or nothing, never a mixture.
            Assert.True(present == 0 || present == graph.PositionalExtras.Count, Name(path));
            (present == 0 ? withoutExtras : withExtras).Add(Name(path));
        }

        Assert.Equal(6, withExtras.Count);
        Assert.Single(withoutExtras);
        Assert.Equal("graph22.dat", withoutExtras[0], ignoreCase: true);
        Assert.Equal(130, nodes);
        Assert.Equal(41, withPairs);
    }

    /// <summary>
    /// A graph file's number is the id of the task that loads it, so the numbers are scattered rather than
    /// sequential — level 1 ships graph 4019 alongside graph 1.
    /// </summary>
    [RequiresGameDataFact]
    public void TheGraphNumbersAreTaskIdsRatherThanASequence()
    {
        string level1 = Path.Combine(GameData.RequireRoot(), "missions", "location0", "level1", "graphs");
        List<int> ids = [];
        foreach (string path in Directory.GetFiles(level1, "graph*.dat"))
        {
            ids.Add(int.Parse(
                Path.GetFileNameWithoutExtension(path).AsSpan("graph".Length),
                provider: System.Globalization.CultureInfo.InvariantCulture));
        }

        ids.Sort();
        Assert.Equal([1, 2, 3, 6, 7, 8, 12, 13, 14, 15, 4019], ids);
    }

    // The route table and the authored links are independent representations. Check their
    // agreement rather than pinning navigation totals to one installation's edited files.
    private static int CountDirectRoutes(GraphFile graph)
    {
        int count = 0;
        foreach (GraphNode from in graph.Nodes)
        {
            foreach (GraphNode to in graph.Nodes)
            {
                if (from.Id != to.Id && graph.RouteTable!.TryGetStep(from.Id, to.Id, out int next, out _)
                    && next == to.Id)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static long CountReachablePairs(GraphFile graph)
    {
        Dictionary<int, List<int>> adjacency = graph.Nodes.ToDictionary(n => n.Id, _ => new List<int>());
        foreach (GraphLink link in graph.Links)
        {
            adjacency[link.NodeA].Add(link.NodeB);
            adjacency[link.NodeB].Add(link.NodeA);
        }

        HashSet<int> visited = [];
        long pairs = 0;
        foreach (GraphNode node in graph.Nodes)
        {
            if (!visited.Add(node.Id))
            {
                continue;
            }

            Queue<int> pending = new();
            pending.Enqueue(node.Id);
            long size = 0;
            while (pending.TryDequeue(out int current))
            {
                size++;
                foreach (int neighbour in adjacency[current])
                {
                    if (visited.Add(neighbour))
                    {
                        pending.Enqueue(neighbour);
                    }
                }
            }

            pairs += size * (size - 1);
        }

        return pairs;
    }

    private static IReadOnlyList<string> GraphFiles() => GameData.EnumerateFiles("graph*.dat");

    private static string Name(string path) => Path.GetFileName(path);
}

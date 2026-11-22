namespace Orange_Tendering_System.Services.Radar;

// Graph DTOs for the network. Node and edge ids are stable so briefings and reports can reference recorded relationships.
public sealed record GraphNode(string Id, string Type, string Label, int? EntityId, string? Category, string? Status, int Weight, int Wins, DateTime? AvailableAt, string? Detail);
public sealed record GraphEdge(string Id, string Source, string Target, string Type, int Weight, string Label, DateTime? AvailableAt);
public sealed record RadarGraphResult(IReadOnlyList<GraphNode> Nodes, IReadOnlyList<GraphEdge> Edges);

// Projections for the network views. Only closed (unsealed) rounds are included, so sealed offers never reach the browser.
// Rounds: who submitted an offer in each analysed round (tender view). CompetitionLinks: supplier pairs with the distinct
// closed rounds both bid in, ordered by date (competition view). Groups: connected components at the default minimum,
// singletons included. Co-participation is ordinary market context, never a suspicion.
public sealed record NetworkRound(int RoundId, int TenderId, string Tender, int RoundNumber, string Department, DateTime AvailableAt, IReadOnlyList<int> SupplierIds, int? AwardedSupplierId);
public sealed record CompetitionLink(string Id, int SupplierA, int SupplierB, IReadOnlyList<int> RoundIds, DateTime FirstAt, DateTime LastAt);
public sealed record RadarNetwork(IReadOnlyList<NetworkRound> Rounds, IReadOnlyList<CompetitionLink> CompetitionLinks, int CompetitionMinimum, IReadOnlyList<IReadOnlyList<int>> CompetitionGroups);

public static class RadarGraph
{
    public static readonly IReadOnlyDictionary<string, string> EdgeTypes = new Dictionary<string, string>
    {
        ["participation"] = "Submitted an offer", ["award"] = "Official award"
    };

    public static RadarGraphResult Build(RadarSnapshot s)
    {
        var stats = RadarAnalytics.Suppliers(s).ToDictionary(x => x.SupplierId);
        var nodes = new Dictionary<string, GraphNode>();
        var edges = new Dictionary<string, GraphEdge>();
        void Node(GraphNode n) => nodes.TryAdd(n.Id, n);
        void Edge(string type, string a, string b, string label, DateTime? at, int weight = 1)
        {
            // Undirected supplier-supplier edges are normalized so A-B and B-A can never be duplicated.
            if (a.StartsWith("s-") && b.StartsWith("s-") && string.CompareOrdinal(a, b) > 0) (a, b) = (b, a);
            var id = $"e-{type}-{a}-{b}";
            if (edges.TryGetValue(id, out var existing))
            {
                edges[id] = existing with { AvailableAt = Max(existing.AvailableAt, at) };
                return;
            }
            edges[id] = new GraphEdge(id, a, b, type, weight, label, at);
        }

        // Every registered supplier is part of the market, including those with no closed-round activity (isolated nodes).
        foreach (var sup in s.Suppliers.OrderBy(x => x.Id))
        {
            var id = sup.Id;
            var st = stats[id];
            Node(new GraphNode($"s-{id}", "supplier", sup.Name, id, sup.Category, sup.Status.ToString(), st.Participations, st.Wins, st.FirstActivity ?? sup.CreatedAt,
                $"{st.Participations} rounds · {st.Wins} awards"));
        }
        foreach (var bid in s.Bids)
        {
            var tender = s.Tender(bid.TenderId); if (tender == null) continue;
            Node(new GraphNode($"t-{tender.Id}", "tender", tender.Title, tender.Id, tender.Department, tender.Status.ToString(),
                s.Bids.Count(b => b.TenderId == tender.Id), 0, s.Rounds.Where(r => r.TenderId == tender.Id && r.Unsealed).Select(r => (DateTime?)r.AvailableAt).Min(), tender.Department));
            Edge("participation", $"s-{bid.SupplierId}", $"t-{tender.Id}", $"Offer {bid.Currency} {bid.Total:N0}", bid.AvailableAt);
        }
        foreach (var award in RadarAnalytics.Awards(s))
            Edge("award", $"s-{award.SupplierId}", $"t-{award.TenderId}", $"Awarded {award.Currency} {award.Value:N0}", award.AwardedAt);

        return new RadarGraphResult(nodes.Values.ToList(), edges.Values.ToList());
    }

    public static RadarNetwork Network(RadarSnapshot s, int competitionMinimum)
    {
        var minimum = Math.Max(1, competitionMinimum);
        var awards = RadarAnalytics.Awards(s).GroupBy(a => a.RoundId).ToDictionary(g => g.Key, g => (int?)g.First().SupplierId);
        var rounds = s.AnalyzedRounds.OrderBy(r => r.AvailableAt).ThenBy(r => r.Id).Select(r => new NetworkRound(r.Id, r.TenderId, s.Tender(r.TenderId)?.Title ?? $"Tender #{r.TenderId}",
            r.RoundNumber, s.Tender(r.TenderId)?.Department ?? "-", r.AvailableAt, s.Bids.Where(b => b.RoundId == r.Id).Select(b => b.SupplierId).Distinct().Order().ToList(),
            awards.GetValueOrDefault(r.Id))).ToList();
        var when = rounds.ToDictionary(r => r.RoundId, r => r.AvailableAt);
        var links = RadarAnalytics.JointRounds(s).Select(p =>
        {
            // Same ordinal normalization as the graph edges, so a pair has exactly one id.
            var (a, b) = string.CompareOrdinal($"s-{p.Key.A}", $"s-{p.Key.B}") < 0 ? (p.Key.A, p.Key.B) : (p.Key.B, p.Key.A);
            var ids = p.Value.Distinct().OrderBy(id => when.GetValueOrDefault(id)).ThenBy(id => id).ToList();
            return new CompetitionLink($"e-competition-s-{a}-s-{b}", a, b, ids, when.GetValueOrDefault(ids[0]), when.GetValueOrDefault(ids[^1]));
        }).OrderBy(l => l.Id, StringComparer.Ordinal).ToList();

        // Connected components over links that meet the minimum. Transitive: members of one group do not all compete directly.
        var adjacency = s.Suppliers.ToDictionary(x => x.Id, _ => new SortedSet<int>());
        foreach (var l in links.Where(l => l.RoundIds.Count >= minimum && adjacency.ContainsKey(l.SupplierA) && adjacency.ContainsKey(l.SupplierB)))
        { adjacency[l.SupplierA].Add(l.SupplierB); adjacency[l.SupplierB].Add(l.SupplierA); }
        var seen = new HashSet<int>(); var groups = new List<IReadOnlyList<int>>();
        foreach (var start in adjacency.Keys.Order())
        {
            if (!seen.Add(start)) continue;
            var group = new List<int>(); var stack = new Stack<int>([start]);
            while (stack.Count > 0) { var u = stack.Pop(); group.Add(u); foreach (var v in adjacency[u]) if (seen.Add(v)) stack.Push(v); }
            groups.Add(group.Order().ToList());
        }
        return new RadarNetwork(rounds, links, minimum, groups.OrderByDescending(g => g.Count).ThenBy(g => g[0]).ToList());
    }

    private static DateTime? Max(DateTime? a, DateTime? b) => a == null ? b : b == null ? a : a > b ? a : b;
}

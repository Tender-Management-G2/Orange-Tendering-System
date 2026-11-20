namespace Orange_Tendering_System.Services.Radar;

// Graph DTOs for the Cytoscape network. Node and edge ids are stable ("s-12", "t-5", "e-joint-3-9") so the AI analyst,
// briefings and reports can reference them; every analytical edge carries the finding ids that justify it.
public sealed record GraphNode(string Id, string Type, string Label, int? EntityId, string? Category, string? Status, int Weight, int Wins, DateTime? AvailableAt, string? Detail);
// Kind: "fact" (recorded relationship), "context" (factual pattern) or "signal" (analytical review signal).
public sealed record GraphEdge(string Id, string Source, string Target, string Type, string Kind, int Weight, string Label, IReadOnlyList<string> FindingIds, DateTime? AvailableAt);
public sealed record RadarGraphResult(IReadOnlyList<GraphNode> Nodes, IReadOnlyList<GraphEdge> Edges);

public static class RadarGraph
{
    public static readonly IReadOnlyDictionary<string, string> EdgeTypes = new Dictionary<string, string>
    {
        ["participation"] = "Submitted an offer", ["award"] = "Official award", ["joint"] = "Repeated joint participation", ["contact"] = "Shared contact attribute",
        ["price"] = "Price pattern", ["award-pattern"] = "Award pattern", ["document"] = "Document match", ["timing"] = "Submission timing"
    };

    public static RadarGraphResult Build(RadarSnapshot s, IReadOnlyList<RadarFinding> findings)
    {
        var stats = RadarAnalytics.Suppliers(s).ToDictionary(x => x.SupplierId);
        var nodes = new Dictionary<string, GraphNode>();
        var edges = new Dictionary<string, GraphEdge>();
        void Node(GraphNode n) => nodes.TryAdd(n.Id, n);
        void Edge(string type, string kind, string a, string b, string label, string? finding, DateTime? at, int weight = 1)
        {
            // Undirected supplier-supplier edges are normalized so A-B and B-A can never be duplicated.
            if (a.StartsWith("s-") && b.StartsWith("s-") && string.CompareOrdinal(a, b) > 0) (a, b) = (b, a);
            var id = $"e-{type}-{a}-{b}";
            if (edges.TryGetValue(id, out var existing))
            {
                var ids = finding == null || existing.FindingIds.Contains(finding) ? existing.FindingIds : existing.FindingIds.Append(finding).ToList();
                var signal = existing.Kind == "signal" || kind == "signal";
                edges[id] = existing with { FindingIds = ids, Kind = signal ? "signal" : existing.Kind, AvailableAt = Max(existing.AvailableAt, at) };
                return;
            }
            edges[id] = new GraphEdge(id, a, b, type, kind, weight, label, finding == null ? [] : [finding], at);
        }

        // Suppliers that took part in the analysed market, plus any supplier referenced by a finding.
        var involved = s.Bids.Select(b => b.SupplierId).Concat(findings.SelectMany(f => f.SupplierIds)).ToHashSet();
        foreach (var id in involved)
        {
            var sup = s.Supplier(id); if (sup == null) continue;
            var st = stats[id];
            Node(new GraphNode($"s-{id}", "supplier", sup.Name, id, sup.Category, sup.Status.ToString(), st.Participations, st.Wins, st.FirstActivity ?? sup.CreatedAt,
                $"{st.Participations} rounds · {st.Wins} awards"));
        }
        foreach (var bid in s.Bids)
        {
            var tender = s.Tender(bid.TenderId); if (tender == null) continue;
            Node(new GraphNode($"t-{tender.Id}", "tender", tender.Title, tender.Id, tender.Department, tender.Status.ToString(),
                s.Bids.Count(b => b.TenderId == tender.Id), 0, s.Rounds.Where(r => r.TenderId == tender.Id && r.Unsealed).Select(r => (DateTime?)r.AvailableAt).Min(), tender.Department));
            Edge("participation", "fact", $"s-{bid.SupplierId}", $"t-{tender.Id}", $"Offer {bid.Currency} {bid.Total:N0}", null, bid.AvailableAt);
        }
        foreach (var award in RadarAnalytics.Awards(s))
            Edge("award", "fact", $"s-{award.SupplierId}", $"t-{award.TenderId}", $"Awarded {award.Currency} {award.Value:N0}", null, award.AwardedAt);

        foreach (var f in findings)
        {
            var kind = f.Level == "ReviewSignal" ? "signal" : "context";
            switch (f.Detector)
            {
                case RadarForensics.SharedContact:
                    var contactId = "c-" + f.Id[6..];
                    Node(new GraphNode(contactId, "contact", f.AttributeLabel ?? f.Title, null, f.Metrics.GetValueOrDefault("attribute"), null, f.SupplierIds.Count, 0, f.AvailableAt, f.Metrics.GetValueOrDefault("value")));
                    foreach (var sid in f.SupplierIds) Edge("contact", kind == "signal" ? "signal" : "fact", $"s-{sid}", contactId, f.Title, f.Id, f.AvailableAt);
                    break;
                case RadarForensics.IdenticalDocument:
                    var docNode = "d-" + f.Id[6..];
                    Node(new GraphNode(docNode, "document", f.AttributeLabel ?? "Identical file", null, null, null, f.Records.Count, 0, f.AvailableAt, f.Metrics.GetValueOrDefault("sha256")));
                    foreach (var sid in f.SupplierIds) Edge("document", "signal", $"s-{sid}", docNode, "Identical file", f.Id, f.AvailableAt);
                    Pairs(f, (a, b) => Edge("document", "signal", a, b, "Identical document", f.Id, f.AvailableAt));
                    break;
                default:
                    var type = f.Detector switch
                    {
                        RadarForensics.JointParticipation => "joint", RadarForensics.PriceGap or RadarForensics.PriceSimilarity => "price",
                        RadarForensics.AwardRotation => "award-pattern", RadarForensics.SemanticDocument => "document", _ => "timing"
                    };
                    Pairs(f, (a, b) => Edge(type, kind, a, b, f.Title, f.Id, f.AvailableAt, f.SampleSize));
                    break;
            }
        }
        return new RadarGraphResult(nodes.Values.ToList(), edges.Values.ToList());
    }

    private static void Pairs(RadarFinding f, Action<string, string> add)
    {
        for (var i = 0; i < f.SupplierIds.Count; i++)
            for (var j = i + 1; j < f.SupplierIds.Count; j++) add($"s-{f.SupplierIds[i]}", $"s-{f.SupplierIds[j]}");
    }

    private static DateTime? Max(DateTime? a, DateTime? b) => a == null ? b : b == null ? a : a > b ? a : b;
}

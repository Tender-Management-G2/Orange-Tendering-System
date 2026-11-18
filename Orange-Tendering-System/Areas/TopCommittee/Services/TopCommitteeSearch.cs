using Orange_Tendering_System.Models.Entities;
namespace Orange_Tendering_System.Areas.TopCommittee.Services;
public static class TopCommitteeSearch
{
    public static IQueryable<CommitteeRound> Apply(IQueryable<CommitteeRound> query,string? search,CommitteeRoundStatus? status)
    {
        var term=search?.Trim();
        if(!string.IsNullOrEmpty(term))
        {
            var reference=term.StartsWith("T-",StringComparison.OrdinalIgnoreCase)?term[2..].Trim():term;
            query=int.TryParse(reference,out var id)&&id>0
                ?query.Where(r=>r.TenderRound.TenderId==id)
                :query.Where(r=>r.TenderRound.Tender.Title.Contains(term));
        }
        return status.HasValue?query.Where(r=>r.Status==status.Value):query;
    }
}

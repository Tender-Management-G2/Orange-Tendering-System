using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Areas.TopCommittee.ViewModels;
using Orange_Tendering_System.Data;
namespace Orange_Tendering_System.Areas.TopCommittee.Services;
public static class ClarificationUploadCleanup
{
    public static async Task UnlessSaved(ApplicationDbContext db,ClarificationFileStore store,StoredClarificationFile? file,ILogger logger)
    {
        if(file is null) return;
        try { if(!await db.Documents.AsNoTracking().AnyAsync(d=>d.StoragePath==file.StoragePath && d.DocumentType=="TopClarificationAttachment")) store.Remove(file.StoragePath); }
        catch(Exception e) when(e is System.Data.Common.DbException or IOException or UnauthorizedAccessException) { logger.LogWarning(e,"Private upload cleanup deferred"); }
    }
}

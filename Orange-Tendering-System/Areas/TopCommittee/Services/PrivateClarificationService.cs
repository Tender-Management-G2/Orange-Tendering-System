using System.Data;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Areas.TopCommittee.ViewModels;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models.Entities;
namespace Orange_Tendering_System.Areas.TopCommittee.Services;
public sealed class PrivateClarificationService(ApplicationDbContext db,UserManager<ApplicationUser> users)
{
    const string ContextAction="TopClarificationContext", SubmissionAction="TopClarificationSubmission";
    TopCommitteeService Committee => new(db,users);
    public async Task<PrivateConversation?> Read(string userId,int threadId)
    {
        var thread=await db.Clarifications.Include(c=>c.Messages).ThenInclude(m=>m.SenderUser)
            .Include(c=>c.Messages).ThenInclude(m=>m.Documents).AsSplitQuery()
            .SingleOrDefaultAsync(c=>c.Id==threadId && (c.FromUserId==userId || c.ToUserId==userId));
        if(thread is null) return null;
        var context=await Context(threadId);
        if(context is null || thread.TenderId!=context.TenderId || thread.FromUserId!=context.SenderId || thread.ToUserId!=context.ManagerId) return null;
        var details=await Committee.Load(context.SenderId,context.RoundId);
        if(details is null || details.Tender.Id!=context.TenderId || details.Manager?.Id!=context.ManagerId) return null;
        return new(thread,details,userId==context.ManagerId);
    }
    public async Task<List<PrivateConversation>> Inbox(string userId)
    {
        var ids=await db.Clarifications.Where(c=>c.FromUserId==userId || c.ToUserId==userId).OrderByDescending(c=>c.CreatedAt).ThenByDescending(c=>c.Id).Take(100).Select(c=>c.Id).ToListAsync();
        var list=new List<PrivateConversation>(); foreach(var id in ids) if(await Read(userId,id) is {} item) list.Add(item); return list;
    }
    async Task<ClarificationContext?> Context(int threadId)
    {
        var json=await db.AuditLogs.Where(a=>a.Action==ContextAction && a.EntityName=="Clarification" && a.EntityId==threadId.ToString()).OrderBy(a=>a.Id).Select(a=>a.NewValues).FirstOrDefaultAsync();
        return json is null?null:JsonSerializer.Deserialize<ClarificationContext>(json);
    }
    async Task Lock(int tenderId) { var resource=$"Orange:TopCommittee:Tender:{tenderId}"; await db.Database.ExecuteSqlInterpolatedAsync($"DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource={resource},@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=10000; IF @r<0 THROW 51000,'Committee operation busy. Retry.',1;"); }
    async Task<ConversationSaved?> Duplicate(string userId,string key,int roundId)
    {
        var json=await db.AuditLogs.Where(a=>a.UserId==userId && a.Action==SubmissionAction && a.EntityId==key).Select(a=>a.NewValues).SingleOrDefaultAsync();
        if(json is null) return null;
        var stamp=JsonSerializer.Deserialize<SubmissionStamp>(json)!;
        if(stamp.RoundId!=roundId) throw new InvalidOperationException("This submission has already been used. Reload the form.");
        return new(stamp.ThreadId,true);
    }
    public async Task<ConversationSaved> Send(string userId,int roundId,int? threadId,string? subject,string message,string submissionId,StoredClarificationFile? file)
    {
        if(!Guid.TryParseExact(submissionId,"N",out _)) throw new InvalidOperationException("Reload the page before submitting this form.");
        message=message.Trim(); if(message.Length is <1 or >10000) throw new InvalidOperationException("Enter a message up to 10,000 characters.");
        if(threadId is null && (string.IsNullOrWhiteSpace(subject) || subject.Trim().Length>200)) throw new InvalidOperationException("Enter a subject up to 200 characters.");
        var tenderId=threadId.HasValue ? await db.Clarifications.Where(c=>c.Id==threadId && (c.FromUserId==userId || c.ToUserId==userId)).Select(c=>c.TenderId).SingleOrDefaultAsync()
            : await Committee.AssignedRounds(userId).Where(r=>r.Id==roundId).Select(r=>(int?)r.TenderRound.TenderId).SingleOrDefaultAsync();
        if(tenderId is null) throw new InvalidOperationException("The conversation is unavailable.");
        await using var transaction=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable); await Lock(tenderId.Value); db.ChangeTracker.Clear();
        PrivateConversation? conversation=threadId.HasValue?await Read(userId,threadId.Value):null;
        if(threadId.HasValue && conversation is null) throw new InvalidOperationException("The conversation is unavailable.");
        var details=conversation?.Details ?? await Committee.Load(userId,roundId) ?? throw new InvalidOperationException("The round is unavailable.");
        roundId=details.Round.Id;
        if(await Duplicate(userId,submissionId,roundId) is {} duplicate) return duplicate;
        if(!details.CanClarify || conversation?.Thread.Status==ClarificationStatus.Closed) throw new InvalidOperationException("This conversation is read-only. A current active round and responsible Manager are required.");
        var now=DateTime.UtcNow;
        var thread=conversation?.Thread ?? new Clarification { TenderId=tenderId,FromUserId=userId,ToUserId=details.Manager!.Id,Subject=subject!.Trim(),CreatedAt=now,Status=ClarificationStatus.Open };
        if(conversation is null) db.Clarifications.Add(thread);
        var item=new ClarificationMessage { SenderUserId=userId,Message=message,SentAt=now };
        if(file is not null) item.Documents.Add(new Document { FileName=file.FileName,StoragePath=file.StoragePath,ContentType=file.ContentType,SizeBytes=file.SizeBytes,DocumentType="TopClarificationAttachment",UploadedByUserId=userId,UploadedAt=now });
        thread.Messages.Add(item); thread.Status=conversation?.IsManager==true?ClarificationStatus.Answered:ClarificationStatus.Open;
        await db.SaveChangesAsync();
        if(conversation is null) db.AuditLogs.Add(new AuditLog {UserId=userId,Action=ContextAction,EntityName="Clarification",EntityId=thread.Id.ToString(),NewValues=JsonSerializer.Serialize(new ClarificationContext(roundId,tenderId.Value,userId,details.Manager!.Id)),CreatedAt=now});
        var recipient=conversation?.IsManager==true?thread.FromUserId:thread.ToUserId;
        db.Notifications.Add(new Notification {UserId=recipient,Title=conversation?.IsManager==true?"Sourcing Manager — Clarification Reply":"Top Committee — Private Clarification",Message=$"Tender #{tenderId}: {thread.Subject}",Link=$"/TopCommittee/Conversations/Details/{thread.Id}",CreatedAt=now});
        db.AuditLogs.Add(new AuditLog {UserId=userId,Action=SubmissionAction,EntityName="Clarification",EntityId=submissionId,NewValues=JsonSerializer.Serialize(new SubmissionStamp(roundId,thread.Id)),CreatedAt=now});
        await db.SaveChangesAsync(); await transaction.CommitAsync(); return new(thread.Id,false);
    }
    sealed record SubmissionStamp(int RoundId,int ThreadId);
}

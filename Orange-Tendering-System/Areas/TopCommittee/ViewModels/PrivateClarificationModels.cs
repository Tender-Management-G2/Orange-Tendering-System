using System.ComponentModel.DataAnnotations;
using Orange_Tendering_System.Models.Entities;
namespace Orange_Tendering_System.Areas.TopCommittee.ViewModels;
public sealed class PrivateQuestionInput
{
    public int RoundId { get; set; }
    [Required,StringLength(200)] public string Subject { get; set; } = "";
    [Required,StringLength(10000)] public string Message { get; set; } = "";
    [Required] public string SubmissionId { get; set; } = "";
    public IFormFile? Attachment { get; set; }
}
public sealed class PrivateReplyInput
{
    public int ThreadId { get; set; }
    [Required,StringLength(10000)] public string Message { get; set; } = "";
    [Required] public string SubmissionId { get; set; } = "";
    public IFormFile? Attachment { get; set; }
}
public sealed record ClarificationContext(int RoundId, int TenderId, string SenderId, string ManagerId);
public sealed record StoredClarificationFile(string FileName,string StoragePath,string ContentType,long SizeBytes);
public sealed record ConversationSaved(int ThreadId,bool Duplicate);
public sealed record PrivateConversation(Clarification Thread,RoundDetails Details,bool IsManager)
{
    public bool CanReply => Details.CanClarify && Thread.Status != ClarificationStatus.Closed;
}

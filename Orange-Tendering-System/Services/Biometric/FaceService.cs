using Microsoft.EntityFrameworkCore;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models.Biometric;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Services.Biometric;

/// <summary>
/// Legacy template storage only. Browser descriptors and liveness measurements are not
/// server-verifiable identity evidence. Enrollment and authentication fail closed.
/// No configuration flag can enable this pipeline without a new reviewed proof mechanism.
/// Existing encrypted templates are preserved, but never used to authorize a login.
/// </summary>
public class FaceService(ApplicationDbContext db)
{
    public const string LoginPurpose = "face-login";
    public const string EnrollPurpose = "face-enroll";
    public const string DisabledReason = "Face enrollment and login are disabled: no secure server-verifiable face identity proof is implemented. Use your password or a WebAuthn passkey.";
    public FaceChallengeResponse CreateChallenge(string purpose, string? userId) => throw new NotSupportedException(DisabledReason);
    public Task<int> CountTemplatesAsync(string userId) => db.FaceTemplates.AsNoTracking().CountAsync(t => t.UserId == userId);
    // Kept only for an explicit authenticated owner-requested removal, never called by security migration/startup.
    public Task<int> RemoveTemplatesAsync(string userId) => db.FaceTemplates.Where(t => t.UserId == userId).ExecuteDeleteAsync();
    public Task<BiometricResult> EnrollAsync(ApplicationUser user, FaceSubmission submission) => Task.FromResult(BiometricResult.Fail(DisabledReason));
    public Task<BiometricResult> VerifyLoginAsync(ApplicationUser? user, FaceSubmission submission) => Task.FromResult(BiometricResult.Fail(DisabledReason));
}

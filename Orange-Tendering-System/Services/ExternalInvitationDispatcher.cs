using System.Net;
using System.Net.Mail;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Orange_Tendering_System.Data;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Services;

// External channel for inviting companies that are not registered suppliers. Disabled until configured;
// the SMTP password belongs in user secrets or deployment configuration.
public class ExternalInvitationOptions
{
    public bool Enabled { get; set; }
    // Public address of this application, used for the supplier registration link.
    public string? PublicBaseUrl { get; set; }
    public int MaxAttempts { get; set; } = 3;
    public bool Configured => Enabled &&
        Uri.TryCreate(PublicBaseUrl, UriKind.Absolute, out var url) && url.Scheme == "https";
}
public record ExternalInvitationMessage(string To, string Subject, string Body);
public interface IExternalInvitationChannel
{
    string Name { get; }
    bool Configured { get; }
    Task Send(ExternalInvitationMessage message, CancellationToken ct);
}
public class SmtpInvitationChannel(IOptions<ExternalInvitationOptions> options, IConfiguration configuration) : IExternalInvitationChannel
{
    public string Name => "Email";
    public bool Configured => options.Value.Configured && SmtpEmailSender.IsConfigured(configuration);
    public Task Send(ExternalInvitationMessage message, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return new SmtpEmailSender(configuration).SendEmailAsync(message.To, message.Subject,
            System.Text.Encodings.Web.HtmlEncoder.Default.Encode(message.Body).Replace("\n", "<br />"));
    }
}

// Delivers queued external invitations for rounds that are still open. Run by the background worker only,
// so one message is not sent twice by concurrent requests. Every outcome is persisted and audited.
public class ExternalInvitationDispatcher(ApplicationDbContext db, IExternalInvitationChannel channel, TimeProvider clock,
    IOptions<ExternalInvitationOptions> options, ILogger<ExternalInvitationDispatcher> logger)
{
    public async Task<int> DispatchPending(CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime; var maxAttempts = Math.Clamp(options.Value.MaxAttempts, 1, 10);
        var pending = await db.Set<ExternalSupplierInvitation>().Include(x => x.TenderRound).ThenInclude(x => x.Tender)
            .Where(x => x.Status == ExternalInvitationStatus.Queued || x.Status == ExternalInvitationStatus.ChannelNotConfigured || x.Status == ExternalInvitationStatus.Failed && x.Attempts < maxAttempts)
            .Where(x => x.TenderRound.Status == TenderRoundStatus.Open && x.TenderRound.Deadline > now)
            .OrderBy(x => x.Id).Take(25).ToListAsync(ct);
        var sent = 0;
        foreach (var invitation in pending)
        {
            // Database lock also protects multiple web instances/background workers. SMTP is
            // at-least-once across a process crash after acceptance; never claim exactly-once email.
            await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT Id FROM ExternalSupplierInvitation WITH (UPDLOCK,HOLDLOCK) WHERE Id={invitation.Id}", ct);
            await db.Entry(invitation).ReloadAsync(ct);
            if (invitation.Status == ExternalInvitationStatus.Sent) { await tx.CommitAsync(ct); continue; }
            if (invitation.SupplierId.HasValue && !await db.Suppliers.AnyAsync(x => x.Id == invitation.SupplierId && x.Status == SupplierStatus.Approved, ct)) { await tx.CommitAsync(ct); continue; }
            if (!channel.Configured)
            {
                if (invitation.Status == ExternalInvitationStatus.ChannelNotConfigured) { await tx.CommitAsync(ct); continue; }
                invitation.Status = ExternalInvitationStatus.ChannelNotConfigured;
                invitation.LastError = "No external invitation channel is configured. Nothing was sent.";
                Audit(invitation, "ExternalInvitationNotSent", now);
                await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
                continue;
            }
            invitation.Attempts++; invitation.Channel = channel.Name;
            try
            {
                var tender = invitation.TenderRound.Tender;
                var register = new Uri(new Uri(options.Value.PublicBaseUrl!.TrimEnd('/') + "/"), invitation.SupplierId.HasValue ? $"Supplier/Workflow/Details/{tender.Id}" : "Identity/Account/Register");
                await channel.Send(new ExternalInvitationMessage(invitation.Email, $"Invitation to tender: {tender.Title}",
                    $"Dear {invitation.CompanyName},\n\nOrange invites you to take part in the tender \"{tender.Title}\" (round {invitation.TenderRound.RoundNumber}).\n" +
                    $"Offers close on {invitation.TenderRound.Deadline:yyyy-MM-dd HH:mm} UTC.\n\nOnly registered and approved suppliers can receive the tender documents and submit an offer. " +
                    $"{(invitation.SupplierId.HasValue ? "Sign in and review your invitation" : "Register your company here")}: {register}\n\nThis message does not grant access to tender documents and is not a contract award."), ct);
                invitation.Status = ExternalInvitationStatus.Sent; invitation.SentAt = now; invitation.LastError = null; sent++;
                Audit(invitation, "ExternalInvitationSent", now);
            }
            catch (Exception e) when (e is SmtpException or IOException or InvalidOperationException or FormatException or UriFormatException)
            {
                logger.LogWarning(e, "External invitation {InvitationId} could not be sent", invitation.Id);
                invitation.Status = ExternalInvitationStatus.Failed;
                invitation.LastError = e.Message.Length > 1000 ? e.Message[..1000] : e.Message;
                Audit(invitation, "ExternalInvitationFailed", now);
            }
            await db.SaveChangesAsync(CancellationToken.None); await tx.CommitAsync(CancellationToken.None);
        }
        await db.SaveChangesAsync(CancellationToken.None);
        return sent;
    }
    private void Audit(ExternalSupplierInvitation invitation, string action, DateTime now) => db.AuditLogs.Add(new AuditLog
    {
        EntityName = "Tender", EntityId = invitation.TenderRound.TenderId.ToString(), Action = action, CreatedAt = now,
        NewValues = JsonSerializer.Serialize(new { invitation.Id, invitation.TenderRound.RoundNumber, invitation.Email, invitation.Channel, invitation.Status, invitation.Attempts })
    });
}

using System.Security.Claims;
using Microsoft.AspNetCore.Identity.UI.Services;
using Orange_Tendering_System.Models.Entities;

namespace Orange_Tendering_System.Services;

// Orchestrates the Officer's "Notify Suppliers": the SQL notifications and delivery rows are committed first by the workflow
// service, then SMTP e-mail is attempted when (and only when) SMTP is configured. Every outcome is stored per recipient.
public sealed class FinalDecisionService(TenderWorkflowService workflow, IEmailSender mail, IConfiguration configuration, ILogger<FinalDecisionService> logger)
{
    public async Task NotifySuppliers(int tenderId, ClaimsPrincipal principal)
    {
        await workflow.NotifySuppliers(tenderId, principal);
        await SendEmails(tenderId, principal, retryOnly: false);
    }

    public Task RetryEmails(int tenderId, ClaimsPrincipal principal) => SendEmails(tenderId, principal, retryOnly: true);
    // Used after the Executive Agent has separately confirmed and committed the existing SQL notification action.
    public Task SendPendingEmails(int tenderId, ClaimsPrincipal principal) => SendEmails(tenderId, principal, retryOnly: false);

    // After the unanimous Top Committee approval awarded the tender (committed in the vote's transaction), e-mail each
    // supplier its own result. Failures stay recorded per recipient and the Officer can retry them from Final Decisions.
    public async Task SendAwardEmails(int tenderId)
    {
        var configured = SmtpEmailSender.IsConfigured(configuration);
        foreach (var (delivery, email) in await workflow.PendingAwardEmails(tenderId))
        {
            if (!configured) { await workflow.RecordSystemEmailResult(delivery.Id, "NotConfigured", "SMTP is not configured; the in-app notification was delivered."); continue; }
            try
            {
                var (subject, body) = Message(delivery.Outcome);
                await mail.SendEmailAsync(email, subject, body);
                await workflow.RecordSystemEmailResult(delivery.Id, "Sent", null);
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Supplier result e-mail {DeliveryId} failed", delivery.Id);
                await workflow.RecordSystemEmailResult(delivery.Id, "Failed", e.Message);
            }
        }
    }

    private static (string Subject, string Body) Message(string outcome) => outcome switch
    {
        "Won" => ("Tender result", "<p>Congratulations: your company has been selected for a tender after all mandatory approvals.</p><p>Sign in to the Supplier Portal to see the details of your own offer.</p>"),
        "Cancelled" => ("Tender cancelled", "<p>A tender you participated in has been cancelled because the company no longer needs it. No supplier was selected and no further offers are accepted. Thank you for participating.</p>"),
        "Reissued" => ("Tender reissued", "<p>A tender you participated in is being reissued as a new bidding round. Your earlier offer stays on record but will not be evaluated. You will receive a new invitation; please submit a new offer in the Supplier Portal.</p>"),
        _ => ("Tender result", "<p>A tender you participated in has been awarded. Your company was not selected. Thank you for participating.</p><p>Sign in to the Supplier Portal for details of your own offer.</p>")
    };

    private async Task SendEmails(int tenderId, ClaimsPrincipal principal, bool retryOnly)
    {
        var configured = SmtpEmailSender.IsConfigured(configuration);
        foreach (var (delivery, email) in await workflow.DeliveriesToEmail(tenderId, principal, retryOnly))
        {
            if (!configured) { await workflow.RecordEmailResult(delivery.Id, "NotConfigured", "SMTP is not configured; the in-app notification was delivered.", principal); continue; }
            try
            {
                var (subject, body) = Message(delivery.Outcome);
                await mail.SendEmailAsync(email, subject, body);
                await workflow.RecordEmailResult(delivery.Id, "Sent", null, principal);
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Supplier result e-mail {DeliveryId} failed", delivery.Id);
                await workflow.RecordEmailResult(delivery.Id, "Failed", e.Message, principal);
            }
        }
    }
}

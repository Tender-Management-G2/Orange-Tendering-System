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

    private async Task SendEmails(int tenderId, ClaimsPrincipal principal, bool retryOnly)
    {
        var configured = SmtpEmailSender.IsConfigured(configuration);
        foreach (var (delivery, email) in await workflow.DeliveriesToEmail(tenderId, principal, retryOnly))
        {
            if (!configured) { await workflow.RecordEmailResult(delivery.Id, "NotConfigured", "SMTP is not configured; the in-app notification was delivered.", principal); continue; }
            try
            {
                var body = delivery.Outcome == "Won"
                    ? "<p>Congratulations: your company has been selected for a tender after all mandatory approvals.</p><p>Sign in to the Supplier Portal to see the details of your own offer.</p>"
                    : "<p>A tender you participated in has been awarded. Your company was not selected. Thank you for participating.</p><p>Sign in to the Supplier Portal for details of your own offer.</p>";
                await mail.SendEmailAsync(email, "Tender result", body);
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

namespace Orange_Tendering_System.Services;

// Time-driven steps: opens scheduled scoring, closes it at the deadline, and delivers queued external
// supplier invitations. Each step is isolated so one failure does not delay the others. Submission
// endpoints enforce the same server times even when this worker is not running.
public class EvaluationDeadlineWorker(IServiceScopeFactory scopes, ILogger<EvaluationDeadlineWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await Step("Evaluation deadline processing", s => s.GetRequiredService<TenderWorkflowService>().CloseDueSessions(stoppingToken), stoppingToken);
                await Step("Evaluation opening processing", s => s.GetRequiredService<TenderWorkflowService>().OpenDueSessions(stoppingToken), stoppingToken);
                await Step("External invitation delivery", s => s.GetRequiredService<ExternalInvitationDispatcher>().DispatchPending(stoppingToken), stoppingToken);
                await Step("Approved registration invitations", s => s.GetRequiredService<TenderWorkflowService>().LinkRegisteredContacts(stoppingToken), stoppingToken);
            }
        }
        // Normal shutdown: the host cancels the token while the timer is waiting. Not an error, so it must not surface as an unhandled exception.
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
    private async Task Step(string name, Func<IServiceProvider, Task> work, CancellationToken stoppingToken)
    {
        try { using var scope = scopes.CreateScope(); await work(scope.ServiceProvider); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception e) { logger.LogWarning(e, "{Step} failed; submission endpoints still enforce the server schedule.", name); }
    }
}

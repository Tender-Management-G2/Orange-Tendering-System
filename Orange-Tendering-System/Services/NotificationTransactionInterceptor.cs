using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Orange_Tendering_System.Services;

// Explicit workflow transactions may save several times. Publish only when SQL commits;
// rolled-back notifications must never appear in another user's browser.
public sealed class NotificationTransactionInterceptor(NotificationPushInterceptor notifications) : DbTransactionInterceptor
{
    public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData)
        => notifications.Push(eventData.Context).GetAwaiter().GetResult();

    public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        => notifications.Push(eventData.Context);

    public override void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData)
        => notifications.Clear(eventData.Context);

    public override Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    { notifications.Clear(eventData.Context); return Task.CompletedTask; }

    public override void TransactionFailed(DbTransaction transaction, TransactionErrorEventData eventData)
        => notifications.Clear(eventData.Context);

    public override Task TransactionFailedAsync(DbTransaction transaction, TransactionErrorEventData eventData, CancellationToken cancellationToken = default)
    { notifications.Clear(eventData.Context); return Task.CompletedTask; }
}

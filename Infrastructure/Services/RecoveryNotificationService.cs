using Core.Application.Interfaces;
using Core.Application.Options;
using Core.Domain.Entities;
using Core.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services;

public sealed class RecoveryNotificationService(ApplicationDbContext db, IEmailDispatcher dispatcher,
    IOptionsSnapshot<EmailOptions> options, TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    // Adds intents to the caller's selection transaction. Never submits SMTP inside it.
    public void Enqueue(Guid accountId, long epoch, string? oldAddress, string newAddress, bool useDefault)
    {
        var now = _time.GetUtcNow();
        if (!string.IsNullOrWhiteSpace(oldAddress) && oldAddress != newAddress)
            db.RecoveryNotifications.Add(new RecoveryNotification(accountId, epoch,
                RecoveryNotificationKind.CustomChangedOldDestination, oldAddress, now, now.AddDays(1), 5));
        db.RecoveryNotifications.Add(new RecoveryNotification(accountId, epoch,
            useDefault ? RecoveryNotificationKind.DefaultSelected : RecoveryNotificationKind.CustomChangedNewDestination,
            newAddress, now, now.AddDays(1), 5));
    }

    public Task<bool> SendCandidateAsync(string address, string code, CancellationToken ct) => SendAsync(address,
        "Verify recovery email change", $"Your recovery email change verification code is {code}. Your current recovery method remains active until verification succeeds.", ct);

    public async Task ProcessBatchAsync(CancellationToken ct)
    {
        var rows = await db.RecoveryNotifications.Where(n => n.DeliveredAtUtc == null && n.AbandonedAtUtc == null)
            .OrderBy(n => n.Id).Take(100).ToListAsync(ct);
        foreach (var row in rows)
        {
            var now = _time.GetUtcNow();
            if (row.TryAbandon(now))
            {
                try { await db.SaveChangesAsync(ct); }
                catch (DbUpdateConcurrencyException) { db.Entry(row).State = EntityState.Detached; }
                continue;
            }
            var lease = Guid.NewGuid();
            if (!row.TryClaim(lease, now, now.AddSeconds(30))) continue;
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { db.Entry(row).State = EntityState.Detached; continue; }
            var sent = await SendAsync(row.Recipient, "Recovery email changed",
                row.Kind == RecoveryNotificationKind.DefaultSelected
                    ? "Your account now uses its trusted default recovery email. If you did not make this change, contact support."
                    : "Your account recovery email was changed. If you did not make this change, contact support.", ct);
            now = _time.GetUtcNow();
            if (sent) row.TryComplete(lease, now);
            else row.TryFail(lease, now, now.AddMinutes(Math.Min(60, row.Attempts * 5)));
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { db.Entry(row).State = EntityState.Detached; }
        }
    }

    private async Task<bool> SendAsync(string address, string subject, string body, CancellationToken ct)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(options.Value.SmtpHost)) return false;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(10));
            await dispatcher.SendAsync(new EmailMessage(address, subject, body), deadline.Token);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return false; }
    }
}

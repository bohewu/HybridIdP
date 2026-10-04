using Core.Application.Interfaces;
using Core.Domain.Models;
using Core.Application.Options;
using Microsoft.Extensions.Options;

namespace Infrastructure.Services;

public sealed class RecoveryOtpDeliveryService(IEmailDispatcher dispatcher, IOptionsSnapshot<EmailOptions> options)
{
    public Task<bool> SendAsync(string address, string code, int lifetimeMinutes, CancellationToken cancellationToken) =>
        SendAsync(address, "Password recovery verification", code, lifetimeMinutes, cancellationToken);

    internal async Task<bool> SendAsync(string address, string subject, string code, int lifetimeMinutes, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(options.Value.SmtpHost)) return false;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(10));
            await dispatcher.SendAsync(new EmailMessage(address, subject,
                $"Your verification code is {code}. It expires in {lifetimeMinutes} minutes."), deadline.Token);
            deadline.Token.ThrowIfCancellationRequested();
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return false; }
    }
}
